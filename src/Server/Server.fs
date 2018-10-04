open System.IO
open Microsoft.Extensions.DependencyInjection
open Giraffe
open Saturn
open FSharp.Control.Tasks.ContextInsensitive
open Shared

let publicPath = Path.GetFullPath "../Client/public"
let audioPath = Path.GetFullPath "../../audio"
let port = 8085us

let webApp = router {
    getf "/api/audio/mp3/%s" (fun fileName next ctx -> task {
        let file = Path.Combine(audioPath,sprintf "mp3/%s.mp3" fileName)
        return! ctx.WriteFileStreamAsync true file None None 
    })

    getf "/api/tags/%s"  (fun (tag:string) next ctx -> task {
        match tag with
        | "celeb" ->
            let txt = sprintf @"http://localhost:%d/api/audio/mp3/%s" port "Celebrate"
            return! text txt next ctx
        | _ -> return! failwithf "Unknown tag %s" tag 
    })

    get "/api/alltags" (fun next ctx -> task {
        let tags = { Tags = [| { Token = "celeb" }|] } 
        let txt = TagList.Encoder tags
        return! json tags next ctx
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
