#r "paket: groupref build //"
#load "./.fake/build.fsx/intellisense.fsx"

#if !FAKE
#r "netstandard"
#r "Facades/netstandard" // https://github.com/ionide/ionide-vscode-fsharp/issues/839#issuecomment-396296095
#endif

open System

open Fake.Core
open Fake.DotNet
open Fake.IO

open Fake.IO.FileSystemOperators
open Fake.IO.Globbing.Operators

let serverPath = Path.getFullName "./src/Server"
let piServerPath = Path.getFullName "./src/PiServer"
let clientPath = Path.getFullName "./src/Client"
let deployDir = Path.getFullName "./deploy"



let dockerUser = Environment.environVarOrDefault "DockerUser" String.Empty
let dockerPassword = Environment.environVarOrDefault "DockerPassword" String.Empty
let dockerLoginServer = Environment.environVarOrDefault "DockerLoginServer" String.Empty
let dockerImageName = Environment.environVarOrDefault "DockerImageName" String.Empty

let release = ReleaseNotes.load "RELEASE_NOTES.md"

let platformTool tool winTool =
    let tool = if Environment.isUnix then tool else winTool
    match Process.tryFindFileOnPath tool with
    | Some t -> t
    | _ ->
        let errorMsg =
            tool + " was not found in path. " +
            "Please install it and make sure it's available from your path. " +
            "See https://safe-stack.github.io/docs/quickstart/#install-pre-requisites for more info"
        failwith errorMsg

let nodeTool = platformTool "node" "node.exe"
let yarnTool = platformTool "yarn" "yarn.cmd"

let install = lazy DotNet.install DotNet.Versions.FromGlobalJson

let inline withWorkDir wd =
    DotNet.Options.lift install.Value
    >> DotNet.Options.withWorkingDirectory wd

let runTool cmd args workingDir =
    let result =
        Process.execSimple (fun info ->
            { info with
                FileName = cmd
                WorkingDirectory = workingDir
                Arguments = args })
            TimeSpan.MaxValue
    if result <> 0 then failwithf "'%s %s' failed" cmd args

let runDotNet cmd workingDir =
    let result =
        DotNet.exec (withWorkDir workingDir) cmd ""
    if result.ExitCode <> 0 then failwithf "'dotnet %s' failed in %s" cmd workingDir

let openBrowser url =
    let result =
        //https://github.com/dotnet/corefx/issues/10361
        Process.execSimple (fun info ->
            { info with
                FileName = url
                UseShellExecute = true })
            TimeSpan.MaxValue
    if result <> 0 then failwithf "opening browser failed"

Target.create "Clean" (fun _ ->
    Shell.cleanDirs [deployDir]
)

Target.create "InstallClient" (fun _ ->
    printfn "Node version:"
    runTool nodeTool "--version" __SOURCE_DIRECTORY__
    printfn "Yarn version:"
    runTool yarnTool "--version" __SOURCE_DIRECTORY__
    runTool yarnTool "install --frozen-lockfile" __SOURCE_DIRECTORY__
    runDotNet "restore" clientPath
)

Target.create "RestoreServer" (fun _ ->
    runDotNet "restore" serverPath
    runDotNet "restore" piServerPath
)

Target.create "Build" (fun _ ->
    runDotNet "build" serverPath
    runDotNet "build" piServerPath
    runDotNet "fable webpack-cli -- --config src/Client/webpack.config.js -p" clientPath
)

Target.create "Run" (fun _ ->
    let server = async {
        runDotNet "watch run" serverPath
    }
    let piServer = async {
        runDotNet "watch run" piServerPath
    }
    let client = async {
        runDotNet "fable webpack-dev-server -- --config src/Client/webpack.config.js" clientPath
    }
    let browser = async {
        do! Async.Sleep 5000
        openBrowser "http://localhost:8080"
    }

    [ server; piServer; client; browser ]
    |> Async.Parallel
    |> Async.RunSynchronously
    |> ignore
)


Target.create "BundleClient" (fun _ ->
    let dotnetOpts = install.Value (DotNet.Options.Create())
    let result =
        Process.execSimple (fun info ->
            { info with
                FileName = dotnetOpts.DotNetCliPath
                WorkingDirectory = serverPath
                Arguments = "publish -c Release -o \"" + Path.getFullName deployDir + "\"" }) TimeSpan.MaxValue
    if result <> 0 then failwith "Publish failed"

    let clientDir = deployDir </> "client"
    let publicDir = clientDir </> "public"
    let jsDir = clientDir </> "js"
    let cssDir = clientDir </> "css"
    let imageDir = clientDir </> "Images"

    !! "src/Client/public/**/*.*" |> Shell.copyFiles publicDir
    !! "src/Client/js/**/*.*" |> Shell.copyFiles jsDir
    !! "src/Client/css/**/*.*" |> Shell.copyFiles cssDir
    !! "src/Client/Images/**/*.*" |> Shell.copyFiles imageDir

    "src/Client/index.html" |> Shell.copyFile clientDir
)


Target.create "CreateDockerImage" (fun _ ->
    if String.IsNullOrEmpty dockerUser then
        failwithf "docker username not given."
    if String.IsNullOrEmpty dockerImageName then
        failwithf "docker image name not given."
    let result =
        Process.execSimple (fun info ->
            { info with
                FileName = "docker"
                UseShellExecute = false
                Arguments = sprintf "build -t %s/%s ." dockerUser dockerImageName }) TimeSpan.MaxValue
    if result <> 0 then failwith "Docker build failed"
)


Target.create "PrepareRelease" (fun _ ->
    Fake.Tools.Git.Branches.checkout "" false "master"
    Fake.Tools.Git.CommandHelper.directRunGitCommand "" "fetch origin" |> ignore
    Fake.Tools.Git.CommandHelper.directRunGitCommand "" "fetch origin --tags" |> ignore

    Fake.Tools.Git.Staging.stageAll ""
    Fake.Tools.Git.Commit.exec "" (sprintf "Bumping version to %O" release.NugetVersion)
    Fake.Tools.Git.Branches.pushBranch "" "origin" "master"

    let tagName = string release.NugetVersion
    Fake.Tools.Git.Branches.tag "" tagName
    Fake.Tools.Git.Branches.pushTag "" "origin" tagName

    let result =
        Process.execSimple (fun info ->
            { info with
                FileName = "docker"
                Arguments = sprintf "tag %s/%s %s/%s:%s" dockerUser dockerImageName dockerUser dockerImageName release.NugetVersion}) TimeSpan.MaxValue
    if result <> 0 then failwith "Docker tag failed"
)

Target.create "Deploy" (fun _ ->
    let result =
        Process.execSimple (fun info ->
            { info with
                FileName = "docker"
                WorkingDirectory = deployDir
                Arguments = sprintf "login %s --username \"%s\" --password \"%s\"" dockerLoginServer dockerUser dockerPassword }) TimeSpan.MaxValue
    if result <> 0 then failwith "Docker login failed"

    let result =
        Process.execSimple (fun info ->
            { info with
                FileName = "docker"
                WorkingDirectory = deployDir
                Arguments = sprintf "push %s/%s" dockerUser dockerImageName }) TimeSpan.MaxValue
    if result <> 0 then failwith "Docker push failed"
)



open Fake.Core.TargetOperators

"Clean"
    ==> "InstallClient"
    ==> "Build"
    ==> "BundleClient"
    ==> "CreateDockerImage"
    ==> "PrepareRelease"
    ==> "Deploy"

"Clean"
    ==> "InstallClient"
    ==> "RestoreServer"
    ==> "Run"

Target.runOrDefault "Build"
