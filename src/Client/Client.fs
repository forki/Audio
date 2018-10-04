module Client

open Elmish
open Elmish.React
open Fable.PowerPack
open Fable.Helpers.React

#if FABLE_COMPILER
open Thoth.Json
#else
open Thoth.Json.Net
#endif

open Shared
open Fulma

type Model = {
    Tags: TagList }

type Msg =
| Fetch
| TagsLoaded of Result<TagList, exn>
| Err of exn

let runIn (timeSpan:System.TimeSpan) successMsg errorMsg =
    let p() = promise {
        do! Promise.sleep (int timeSpan.TotalMilliseconds)
        return ()
    }
    Cmd.ofPromise p () (fun _ -> successMsg) errorMsg


let fetchData() = promise {
    let! res = Fetch.fetch "api/alltags" []
    let! txt = res.text()
    match Decode.fromString TagList.Decoder txt with
    | Ok tags -> return tags
    | Error msg -> return failwith msg
}

let fetchBestPlanCmd =
    Cmd.ofPromise
        fetchData
        ()
        (Ok >> TagsLoaded)
        (Error >> TagsLoaded)

let init () : Model * Cmd<Msg> =
    let initialModel = {
        Tags = { Tags = [||] }}

    initialModel, Cmd.ofMsg Fetch


let update (msg : Msg) (model : Model) : Model * Cmd<Msg> =
    match msg with

    | Fetch ->
        model, fetchBestPlanCmd

    | TagsLoaded (Ok tags) ->
        { model with Tags = tags }, Cmd.none

    | TagsLoaded (Error _ ) ->
        model, Cmd.ofMsg Fetch

    | Err _ ->
        model, Cmd.none //runIn (System.TimeSpan.FromSeconds 5.) Fetch Err


let view (model : Model) (dispatch : Msg -> unit) =
    Hero.hero [ Hero.Color IsPrimary; Hero.IsFullHeight ]
        [ Hero.body [ ]
            [ Container.container [ Container.Modifiers [ Modifier.TextAlignment (Screen.All, TextAlignment.Centered) ] ]
                [ div [] [
                    h2 [] [str "Available Tags"]
                    table [][
                        tbody [][
                            for tag in model.Tags.Tags ->
                                tr [] [
                                    td [] [ str tag.Token]
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
