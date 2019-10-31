module GeneralIO

open System
open System.Threading.Tasks
open FSharp.Control.Tasks.ContextInsensitive
open System.Device.Gpio

type LED internal (controller:GpioController,pin:int) =
    let mutable active = false
    let mutable blinking = false
    do
        controller.OpenPin(pin, PinMode.Output)
        controller.Write(pin, PinValue.Low)

    with
        member __.IsActive = active

        member __.Activate() =
            controller.Write(pin, PinValue.High)
            blinking <- false
            active <- true

        member __.Deactivate() =
            controller.Write(pin, PinValue.Low)
            blinking <- false
            active <- false

        member this.Blink(times:int) = task {
            blinking <- false
            for _ in 0..times-1 do
                this.Activate()
                do! Task.Delay(300)
                this.Deactivate()
                do! Task.Delay(300)
        }

        member this.StartBlinking() = task {
            blinking <- true
            while blinking do
                this.Activate()
                do! Task.Delay(300)
                this.Deactivate()
                do! Task.Delay(300)
        }

        member this.StopBlinking() = this.Deactivate()

type CableConnection internal (controller:GpioController,pin:int) =
    let mutable onConnected = None
    let mutable onDisconnected = None
    let mutable lastState = None

    let execute expectedState f =
        match lastState with
        | Some s ->
            let highResistance = controller.Read(pin) = PinValue.High
            if highResistance = expectedState && s <> highResistance then
                f()
                lastState <- Some highResistance
        | None ->
            let highResistance = controller.Read(pin) = PinValue.High
            if highResistance = expectedState then
                f()
                lastState <- Some highResistance

    do
        controller.OpenPin(pin, PinMode.InputPullUp)

        let rising(_sender:obj) (_e:PinValueChangedEventArgs) =
            match onDisconnected with
            | None -> ()
            | Some f -> execute true f

        let falling(_sender:obj) (_e:PinValueChangedEventArgs) =
            match onConnected with
            | None -> ()
            | Some f -> execute false f

        controller.RegisterCallbackForPinValueChangedEvent(
            pin,
            PinEventTypes.Rising,
            PinChangeEventHandler rising)

        controller.RegisterCallbackForPinValueChangedEvent(
            pin,
            PinEventTypes.Falling,
            PinChangeEventHandler falling)

    member __.SetOnConnected f =
        onConnected <- Some f
        execute false f

    member __.SetOnDisConnected f =
        onDisconnected <- Some f
        execute true f

type Button internal (controller:GpioController,pin:int,onPress) =
    let mutable cableConnection = CableConnection (controller,pin)
    do
        cableConnection.SetOnConnected onPress


type Controller() =
    let controller = new GpioController(PinNumberingScheme.Logical)

    with
        member __.NewLED(pin) =
            LED(controller,pin)

        member __.NewCableConnection(pin:int) =
            CableConnection(controller,pin)

        member __.NewButton(pin:int,onPress) =
            Button(controller,pin,onPress)

        interface IDisposable with
            member __.Dispose() = controller.Dispose()
