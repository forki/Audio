module Elmish.WebSocket

open Fable.Import.Browser
open Fable.Import.JS
open Fable.Core.JsInterop


#if FABLE_COMPILER
open Thoth.Json
#else
open Thoth.Json.Net
#endif


[<RequireQualifiedAccess>]
type WebSocketStatus =
| CONNECTING
| CONNECTED
| CLOSING
| CLOSED
| UNKNOWN


[<RequireQualifiedAccess>]
type WebSocketMsg<'Data> =
   | Opening
   | Closing
   | Error of string
   | Data of 'Data


let create (url:string) =
    WebSocket.Create(url)

let createAuthenticated (url:string,jwt:string) =
    let url = url + "?token=" + encodeURI jwt
    WebSocket.Create(url)

let send (ws:Fable.Import.Browser.WebSocket) msg =
    let m = Encode.Auto.toString(0, msg)
    ws.send m

module Cmd =

    let getConnectionState (ws:Fable.Import.Browser.WebSocket) =
        match ws.readyState with
        | 0. -> WebSocketStatus.CONNECTING
        | 1. -> WebSocketStatus.CONNECTED
        | 2. -> WebSocketStatus.CLOSING
        | 3. -> WebSocketStatus.CLOSED
        | _ -> WebSocketStatus.UNKNOWN


    let inline Configure<'Data,'msg> (ws:Fable.Import.Browser.WebSocket) (messageCtor:WebSocketMsg<'Data> -> 'msg) =
        let configure dispatch =
            ws.onmessage <-
                unbox (fun (msg: MessageEvent) ->
                        let msg' = msg.data |> string
                        if not (System.String.IsNullOrWhiteSpace msg') then
                            let decoded = Decode.Auto.unsafeFromString<'Data> msg'
                            dispatch (messageCtor (WebSocketMsg.Data decoded))
                            unbox None)

            ws.onopen <- unbox (fun _ ->
                match getConnectionState ws with
                | WebSocketStatus.CONNECTED -> dispatch (messageCtor WebSocketMsg.Opening)
                | _ -> ()
            )

            ws.onclose <- unbox (fun _ ->
                match getConnectionState ws with
                | WebSocketStatus.CLOSED -> dispatch (messageCtor WebSocketMsg.Closing)
                | _ -> ()
            )

            ws.onerror <- unbox (fun evt-> dispatch (messageCtor (WebSocketMsg.Error (unbox evt?data))))
            ()

        [configure]