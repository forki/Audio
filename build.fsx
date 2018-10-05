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
let piDeployDir = Path.getFullName "./pideploy"
let firmwareDeployDir = Path.getFullName "./firmware"


let dockerUser = Environment.environVarOrDefault "DockerUser" String.Empty
let dockerPassword = Environment.environVarOrDefault "DockerPassword" String.Empty
let dockerLoginServer = Environment.environVarOrDefault "DockerLoginServer" String.Empty
let dockerImageName = Environment.environVarOrDefault "DockerImageName" String.Empty

let releases = 
    "RELEASE_NOTES.md"
    |> System.IO.File.ReadAllLines
    
let release = ReleaseNotes.load "RELEASE_NOTES.md"

let currentFirmware = firmwareDeployDir </> (sprintf "PiFirmware.%s.zip" release.NugetVersion)

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
    Shell.cleanDirs [deployDir; piDeployDir; firmwareDeployDir]
)

Target.create "InstallClient" (fun _ ->
    printfn "Node version:"
    runTool nodeTool "--version" __SOURCE_DIRECTORY__
    printfn "Yarn version:"
    runTool yarnTool "--version" __SOURCE_DIRECTORY__
    runTool yarnTool "install --frozen-lockfile" __SOURCE_DIRECTORY__
    try runTool yarnTool "install" piServerPath with _ -> ()
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


Target.create "SetReleaseNotes" (fun _ ->
    let lines = [
            "module internal ReleaseNotes"
            ""
            (sprintf "let Version = \"%s\"" release.NugetVersion)
            ""
            (sprintf "let IsPrerelease = %b" (release.SemVer.PreRelease <> None))
            ""
            "let Notes = \"\"\""] @ Array.toList releases @ ["\"\"\""]
    System.IO.File.WriteAllLines("src/Client/ReleaseNotes.fs",lines)
)

Target.create "BundleClient" (fun _ ->
    let dotnetOpts = install.Value (DotNet.Options.Create())
    let result =
        Process.execSimple (fun info ->
            { info with
                FileName = dotnetOpts.DotNetCliPath
                WorkingDirectory = serverPath
                Arguments = "publish -c Release -o \"" + Path.getFullName deployDir + "\"" }) TimeSpan.MaxValue
    if result <> 0 then failwith "Publish Server failed"

    let result =
        Process.execSimple (fun info ->
            { info with
                FileName = dotnetOpts.DotNetCliPath
                WorkingDirectory = piServerPath
                Arguments = "publish -c Release -r linux-arm -o \"" + Path.getFullName piDeployDir + "\"" }) TimeSpan.MaxValue
    if result <> 0 then failwith "Publish PiServer failed"

    !! (piServerPath </> "*.js") |> Shell.copyFiles piDeployDir
    let targetNodeModules = piDeployDir </> "node_modules"
    Shell.cleanDirs [targetNodeModules]
    Shell.copyRecursive (piServerPath </> "node_modules") targetNodeModules true |> ignore

    System.IO.Compression.ZipFile.CreateFromDirectory(piDeployDir, currentFirmware)
    let clientDir = deployDir </> "client"
    let publicDir = clientDir </> "public"
    let jsDir = clientDir </> "js"
    let imageDir = clientDir </> "Images"

    !! "src/Client/public/**/*.*" |> Shell.copyFiles publicDir
    !! "src/Client/js/**/*.*" |> Shell.copyFiles jsDir
    !! "src/Client/Images/**/*.*" |> Shell.copyFiles imageDir

    !! "src/Client/*.css" |> Shell.copyFiles clientDir
    "src/Client/index.html" |> Shell.copyFile clientDir
    
    let indexFile = System.IO.FileInfo(clientDir </> "index.html")
    let content = System.IO.File.ReadAllText(indexFile.FullName)
    let newContent = 
        content
          .Replace("""<script src="./public/main.js"></script>""",sprintf """<script src="./public/main.%s.js"></script>""" release.NugetVersion)
          .Replace("""<script src="./public/vendors.js"></script>""",sprintf """<script src="./public/vendors.%s.js"></script>""" release.NugetVersion)
          .Replace("""<link rel="stylesheet" type="text/css" href="landing.css">""",sprintf """<link rel="stylesheet" type="text/css" href="landing.%s.css">""" release.NugetVersion)
    System.IO.File.WriteAllText(indexFile.FullName,newContent)   

    let bundleFile = System.IO.FileInfo(publicDir </> "main.js")
    let newBundleFile = System.IO.FileInfo(publicDir </> sprintf "main.%s.js" release.NugetVersion)
    System.IO.File.Move (bundleFile.FullName,newBundleFile.FullName)

    let vendorFile = System.IO.FileInfo(publicDir </> "vendors.js")
    let newVendorFile = System.IO.FileInfo(publicDir </> sprintf "vendors.%s.js" release.NugetVersion)
    System.IO.File.Move (vendorFile.FullName,newVendorFile.FullName)

    let cssFile = System.IO.FileInfo(clientDir </> "landing.css")
    let newcssFile = System.IO.FileInfo(clientDir </> sprintf "landing.%s.css" release.NugetVersion)
    System.IO.File.Move (cssFile.FullName,newcssFile.FullName)
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


Target.createFinal "KillProcess" (fun _ ->
    Process.killAllByName "dotnet"
    Process.killAllByName "dotnet.exe"
)

Target.create "TestDockerImage" (fun _ ->
    Target.activateFinal "KillProcess"
    let testImageName = "test"

    let result =
        Process.execSimple (fun info ->
            { info with
                FileName = "docker"
                Arguments = sprintf "run -d -p 127.0.0.1:8085:8085 --rm --name %s -it %s/%s" testImageName dockerUser dockerImageName }) TimeSpan.MaxValue
    if result <> 0 then failwith "Docker run failed"

    System.Threading.Thread.Sleep 5000 |> ignore  // give server some time to start

    // !! clientTestExecutables
    // |> Testing.Expecto.run (fun p -> { p with Parallel = false } )
    // |> ignore

    let result =
        Process.execSimple (fun info ->
            { info with
                FileName = "docker"
                Arguments = sprintf "stop %s" testImageName }) TimeSpan.MaxValue
    if result <> 0 then failwith "Docker stop failed"
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

#load "paket-files/build/fsharp/FAKE/modules/Octokit/Octokit.fsx"
open Octokit


Target.create "Deploy" (fun _ ->
    let user = Environment.environVarOrDefault "GithubUser" String.Empty        
    let pw = Environment.environVarOrDefault "GithubPassword" String.Empty        
   
    // release on github
    createClient user pw
    |> createDraft "forki" "Audio" release.NugetVersion (release.SemVer.PreRelease <> None) release.Notes
    |> uploadFile currentFirmware
    |> releaseDraft
    |> Async.RunSynchronously

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
    ==> "SetReleaseNotes"
    ==> "Build"
    ==> "BundleClient"
    ==> "CreateDockerImage"
    ==> "TestDockerImage"
    ==> "PrepareRelease"
    ==> "Deploy"

"Clean"
    ==> "InstallClient"
    ==> "RestoreServer"
    ==> "Run"

Target.runOrDefault "Build"
