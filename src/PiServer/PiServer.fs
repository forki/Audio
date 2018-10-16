open Microsoft.Extensions.DependencyInjection
open Giraffe
open Saturn
open FSharp.Control.Tasks.ContextInsensitive
open System
open Microsoft.AspNetCore.NodeServices
open System.Runtime.InteropServices
open Thoth.Json.Net
open ServerCore.Domain
open System.Threading.Tasks
open System.Threading
open System.Diagnostics

let tagServer = "https://audio-hub.azurewebsites.net"
// let tagServer = "http://localhost:8085"

let userID = "9bb2b109-bf08-4342-9e09-f4ce3fb01c0f"


let port = 8086us

let cts = new CancellationTokenSource()
let mutable runningProcess = null

let play  (cancellationToken:CancellationToken) (uri:string) = task {
    printfn "Playing %s" uri
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
        startInfo.Arguments <- uri
        p.StartInfo <- startInfo
        let _ = p.Start()
        let! _ = tcs.Task
        return "Started"
    finally
        p.Exited.RemoveHandler handler
}

let getMusikPlayerProcesses() = Process.GetProcessesByName("omxplayer.bin")

let stop() = task {
    printfn "trying to kill"
    for p in getMusikPlayerProcesses() do
        if not p.HasExited then
            printfn "kill"
            p.Kill()
    cts.Cancel()
    return "Test"
}

let mutable currentTask = null

let executeAction (action:TagAction) =
    match action with
    | TagAction.UnknownTag ->
        task {
            return "Unknown Tag"
        }
    | TagAction.StopMusik ->
        task {
            let! _ = stop()
            return "Stopped"
        }
    | TagAction.PlayMusik url ->
        task {
            let! r = stop()
            currentTask <- play cts.Token url
            return (sprintf "Playing %s" url)
        }
    | TagAction.PlayBlobMusik _ ->
        failwithf "Blobs links need to be converted to direct links by the tag server"


let executeTag (tag:string) = task {
    use webClient = new System.Net.WebClient()
    let tagsUrl tag = sprintf @"%s/api/tags/%s/%s" tagServer userID tag
    let! result = webClient.DownloadStringTaskAsync(System.Uri (tagsUrl tag))

    match Decode.fromString Tag.Decoder result with
    | Error msg -> return failwith msg
    | Ok tag -> return! executeAction tag.Action
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
        printfn "Decoder error: %s" msg
        return failwith msg
    | Ok firmware ->
        try
            if firmware.Version <> ReleaseNotes.Version then
                let localFileName = System.IO.Path.GetTempFileName().Replace(".tmp", ".zip")
                printfn "Starting download of %s" firmware.Url
                do! webClient.DownloadFileTaskAsync(firmware.Url,localFileName)
                printfn "Download done."
                let target = System.IO.Path.GetFullPath "/home/pi/firmware"
                if System.IO.Directory.Exists target then
                    System.IO.Directory.Delete(target,true)
                System.IO.Directory.CreateDirectory(target) |> ignore
                System.IO.Compression.ZipFile.ExtractToDirectory(localFileName, target)
                System.IO.File.Delete localFileName
                runFirmwareUpdate()
                while true do
                    printfn "Running firmware update."
                    do! Task.Delay 3000
                    ()
            else
                printfn "Firmware %s is uptodate." ReleaseNotes.Version
        with
        | exn ->
            printfn "Upgrade error: %s" exn.Message
}

let executeStartupActions () = task {
    use webClient = new System.Net.WebClient()
    let url = sprintf @"%s/api/startup" tagServer
    let! result = webClient.DownloadStringTaskAsync(System.Uri url)

    match Decode.fromString (Decode.list TagAction.Decoder) result with
    | Error msg -> return failwith msg
    | Ok actions ->
        for t in actions do
            let! _ = executeAction t
            ()
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

printfn "Server started"

let firmwareCheck = checkFirmware()
firmwareCheck.Wait()

let startupTask = executeStartupActions()
startupTask.Wait()

printfn "Startup: %A" startupTask.Result
let mutable running = null

let nodeServices = app.Services.GetService(typeof<INodeServices>) :?> INodeServices

let rfidLoop() = task {
    while true do
        let! token = nodeServices.InvokeExportAsync<string>("./read-tag", "read", "tag")

        if String.IsNullOrEmpty token then
            let! _ = Task.Delay(TimeSpan.FromSeconds 0.5)
            ()
        else
            printfn "Read: %s" token
            running <- executeTag token
            let mutable waiting = true
            while waiting do
                do! Task.Delay(TimeSpan.FromSeconds 0.5)
                if running.IsCompleted then
                    for p in getMusikPlayerProcesses() do
                        if p.HasExited then
                            printfn "omxplayer was shut down"
                            waiting <- false
                let! newToken = nodeServices.InvokeExportAsync<string>("./read-tag", "read", "tag")
                if newToken <> token then
                    printfn "tag was removed from reader"
                    waiting <- false

            let! _ = stop()
            ()
}

rfidLoop().Wait()