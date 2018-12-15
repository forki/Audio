module TagHistory


open System
open Fable.Helpers.React
open Fable.PowerPack
open Elmish
open ServerCore.Domain

#if FABLE_COMPILER
open Thoth.Json
#else
open Thoth.Json.Net
#endif

type Model = {
    WebSocket : Fable.Import.Browser.WebSocket
    Connected : bool
    UserID : string
}

type Msg =
| Refresh
| ServerMsg of Elmish.WebSocket.WebSocketMsg<TagHistorySocketEvent>
| ConnectToWebsocket
| RetrySocketConnection of TimeSpan
| Error of exn

let init (userID) : Model * Cmd<Msg> =
    let initialModel = {
        WebSocket = null
        Connected = false
        UserID = userID
    }

    initialModel,
        Cmd.batch [
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
        model, runIn delay ConnectToWebsocket Error

    | Error _exn ->
        model,
            if model.Connected then
                Cmd.ofMsg Refresh
            else
                Cmd.batch [
                    Cmd.ofMsg Refresh
                    Cmd.ofMsg (RetrySocketConnection (TimeSpan.FromSeconds 10.))
                ]



let view (dispatch: Msg -> unit) (model:Model) =
    div [][ ]