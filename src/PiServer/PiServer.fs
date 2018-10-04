open System.IO
open Microsoft.Extensions.DependencyInjection
open Giraffe
open Saturn
open FSharp.Control.Tasks.ContextInsensitive

open Microsoft.AspNetCore.NodeServices
open System.Runtime.InteropServices

let tagServer = "http://localhost:8085"

let port = 8086us


let webClient = new System.Net.WebClient()
let isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)

let mutable runningTask = null

let play (nodeServices : INodeServices) (uri:string) = task {
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
        printfn "Stopped"
        return r
    else
        let! r = nodeServices.InvokeExportAsync<string>("./play-audiostream", "stop")
        printfn "Stopped"
        return r
}


let executeTag (nodeServices : INodeServices) (tag:string) = task {
    let tagsUrl tag = sprintf @"%s/api/tags/%s" tagServer tag
    let! url = webClient.DownloadStringTaskAsync(System.Uri (tagsUrl tag))
    let! _ = stop nodeServices
    runningTask <- play nodeServices url
    return url
}

let webApp = router {
    getf "/play/%s" (fun tag next ctx -> task {
        let service = ctx.RequestServices.GetService(typeof<INodeServices>) :?> INodeServices
        let! r = executeTag service tag 
        return! text r next ctx
    })

    get "/stop" (fun next ctx -> task {
        let service = ctx.RequestServices.GetService(typeof<INodeServices>) :?> INodeServices
        let! r = stop service
        let txt = sprintf "Stopped"
        return! text txt next ctx
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

// let x = app.Build()
// x.Start()

// printfn "Started"

// let service = x.Services.GetService(typeof<INodeServices>) :?> INodeServices

// let t = executeTag service "celeb"
// t.Wait()

run app