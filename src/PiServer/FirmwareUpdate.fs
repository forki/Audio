module FirmwareUpdate

open System.Diagnostics
open System
open FSharp.Control.Tasks.ContextInsensitive
open Thoth.Json.Net
open ServerCore.Domain
open System.Threading.Tasks
open Paket

let firmwareTarget = System.IO.Path.GetFullPath "/home/pi/firmware"

let runFirmwareUpdate() =
    let p = new Process()
    let startInfo = ProcessStartInfo()
    startInfo.WorkingDirectory <- "/home/pi/firmware/"
    startInfo.FileName <- "sudo"
    startInfo.Arguments <- "sh update.sh"
    startInfo.RedirectStandardOutput <- true
    startInfo.UseShellExecute <- false
    startInfo.CreateNoWindow <- true
    p.StartInfo <- startInfo
    p.Start() |> ignore

let checkFirmware (log:log4net.ILog,tagServer) = task {
    use webClient = new System.Net.WebClient()
    System.Net.ServicePointManager.SecurityProtocol <-
        System.Net.ServicePointManager.SecurityProtocol |||
          System.Net.SecurityProtocolType.Tls11 |||
          System.Net.SecurityProtocolType.Tls12

    let url = sprintf @"%s/api/firmware" tagServer
    let! result = webClient.DownloadStringTaskAsync(System.Uri url)

    match Decode.fromString Firmware.Decoder result with
    | Error msg ->
        log.ErrorFormat("Decoder error: {0}", msg)
        return failwith msg
    | Ok firmware ->
        try
            log.InfoFormat("Latest firmware on server: {0}", firmware.Version)
            let serverVersion = SemVer.Parse firmware.Version
            let localVersion = SemVer.Parse ReleaseNotes.Version
            if serverVersion > localVersion then
                let localFileName = System.IO.Path.GetTempFileName().Replace(".tmp", ".zip")
                log.InfoFormat("Starting download of {0}", firmware.Url)
                do! webClient.DownloadFileTaskAsync(firmware.Url,localFileName)
                log.Info "Download done."

                if System.IO.Directory.Exists firmwareTarget then
                    System.IO.Directory.Delete(firmwareTarget,true)
                System.IO.Directory.CreateDirectory(firmwareTarget) |> ignore
                System.IO.Compression.ZipFile.ExtractToDirectory(localFileName, firmwareTarget)
                System.IO.File.Delete localFileName
                runFirmwareUpdate()
                while true do
                    log.Info "Running firmware update."
                    do! Task.Delay 3000
                    ()
            else
                if System.IO.Directory.Exists firmwareTarget then
                    System.IO.Directory.Delete(firmwareTarget,true)
        with
        | exn ->
            log.ErrorFormat("Upgrade error: {0}", exn.Message)
}
