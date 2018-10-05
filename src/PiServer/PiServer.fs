open Microsoft.Extensions.DependencyInjection
open Giraffe
open Saturn
open FSharp.Control.Tasks.ContextInsensitive

open Microsoft.AspNetCore.NodeServices
open System.Runtime.InteropServices
open Thoth.Json.Net
open Shared
open System.Threading.Tasks

let tagServer = "https://audio-hub.azurewebsites.net"
// let tagServer = "http://localhost:8085"

let port = 8086us

let isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)


let play (nodeServices : INodeServices) (uri:string) = task {
    use webClient = new System.Net.WebClient()
    if isWindows then
        let localFileName = System.IO.Path.GetTempFileName().Replace(".tmp", ".mp3")
        printfn "Starting download of %s" uri
        do! webClient.DownloadFileTaskAsync(uri,localFileName)
        printfn "Playing %s" localFileName
        let! r = nodeServices.InvokeExportAsync<string>("./play-audio", "play", localFileName)
        printfn "Done playing %s" localFileName
        System.IO.File.Delete localFileName
        return r
    else
        printfn "Playing %s" uri
        let! r = nodeServices.InvokeExportAsync<string>( "./play-audiostream", "play", uri)
        printfn "Done playing %s" uri
        return r
}

let stop (nodeServices : INodeServices) = task {
    if isWindows then
        let! r = nodeServices.InvokeExportAsync<string>("./play-audio", "stop")
        return r
    else
        let! r = nodeServices.InvokeExportAsync<string>("./play-audiostream", "stop")
        return r
}

let mutable currentTask = null

let executeAction (nodeServices : INodeServices) (action:TagAction) =
    match action with
    | TagAction.UnknownTag -> 
        task { 
            return "Unknown Tag"
        }
    | TagAction.StopMusik -> 
        task {
            let! _ = stop nodeServices
            return "Stopped"
        }
    | TagAction.PlayMusik url -> 
        task {
            let! r = stop nodeServices
            currentTask <- play nodeServices url
            return (sprintf "Playing %s" url)
        }


let executeTag (nodeServices : INodeServices) (tag:string) = task {
    use webClient = new System.Net.WebClient()
    let tagsUrl tag = sprintf @"%s/api/tags/%s" tagServer tag
    let! result = webClient.DownloadStringTaskAsync(System.Uri (tagsUrl tag))
    
    match Decode.fromString Tag.Decoder result with
    | Error msg -> return failwith msg
    | Ok tag -> return! executeAction nodeServices tag.Action
}

let checkFirmware () = task {
    use webClient = new System.Net.WebClient()
    System.Net.ServicePointManager.SecurityProtocol <- 
        System.Net.ServicePointManager.SecurityProtocol ||| 
          System.Net.SecurityProtocolType.Tls11 |||
          System.Net.SecurityProtocolType.Tls12
            
    let url = sprintf @"%s/api/firmware" tagServer
    let! result = webClient.DownloadStringTaskAsync(System.Uri url)
    
    match Decode.fromString Firmware.Decoder result with
    | Error msg -> return failwith msg
    | Ok firmware ->
        try
            if firmware.Version <> ReleaseNotes.Version then
                let localFileName = System.IO.Path.GetTempFileName().Replace(".tmp", ".zip")
                printfn "Starting download of %s" firmware.Url
                do! webClient.DownloadFileTaskAsync(firmware.Url,localFileName)
                printfn "Download done."
                System.IO.Compression.ZipFile.ExtractToDirectory(localFileName, System.IO.Path.GetFullPath "./install")
                System.IO.File.Delete localFileName
                return Some firmware.Version
            else
                return None
        with
        | _ -> return None
}

let executeStartupActions (nodeServices : INodeServices) = task {
    use webClient = new System.Net.WebClient()
    let url = sprintf @"%s/api/startup" tagServer
    let! result = webClient.DownloadStringTaskAsync(System.Uri url)
    
    match Decode.fromString (Decode.list TagAction.Decoder) result with
    | Error msg -> return failwith msg
    | Ok actions ->
        return!
            actions
            |> List.map (executeAction nodeServices)
            |> Task.WhenAll
}

let webApp = router {
    getf "/execute/%s" (fun tag next ctx -> task {
        let nodeServices = ctx.RequestServices.GetService(typeof<INodeServices>) :?> INodeServices
        let! r = executeTag nodeServices tag 
        return! text r next ctx
    })
}

let configureSerialization (services:IServiceCollection) =
    services.AddNodeServices(fun x -> x.InvocationTimeoutMilliseconds <- 2 * 60 * 60 * 1000)
    services

let app = application {
    url ("http://0.0.0.0:" + port.ToString() + "/")
    use_router webApp
    memory_cache
    service_config configureSerialization
    use_gzip
}

let x = app.Build()
x.Start()

printfn "Server started"

let firmwareCheck = checkFirmware()
firmwareCheck.Wait()
let r = firmwareCheck.Result

let service = x.Services.GetService(typeof<INodeServices>) :?> INodeServices

let t = executeStartupActions service
t.Wait()
printfn "%A" t.Result

System.Console.ReadKey() |> ignore