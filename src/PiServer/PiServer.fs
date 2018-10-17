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

let play (cancellationToken:CancellationToken) (uri:string) = task {
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

Thread.Sleep 10000

let cts2 = new CancellationTokenSource()

try
    let youtubeURL = "https://www.youtube.com/watch?v=TJAfLE39ZZ8"
    log.InfoFormat("Starting Youtube-Download: {0}", youtubeURL)
    let youtubeFile:string = nodeServices.InvokeExportAsync<string>("./youtube", "download", youtubeURL) |> Async.AwaitTask |> Async.RunSynchronously
    log.InfoFormat("Downloaded to: {0}", youtubeFile)
    currentTask <- play cts2.Token "https://r4---sn-4g5e6ne6.googlevideo.com/videoplayback?ei=kCbHW8OpDc_lgAfb1Y34Cw&c=WEB&key=yt6&mm=31%2C26&ipbits=0&mn=sn-4g5e6ne6%2Csn-5hnekn7z&pl=25&mt=1539778069&dur=250.321&mv=m&initcwndbps=928750&ms=au%2Conr&itag=251&sparams=clen%2Cdur%2Cei%2Cgir%2Cid%2Cinitcwndbps%2Cip%2Cipbits%2Citag%2Ckeepalive%2Clmt%2Cmime%2Cmm%2Cmn%2Cms%2Cmv%2Cpl%2Crequiressl%2Csource%2Cexpire&id=o-AFJ3Ym8J435Qob5eRIvlY1_z1BUJS3K9epWSpVs6Y5ga&fvip=4&keepalive=yes&requiressl=yes&mime=audio%2Fwebm&gir=yes&lmt=1537609986566589&expire=1539799792&source=youtube&ip=2003%3Ae5%3Adbcb%3Aa900%3A928b%3Ad5a5%3Ae38c%3A878d&clen=4064541&signature=B682E7F5BE3CC05B3C9119B38A699783BABF0A20.297295EB09411EBF65AB363B56BE4868DA5C9002&ratebypass=yes"
    ()
with
| exn ->
    log.ErrorFormat("Youtube error: {0}", exn.Message)
    if not (isNull exn.InnerException) then
        log.ErrorFormat("Youtube error: {0}", exn.InnerException.Message)

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