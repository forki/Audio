module TagHistory


open Elmish
open Elmish.React
open Elmish.HMR
open Fable.PowerPack

#if FABLE_COMPILER
open Thoth.Json
#else
open Thoth.Json.Net
#endif

open System
open Fable.PowerPack.Fetch
open Fulma
open ServerCore.Domain


type Model = {
    WebSocket : Fable.Import.Browser.WebSocket
    Connected : bool
    UserID : string
    Requests : Request []
}

type Msg =
| Refresh
| ServerMsg of Elmish.WebSocket.WebSocketMsg<TagHistorySocketEvent>
| ConnectToWebsocket
| RetrySocketConnection of TimeSpan
| HistoryLoaded of Result<RequestList, exn>
| MsgError of exn


let fetchHistory (userID) = promise {
    let! res = Fetch.fetch (sprintf "api/history/%s" userID) []
    let! txt = res.text()

    match Decode.fromString RequestList.Decoder txt with
    | Ok tags -> return tags
    | Error msg -> return failwith msg
}

let fetchHistoryCmd userID = Cmd.ofPromise fetchHistory userID (Ok >> HistoryLoaded) (Error >> HistoryLoaded)


let init (userID) : Model * Cmd<Msg> =
    let initialModel = {
        WebSocket = null
        Connected = false
        UserID = userID
        Requests = [||]
    }

    initialModel,
        Cmd.batch [
            fetchHistoryCmd userID
            Cmd.ofMsg ConnectToWebsocket
            Cmd.ofMsg Refresh
        ]


let runIn (timeSpan:System.TimeSpan) successMsg errorMsg =
    let p() = promise {
        do! Promise.sleep (int timeSpan.TotalMilliseconds)
        return ()
    }
    Cmd.ofPromise p () (fun _ -> successMsg) errorMsg



let update (msg : Msg) (model : Model) : Model * Cmd<Msg> =
    match msg with
    | Refresh ->
        model, Cmd.none

    | ConnectToWebsocket ->
        if model.Connected then
            model, Cmd.none
        else
            let ws =
                let url = sprintf "%s/api/taghistorysocket/%s" URLs.websocketURL model.UserID
                // match model.User with
                // | Some user ->
                //     Elmish.WebSocket.createAuthenticated(url,user.Token)
                // | None ->
                Elmish.WebSocket.create url
            { model with
                WebSocket = ws }, Elmish.WebSocket.Cmd.Configure ws ServerMsg

    | HistoryLoaded (Ok requests) ->
        { model with Requests = requests.Requests }, Cmd.ofMsg Refresh

    | HistoryLoaded _  ->
        model, Cmd.none

    | ServerMsg Elmish.WebSocket.WebSocketMsg.Opening ->
        { model with Connected = true }, Cmd.none

    | ServerMsg Elmish.WebSocket.WebSocketMsg.Closing ->
        { model with Connected = false }, Cmd.ofMsg (RetrySocketConnection (TimeSpan.FromSeconds 10.))

    | ServerMsg (Elmish.WebSocket.WebSocketMsg.Error e) ->
        printfn "error: %A" e
        model, Cmd.none

    | ServerMsg (Elmish.WebSocket.WebSocketMsg.Data msg) ->
        model,
            match msg with
            | TagHistorySocketEvent.ToDo -> Cmd.ofMsg Refresh

    | RetrySocketConnection delay ->
        model, runIn delay ConnectToWebsocket MsgError

    | MsgError _exn ->
        model,
            if model.Connected then
                Cmd.ofMsg Refresh
            else
                Cmd.batch [
                    Cmd.ofMsg Refresh
                    Cmd.ofMsg (RetrySocketConnection (TimeSpan.FromSeconds 10.))
                ]

open Fable.Helpers.React
open Fable.Helpers.React.Props
open Fable.Core.JsInterop

let historyTable (model : Model) (dispatch : Msg -> unit) =
    div [] [
        table [][
            thead [][
                tr [] [
                    th [] [ str "Time"]
                    th [] [ str "Token"]
                ]
            ]
            tbody [][
                for tag in model.Requests ->
                    tr [ Id tag.Token ] [
                        yield td [ ] [ str (tag.Timestamp.ToString("o")) ]
                        yield td [ Title tag.Token ] [ str tag.Token ]
                    ]
            ]
        ]
    ]


let view (dispatch: Msg -> unit) (model:Model) =
    div [][ 
        historyTable model dispatch
    ]