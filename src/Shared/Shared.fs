namespace ServerCore.Domain

open System

#if FABLE_COMPILER
open Thoth.Json
#else
open Thoth.Json.Net
#endif


type Firmware =
    { Version : string
      Url : string }

    static member Encoder (firmware : Firmware) =
        Encode.object [
            "Version", Encode.string firmware.Version
            "Url", Encode.string firmware.Url
        ]

    static member Decoder =
        Decode.object (fun get ->
            { Version = get.Required.Field "Version" Decode.string
              Url = get.Required.Field "Url" Decode.string }
        )

[<RequireQualifiedAccess>]
type TagAction =
| UnknownTag
| StopMusik
| PlayMusik of string []
| PlayYoutube of string
| PlayBlobMusik of System.Guid []

    static member Encoder (action : TagAction) =
        match action with
        | TagAction.UnknownTag ->
            Encode.object [
                "UnknownTag", Encode.nil
            ]
        | TagAction.StopMusik ->
            Encode.object [
                "StopMusik", Encode.nil
            ]
        | TagAction.PlayMusik urls ->
            Encode.object [
                "PlayMusik", Encode.array (Array.map Encode.string urls)
            ]
        | TagAction.PlayYoutube url ->
            Encode.object [
                "PlayYoutube", Encode.string url
            ]
        | TagAction.PlayBlobMusik urls ->
            Encode.object [
                "PlayBlobMusik", Encode.array (Array.map Encode.guid urls)
            ]

    static member Decoder =
        Decode.oneOf [
            Decode.field "UnknownTag" (Decode.succeed TagAction.UnknownTag)
            Decode.field "StopMusik" (Decode.succeed TagAction.StopMusik)
            Decode.field "PlayMusik" (Decode.array Decode.string)
                |> Decode.map TagAction.PlayMusik
            Decode.field "PlayYoutube" Decode.string
                |> Decode.map TagAction.PlayYoutube
            Decode.field "PlayBlobMusik" (Decode.array Decode.guid)
                |> Decode.map TagAction.PlayBlobMusik
        ]


[<RequireQualifiedAccess>]
type TagActionForBox =
| UnknownTag
| Ignore
| StopMusik
| PlayMusik of string

    static member GetFromTagAction(action:TagAction,position) =
        match action with
        | TagAction.StopMusik -> TagActionForBox.StopMusik
        | TagAction.UnknownTag -> TagActionForBox.UnknownTag
        | TagAction.PlayMusik urls ->
            if Array.isEmpty urls then
                TagActionForBox.StopMusik
            else
                let pos = Math.Abs(position % urls.Length)
                TagActionForBox.PlayMusik urls.[pos]
        | _ -> failwithf "Can't convert %A" action

    static member Encoder (action : TagActionForBox) =
        match action with
        | TagActionForBox.UnknownTag ->
            Encode.object [
                "UnknownTag", Encode.nil
            ]
        | TagActionForBox.StopMusik ->
            Encode.object [
                "StopMusik", Encode.nil
            ]
        | TagActionForBox.Ignore ->
            Encode.object [
                "Ignore", Encode.nil
            ]
        | TagActionForBox.PlayMusik url ->
            Encode.object [
                "PlayMusik", Encode.string url
            ]

    static member Decoder =
        Decode.oneOf [
            Decode.field "UnknownTag" (Decode.succeed TagActionForBox.UnknownTag)
            Decode.field "StopMusik" (Decode.succeed TagActionForBox.StopMusik)
            Decode.field "Ignore" (Decode.succeed TagActionForBox.Ignore)
            Decode.field "PlayMusik" Decode.string |> Decode.map TagActionForBox.PlayMusik
        ]

type Link = {
    Token : string
    Url : string
    Order : int
}

type Tag =
    { Token : string
      Object : string
      Description : string
      LastVerified : DateTimeOffset
      UserID : string
      Action : TagAction }

    static member Encoder (tag : Tag) =
        Encode.object [
            "Token", Encode.string tag.Token
            "Description", Encode.string tag.Description
            "UserID", Encode.string tag.UserID
            "Object", Encode.string tag.Object
            "LastVerified", Encode.datetimeOffset tag.LastVerified
            "Action", TagAction.Encoder tag.Action
        ]
    static member Decoder =
        Decode.object (fun get ->
            { Token = get.Required.Field "Token" Decode.string
              Object = get.Required.Field "Object" Decode.string
              Description = get.Required.Field "Description" Decode.string
              UserID = get.Required.Field "UserID" Decode.string
              LastVerified =
                    get.Optional.Field "LastVerified" Decode.datetimeOffset
                    |> Option.defaultValue DateTimeOffset.MinValue
              Action = get.Required.Field "Action" TagAction.Decoder }
        )

