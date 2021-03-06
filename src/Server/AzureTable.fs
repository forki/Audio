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


open Microsoft.Azure.KeyVault
open Microsoft.Azure.Services.AppAuthentication

let keyVaultClient = lazy (
    let azureServiceTokenProvider = AzureServiceTokenProvider()
    let callback authority resource scope = azureServiceTokenProvider.KeyVaultTokenCallback.Invoke(authority,resource,scope)
    new KeyVaultClient(new KeyVaultClient.AuthenticationCallback(callback))
)

let getSecretAsync vault secret =
    keyVaultClient.Force().GetSecretAsync(sprintf "https://%s.vault.azure.net/secrets/%s" vault secret)

let getSecret vault secret =
    getSecretAsync vault secret
    |> Async.AwaitTask
    |> Async.RunSynchronously

let connection = lazy (
    let storageConnectionString = getSecret "AudioVault" "StorageConnectionKey"
    CloudStorageAccount.Parse storageConnectionString.Value
)

let tagsTable = lazy(getTable "tags" (connection.Force()))
let usersTable = lazy(getTable "users" (connection.Force()))
let positionsTable = lazy(getTable "positions" (connection.Force()))
let requestsTable = lazy(getTable "requests" (connection.Force()))

open ServerCore.Domain
open Thoth.Json.Net
open System


let mapTag (entity: DynamicTableEntity) : Tag =
    { UserID = entity.PartitionKey
      Token = entity.RowKey
      Description = getStringProperty "Description" entity
      Object = getStringProperty "Object" entity
      Action =
        match Decode.fromString TagAction.Decoder (getStringProperty "Action" entity) with
        | Error msg -> failwith msg
        | Ok action -> action }

let mapUser (entity: DynamicTableEntity) : User =
    { UserID = entity.RowKey }

let mapRequest (entity: DynamicTableEntity) : Request =
    { UserID = entity.PartitionKey
      Token = getStringProperty "Token" entity
      Timestamp = DateTimeOffset.Parse entity.RowKey }

let mapPlayListPosition (entity: DynamicTableEntity) : PlayListPosition =
    { UserID = entity.PartitionKey
      Token = entity.RowKey
      Position = getIntProperty "Position" entity }

let saveTag (tag:Tag) =
    let entity = DynamicTableEntity()
    entity.PartitionKey <- tag.UserID
    entity.RowKey <- tag.Token
    entity.Properties.["Action"] <- EntityProperty.GeneratePropertyForString (TagAction.Encoder tag.Action |> Encode.toString 0)
    entity.Properties.["Description"] <- EntityProperty.GeneratePropertyForString tag.Description
    entity.Properties.["Object"] <- EntityProperty.GeneratePropertyForString tag.Object
    let operation = TableOperation.InsertOrReplace entity
    tagsTable.Force().ExecuteAsync operation


let saveRequest (userID:string) (token:string) =
    let entity = DynamicTableEntity()
    entity.PartitionKey <- userID
    entity.RowKey <- System.DateTimeOffset.UtcNow.ToString("o")
    entity.Properties.["Token"] <- EntityProperty.GeneratePropertyForString token
    let operation = TableOperation.InsertOrReplace entity
    requestsTable.Force().ExecuteAsync operation


let getAllRequestsForUser (userID:string) = task {
    let rec getResults token = task {
        let query = TableQuery.GenerateFilterCondition("PartitionKey", QueryComparisons.Equal, userID)
        let! result = requestsTable.Force().ExecuteQuerySegmentedAsync(TableQuery(FilterString = query), token)
        let token = result.ContinuationToken
        let result = result |> Seq.toList
        if isNull token then
            return result
        else
            let! others = getResults token
            return result @ others }

    let! results = getResults null

    return [| for result in results -> mapRequest result |]
}

let savePlayListPosition (userID:string) (token:string) position =
    let entity = DynamicTableEntity()
    entity.PartitionKey <- userID
    entity.RowKey <- token
    entity.Properties.["Position"] <- EntityProperty.GeneratePropertyForInt(Nullable position)
    let operation = TableOperation.InsertOrReplace entity
    positionsTable.Force().ExecuteAsync operation

let getTag (userID:string) token = task {
    let query = TableOperation.Retrieve(userID, token)
    let! r = tagsTable.Force().ExecuteAsync(query)
    if r.HttpStatusCode <> 200 then
        return None
    else
        let result = r.Result :?> DynamicTableEntity
        if isNull result then return None else return Some(mapTag result)
}

let getUser (userID:string) = task {
    let query = TableOperation.Retrieve("users", userID)
    let! r = usersTable.Force().ExecuteAsync(query)
    if r.HttpStatusCode <> 200 then
        return None
    else
        let result = r.Result :?> DynamicTableEntity
        if isNull result then return None else return Some(mapUser result)
}

let getPlayListPosition (userID:string) token = task {
    let query = TableOperation.Retrieve(userID, token)
    let! r = positionsTable.Force().ExecuteAsync(query)
    if r.HttpStatusCode <> 200 then
        return None
    else
        let result = r.Result :?> DynamicTableEntity
        if isNull result then return None else return Some(mapPlayListPosition result)
}

let getAllTagsForUser (userID:string) = task {
    let rec getResults token = task {
        let query = TableQuery.GenerateFilterCondition("PartitionKey", QueryComparisons.Equal, userID)
        let! result = tagsTable.Force().ExecuteQuerySegmentedAsync(TableQuery(FilterString = query), token)
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
        let! result = tagsTable.Force().ExecuteQuerySegmentedAsync(TableQuery(), token)
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

