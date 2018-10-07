open System.IO
open Microsoft.Extensions.DependencyInjection
open Giraffe
open Saturn
open FSharp.Control.Tasks.ContextInsensitive
open ServerCore.Domain
open System.Threading.Tasks
open Thoth.Json.Net
open ServerCode.Storage
open Microsoft.WindowsAzure.Storage.Blob
open System


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

let uploadMusik () = task {
    let connection = AzureTable.connection
    let mediaID = System.Guid.NewGuid()

    use stream = new MemoryStream()
    // todo: write to stream

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

let tagEndpoint (userID,token) =
    pipeline {
        set_header "Content-Type" "application/json"
        plug (fun next ctx -> task {
            
            let! tag = AzureTable.getTag userID token
            let! tag =
                match tag with
                | Some t -> mapBlobMusikTag t
                | _ -> task { return { Token = token; Action = TagAction.UnknownTag } }

            let txt = Tag.Encoder tag |> Encode.toString 0
            return! setBodyFromString txt next ctx
        })
    }


let allTagsEndpoint userID =
    pipeline {
        set_header "Content-Type" "application/json"
        plug (fun next ctx -> task {
            let! tags = AzureTable.getAllTagsForUser userID
            let! tags =
                tags
                |> Array.map mapBlobMusikTag
                |> Task.WhenAll
                
            let txt = TagList.Encoder { Tags = tags } |> Encode.toString 0
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
