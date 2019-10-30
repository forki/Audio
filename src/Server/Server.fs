module Server

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

#if DEBUG
let publicPath = Path.GetFullPath "../Client/public"
#else
let publicPath = Path.GetFullPath "client"
#endif

let getSASMediaLink mediaID = task {
    let connection = AzureTable.connection
    let blobClient = connection.Force().CreateCloudBlobClient()
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

    let blobClient = connection.Force().CreateCloudBlobClient()
    let mediaContainer = blobClient.GetContainerReference "media"
    let! _ = mediaContainer.CreateIfNotExistsAsync()

    let blockBlob = mediaContainer.GetBlockBlobReference(mediaID.ToString())
    blockBlob.Properties.ContentType <- "audio/mpeg"
    do! blockBlob.UploadFromStreamAsync(stream)
    return TagAction.PlayBlobMusik [|mediaID|]
}

let mapBlobMusikTag (tag:Tag) = task {
    match tag.Action with
    | TagAction.PlayBlobMusik mediaIDs ->
        let list = System.Collections.Generic.List<_>()
        for mediaID in mediaIDs do
            let! sas = getSASMediaLink(mediaID.ToString())
            list.Add sas
        return { tag with Action = TagAction.PlayMusik(Seq.toArray list) }
    | _ -> return tag
}

let uploadEndpoint (userID:string) =
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
                let tag : Tag = {
                    Token = System.Guid.NewGuid().ToString()
                    Action = tagAction
                    Description = ""
                    UserID = userID
                    Object = "" }
                let! _saved = AzureTable.saveTag tag
                let! tag = mapBlobMusikTag tag
                let txt = Tag.Encoder tag |> Encode.toString 0
                return! setBodyFromString txt next ctx
        })
    }


let previousFileEndpoint (userID,token) =
    pipeline {
        set_header "Content-Type" "application/json"
        plug (fun next ctx -> task {
            let! _ = AzureTable.saveRequest userID token
            match! AzureTable.getUser userID with
            | None ->
                return! Response.notFound ctx userID
            | Some user ->
                let! tag = AzureTable.getTag userID token
                let! position = AzureTable.getPlayListPosition userID token
                let position = position |> Option.map (fun p -> p.Position + 1) |> Option.defaultValue 0
                let! _ = AzureTable.savePlayListPosition userID token position
                let! tag =
                    match tag with
                    | Some t -> mapBlobMusikTag t
                    | _ ->
                        let t = {
                            Token = token
                            UserID = userID
                            Action = TagAction.UnknownTag
                            Description = ""
                            Object = "" }
                        task { return t }

                let tag : TagForBox = {
                    Token = tag.Token
                    Object = tag.Object
                    Description = tag.Description
                    Action = TagActionForBox.GetFromTagAction(tag.Action,position) }

                let txt = TagForBox.Encoder tag |> Encode.toString 0
                return! setBodyFromString txt next ctx
        })
    }


let nextFileEndpoint (userID,token) =
    pipeline {
        set_header "Content-Type" "application/json"
        plug (fun next ctx -> task {
            let! _ = AzureTable.saveRequest userID token
            match! AzureTable.getUser userID with
            | None ->
                return! Response.notFound ctx userID
            | Some user ->
                let! tag = AzureTable.getTag userID token

                let! position = AzureTable.getPlayListPosition userID token
                let position = position |> Option.map (fun p -> p.Position - 1) |> Option.defaultValue 0

                let! tag =
                    match tag with
                    | Some t -> mapBlobMusikTag t
                    | _ ->
                        let t = {
                            Token = token
                            UserID = userID
                            Action = TagAction.UnknownTag
                            Description = ""
                            Object = "" }
                        task { return t }

                let! _ = AzureTable.savePlayListPosition userID token position

                let tag : TagForBox = {
                    Token = tag.Token
                    Object = tag.Object
                    Description = tag.Description
                    Action = TagActionForBox.GetFromTagAction(tag.Action,position) }

                let txt = TagForBox.Encoder tag |> Encode.toString 0
                return! setBodyFromString txt next ctx
        })
    }

