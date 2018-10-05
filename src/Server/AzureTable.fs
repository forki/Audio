module ServerCode.Storage.AzureTable

open Microsoft.WindowsAzure.Storage
open Microsoft.WindowsAzure.Storage.Table
open System.Threading.Tasks

type AzureConnection = 
    | AzureConnection of string
    member this.Connect() =
        match this with
        | AzureConnection connectionString -> CloudStorageAccount.Parse connectionString

let getTable tableName (connection: CloudStorageAccount) =
    async {
        let client = connection.CreateCloudTableClient()
        let table = client.GetTableReference tableName

        // Azure will temporarily lock table names after deleting and can take some time before the table name is made available again.
        let rec createTableSafe() = async {
            try
                let! _  = table.CreateIfNotExistsAsync() |> Async.AwaitTask
                ()
            with _ ->
                do! Task.Delay 5000 |> Async.AwaitTask
                return! createTableSafe() }

        do! createTableSafe()
        return table 
    } |> Async.RunSynchronously


let inline getProperty (propName:string) (entity: DynamicTableEntity) =
    try
        entity.Properties.[propName]
    with
    | exn -> failwithf "Could not get property %s for entity %s %s. Message: %s" propName entity.PartitionKey entity.RowKey exn.Message


let inline hasProperty (propName:string) (entity: DynamicTableEntity) =
    match entity.Properties.TryGetValue propName with
    | true, v -> true
    | _ -> false

let inline getOptionalProperty (propName:string) (entity: DynamicTableEntity) =
    match entity.Properties.TryGetValue propName with
    | true, v -> Some v
    | _ -> None

let inline getBoolProperty (propName:string) (entity: DynamicTableEntity) =
    let prop = getProperty propName entity
    try
        prop.BooleanValue.Value
    with
    | exn -> failwithf "Could not get boolean value of property %s for entity %s %s. Message: %s" propName entity.PartitionKey entity.RowKey exn.Message

let inline getStringProperty (propName:string) (entity: DynamicTableEntity) =
    let prop = getProperty propName entity
    try
        prop.StringValue
    with
    | exn -> failwithf "Could not get string value of property %s for entity %s %s. Message: %s" propName entity.PartitionKey entity.RowKey exn.Message

let inline getOptionalBoolProperty (propName:string) (entity: DynamicTableEntity) =
    try
        getOptionalProperty propName entity
        |> Option.map (fun prop -> prop.BooleanValue.Value)
    with
    | exn -> failwithf "Could not get bool value of property %s for entity %s %s. Message: %s" propName entity.PartitionKey entity.RowKey exn.Message

let inline getOptionalStringProperty (propName:string) (entity: DynamicTableEntity) =
    try
        getOptionalProperty propName entity
        |> Option.map (fun prop -> prop.StringValue)
    with
    | exn -> failwithf "Could not get string value of property %s for entity %s %s. Message: %s" propName entity.PartitionKey entity.RowKey exn.Message

let inline getDateTimeOffsetProperty (propName:string) (entity: DynamicTableEntity) =
    let prop = getProperty propName entity
    try
        prop.DateTimeOffsetValue.Value
    with
    | exn -> failwithf "Could not get DateTimeOffset value of property %s for entity %s %s. Message: %s" propName entity.PartitionKey entity.RowKey exn.Message

let inline getOptionalDateTimeOffsetProperty (propName:string) (entity: DynamicTableEntity) =
    try
        getOptionalProperty propName entity
        |> Option.map (fun prop -> prop.DateTimeOffsetValue.Value)
    with
    | exn -> failwithf "Could not get DateTimeOffset value of property %s for entity %s %s. Message: %s" propName entity.PartitionKey entity.RowKey exn.Message

let inline getIntProperty (propName:string) (entity: DynamicTableEntity) =
    let prop = getProperty propName entity
    try
        prop.Int32Value.Value
    with
    | exn -> failwithf "Could not get Int32 value of property %s for entity %s %s. Message: %s" propName entity.PartitionKey entity.RowKey exn.Message


let inline getOptionalIntProperty (propName:string) (entity: DynamicTableEntity) =
    try
        getOptionalProperty propName entity
        |> Option.map (fun prop -> prop.Int32Value.Value)
    with
    | exn -> failwithf "Could not get Int32 value of property %s for entity %s %s. Message: %s" propName entity.PartitionKey entity.RowKey exn.Message

let inline getDoubleProperty (propName:string) (entity: DynamicTableEntity) =
    let prop = getProperty propName entity
    try
        prop.DoubleValue.Value
    with
    | exn -> failwithf "Could not get Double value of property %s for entity %s %s. Message: %s" propName entity.PartitionKey entity.RowKey exn.Message

let inline getOptionalDoubleProperty (propName:string) (entity: DynamicTableEntity) =
    try
        getOptionalProperty propName entity
        |> Option.map (fun prop -> prop.DoubleValue.Value)
    with
    | exn -> failwithf "Could not get Double value of property %s for entity %s %s. Message: %s" propName entity.PartitionKey entity.RowKey exn.Message

let storageConnectionString = System.Environment.GetEnvironmentVariable("SQLAZURECONNSTR_STORAGE")
        
let getConnectionToAzureStorage() = (AzureConnection storageConnectionString).Connect()
