namespace Shared

#if FABLE_COMPILER
open Thoth.Json
#else
open Thoth.Json.Net
#endif

type Tag =
    { Token : string }

    static member Encoder (tag : Tag) =
        Encode.object [
            "token", Encode.string tag.Token
        ]

    static member Decoder =
        Decode.object (fun get ->
            { Token = get.Required.Field "token" Decode.string }
        )

type TagList =
    { Tags : Tag [] }

    static member Encoder (tagList : TagList) =
        Encode.object [
            "tags", tagList.Tags |> Array.map Tag.Encoder |> Encode.array
        ]

    static member Decoder =
        Decode.object (fun get ->
            { Tags = get.Required.Field "tags" (Decode.array Tag.Decoder) }
        )
