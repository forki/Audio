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


let firmwareUpdateInterval = TimeSpan.FromHours 1.
let log = configureLogging()

let port = 8086us

let mutable runningProcess = null

let mutable globalStop = false

let play (uris:string []) = task {
    for mediaFile in uris do
        if not globalStop then
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
                log.InfoFormat( "Starting omxplayer with {0}", mediaFile)
                startInfo.FileName <- "omxplayer"
                startInfo.Arguments <- mediaFile
                p.StartInfo <- startInfo
                let _ = p.Start()
                let! _ = tcs.Task
                ()
            finally
                p.Exited.RemoveHandler handler
}


let youtubeLinks = System.Collections.Concurrent.ConcurrentDictionary<_,_>()

let discoverYoutubeLink (youtubeURL:string) = task {
    log.InfoFormat("Starting youtube-dl -g {0}", youtubeURL)
    let lines = System.Collections.Generic.List<_>()
    let proc = new Process ()
    let startInfo = new ProcessStartInfo()
    startInfo.FileName <- "sudo"
    startInfo.Arguments <- sprintf "youtube-dl -g \"%s\"" youtubeURL
    startInfo.UseShellExecute <- false
    startInfo.RedirectStandardOutput <- true
    startInfo.CreateNoWindow <- true
    proc.StartInfo <- startInfo

    proc.Start() |> ignore
    while not proc.StandardOutput.EndOfStream do
        let! line = proc.StandardOutput.ReadLineAsync()
        lines.Add line

    let lines = Seq.toArray lines
    let links =
        lines
        |> Array.filter (fun x -> x.Contains "&mime=audio")

    for vLink in links do
        log.InfoFormat( "Youtube audio link detected: {0}", vLink)
    return links
}


let getYoutubeLink youtubeURL : Task<string []> = task {
    match youtubeLinks.TryGetValue youtubeURL with
    | true, vlinks -> return vlinks
    | _ ->
        let! vlinks = discoverYoutubeLink youtubeURL
        youtubeLinks.AddOrUpdate(youtubeURL,vlinks,Func<_,_,_>(fun _ _ -> vlinks)) |> ignore
        return vlinks
}

let getMusikPlayerProcesses() = Process.GetProcessesByName("omxplayer.bin")

let stop () = task {
    globalStop <- true
    for p in getMusikPlayerProcesses() do
        if not p.HasExited then
            log.InfoFormat "stopping omxplaxer"
            try p.Kill() with _ -> log.WarnFormat "couldn't kill omxplayer"
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
            let! _ = stop ()
            log.InfoFormat "Musik stopped"
        }
    | TagAction.PlayMusik url ->
        task {
            let! _ = stop ()
            globalStop <- false
            currentTask <- play [|url|]
        }
    | TagAction.PlayYoutube youtubeURL ->
        task {
            let! _ = stop ()
            log.InfoFormat( "Playing Youtube {0}", youtubeURL)
            let! vlinks = getYoutubeLink youtubeURL
            globalStop <- false
            currentTask <- play vlinks
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
            log.InfoFormat( "Object: {0}", tag.Object)
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

let mutable nextFirmwareCheck = DateTimeOffset.MinValue

let firmwareTarget = System.IO.Path.GetFullPath "/home/pi/firmware"

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
            nextFirmwareCheck <- DateTimeOffset.UtcNow.Add firmwareUpdateInterval
            if firmware.Version < ReleaseNotes.Version then
                let localFileName = System.IO.Path.GetTempFileName().Replace(".tmp", ".zip")
                log.InfoFormat("Starting download of {0}", firmware.Url)
                do! webClient.DownloadFileTaskAsync(firmware.Url,localFileName)
                log.InfoFormat "Download done."

                if System.IO.Directory.Exists firmwareTarget then
                    System.IO.Directory.Delete(firmwareTarget,true)
                System.IO.Directory.CreateDirectory(firmwareTarget) |> ignore
                System.IO.Compression.ZipFile.ExtractToDirectory(localFileName, firmwareTarget)
                System.IO.File.Delete localFileName
                runFirmwareUpdate()
                while true do
                    log.InfoFormat "Running firmware update."
                    do! Task.Delay 3000
                    ()
            else
                if System.IO.Directory.Exists firmwareTarget then
                    System.IO.Directory.Delete(firmwareTarget,true)
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

let discoverAllYoutubeLinks () = task {
    try
        use webClient = new System.Net.WebClient()
        let url = sprintf  @"%s/api/usertags/%s" tagServer userID
        let! result = webClient.DownloadStringTaskAsync(System.Uri url)

        match Decode.fromString TagList.Decoder result with
        | Error msg -> return failwith msg
        | Ok list ->
            for tag in list.Tags do
                match tag.Action with
                | TagAction.PlayYoutube youtubeURL ->
                    let! _ = getYoutubeLink youtubeURL
                    ()
                | _ -> ()
    with
    | exn ->
        log.ErrorFormat("Youtube discovering error: {0}", exn.Message)
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

let youtubeDiscoverer = discoverAllYoutubeLinks ()

let mutable running = null

let nodeServices = app.Services.GetService(typeof<INodeServices>) :?> INodeServices


let rfidLoop() = task {
    while true do
        let! token = nodeServices.InvokeExportAsync<string>("./read-tag", "read", "tag")

        if String.IsNullOrEmpty token then
            if nextFirmwareCheck < DateTimeOffset.UtcNow then
                let! _ = checkFirmware()
                ()
            else
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
                    // recheck in 2 seconds to make it a bit more stable
                    let! _ = Task.Delay(TimeSpan.FromSeconds 2.)
                    let! newToken = nodeServices.InvokeExportAsync<string>("./read-tag", "read", "tag")
                    if newToken <> token then
                        log.InfoFormat("RFID/NFC {0} was removed from reader", token)
                        waiting <- false

            let! _ = stop ()
            ()
}

rfidLoop().Wait()