module Elmish.Audio

open FSharp.Control.Tasks.ContextInsensitive
open Elmish
open System.Threading.Tasks
open System.Diagnostics
open System.IO

type Audio = {
    Url : string option
    Volume : float
}


let getMusikPlayerProcesses() = Process.GetProcessesByName("omxplayer.bin")


let setVolumeScript volume =
    let volumeScript = "./volume.sh"
    let txt = sprintf """export DBUS_SESSION_BUS_ADDRESS=$(cat /tmp/omxplayerdbus.root)
dbus-send --print-reply --session --reply-timeout=500 \
           --dest=org.mpris.MediaPlayer2.omxplayer \
           /org/mpris/MediaPlayer2 org.freedesktop.DBus.Properties.Set \
           string:"org.mpris.MediaPlayer2.Player" \
           string:"Volume" double:%.2f""" volume

    if File.Exists volumeScript then
        File.Delete(volumeScript)
    File.WriteAllText(volumeScript,txt.Replace("\r\n","\n").Replace("\r","\n"))
    let p = new Process()
    let startInfo = new ProcessStartInfo()
    startInfo.WorkingDirectory <- "./"
    startInfo.FileName <- "sudo"
    startInfo.Arguments <- "sh volume.sh"
    startInfo.RedirectStandardOutput <- true
    startInfo.UseShellExecute <- false
    startInfo.CreateNoWindow <- true
    p.StartInfo <- startInfo
    p.Start() |> ignore

[<RequireQualifiedAccess>]
module Program =

    let withAudio stoppedMsg (program:Elmish.Program<_,_,_,_>) =
        let mutable lastView = None

        let sendMsg file dispatch _args = 
            dispatch (stoppedMsg file)

        let play dispatch file volume =
            let p = new System.Diagnostics.Process()
            p.EnableRaisingEvents <- true
            p.Exited.Add (sendMsg file dispatch)

            let startInfo = System.Diagnostics.ProcessStartInfo()
            startInfo.FileName <- "omxplayer"
            let volume = int (System.Math.Round(2000. * System.Math.Log10 volume))
            startInfo.Arguments <- sprintf "--vol %d " volume + file
            p.StartInfo <- startInfo
            p.Start() |> ignore

        let killMusikPlayer() = task {
            for p in getMusikPlayerProcesses() do
                if not p.HasExited then
                    try
                        let killP = new System.Diagnostics.Process()
                        let startInfo = System.Diagnostics.ProcessStartInfo()
                        startInfo.FileName <- "sudo"
                        startInfo.Arguments <- "kill -9 " + p.Id.ToString()
                        killP.StartInfo <- startInfo
                        killP.Start() |> ignore
                        while not p.HasExited do
                            do! Task.Delay 10
                    with _ -> ()
        }

        let setState model dispatch dispatch2 =
            let (v:Audio) = Program.view program model dispatch
            match lastView with
            | Some r when r = v -> ()
            | Some r ->
                if r.Url <> v.Url then
                    killMusikPlayer () |> Async.AwaitTask |> Async.RunSynchronously

                if r.Url = v.Url && v.Url <> None && r.Volume <> v.Volume then
                    setVolumeScript v.Volume

                match v.Url with
                | Some url when v.Url <> r.Url ->
                    play dispatch url v.Volume
                | _ -> ()
            | _ ->
                match v.Url with
                | Some url ->
                    play dispatch url v.Volume
                | _ -> ()

            lastView <- Some v

        Program.map id id id setState id program
