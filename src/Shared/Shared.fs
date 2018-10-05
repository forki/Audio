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


    static member Decoder =
        Decode.oneOf [
            Decode.field "UnknownTag" (Decode.succeed TagAction.UnknownTag)
            Decode.field "StopMusik" (Decode.succeed TagAction.StopMusik)
            Decode.field "PlayMusik" Decode.string 
                |> Decode.map TagAction.PlayMusik
        ]

type Tag =
    { Token : string
      Action : TagAction }

    static member Encoder (tag : Tag) =
        Encode.object [
            "Token", Encode.string tag.Token
            "Action", TagAction.Encoder tag.Action
        ]

    static member Decoder =
        Decode.object (fun get ->
            { Token = get.Required.Field "Token" Decode.string
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
