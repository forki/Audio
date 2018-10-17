open Microsoft.Extensions.DependencyInjection
open Giraffe
open Saturn
open FSharp.Control.Tasks.ContextInsensitive
open System
open System.IO
open Microsoft.AspNetCore.NodeServices
open Thoth.Json.Net
open ServerCore.Domain
open System.Threading.Tasks
open System.Threading
open System.Diagnostics
open System.Xml
open System.Reflection

let tagServer = "https://audio-hub.azurewebsites.net"
let userID = "9bb2b109-bf08-4342-9e09-f4ce3fb01c0f"


let configureLogging() =
    let log4netConfig = XmlDocument()
    log4netConfig.Load(File.OpenRead("log4net.config"))
    let repo = log4net.LogManager.CreateRepository(Assembly.GetEntryAssembly(), typeof<log4net.Repository.Hierarchy.Hierarchy>)
    log4net.Config.XmlConfigurator.Configure(repo, log4netConfig.["log4net"]) |> ignore

    let log = log4net.LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType)
    log


let log = configureLogging()

let port = 8086us

let cts = new CancellationTokenSource()
let mutable runningProcess = null

let play  (cancellationToken:CancellationToken) (uri:string) = task {
    let mediaFile = uri
    let p = new System.Diagnostics.Process()
    runningProcess <- p
    let startInfo = System.Diagnostics.ProcessStartInfo()
    p.EnableRaisingEvents <- true
    let tcs = new TaskCompletionSource<obj>()
    let handler = System.EventHandler(fun _ args ->
        tcs.TrySetResult(null) |> ignore
    )

    p.Exited.AddHandler handler
    try
        cancellationToken.Register(fun () -> tcs.SetCanceled()) |> ignore
        startInfo.FileName <- "omxplayer"
        startInfo.Arguments <- mediaFile
        p.StartInfo <- startInfo
        let _ = p.Start()
        let! _ = tcs.Task
        return "Started"
    finally
        p.Exited.RemoveHandler handler
}

let getMusikPlayerProcesses() = Process.GetProcessesByName("omxplayer.bin")

let stop() = task {
    for p in getMusikPlayerProcesses() do
        if not p.HasExited then
            log.InfoFormat "stopping omxplaxer"
            try p.Kill() with _ -> log.WarnFormat "couldn't kill omxplayer"
    cts.Cancel()
}

let mutable currentTask = null

let executeAction (action:TagAction) =
    match action with
    | TagAction.UnknownTag ->
        task {
            log.WarnFormat "Unknown Tag"
        }
    | TagAction.StopMusik ->
        task {
            let! _ = stop()
            log.InfoFormat "Musik stopped"
        }
    | TagAction.PlayMusik url ->
        task {
            let! _ = stop()
            currentTask <- play cts.Token url
            log.InfoFormat( "Playing {0}", url)
        }
    | TagAction.PlayBlobMusik _ ->
        failwithf "Blobs links need to be converted to direct links by the tag server"


let executeTag (tag:string) = task {
    try
        use webClient = new System.Net.WebClient()
        let tagsUrl tag = sprintf @"%s/api/tags/%s/%s" tagServer userID tag
        let! result = webClient.DownloadStringTaskAsync(System.Uri (tagsUrl tag))

        match Decode.fromString Tag.Decoder result with
        | Error msg -> return failwith msg
        | Ok tag ->
            log.InfoFormat( "Object: {0}:", tag.Object)
            log.InfoFormat( "Description: {0}", tag.Description)
            return! executeAction tag.Action
    with
    | exn ->
        log.ErrorFormat("Token action error: {0}", exn.Message)
}

let runFirmwareUpdate() =
    let p = new Process()
    let startInfo = new ProcessStartInfo()
    startInfo.WorkingDirectory <- "/home/pi/firmware/"
    startInfo.FileName <- "sudo"
    startInfo.Arguments <- "sh update.sh"
    startInfo.RedirectStandardOutput <- true
    startInfo.UseShellExecute <- false
    startInfo.CreateNoWindow <- true
    p.StartInfo <- startInfo
    p.Start() |> ignore

