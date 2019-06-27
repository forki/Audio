module TagHistory


open Elmish
open Elmish.React
open Elmish.HMR

#if FABLE_COMPILER
open Thoth.Json
#else
open Thoth.Json.Net
#endif

open System
open Fetch
open Fulma
open ServerCore.Domain
open Browser.Types

type Model = {
    WebSocket : WebSocket
    Connected : bool
    UserID : string
    Requests : Request []
}

type Msg =
| Refresh
| ServerMsg of WebSockets.WebSocketMsg<TagHistorySocketEvent>
| ConnectToWebsocket
| RetrySocketConnection of TimeSpan
| HistoryLoaded of Result<RequestList, exn>
| MsgError of exn


let fetchHistory (userID) = promise {
    let! res = fetch (sprintf "api/history/%s" userID) []
    let! txt = res.text()

    match Decode.fromString RequestList.Decoder txt with
    | Ok tags -> return tags
    | Error msg -> return failwith msg
}

let fetchHistoryCmd userID = Cmd.OfPromise.either fetchHistory userID (Ok >> HistoryLoaded) (Error >> HistoryLoaded)


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
    Cmd.OfPromise.either p () (fun _ -> successMsg) errorMsg



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
                WebSockets.create url
            { model with
                WebSocket = ws }, WebSockets.Cmd.Configure ws ServerMsg

    | HistoryLoaded (Ok requests) ->
        { model with Requests = requests.Requests }, Cmd.ofMsg Refresh

    | HistoryLoaded _  ->
        model, Cmd.none

    | ServerMsg WebSockets.WebSocketMsg.Opening ->
        { model with Connected = true }, Cmd.none

    | ServerMsg WebSockets.WebSocketMsg.Closing ->
        { model with Connected = false }, Cmd.ofMsg (RetrySocketConnection (TimeSpan.FromSeconds 10.))

    | ServerMsg (WebSockets.WebSocketMsg.Error e) ->
        printfn "error: %A" e
        model, Cmd.none

    | ServerMsg (WebSockets.WebSocketMsg.Data msg) ->
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

open Fable.React
open Fable.React.Props

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