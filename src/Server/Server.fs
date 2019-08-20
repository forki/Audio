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
open System.Diagnostics
open ServerCode.Storage.AzureTable

#if DEBUG
let publicPath = Path.GetFullPath "../Client/public"
#else
let publicPath = Path.GetFullPath "client"
#endif

let getSASMediaLink mediaID = task {
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


let discoverYoutubeLink (youtubeURL:string) = task {
    let lines = System.Collections.Generic.List<_>()
    let proc = new Process ()
    let startInfo = ProcessStartInfo()
    startInfo.FileName <- "/usr/local/bin/youtube-dl"
    startInfo.Arguments <- sprintf " -g \"%s\"" youtubeURL
    startInfo.UseShellExecute <- false
    startInfo.RedirectStandardOutput <- true
    startInfo.RedirectStandardError <- true
    startInfo.CreateNoWindow <- true
    proc.StartInfo <- startInfo

    proc.Start() |> ignore

    while not proc.StandardOutput.EndOfStream do
        let! line = proc.StandardOutput.ReadLineAsync()
        lines.Add line

    while not proc.StandardError.EndOfStream do
        let! line = proc.StandardError.ReadLineAsync()
        lines.Add line

    let lines = Seq.toArray lines
    return youtubeURL,lines
}

let mapYoutube (tag:Tag) = task {
    match tag.Action with
    | TagAction.PlayYoutube _ ->
        let! links = getAllLinksForTag tag.Token
        let links = links |> Array.map (fun l -> l.Url)
        return { tag with Action = TagAction.PlayMusik links }
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
                    LastVerified = DateTimeOffset.UtcNow
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
                            LastVerified = DateTimeOffset.MinValue
                            Description = ""
                            Object = "" }
                        task { return t }

                let! tag = mapYoutube tag
                match user.SpeakerType with
                | SpeakerType.Local ->
                    let tag : TagForBox = {
                        Token = tag.Token
                        Object = tag.Object
                        Description = tag.Description
                        Action = TagActionForBox.GetFromTagAction(tag.Action,position) }

                    let txt = TagForBox.Encoder tag |> Encode.toString 0
                    return! setBodyFromString txt next ctx
                | SpeakerType.Sonos ->
                    let logger = ctx.GetLogger "PreviousFile"
                    let! session = Sonos.createOrJoinSession logger user.SonosAccessToken Sonos.group
                    do! Sonos.playStream logger user.SonosAccessToken session tag

                    let tag : TagForBox = {
                        Token = tag.Token
                        Object = tag.Object
                        Description = tag.Description
                        Action = TagActionForBox.Ignore }

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
                            LastVerified = DateTimeOffset.MinValue
                            Description = ""
                            Object = "" }
                        task { return t }

                let! tag = mapYoutube tag
                let! _ = AzureTable.savePlayListPosition userID token position

                match user.SpeakerType with
                | SpeakerType.Local ->
                    let tag : TagForBox = {
                        Token = tag.Token
                        Object = tag.Object
                        Description = tag.Description
                        Action = TagActionForBox.GetFromTagAction(tag.Action,position) }


                    let txt = TagForBox.Encoder tag |> Encode.toString 0
                    return! setBodyFromString txt next ctx
                | SpeakerType.Sonos ->
                    let logger = ctx.GetLogger "NextFile"
                    let! session = Sonos.createOrJoinSession logger user.SonosAccessToken Sonos.group
                    do! Sonos.playStream logger user.SonosAccessToken session tag

                    let tag : TagForBox = {
                        Token = tag.Token
                        Object = tag.Object
                        Description = tag.Description
                        Action = TagActionForBox.Ignore }

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
        set_header "Content-Type" "application/json"
        plug (fun next ctx -> task {
            let! tags = AzureTable.getAllTagsForUser userID
            let txt = TagList.Encoder { Tags = tags } |> Encode.toString 0
            return! setBodyFromString txt next ctx
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

let startupEndpoint =
    pipeline {
        set_header "Content-Type" "application/json"
        plug (fun next ctx -> task {
            let! sas = getSASMediaLink "d97cdddb-8a19-4690-8ba5-b8ea43d3641f"
            let action = TagActionForBox.PlayMusik sas

            let txt =
                action
                |> TagActionForBox.Encoder
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

let discoverYoutube() = task {
    try
        let! tags = getAllTags()
        for tag in tags do
            try
                match tag.Action with
                | TagAction.PlayYoutube url ->
                    let! _,urls = discoverYoutubeLink url
                    let urls =
                        urls
                        |> Array.filter (fun x -> x.Contains "&mime=audio")
                    let! _ = saveLinks tag urls
                    let! _ = saveTag { tag with LastVerified = DateTimeOffset.UtcNow }
                    ()
                | _ -> ()
            with
            | _ -> ()
    with
    | _ -> ()
}

let youtubeEndpoint =
    pipeline {
        set_header "Content-Type" "application/json"
        plug (fun next ctx -> task {
            do! discoverYoutube()
            let! _,urls = discoverYoutubeLink "https://www.youtube.com/watch?v=DeTePthFfDY"
            let txt = sprintf "%A" urls
            return! setBodyFromString txt next ctx
        })
    }

let tagHistoryBroadcaster = ConnectionManager()

let t = task {
    while true do
        let body = Encode.Auto.toString(0, TagHistorySocketEvent.ToDo)
        printfn "Sending"
        do! tagHistoryBroadcaster.BroadcastTextAsync(body,key = "B827EB8E6CA5")
        do! Task.Delay (1000 * 60 * 20)
}

let webApp =
    router {
        getf "/api/nextfile/%s/%s" nextFileEndpoint
        getf "/api/previousfile/%s/%s" previousFileEndpoint
        getf "/api/usertags/%s" userTagsEndPoint
        postf "/api/volumeup/%s" volumeUpEndpoint
        postf "/api/volumedown/%s" volumeUpEndpoint
        postf "/api/upload/%s" uploadEndpoint
        getf "/api/history/%s" historyEndPoint
        get "/api/startup" startupEndpoint
        get "/api/firmware" firmwareEndpoint
        get "/api/youtube" youtubeEndpoint
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

let discoverTask = task {
    while true do
        do! discoverYoutube()
        do! Task.Delay (1000 * 60 * 20)
    return ()
}

run app
