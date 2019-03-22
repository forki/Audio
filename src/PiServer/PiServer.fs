open ServerCore.Domain
open Microsoft.Extensions.DependencyInjection
open Giraffe
open Saturn
open FSharp.Control.Tasks.ContextInsensitive
open System
open System.IO
open Microsoft.AspNetCore.NodeServices
open Thoth.Json.Net
open System.Threading.Tasks
open System.Xml
open System.Reflection
open GeneralIO
open Elmish
open Elmish.Audio
open Unosquare.RaspberryIO.Abstractions


GeneralIO.init()


let runIn (timeSpan:TimeSpan) successMsg errorMsg =
    let t() = task {
        do! Task.Delay (int timeSpan.TotalMilliseconds)
        return ()
    }
    Cmd.ofTask t () (fun _ -> successMsg) errorMsg


let log =
    let log4netConfig = XmlDocument()
    log4netConfig.Load(File.OpenRead("log4net.config"))
    let repo = log4net.LogManager.CreateRepository(Assembly.GetEntryAssembly(), typeof<log4net.Repository.Hierarchy.Hierarchy>)
    log4net.Config.XmlConfigurator.Configure(repo, log4netConfig.["log4net"]) |> ignore

    let log = log4net.LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType)
    log


type MediaFile = {
    FileName : string
}

type Model = {
    Playing : MediaFile option
    FirmwareUpdateInterval : TimeSpan
    UserID : string
    TagServer : string
    Volume : float
    RFID : string option
    NodeServices : INodeServices
}

type Msg =
| VolumeUp
| VolumeDown
| NewRFID of string
| CheckFirmware
| FirmwareUpToDate of unit
| ExecuteAction of TagActionForBox
| RFIDRemoved
| DiscoverStartup
| NewTag of TagForBox
| Play of MediaFile
| NextMediaFile
| PreviousMediaFile
| PlayerStopped of string
| FinishPlaylist of unit
| Noop of unit
| Err of exn


let rfidLoop (dispatch,nodeServices:INodeServices) = task {
    log.InfoFormat("Connecting all buttons")

    use _nextButton = new Button(Unosquare.RaspberryIO.Pi.Gpio.[BcmPin.Gpio04], fun () -> dispatch NextMediaFile)
    use _previousButton = new Button(Unosquare.RaspberryIO.Pi.Gpio.[BcmPin.Gpio18], fun () -> dispatch PreviousMediaFile)
    use _volumeDownButton = new Button(Unosquare.RaspberryIO.Pi.Gpio.[BcmPin.Gpio26], fun () -> dispatch VolumeDown)
    use _volumeUpButton = new Button(Unosquare.RaspberryIO.Pi.Gpio.[BcmPin.Gpio12], fun () -> dispatch VolumeUp)
    let blueLight = GeneralIO.LED(Unosquare.RaspberryIO.Pi.Gpio.[BcmPin.Gpio20])
    let yellowLight = GeneralIO.LED(Unosquare.RaspberryIO.Pi.Gpio.[BcmPin.Gpio21])
    let allLights = [| blueLight; yellowLight|]

    let! _ = allLights |> Array.map (fun l -> l.Blink(2)) |> Task.WhenAll

    log.InfoFormat("Waiting for RFID cards or NFC tags...")
    while true do
        let! token = nodeServices.InvokeExportAsync<string>("./read-tag", "read", "tag")

        if String.IsNullOrEmpty token then
            let! _ = Task.Delay(TimeSpan.FromSeconds 0.5)
            ()
        else
            let! _ = allLights |> Array.map (fun l -> l.Blink(2)) |> Task.WhenAll
            dispatch (NewRFID token)
            let mutable waiting = true
            while waiting do
                do! Task.Delay(TimeSpan.FromSeconds 0.5)

                let! newToken = nodeServices.InvokeExportAsync<string>("./read-tag", "read", "tag")
                if newToken <> token then
                    // recheck in 2 seconds to make it a bit more stable
                    let! _ = Task.Delay(TimeSpan.FromSeconds 2.)
                    let! newToken = nodeServices.InvokeExportAsync<string>("./read-tag", "read", "tag")
                    if newToken <> token then
                        let! _ = allLights |> Array.map (fun l -> l.Blink(2)) |> Task.WhenAll
                        dispatch RFIDRemoved
                        waiting <- false
}


let init nodeServices : Model * Cmd<Msg> =
    { Playing = None
      FirmwareUpdateInterval = TimeSpan.FromHours 1.
      UserID = Utils.getMACAddress()
      TagServer = "https://audio-hub.azurewebsites.net" // TODO: load from some config
      Volume = 0.5 // TODO: load from webserver
      RFID = None
      NodeServices = nodeServices }, Cmd.ofMsg CheckFirmware


let nextFile (model:Model,token:string) = task {
    use webClient = new System.Net.WebClient()
    let url = sprintf @"%s/api/nextfile/%s/%s" model.TagServer model.UserID token
    let! result = webClient.DownloadStringTaskAsync(System.Uri url)

    match Decode.fromString TagForBox.Decoder result with
    | Error msg -> return failwith msg
    | Ok tag -> return tag
}

