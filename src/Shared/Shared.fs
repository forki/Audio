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
| StopMusik
| PlayMusik of string []

    static member GetFromTagAction(action:TagAction) =
        match action with
        | TagAction.StopMusik -> TagActionForBox.StopMusik
        | TagAction.UnknownTag -> TagActionForBox.UnknownTag
        | TagAction.PlayMusik urls -> TagActionForBox.PlayMusik urls
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
        | TagActionForBox.PlayMusik urls ->
            Encode.object [
                "PlayMusik", Encode.array (Array.map Encode.string urls)
            ]

    static member Decoder =
        Decode.oneOf [
            Decode.field "UnknownTag" (Decode.succeed TagActionForBox.UnknownTag)
            Decode.field "StopMusik" (Decode.succeed TagActionForBox.StopMusik)
            Decode.field "PlayMusik" (Decode.array Decode.string)
                |> Decode.map TagActionForBox.PlayMusik
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
      Action : TagAction }

    static member Encoder (tag : Tag) =
        Encode.object [
            "Token", Encode.string tag.Token
            "Description", Encode.string tag.Description
            "Object", Encode.string tag.Object
            "LastVerified", Encode.datetimeOffset tag.LastVerified
            "Action", TagAction.Encoder tag.Action
        ]

    static member Decoder =
        Decode.object (fun get ->
            { Token = get.Required.Field "Token" Decode.string
              Object = get.Required.Field "Object" Decode.string
              Description = get.Required.Field "Description" Decode.string
              LastVerified = 
                    get.Optional.Field "LastVerified" Decode.datetimeOffset 
                    |> Option.defaultValue DateTimeOffset.MinValue
              Action = get.Required.Field "Action" TagAction.Decoder }
        )


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


    let websocketURL =
#if DEBUG
        "ws://localhost:8087"
#else
        "wss:/audio-hub.azurewebsites.net"
#endif
