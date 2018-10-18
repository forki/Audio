module GeneralIO

open System
open System.Threading.Tasks
open FSharp.Control.Tasks.ContextInsensitive

open Unosquare.RaspberryIO

type LED(pin:Gpio.GpioPin) =
    let mutable active = false
    do
        pin.PinMode <- Gpio.GpioPinDriveMode.Output
        pin.Write false

    with
        member __.IsActive = active

        member __.Activate() =
            pin.Write true
            active <- true

        member __.Deactivate() =
            pin.Write false
            active <- false

        member this.Blink(times:int) = task {
            for _ in 0..times-1 do
                this.Activate()
                do! Task.Delay(300)
                this.Deactivate()
                do! Task.Delay(300)
        }