let checkFirmware () = task {
    use webClient = new System.Net.WebClient()
    System.Net.ServicePointManager.SecurityProtocol <-
        System.Net.ServicePointManager.SecurityProtocol |||
          System.Net.SecurityProtocolType.Tls11 |||
          System.Net.SecurityProtocolType.Tls12

    let url = sprintf @"%s/api/firmware" tagServer
    let! result = webClient.DownloadStringTaskAsync(System.Uri url)

    match Decode.fromString Firmware.Decoder result with
    | Error msg ->
        log.ErrorFormat("Decoder error: {0}", msg)
        return failwith msg
    | Ok firmware ->
        try
            if firmware.Version <> ReleaseNotes.Version then
                let localFileName = System.IO.Path.GetTempFileName().Replace(".tmp", ".zip")
                log.InfoFormat("Starting download of {0}", firmware.Url)
                do! webClient.DownloadFileTaskAsync(firmware.Url,localFileName)
                log.InfoFormat "Download done."
                let target = System.IO.Path.GetFullPath "/home/pi/firmware"
                if System.IO.Directory.Exists target then
                    System.IO.Directory.Delete(target,true)
                System.IO.Directory.CreateDirectory(target) |> ignore
                System.IO.Compression.ZipFile.ExtractToDirectory(localFileName, target)
                System.IO.File.Delete localFileName
                runFirmwareUpdate()
                while true do
                    log.InfoFormat "Running firmware update."
                    do! Task.Delay 3000
                    ()
            else
                log.InfoFormat( "Firmware {0} is uptodate.", ReleaseNotes.Version)
        with
        | exn ->
            log.ErrorFormat("Upgrade error: {0}", exn.Message)
}

let executeStartupActions () = task {
    try
        use webClient = new System.Net.WebClient()
        let url = sprintf @"%s/api/startup" tagServer
        let! result = webClient.DownloadStringTaskAsync(System.Uri url)

        match Decode.fromString (Decode.list TagAction.Decoder) result with
        | Error msg -> return failwith msg
        | Ok actions ->
            for t in actions do
                let! _ = executeAction t
                ()
    with
    | exn ->
        log.ErrorFormat("Startup error: {0}", exn.Message)
}

let webApp = router {
    get "/version" (fun next ctx -> task {
        return! text ReleaseNotes.Version next ctx
    })
}

let configureSerialization (services:IServiceCollection) =
    services.AddNodeServices(fun x -> x.InvocationTimeoutMilliseconds <- 2 * 60 * 60 * 1000)
    services

let builder = application {
    url ("http://0.0.0.0:" + port.ToString() + "/")
    use_router webApp
    memory_cache
    service_config configureSerialization
    use_gzip
}

let app = builder.Build()
app.Start()

log.InfoFormat("PiServer {0} started.", ReleaseNotes.Version)

let firmwareCheck = checkFirmware()
firmwareCheck.Wait()

let startupTask = executeStartupActions()
startupTask.Wait()

let mutable running = null

let nodeServices = app.Services.GetService(typeof<INodeServices>) :?> INodeServices

let playYoutube  (cancellationToken:CancellationToken) (uri:string) = task {
    let mediaFile = uri
    let p = new System.Diagnostics.Process()
    runningProcess <- p
    let startInfo = System.Diagnostics.ProcessStartInfo()
    p.EnableRaisingEvents <- true
    let tcs = new TaskCompletionSource<obj>()
    let handler = System.EventHandler(fun _ args ->
        tcs.TrySetResult(null) |> ignore
    )

    p.Exited.AddHandler handler
    try
        cancellationToken.Register(fun () -> tcs.SetCanceled()) |> ignore
        startInfo.FileName <- "omxplayer"
        startInfo.Arguments <- mediaFile
        p.StartInfo <- startInfo
        let _ = p.Start()
        let! _ = tcs.Task
        return "Started"
    finally
        p.Exited.RemoveHandler handler
}

let youtubeURL = "https://www.youtube.com/watch?v=TJAfLE39ZZ8"
log.InfoFormat("Starting Youtube-Download: {0}", youtubeURL)
let youtubeFile:string = nodeServices.InvokeExportAsync<string>("./youtube", "download", youtubeURL) |> Async.AwaitTask |> Async.RunSynchronously
log.InfoFormat("Downloaded to: {0}", youtubeFile)
let _ = playYoutube cts.Token youtubeFile |> Async.AwaitTask |> Async.RunSynchronously

let rfidLoop() = task {
    while true do
        let! token = nodeServices.InvokeExportAsync<string>("./read-tag", "read", "tag")

        if String.IsNullOrEmpty token then
            let! _ = Task.Delay(TimeSpan.FromSeconds 0.5)
            ()
        else
            log.InfoFormat("RFID/NFC: {0}", token)
            running <- executeTag token
            let mutable waiting = true
            while waiting do
                do! Task.Delay(TimeSpan.FromSeconds 0.5)
                if running.IsCompleted then
                    for p in getMusikPlayerProcesses() do
                        if p.HasExited then
                            log.InfoFormat "omxplayer shut down"
                            waiting <- false

                let! newToken = nodeServices.InvokeExportAsync<string>("./read-tag", "read", "tag")
                if newToken <> token then
                    log.InfoFormat("RFID/NFC {0} was removed from reader", token)
                    waiting <- false

            let! _ = stop()
            ()
}

rfidLoop().Wait()