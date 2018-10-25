open System.IO
open Microsoft.Extensions.DependencyInjection
open Giraffe
open Saturn
open FSharp.Control.Tasks.ContextInsensitive
open ServerCore.Domain
open Thoth.Json.Net
open ServerCode.Storage
open Microsoft.WindowsAzure.Storage.Blob
open System
open System.Threading
open System.Threading.Tasks
open Giraffe.WebSocket
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting

#if DEBUG
let publicPath = Path.GetFullPath "../Client/public"
#else
let publicPath = Path.GetFullPath "client"
#endif

let getSASMediaLink mediaID =  task {
    let connection = AzureTable.connection
    let blobClient = connection.CreateCloudBlobClient()
    let mediaContainer = blobClient.GetContainerReference("media")
    let! _x = mediaContainer.CreateIfNotExistsAsync()
    let blockBlob = mediaContainer.GetBlockBlobReference(mediaID)
    let policy = SharedAccessBlobPolicy()
    policy.SharedAccessExpiryTime <- Nullable(DateTimeOffset.UtcNow.AddMinutes 100.)
    policy.SharedAccessStartTime <- Nullable(DateTimeOffset.UtcNow)
    policy.Permissions <- SharedAccessBlobPermissions.Read
    let sas = blockBlob.GetSharedAccessSignature(policy)

    return blockBlob.Uri.ToString() + sas
}

let uploadMusik (stream:Stream) = task {
    let connection = AzureTable.connection
    let mediaID = System.Guid.NewGuid()

    let blobClient = connection.CreateCloudBlobClient()
    let mediaContainer = blobClient.GetContainerReference "media"
    let! _ = mediaContainer.CreateIfNotExistsAsync()

    let blockBlob = mediaContainer.GetBlockBlobReference(mediaID.ToString())
    blockBlob.Properties.ContentType <- "audio/mpeg"
    do! blockBlob.UploadFromStreamAsync(stream)
    return TagAction.PlayBlobMusik mediaID
}

let mapBlobMusikTag (tag:Tag) = task {
    match tag.Action with
    | TagAction.PlayBlobMusik mediaID ->
        let! sas = getSASMediaLink(mediaID.ToString())
        return { tag with Action = TagAction.PlayMusik sas }
    | _ -> return tag
}

let uploadEndpoint (token:string) =
    pipeline {
        set_header "Content-Type" "application/json"
        plug (fun next ctx -> task {
            if not ctx.Request.HasFormContentType then
                return! RequestErrors.BAD_REQUEST "bad request" next ctx
            else
                let formFeature = ctx.Features.Get<Microsoft.AspNetCore.Http.Features.IFormFeature>()
                let! form = formFeature.ReadFormAsync(CancellationToken.None)
                let file = form.Files.[0]
                use stream = file.OpenReadStream()
                let! tagAction = uploadMusik stream
                let tag = { Token = System.Guid.NewGuid().ToString(); Action = tagAction; Description = ""; Object = "" }
                let! _saved = AzureTable.saveTag token tag
                let! tag = mapBlobMusikTag tag
                let txt = Tag.Encoder tag |> Encode.toString 0
                return! setBodyFromString txt next ctx
        })
    }

let tagEndpoint (userID,token) =
    pipeline {
        set_header "Content-Type" "application/json"
        plug (fun next ctx -> task {
            let! _ = AzureTable.saveRequest userID token
            let! tag = AzureTable.getTag userID token
            let! tag =
                match tag with
                | Some t -> mapBlobMusikTag t
                | _ -> task { return { Token = token; Action = TagAction.UnknownTag; Description = ""; Object = "" } }

            let txt = Tag.Encoder tag |> Encode.toString 0
            return! setBodyFromString txt next ctx
        })
    }

let allTagsEndpoint userID =
    pipeline {
        set_header "Content-Type" "application/json"
        plug (fun next ctx -> task {
            let! tags = AzureTable.getAllTagsForUser userID
            let txt = TagList.Encoder { Tags = tags } |> Encode.toString 0
            return! setBodyFromString txt next ctx
        })
    }

let startupEndpoint =
    pipeline {
        set_header "Content-Type" "application/json"
        plug (fun next ctx -> task {
            let! sas = getSASMediaLink "d97cdddb-8a19-4690-8ba5-b8ea43d3641f"
            let actions = [TagAction.PlayMusik sas]

            let txt =
                actions
                |> List.map TagAction.Encoder
                |> Encode.list
                |> Encode.toString 0
            return! setBodyFromString txt next ctx
        })
    }

let firmwareLink = sprintf "https://github.com/forki/Audio/releases/download/%s/PiFirmware.%s.zip" ReleaseNotes.Version ReleaseNotes.Version

let getLatestFirmware next ctx = redirectTo false firmwareLink next ctx

let firmwareEndpoint =
    pipeline {
        set_header "Content-Type" "application/json"
        plug (fun next ctx -> task {
            let current = {
                Version = ReleaseNotes.Version
                Url = firmwareLink
            }

            let txt = Firmware.Encoder current |> Encode.toString 0
            return! setBodyFromString txt next ctx
        })
    }


let tagHistoryBroadcaster = ConnectionManager()

let t = task {
    while true do
        let body = Encode.Auto.toString(0, TagHistorySocketEvent.ToDo)
        do! tagHistoryBroadcaster.BroadcastTextAsync(body,key = "9bb2b109-bf08-4342-9e09-f4ce3fb01c0f")
        do! Task.Delay 10000
}

let webApp =
    router {
        getf "/api/tags/%s/%s" tagEndpoint
        getf "/api/usertags/%s" allTagsEndpoint
        postf "/api/upload/%s" uploadEndpoint
        get "/api/startup" startupEndpoint
        get "/api/firmware" firmwareEndpoint
        get "/api/latestfirmware" getLatestFirmware
        getf "/api/taghistorysocket/%s" (TagHistorySocket.openSocket tagHistoryBroadcaster)
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

let configureApp (app : IApplicationBuilder) =
    app.UseWebSockets(Giraffe.WebSocket.DefaultWebSocketOptions)
    |> ignore

let app' = app.Configure(Action<IApplicationBuilder> configureApp) 
run app'
