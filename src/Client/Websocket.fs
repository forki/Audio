module WebSockets

open Fable.Core.JS
open Fable.Core.JsInterop
open Browser
open Browser.Types
open Thoth.Json

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

let inline send (ws:WebSocket) msg =
    let m = Encode.Auto.toString(0, msg)
    ws.send m

module Cmd =

    let inline Configure<'Data,'msg> (ws:WebSocket) (messageCtor:WebSocketMsg<'Data> -> 'msg) =
        let configure dispatch =
            ws.onmessage <- 
                unbox (fun (msg: MessageEvent) ->
                        let msg' = msg.data |> string
                        if not (System.String.IsNullOrWhiteSpace msg') then
                            let decoded = Decode.Auto.unsafeFromString<'Data> msg'
                            dispatch (messageCtor (WebSocketMsg.Data decoded))
                            unbox None)
                
            ws.onopen <- unbox (fun _ ->
                match ws.readyState with
                | WebSocketState.OPEN -> dispatch (messageCtor WebSocketMsg.Opening)
                | _ -> ()
            )

            ws.onclose <- unbox (fun _ ->
                match ws.readyState with
                | WebSocketState.CLOSED -> dispatch (messageCtor WebSocketMsg.Closing)
                | _ -> ()
            )

            ws.onerror <- unbox (fun evt-> dispatch (messageCtor (WebSocketMsg.Error (unbox evt?data))))
            ()

        [configure]