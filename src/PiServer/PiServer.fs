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
open System.Diagnostics
open System.Xml
open System.Reflection
open GeneralIO

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

let mutable currentAudio = 0
let mutable taskID = Guid.NewGuid().ToString()

let getMusikPlayerProcesses() = Process.GetProcessesByName("omxplayer.bin")

let killMusikPlayer() = task {
    for p in getMusikPlayerProcesses() do
        if not p.HasExited then
            try
                log.InfoFormat "stopping omxplaxer"
                let killP = new System.Diagnostics.Process()
                let startInfo = System.Diagnostics.ProcessStartInfo()
                startInfo.FileName <- "sudo"
                startInfo.Arguments <- "kill -9 " + p.Id.ToString()
                killP.StartInfo <- startInfo
                let _ = killP.Start()

                while not killP.HasExited do
                    do! Task.Delay 10
                log.InfoFormat "stopped"
            with _ -> log.WarnFormat "couldn't kill omxplayer"
}


let youtubeLinks = System.Collections.Concurrent.ConcurrentDictionary<_,_>()


let play (myTaskID:string) (uri:string) = task {
    currentAudio <- 0
    let mutable uris = 
        match youtubeLinks.TryGetValue uri with
        | true, links -> links
        | _ -> [| uri |]
    log.InfoFormat( "Playing with TaskID: {0}: Files: {1}", myTaskID, uris.Length)
    
    while myTaskID = taskID && currentAudio >= 0 && currentAudio < uris.Length do
        log.InfoFormat( "Playing audio file {0} / {1}", currentAudio + 1, uris.Length)
        let mediaFile = uris.[currentAudio]
        let p = new System.Diagnostics.Process()
        runningProcess <- p
        let startInfo = System.Diagnostics.ProcessStartInfo()
        startInfo.FileName <- "omxplayer"
        startInfo.Arguments <- mediaFile
        p.StartInfo <- startInfo
        do! Task.Delay 100
        let _ = p.Start()

        while currentAudio >= 0 && not p.HasExited do
            do! Task.Delay 100

        currentAudio <- currentAudio + 1
        uris <-
            match youtubeLinks.TryGetValue uri with
            | true, links -> links
            | _ -> [| uri |]
}


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
        log.InfoFormat("Youtube audio link detected: {0}", vLink)
    return links
}


let getYoutubeLink youtubeURL = task {
    match youtubeLinks.TryGetValue youtubeURL with
    | true,_ -> ()
    | _ ->
        let! vlinks = discoverYoutubeLink youtubeURL
        youtubeLinks.AddOrUpdate(youtubeURL,vlinks,Func<_,_,_>(fun _ _ -> vlinks)) |> ignore
}


let stop () = task {
    taskID <- Guid.NewGuid().ToString()
    currentAudio <- -100
    do! killMusikPlayer()
}

let next () = task {
    log.InfoFormat "Next button pressed"
    currentAudio <- currentAudio + 1
    do! killMusikPlayer()
}

let previous () = task {
    log.InfoFormat "Previous button pressed"
    currentAudio <- max -1 (currentAudio - 2)
    do! killMusikPlayer()
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
            do! stop()
            taskID <- Guid.NewGuid().ToString()
            currentTask <- play taskID url 
        }
    | TagAction.PlayYoutube youtubeURL ->
        task {
            do! getYoutubeLink youtubeURL
            log.InfoFormat( "Playing Youtube {0}", youtubeURL)
            do! stop()
            taskID <- Guid.NewGuid().ToString()
            currentTask <- play taskID youtubeURL
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
            log.InfoFormat("Latest firmware on server: {0}", firmware.Version)
            let serverVersion = Paket.SemVer.Parse firmware.Version
            let localVersion = Paket.SemVer.Parse ReleaseNotes.Version
            if serverVersion > localVersion then
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
            log.InfoFormat("Actions: {0}", sprintf "%A" actions)
            for t in actions do
                let! _ = executeAction t
                ()
    with
    | exn ->
        log.ErrorFormat("Startup error: {0}", exn.Message)
}

let discoverAllYoutubeLinks () = task {
    while true do
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
                        let! vlinks = discoverYoutubeLink youtubeURL
                        youtubeLinks.AddOrUpdate(youtubeURL,vlinks,Func<_,_,_>(fun _ _ -> vlinks)) |> ignore
                    | _ -> ()
        with
        | exn ->
            log.ErrorFormat("Youtube discovering error: {0}", exn.Message)
        do! Task.Delay(TimeSpan.FromMinutes 60.)
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
    use _nextButton = new Button(Unosquare.RaspberryIO.Pi.Gpio.Pin07, fun () -> next() |> Async.AwaitTask |> Async.RunSynchronously)
    use _previousButton = new Button(Unosquare.RaspberryIO.Pi.Gpio.Pin01, fun () -> previous() |> Async.AwaitTask |> Async.RunSynchronously)
    use _nextButton2 = new Button(Unosquare.RaspberryIO.Pi.Gpio.Pin26, fun () -> next() |> Async.AwaitTask |> Async.RunSynchronously)
    use _previousButton2 = new Button(Unosquare.RaspberryIO.Pi.Gpio.Pin25, fun () -> previous() |> Async.AwaitTask |> Async.RunSynchronously)
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