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

type Button(pin:Gpio.GpioPin,onPress) =
    let mutable lastChangedState = DateTime.MinValue
    let bounceTimeSpan = TimeSpan.FromMilliseconds 30.
    let mutable lastState = false
    do
        pin.PinMode <- Gpio.GpioPinDriveMode.Input
        pin.InputPullMode <- Gpio.GpioPinResistorPullMode.PullUp
        lastState <- pin.Read()
        pin.RegisterInterruptCallback(
            Gpio.EdgeDetection.RisingAndFallingEdges,
            fun () ->
                let state = pin.Read()
                let time = DateTime.UtcNow
                let bounceTimeReached = lastChangedState.Add bounceTimeSpan < time
                if bounceTimeReached && (not state) && lastState then
                    ()
                if bounceTimeReached && state && not lastState then
                    onPress()
                if lastState <> state then
                    lastChangedState <- time
                lastState <- state)

    let d =
        { new IDisposable with
                member __.Dispose() = () }

    interface IDisposable with
        member __.Dispose() = d.Dispose()

let waitForButtonPress (pin:Gpio.GpioPin) = task {
    let pressed = ref false
    use _button = new Button(pin,(fun _ -> pressed := true))
    while not !pressed do
        do! Task.Delay(100)
}