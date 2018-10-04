module Client

open Elmish
open Elmish.React
open Fable.PowerPack
open Fable.Helpers.React
open Fable.PowerPack.Fetch
open Thoth.Json
open Shared

open Fulma
open Fable.Core.JsInterop

type Model = {
    Clusters: Cluster []
    CenterChanged : bool }

type Msg =
| Fetch
| ClustersLoaded of Result<Cluster [], exn>
| Err of exn

let runIn (timeSpan:System.TimeSpan) successMsg errorMsg =
    let p() = promise {
        do! Promise.sleep (int timeSpan.TotalMilliseconds)
        return ()
    }
    Cmd.ofPromise p () (fun _ -> successMsg) errorMsg


let fetchData() = promise {
    let! res = Fetch.fetch "/api/tours" []
    let! txt = res.text()
    return Decode.Auto.unsafeFromString<Cluster []> txt
}

let fetchBestPlanCmd =
    Cmd.ofPromise
        fetchData
        ()
        (Ok >> ClustersLoaded)
        (Error >> ClustersLoaded)

let init () : Model * Cmd<Msg> =
    let initialModel = {
        Clusters = [||]
        CenterChanged = false }

    initialModel, Cmd.none //Cmd.ofMsg Fetch


let update (msg : Msg) (model : Model) : Model * Cmd<Msg> =
    match msg with

    | Fetch ->
        model, fetchBestPlanCmd

    | ClustersLoaded (Ok clusters) ->
        { model with Clusters = clusters }, Cmd.none

    | ClustersLoaded (Error _ ) ->
        { model with Clusters = [||] }, Cmd.ofMsg Fetch

    | Err _ ->
        model, Cmd.none //runIn (System.TimeSpan.FromSeconds 5.) Fetch Err


let view (model : Model) (dispatch : Msg -> unit) =
    Hero.hero [ Hero.Color IsPrimary; Hero.IsFullHeight ]
        [ Hero.body [ ]
            [ Container.container [ Container.Modifiers [ Modifier.TextAlignment (Screen.All, TextAlignment.Centered) ] ]
                [ div [] [
                    str "Hello"
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
