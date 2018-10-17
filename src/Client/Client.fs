module Client

open Elmish
open Elmish.React
open Fable.PowerPack

#if FABLE_COMPILER
open Thoth.Json
#else
open Thoth.Json.Net
#endif

open ServerCore.Domain
open Fulma
open Fable.PowerPack.Fetch

type Model = {
    Tags: TagList
    IsUploading :bool
    Message : string
    File: obj option
    Firmware: Firmware option
}

type Msg =
| FetchTags
| FileNameChanged of obj
| Upload
| FileUploaded of Tag
| UploadFailed of exn
| TagsLoaded of Result<TagList, exn>
| FirmwareLoaded of Result<Firmware, exn>
| Err of exn

let runIn (timeSpan:System.TimeSpan) successMsg errorMsg =
    let p() = promise {
        do! Promise.sleep (int timeSpan.TotalMilliseconds)
        return ()
    }
    Cmd.ofPromise p () (fun _ -> successMsg) errorMsg


let userID = "9bb2b109-bf08-4342-9e09-f4ce3fb01c0f"

let fetchData() = promise {
    let! res = Fetch.fetch (sprintf "api/usertags/%s" userID) []
    let! txt = res.text()

    match Decode.fromString TagList.Decoder txt with
    | Ok tags -> return tags
    | Error msg -> return failwith msg
}

let fetchFirmware() = promise {
    let! res = Fetch.fetch "api/firmware" []
    let! txt = res.text()

    match Decode.fromString Firmware.Decoder txt with
    | Ok tags -> return tags
    | Error msg -> return failwith msg
}


let uploadFile (fileName) = promise {
    let formData = Fable.Import.Browser.FormData.Create()
    formData.append("file",fileName)

    let props =
        [ RequestProperties.Method HttpMethod.POST
          Fetch.requestHeaders [
            // HttpRequestHeaders.Authorization ("Bearer " + model.User.Token)
            //HttpRequestHeaders.ContentType "multipart/form-data"
             ]
          RequestProperties.Body (unbox formData) ]

    let! res = Fetch.fetch "api/upload" props
    let! txt = res.text()

    match Decode.fromString Tag.Decoder txt with
    | Ok tags -> return tags
    | Error msg -> return failwith msg
}

let fetchFirmwareCmd = Cmd.ofPromise fetchFirmware () (Ok >> FirmwareLoaded) (Error >> FirmwareLoaded)
let fetchTagsCmd = Cmd.ofPromise fetchData () (Ok >> TagsLoaded) (Error >> TagsLoaded)

let init () : Model * Cmd<Msg> =
    let initialModel = {
        Tags = { Tags = [||] }
        Firmware = None
        File = None
        IsUploading = false
        Message = ""
    }

    initialModel,
        Cmd.batch [
            Cmd.ofMsg FetchTags
            fetchFirmwareCmd
        ]


let update (msg : Msg) (model : Model) : Model * Cmd<Msg> =
    match msg with

    | FetchTags ->
        model, fetchTagsCmd

    | FirmwareLoaded (Ok tags) ->
        { model with Firmware = Some tags }, Cmd.none

    | FirmwareLoaded _ ->
        { model with Firmware = None }, Cmd.none

    | TagsLoaded (Ok tags) ->
        { model with Tags = tags }, Cmd.none

    | TagsLoaded _  ->
        model, Cmd.ofMsg FetchTags

    | FileNameChanged file ->
        { model with File = Some file }, Cmd.none

    | FileUploaded tag ->
        { model with IsUploading = false; Message = "Done" }, Cmd.none

    | UploadFailed exn ->
        { model with IsUploading = false; Message = exn.Message }, Cmd.none

    | Upload ->
        match model.File with
        | None -> model, Cmd.none
        | Some fileName ->
            { model with File = None; IsUploading = true; Message = "Upload started" }, Cmd.ofPromise uploadFile fileName FileUploaded UploadFailed

    | Err exn ->
        { model with Message = exn.Message }, Cmd.none //runIn (System.TimeSpan.FromSeconds 5.) Fetch Err



open Fable.Helpers.React
open Fable.Helpers.React.Props
open Fable.Import
open Fable.Core.JsInterop


let view (model : Model) (dispatch : Msg -> unit) =
    Hero.hero [ Hero.Color IsPrimary; Hero.IsFullHeight ]
        [ Hero.body [ ]
            [ Container.container [ Container.Modifiers [ Modifier.TextAlignment (Screen.All, TextAlignment.Centered) ] ]
                [ div [] [
                    input [
                        Type "file"
                        OnChange (fun x -> FileNameChanged (!!x.target?files?(0)) |> dispatch) ]
                    br []
                    button [ OnClick (fun _ -> dispatch Upload) ] [str "Upload"]
                    br []
                    str "Message: "
                    str model.Message
                    br []
                    br []
                    str "Latest Firmware: "
                    (match model.Firmware with
                     | None -> str "Unknown"
                     | Some fw -> a [ Href fw.Url ] [ str fw.Version ])
                    h2 [] [str "Available Tags"]
                    table [][
                        thead [][
                            tr [] [
                                th [] [ str "Tag"]
                                th [] [ str "Object"]
                                th [] [ str "Action"]
                                th [] [ str "Description"]
                            ]
                        ]
                        tbody [][
                            for tag in model.Tags.Tags ->
                                tr [ Id tag.Token ] [
                                    td [ ] [ str tag.Token ]
                                    td [ ] [ str tag.Object ]
                                    td [ ] [ str (sprintf "%O" tag.Action) ]
                                    td [ ] [ str tag.Description ]
                                ]
                        ]
                    ]
                  ] ] ] ]


#if DEBUG
open Elmish.Debug
open Elmish.HMR
#endif

Program.mkProgram init update view
#if DEBUG
|> Program.withConsoleTrace
|> Program.withHMR
#endif
|> Program.withReact "elmish-app"
#if DEBUG
|> Program.withDebugger
#endif
|> Program.run