let previousFile (model:Model,token:string) = task {
    use webClient = new System.Net.WebClient()
    let url = sprintf @"%s/api/previousfile/%s/%s" model.TagServer model.UserID token
    let! result = webClient.DownloadStringTaskAsync(System.Uri url)

    match Decode.fromString TagForBox.Decoder result with
    | Error msg -> return failwith msg
    | Ok tag -> return tag
}

let getStartupAction (model:Model) = task {
    use webClient = new System.Net.WebClient()
    let url = sprintf @"%s/api/startup" model.TagServer
    let! result = webClient.DownloadStringTaskAsync(System.Uri url)

    match Decode.fromString (TagActionForBox.Decoder) result with
    | Error msg -> return failwith msg
    | Ok actions -> return actions
}

let update (msg:Msg) (model:Model) =
    match msg with
    | VolumeUp ->
        let vol = min 1. (model.Volume + 0.1)
        { model with Volume = vol }, Cmd.ofFunc setVolumeScript vol Noop Err

    | VolumeDown ->
        let vol = max 0. (model.Volume - 0.1)
        { model with Volume = vol }, Cmd.ofFunc setVolumeScript vol Noop Err

    | NewRFID rfid ->
        { model with RFID = Some rfid }, Cmd.ofTask nextFile (model,rfid) NewTag Err

    | RFIDRemoved ->
        { model with RFID = None }, Cmd.ofMsg (FinishPlaylist())

    | NewTag tag ->
        model, Cmd.ofMsg (ExecuteAction tag.Action)

    | Play mediaFile ->
        let model = { model with Playing = Some mediaFile }
        model, Cmd.none

    | PlayerStopped file ->
        match model.Playing with
        | Some mediaFile ->
            try
                let current = mediaFile.FileName
                if current = file then
                    model,Cmd.ofMsg NextMediaFile
                else
                    model,Cmd.none
            with
            | _ ->
                model,Cmd.none
        | _ ->
            model,Cmd.none

    | NextMediaFile ->
        match model.RFID with
        | Some rfid ->
            model, Cmd.ofTask nextFile (model,rfid) NewTag Err
        | None ->
            model, Cmd.none

    | PreviousMediaFile ->
        match model.RFID with
        | Some rfid ->
            model, Cmd.ofTask previousFile (model,rfid) NewTag Err
        | None ->
            model, Cmd.none

    | FinishPlaylist _ ->
        { model with Playing = None }, Cmd.none

    | CheckFirmware ->
        model, Cmd.ofTask FirmwareUpdate.checkFirmware (log,model.TagServer) FirmwareUpToDate Err

    | Noop _ ->
        model, Cmd.none

    | DiscoverStartup ->
        model, Cmd.ofTask getStartupAction model ExecuteAction Err

    | FirmwareUpToDate _ ->
        log.InfoFormat("Firmware {0} is uptodate.", ReleaseNotes.Version)
        model,
            Cmd.batch [
                Cmd.ofMsg DiscoverStartup
                [fun dispatch -> rfidLoop (dispatch,model.NodeServices) |> Async.AwaitTask |> Async.StartImmediate ]
            ]

    | ExecuteAction action ->
        match action with
        | TagActionForBox.UnknownTag ->
            log.Warn "Unknown Tag"
            model, Cmd.none
        | TagActionForBox.StopMusik ->
            model, Cmd.ofMsg (FinishPlaylist())
        | TagActionForBox.PlayMusik url ->
            let mediaFile : MediaFile = {
                FileName = url
            }
            model, Cmd.batch [Cmd.ofMsg (Play mediaFile) ]
    | Err exn ->
        log.ErrorFormat("Error: {0}", exn.Message)
        model, Cmd.none



let webApp = router {
    get "/version" (fun next ctx -> task {
        return! text ReleaseNotes.Version next ctx
    })
}

let configureSerialization (services:IServiceCollection) =
    services.AddNodeServices(fun x -> x.InvocationTimeoutMilliseconds <- 2 * 60 * 60 * 1000)
    services

let builder = application {
    url "http://0.0.0.0:8086/"
    use_router webApp
    memory_cache
    service_config configureSerialization
    use_gzip
}

let aspnetapp = builder.Build()
aspnetapp.Start()
log.InfoFormat("PiServer {0} started.", ReleaseNotes.Version)
let nodeServices = aspnetapp.Services.GetService(typeof<INodeServices>) :?> INodeServices

let view (model:Model) dispatch : Audio =
    match model.Playing with
    | Some mediaFile ->
        { Url = Some mediaFile.FileName; Volume = model.Volume }
    | _ -> { Url = None; Volume = model.Volume }

let app =
    Program.mkProgram init update view
    |> Program.withTrace (fun msg _model -> log.InfoFormat("{0}", msg))
    |> Program.withAudio PlayerStopped


Program.runWith nodeServices app

let t = task { while true do do! Task.Delay 10000 }

t |> Async.AwaitTask |> Async.RunSynchronously

log.InfoFormat("PiServer {0} is shutting down.", ReleaseNotes.Version)