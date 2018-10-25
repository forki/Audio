module TagHistorySocket

open Giraffe
open Microsoft.AspNetCore.Http
open Giraffe.WebSocket
open FSharp.Control.Tasks.ContextInsensitive
open Microsoft.Extensions.Logging

let openSocket (broadcaster:ConnectionManager) (userID:string) _next (ctx: HttpContext) = task {
    let logger = ctx.GetLogger "TagHistorySocket"

    let onConnected _reference = task {
        logger.LogInformation(EventId(), "Socket connected")
    }

    let onMessage _reference message = task {
        logger.LogInformation(EventId(), "Socket message: " + message)
    }

    let onClosed() = task {
        logger.LogInformation(EventId(), "Socket disconnected")
    }

    return! broadcaster.OpenSocket(ctx, onConnected, onMessage, onClosed, keyFilter = userID)
}