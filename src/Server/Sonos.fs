module Sonos

let group = "RINCON_347E5CF009E001400:4129486752"

open System.IO
open FSharp.Control.Tasks.ContextInsensitive
open ServerCore.Domain
open Thoth.Json.Net
open System.Net
open Microsoft.Extensions.Logging

let post (log:ILogger) (url:string) headers (data:string) = task {
    let req = HttpWebRequest.Create(url) :?> HttpWebRequest
    req.ProtocolVersion <- HttpVersion.Version10
    req.Method <- "POST"

    for (name:string),(value:string) in headers do
        req.Headers.Add(name,value) |> ignore

    let postBytes = System.Text.Encoding.UTF8.GetBytes(data)
    req.ContentType <- "application/json; charset=utf-8"
    req.ContentLength <- int64 postBytes.Length

    try
        // Write data to the request
        use reqStream = req.GetRequestStream()
        do! reqStream.WriteAsync(postBytes, 0, postBytes.Length)
        reqStream.Close()

        use resp = req.GetResponse()
        use stream = resp.GetResponseStream()
        use reader = new StreamReader(stream)
        let! html = reader.ReadToEndAsync()
        return html
    with
    | :? WebException as exn when not (isNull exn.Response) ->
        use errorResponse = exn.Response :?> HttpWebResponse
        use reader = new StreamReader(errorResponse.GetResponseStream())
        let error = reader.ReadToEnd()
        log.LogError(sprintf "Request to %s failed with: %s %s" url exn.Message error)
        return! raise exn
}

type Session =
    { ID : string }

    static member Decoder =
        Decode.object (fun get ->
            { ID = get.Required.Field "sessionId" Decode.string }
        )

let playModeToSingle (log:ILogger) accessToken group = task {
    let headers = ["Authorization", "Bearer " + accessToken]
    let url = sprintf "https://api.ws.sonos.com/control/api/v1/groups/%s/playback/playMode" group
    let body = """{
    "playModes": {
      "repeat": false,
      "repeatOne": false,
      "crossfade": false,
      "shuffle": false
    }
  }"""

    let! _result = post log url headers body

    ()
}

let createOrJoinSession (log:ILogger) accessToken group = task {
    let headers = ["Authorization", "Bearer " + accessToken]
    let url = sprintf "https://api.ws.sonos.com/control/api/v1/groups/%s/playbackSession" group
    let body = """{
    "appId": "com.Forkmann.AudioHub",
    "appContext": "1a2b3c",
    "customData": "playlistid:12345"
}"""

    let! result = post log url headers body

    match Decode.fromString Session.Decoder result with
    | Error msg -> return failwith msg
    | Ok session ->
        do! playModeToSingle log accessToken group
        return session
}

let volumeUp (log:ILogger) accessToken group = task {
    let headers = ["Authorization", "Bearer " + accessToken]
    let url = sprintf "https://api.ws.sonos.com/control/api/v1/groups/%s/groupVolume/relative" group
    let body = """{
  "volumeDelta": 10
}"""

    let! _result = post log url headers body

    ()
}

let volumeDown (log:ILogger) accessToken group = task {
    let headers = ["Authorization", "Bearer " + accessToken]
    let url = sprintf "https://api.ws.sonos.com/control/api/v1/groups/%s/groupVolume/relative" group
    let body = """{
  "volumeDelta": -10
}"""

    let! _result = post log url headers body

    ()
}


let playURL (log:ILogger) accessToken (session:Session) itemID mediaURL description = task {
    let headers = ["Authorization", "Bearer " + accessToken]
    let url = sprintf "https://api.ws.sonos.com/control/api/v1/playbackSessions/%s/playbackSession/loadStreamUrl" session.ID

    let body = sprintf """{
      "streamUrl": "%s",
      "playOnCompletion": true,
      "stationMetadata": {
        "name": "%s"
      },
      "itemId" : "%s"
    }"""                mediaURL description itemID


    let! _result = post log url headers body
    ()
}

let playStream (log:ILogger) accessToken (session:Session) (tag:Tag) position = task {
    match tag.Action with
    | TagAction.PlayMusik stream ->
        let pos = System.Math.Abs(position % stream.Length)
        do! playURL log accessToken session tag.Token stream.[pos] (tag.Object + " - " + tag.Description)
    | _ ->
        log.LogError(sprintf "TagAction %A can't be played on Sonos" tag.Action)
}
