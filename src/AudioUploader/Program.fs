open System
open System.IO
open FSharp.Control.Tasks.ContextInsensitive
open ServerCode.Storage
open ServerCore.Domain

let uploadMusik (stream:Stream) = task {
    let connection = AzureTable.connection
    let mediaID = System.Guid.NewGuid()

    let blobClient = connection.Force().CreateCloudBlobClient()
    let mediaContainer = blobClient.GetContainerReference "media"
    let! _ = mediaContainer.CreateIfNotExistsAsync()

    let blockBlob = mediaContainer.GetBlockBlobReference(mediaID.ToString())
    blockBlob.Properties.ContentType <- "audio/mpeg"
    do! blockBlob.UploadFromStreamAsync(stream)
    return mediaID
}

let run task = task |> Async.AwaitTask |> Async.RunSynchronously

[<EntryPoint>]
let main argv =
    let userID = ""
    let token = ""
    let tag = AzureTable.getTag userID token |> run
    let tag = tag.Value

    let directory = DirectoryInfo(".")
    let mediaIDs =
        directory.GetFiles("*.*")
        |> Seq.map (fun file ->
            printfn "Uploading: %s" file.FullName
            use stream = File.Open(file.FullName, FileMode.Open)
            uploadMusik stream
            |> run)
        |> Seq.toArray

    let tagAction = TagAction.PlayBlobMusik mediaIDs
    let tag = { tag with Action = tagAction }


    let _saved = AzureTable.saveTag tag |> run

    0
