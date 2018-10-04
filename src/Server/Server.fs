open System.IO
open Microsoft.Extensions.DependencyInjection
open Giraffe
open Saturn
open FSharp.Control.Tasks.ContextInsensitive


let publicPath = Path.GetFullPath "../Client/public"
let port = 8085us

let webApp = router {
    getf "/audio/mp3/%s" (fun fileName next ctx -> task {
        let file = sprintf @"C:\code\Audio\audio\mp3\%s.mp3" fileName
        return! ctx.WriteFileStreamAsync true file None None 
    })

    getf "/tags/%s"  (fun (tag:string) next ctx -> task {
        match tag with
        | "celeb" ->
            let txt = sprintf @"http://localhost:%d/audio/mp3/%s" port "Celebrate"
            return! text txt next ctx
        | _ -> return! failwithf "Unknown tag %s" tag 
    })
        
}

let configureSerialization (services:IServiceCollection) =
   services

let app = application {
    url ("http://0.0.0.0:" + port.ToString() + "/")
    use_router webApp
    memory_cache
    use_static publicPath
    service_config configureSerialization
    use_gzip
}

run app
