module Elmish
open System.Threading.Tasks

type Cmd<'Msg> = (('Msg -> unit) -> unit) list

module Cmd =
    let none : Cmd<'Msg> = []

    let ofMsg<'Msg> (msg:'Msg) : Cmd<'Msg> = [fun dispatch -> dispatch msg]

    let batch<'Msg> (cmds:list<Cmd<'Msg>>) : Cmd<'Msg> =
        cmds |> List.concat

    let ofTask<'a,'b,'Msg> (f:'a -> Task<'b>) (parameters:'a) (successM: 'b -> 'Msg) (failM: exn -> 'Msg) : Cmd<'Msg> =
        [fun dispatch ->
            try
                let t = f parameters
                let r = t |> Async.AwaitTask |> Async.RunSynchronously
                dispatch (successM r)
            with
            | exn -> dispatch (failM exn)]

    let ofFunc<'a,'b,'Msg> (f:'a -> 'b) (parameters:'a) (successM: 'b -> 'Msg) (failM: exn -> 'Msg) : Cmd<'Msg> =
        [fun dispatch ->
            try
                let r = f parameters
                dispatch (successM r)
            with
            | exn -> dispatch (failM exn)]


type Program<'Model,'Msg>(log:log4net.ILog, init: unit -> 'Model, update: 'Model -> 'Msg -> ('Model * (Cmd<'Msg>))) =
    let agent = MailboxProcessor.Start (fun inbox ->
        let rec messageLoop (model:'Model) = async {
            let! msg = inbox.Receive()
            log.InfoFormat (sprintf "Msg: %A" msg)
            try
                let newModel,newCmds = update model msg
                log.InfoFormat (sprintf "Model: %A" newModel)
                for cmd in newCmds do
                    cmd inbox.Post
                return! messageLoop newModel
            with
            | exn ->
                log.ErrorFormat("Error during update: %s", exn.Message)
                return! messageLoop model
        }

        try
            let (initialModel:'Model) = init()
            log.InfoFormat (sprintf "Initial model: %A" initialModel)
            messageLoop initialModel
        with
        | exn ->
            log.ErrorFormat("Error during init: %s", exn.Message)
            reraise()
    )

    member __.Dispatch msg = agent.Post msg