[<RequireQualifiedAccess>]
type SpeakerType =
| Local
| Sonos

    static member Encoder (action : SpeakerType) =
        match action with
        | Local ->
            Encode.object [
                "Local", Encode.nil
            ]
        | Sonos ->
            Encode.object [
                "Sonos", Encode.nil
            ]

    static member Decoder =
        Decode.oneOf [
            Decode.field "Local" (Decode.succeed SpeakerType.Local)
            Decode.field "Sonos" (Decode.succeed SpeakerType.Sonos)
        ]

type User =
    { UserID : string
      SpeakerType : SpeakerType
      SonosAccessToken : string
      SonosRefreshToken : string
      SonosID : string }

    static member Encoder (user : User) =
        Encode.object [
            "UserID", Encode.string user.UserID
            "SpeakerType", SpeakerType.Encoder user.SpeakerType
            "SonosAccessToken", Encode.string user.SonosAccessToken
            "SonosRefreshToken", Encode.string user.SonosRefreshToken
            "SonosID", Encode.string user.SonosID
        ]
    static member Decoder =
        Decode.object (fun get ->
            { UserID = get.Required.Field "UserID" Decode.string
              SpeakerType = get.Required.Field "SpeakerType" SpeakerType.Decoder
              SonosAccessToken = get.Required.Field "SonosAccessToken" Decode.string
              SonosRefreshToken = get.Required.Field "SonosRefreshToken" Decode.string
              SonosID = get.Required.Field "SonosID" Decode.string }
        )

type Request =
    { Token : string
      Timestamp : DateTimeOffset
      UserID : string }

    static member Encoder (request : Request) =
        Encode.object [
            "Token", Encode.string request.Token
            "UserID", Encode.string request.UserID
            "Timestamp", Encode.datetimeOffset request.Timestamp
        ]
    static member Decoder =
        Decode.object (fun get ->
            { Token = get.Required.Field "Token" Decode.string
              UserID = get.Required.Field "UserID" Decode.string
              Timestamp = get.Required.Field "Timestamp" Decode.datetimeOffset }
        )

type PlayListPosition = {
    Token : string
    UserID : string
    Position : int
}

type TagForBox =
    { Token : string
      Object : string
      Description : string
      Action : TagActionForBox }

    static member Encoder (tag : TagForBox) =
        Encode.object [
            "Token", Encode.string tag.Token
            "Description", Encode.string tag.Description
            "Object", Encode.string tag.Object
            "Action", TagActionForBox.Encoder tag.Action
        ]

    static member Decoder =
        Decode.object (fun get ->
            { Token = get.Required.Field "Token" Decode.string
              Object = get.Required.Field "Object" Decode.string
              Description = get.Required.Field "Description" Decode.string
              Action = get.Required.Field "Action" TagActionForBox.Decoder }
        )

type RequestList =
    { Requests : Request [] }

    static member Encoder (requesrList : RequestList) =
        Encode.object [
            "Requests", requesrList.Requests |> Array.map Request.Encoder |> Encode.array
        ]

    static member Decoder =
        Decode.object (fun get ->
            { Requests = get.Required.Field "Requests" (Decode.array Request.Decoder) }
        )


type TagList =
    { Tags : Tag [] }

    static member Encoder (tagList : TagList) =
        Encode.object [
            "Tags", tagList.Tags |> Array.map Tag.Encoder |> Encode.array
        ]

    static member Decoder =
        Decode.object (fun get ->
            { Tags = get.Required.Field "Tags" (Decode.array Tag.Decoder) }
        )

[<RequireQualifiedAccess>]
type TagHistorySocketEvent =
| ToDo

module URLs =
    let serverPort = 5000us

#if DEBUG
    let mediaServer = sprintf "http://localhost:%d" serverPort
#else
    let mediaServer = "https://audio-hub.azurewebsites.net"
#endif

    let mp3Server = sprintf "%s/api/audio/mp3" mediaServer


    let websocketURL =
#if DEBUG
        "ws://localhost:5000"
#else
        "wss:/audio-hub.azurewebsites.net"
#endif
