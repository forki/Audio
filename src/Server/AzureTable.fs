module ServerCode.Storage.AzureTable

open Microsoft.WindowsAzure.Storage
open Microsoft.WindowsAzure.Storage.Table
open System.Threading.Tasks
open FSharp.Control.Tasks.ContextInsensitive


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


let storageConnectionString =
    let str = System.Environment.GetEnvironmentVariable("APPSETTING_CONNSTR_STORAGE")
    if isNull str then
        "UseDevelopmentStorage=true"
    else
        str

let connection = CloudStorageAccount.Parse storageConnectionString

let tagsTable = getTable "tags" connection
let linksTable = getTable "links" connection
let requestsTable = getTable "requests" connection


open ServerCore.Domain
open Thoth.Json.Net
open System


let mapLink (entity: DynamicTableEntity) : Link =
    { Token = entity.PartitionKey
      Order = int entity.RowKey
      Url = getStringProperty "Url" entity }

let mapTag (entity: DynamicTableEntity) : Tag =
    { UserID = entity.PartitionKey
      Token = entity.RowKey
      Description = getStringProperty "Description" entity
      Object = getStringProperty "Object" entity
      LastVerified = getOptionalDateTimeOffsetProperty "LastVerified" entity |> Option.defaultValue DateTimeOffset.MinValue
      Action =
        match Decode.fromString TagAction.Decoder (getStringProperty "Action" entity) with
        | Error msg -> failwith msg
        | Ok action -> action }


let saveTag (tag:Tag) =
    let entity = DynamicTableEntity()
    entity.PartitionKey <- tag.UserID
    entity.RowKey <- tag.Token
    entity.Properties.["Action"] <- EntityProperty.GeneratePropertyForString (TagAction.Encoder tag.Action |> Encode.toString 0)
    entity.Properties.["Description"] <- EntityProperty.GeneratePropertyForString tag.Description
    entity.Properties.["Object"] <- EntityProperty.GeneratePropertyForString tag.Object
    entity.Properties.["LastVerified"] <- EntityProperty.GeneratePropertyForDateTimeOffset(Nullable tag.LastVerified)
    let operation = TableOperation.InsertOrReplace entity
    tagsTable.ExecuteAsync operation


let saveLinks (tag:Tag) (urls:string []) = task {
    let batch = TableBatchOperation()
    let mutable i = 0
    for url in urls do
        let entity = DynamicTableEntity()
        entity.PartitionKey <- tag.Token
        entity.RowKey <- string i
        entity.Properties.["Url"] <- EntityProperty.GeneratePropertyForString url
        batch.InsertOrReplace entity
        i <- i + 1
    if i > 0 then
        let! _ = linksTable.ExecuteBatchAsync batch
        ()
}

let saveRequest (userID:string) (token:string) =
    let entity = DynamicTableEntity()
    entity.PartitionKey <- userID
    entity.RowKey <- System.DateTimeOffset.UtcNow.ToString("o")
    entity.Properties.["Token"] <- EntityProperty.GeneratePropertyForString token
    let operation = TableOperation.InsertOrReplace entity
    requestsTable.ExecuteAsync operation

let getTag (userID:string) token = task {
    let query = TableOperation.Retrieve(userID, token)
    let! r = tagsTable.ExecuteAsync(query)
    if r.HttpStatusCode <> 200 then
        return None
    else
        let result = r.Result :?> DynamicTableEntity
        if isNull result then return None else return Some(mapTag result)
}

let getAllTagsForUser (userID:string) = task {
    let rec getResults token = task {
        let query = TableQuery.GenerateFilterCondition("PartitionKey", QueryComparisons.Equal, userID)
        let! result = tagsTable.ExecuteQuerySegmentedAsync(TableQuery(FilterString = query), token)
        let token = result.ContinuationToken
        let result = result |> Seq.toList
        if isNull token then
            return result
        else
            let! others = getResults token
            return result @ others }

    let! results = getResults null

    return [| for result in results -> mapTag result |]
}

let getAllTags () = task {
    let rec getResults token = task {
        let! result = tagsTable.ExecuteQuerySegmentedAsync(TableQuery(), token)
        let token = result.ContinuationToken
        let result = result |> Seq.toList
        if isNull token then
            return result
        else
            let! others = getResults token
            return result @ others }

    let! results = getResults null

    return [| for result in results -> mapTag result |]
}

let getAllLinksForTag (tagToken:string) = task {
    let rec getResults token = task {
        let query = TableQuery.GenerateFilterCondition("PartitionKey", QueryComparisons.Equal, tagToken)
        let! result = linksTable.ExecuteQuerySegmentedAsync(TableQuery(FilterString = query), token)
        let token = result.ContinuationToken
        let result = result |> Seq.toList
        if isNull token then
            return result
        else
            let! others = getResults token
            return result @ others }

    let! results = getResults null

    return [| for result in results -> mapLink result |]
}