open System.IO
open Microsoft.Extensions.DependencyInjection
open Giraffe
open Saturn
open FSharp.Control.Tasks.ContextInsensitive
open ServerCore.Domain

open Thoth.Json.Net
open ServerCode.Storage
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.Primitives


#if DEBUG
let publicPath = Path.GetFullPath "../Client/public"
#else
let publicPath = Path.GetFullPath "client"
#endif

let allTagsEndpoint userID =
    pipeline {
        set_header "Content-Type" "application/json"
        plug (fun next ctx -> task {
            let! tags = AzureTable.getAllTagsForUser userID
            let txt = TagList.Encoder { Tags = tags } |> Encode.toString 0
            return! setBodyFromString txt next ctx
        })
    }


let audioStream (stream : Stream) : HttpHandler =
    fun (next : HttpFunc) (ctx : HttpContext) ->
        task {
            let l = int stream.Length
            stream.Position <- int64 0
            ctx.Response.Headers.["Content-Length"] <- StringValues(l.ToString())
            do! stream.CopyToAsync(ctx.Response.Body)
            return! next ctx
        }

let blobMusikEndpoint mediaID =
    pipeline {
        set_header "Content-Type" "audio/mpeg"
        plug (fun next ctx -> task {
            let connection = AzureTable.connection
            let blobClient = connection.CreateCloudBlobClient()
            let mediaContainer = blobClient.GetContainerReference("media")
            let! _x = mediaContainer.CreateIfNotExistsAsync()
            let blockBlob = mediaContainer.GetBlockBlobReference(mediaID)
            use stream = new MemoryStream()
            do! blockBlob.DownloadToStreamAsync(stream)
            return! Successful.ok (audioStream stream) next ctx
        })
    }

let tagEndpoint (userID,token) =
    pipeline {
        set_header "Content-Type" "application/json"
        plug (fun next ctx -> task {
            
            let! tag = AzureTable.getTag userID token
            let tag =
                match tag with
                | Some t -> t
                | _ -> { Token = token; Action = TagAction.UnknownTag }

            let txt = Tag.Encoder tag |> Encode.toString 0
            return! setBodyFromString txt next ctx
        })
    }


let startupEndpoint =
    pipeline {
        set_header "Content-Type" "application/json"
        plug (fun next ctx -> task {
            let actions = [TagAction.PlayBlobMusik (System.Guid "d97cdddb-8a19-4690-8ba5-b8ea43d3641f")]
            
            let txt = 
                actions
                |> List.map TagAction.Encoder
                |> Encode.list
                |> Encode.toString 0
            return! setBodyFromString txt next ctx
        })
    }

    
let firmwareEndpoint =
    pipeline {
        set_header "Content-Type" "application/json"
        plug (fun next ctx -> task {
            let current = { 
                Version = ReleaseNotes.Version
                Url = sprintf "https://github.com/forki/Audio/releases/download/%s/PiFirmware.%s.zip" ReleaseNotes.Version ReleaseNotes.Version
            }
            
            let txt = Firmware.Encoder current |> Encode.toString 0
            return! setBodyFromString txt next ctx
        })
    }

let webApp =
    router {
        getf "/api/audio/%s" blobMusikEndpoint
        getf "/api/tags/%s/%s" tagEndpoint
        getf "/api/usertags/%s" allTagsEndpoint
        get "/api/startup" startupEndpoint
        get "/api/firmware" firmwareEndpoint
    }

let configureSerialization (services:IServiceCollection) =
    services

let app = application {
    url ("http://0.0.0.0:" + URLs.serverPort.ToString() + "/")
    use_router webApp
    memory_cache
    use_static publicPath
    service_config configureSerialization
    use_gzip
}

run app
