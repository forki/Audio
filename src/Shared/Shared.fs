namespace ServerCore.Domain

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
| PlayMusik of string
| PlayYoutube of string
| PlayBlobMusik of System.Guid

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
        | TagAction.PlayMusik url ->
            Encode.object [
                "PlayMusik", Encode.string url
            ]
        | TagAction.PlayYoutube url ->
            Encode.object [
                "PlayYoutube", Encode.string url
            ]
        | TagAction.PlayBlobMusik url ->
            Encode.object [
                "PlayBlobMusik", Encode.guid url
            ]

    static member Decoder =
        Decode.oneOf [
            Decode.field "UnknownTag" (Decode.succeed TagAction.UnknownTag)
            Decode.field "StopMusik" (Decode.succeed TagAction.StopMusik)
            Decode.field "PlayMusik" Decode.string
                |> Decode.map TagAction.PlayMusik
            Decode.field "PlayYoutube" Decode.string
                |> Decode.map TagAction.PlayYoutube
            Decode.field "PlayBlobMusik" Decode.guid
                |> Decode.map TagAction.PlayBlobMusik
        ]

type Tag =
    { Token : string
      Object : string
      Description : string
      Action : TagAction }

    static member Encoder (tag : Tag) =
        Encode.object [
            "Token", Encode.string tag.Token
            "Description", Encode.string tag.Description
            "Object", Encode.string tag.Object
            "Action", TagAction.Encoder tag.Action
        ]

    static member Decoder =
        Decode.object (fun get ->
            { Token = get.Required.Field "Token" Decode.string
              Object = get.Required.Field "Object" Decode.string
              Description = get.Required.Field "Description" Decode.string
              Action = get.Required.Field "Action" TagAction.Decoder }
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
    let serverPort = 8085us

#if DEBUG
    let mediaServer = sprintf "http://localhost:%d" serverPort
#else
    let mediaServer = "https://audio-hub.azurewebsites.net"
#endif

    let mp3Server = sprintf "%s/api/audio/mp3" mediaServer
