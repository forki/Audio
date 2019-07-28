module FirmwareUpdate

open System.Diagnostics
open System
open FSharp.Control.Tasks.ContextInsensitive
open Thoth.Json.Net
open ServerCore.Domain
open System.Threading.Tasks
open Paket
open System.IO

let firmwareTarget = Path.GetFullPath "/home/pi/firmware"

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
    use webClient = new Net.WebClient()
    Net.ServicePointManager.SecurityProtocol <-
        Net.ServicePointManager.SecurityProtocol |||
          Net.SecurityProtocolType.Tls11 |||
          Net.SecurityProtocolType.Tls12

    let url = sprintf @"%s/api/firmware" tagServer
    let! result = webClient.DownloadStringTaskAsync(Uri url)

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
                let localFileName = Path.GetTempFileName().Replace(".tmp", ".zip")
                log.InfoFormat("Starting download of {0}", firmware.Url)
                do! webClient.DownloadFileTaskAsync(firmware.Url,localFileName)
                log.Info "Download done."

                if Directory.Exists firmwareTarget then
                    Directory.Delete(firmwareTarget,true)
                Directory.CreateDirectory(firmwareTarget) |> ignore
                Compression.ZipFile.ExtractToDirectory(localFileName, firmwareTarget)
                File.Delete localFileName
                runFirmwareUpdate()
                while true do
                    log.Info "Running firmware update."
                    do! Task.Delay 3000
                    ()
            else
                if Directory.Exists firmwareTarget then
                    Directory.Delete(firmwareTarget,true)
        with
        | exn ->
            log.ErrorFormat("Upgrade error: {0}", exn.Message)
}