let userTagsEndPoint userID =
    pipeline {
        set_header "Content-Type" "application/json"
        plug (fun next ctx -> task {
            let! tags = AzureTable.getAllTagsForUser userID
            let txt = TagList.Encoder { Tags = tags } |> Encode.toString 0
            return! setBodyFromString txt next ctx
        })
    }


let volumeUpEndpoint userID =
    pipeline {
        plug (fun next ctx -> task {
            match! AzureTable.getUser userID with
            | None ->
                return! Response.notFound ctx userID
            | Some user ->
                let logger = ctx.GetLogger "VolumeUp"
                return! Response.ok ctx userID
        })
    }

let volumeDownEndpoint userID =
    pipeline {
        plug (fun next ctx -> task {
            match! AzureTable.getUser userID with
            | None ->
                return! Response.notFound ctx userID
            | Some user ->
                let logger = ctx.GetLogger "VolumeDown"
                return! Response.ok ctx userID
        })
    }

let historyEndPoint userID =
    pipeline {
        set_header "Content-Type" "application/json"
        plug (fun next ctx -> task {
            let! requests = AzureTable.getAllRequestsForUser userID
            let txt = RequestList.Encoder { Requests = requests } |> Encode.toString 0
            return! setBodyFromString txt next ctx
        })
    }

let startupEndpoint userID =
    pipeline {
        set_header "Content-Type" "application/json"
        plug (fun next ctx -> task {
            match! AzureTable.getUser userID with
            | None ->
                return! Response.notFound ctx userID
            | Some user ->
                let! sas = getSASMediaLink "d97cdddb-8a19-4690-8ba5-b8ea43d3641f"

                let txt =
                    TagActionForBox.PlayMusik sas
                    |> TagActionForBox.Encoder
                    |> Encode.toString 0
                return! setBodyFromString txt next ctx
        })
    }

let firmwareLink = @"https://audiohubstorage.blob.core.windows.net/firmware/PiFirmware.1.8.0.zip?st=2019-10-30T20%3A02%3A09Z&se=2019-10-31T20%3A02%3A09Z&sp=rl&sv=2018-03-28&sr=b&sig=mfrAkduWnctkNsQbcqh2PzUfptKLuArdtiZWu1dU%2B60%3D"
//sprintf "https://github.com/forki/Audio/releases/download/%s/PiFirmware.%s.zip" ReleaseNotes.Version ReleaseNotes.Version

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
        do! tagHistoryBroadcaster.BroadcastTextAsync(body,key = "B827EBA39A31")
        do! Task.Delay (1000 * 60 * 20)
}

let webApp =
    router {
        getf "/api/nextfile/%s/%s" nextFileEndpoint
        getf "/api/previousfile/%s/%s" previousFileEndpoint
        getf "/api/usertags/%s" userTagsEndPoint
        getf "/api/volumeup/%s" volumeUpEndpoint
        getf "/api/volumedown/%s" volumeDownEndpoint
        postf "/api/upload/%s" uploadEndpoint
        getf "/api/history/%s" historyEndPoint
        getf "/api/startup/%s" startupEndpoint
        get "/api/firmware" firmwareEndpoint
        get "/api/latestfirmware" getLatestFirmware
        getf "/api/taghistorysocket/%s" (TagHistorySocket.openSocket tagHistoryBroadcaster)
    }

let configureSerialization (services:IServiceCollection) =
    services

let configureApp (app : IApplicationBuilder) =
    app.UseWebSockets(Giraffe.WebSocket.DefaultWebSocketOptions)

let app = application {
    url ("http://0.0.0.0:" + URLs.serverPort.ToString() + "/")
    use_router webApp
    memory_cache
    use_static publicPath
    service_config configureSerialization
    use_gzip
    app_config configureApp
}

run app
