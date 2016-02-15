#r @"packages/build/FAKE/tools/FakeLib.dll"
open Fake
open System
open System.IO

let project = "FSDN"

// List of author names (for NuGet package)
let authors = [ "pocketberserker" ]

// Tags for your project (for NuGet package)
let tags = "fsharp F#"

// File system information
let solutionFile  = "FSDN.sln"

let configuration = environVarOrDefault "configuration" "Release"

// Pattern specifying assemblies to be tested using NUnit
let testAssemblies = "tests/**/bin" @@ configuration @@ "*Tests*.dll"

// Git configuration (used for publishing documentation in gh-pages branch)
// The profile where the project is posted
let gitOwner = "pocketberserker"
let gitHome = "https://github.com/" + gitOwner

// The name of the project on GitHub
let gitName = "FSDN"

// The url for the raw files hosted
let gitRaw = environVarOrDefault "gitRaw" "https://raw.github.com/pocketberserker"

// --------------------------------------------------------------------------------------
// END TODO: The rest of the file includes standard build steps
// --------------------------------------------------------------------------------------

// Copies binaries from default VS location to exepcted bin folder
// But keeps a subdirectory structure for each project in the
// src folder to support multiple project outputs
Target "CopyBinaries" (fun _ ->
    !! "src/**/*.??proj"
    |>  Seq.map (fun f -> ((System.IO.Path.GetDirectoryName f) @@ "bin" @@ configuration, "bin" @@ (System.IO.Path.GetFileNameWithoutExtension f)))
    |>  Seq.iter (fun (fromDir, toDir) -> CopyDir toDir fromDir (fun _ -> true))
)

Target "CopyWebConfig" (fun _ ->
  CopyFile ("bin" @@ project @@ "Web.config") ("config" @@ sprintf "Web.%s.config" configuration)
)

// --------------------------------------------------------------------------------------
// Clean build results

Target "Clean" (fun _ ->
    CleanDirs ["bin"; "temp"]
)

// --------------------------------------------------------------------------------------
// Build library & test project

Target "Build" (fun _ ->
    !! solutionFile
    |> MSBuild "" "Rebuild" [("Configuration", configuration)]
    |> ignore
)

open NpmHelper

Target "BuildFront" (fun _ ->
  let npm =
    let target = if isUnix then"npm" else "npm.cmd"
    match tryFindFileOnPath target with
    | Some npm -> npm
    | None -> findToolInSubPath target (currentDirectory @@ "packages/build")
  Npm (fun p ->
    {
      p with
        Command = Install Standard
        WorkingDirectory = currentDirectory
        NpmFilePath = npm
    })
  let exitCode =
    ExecProcess (fun info ->
      info.FileName <- npm
      info.Arguments <- "install typings -g")
      TimeSpan.MaxValue
  if exitCode <> 0 then failwith "Failed: npm install typings -g"
  let typings =
    let target = if isUnix then"typings" else "typings.cmd"
    match tryFindFileOnPath target with
    | Some typings -> typings
    | None -> findToolInSubPath target currentDirectory
  let exitCode =
    ExecProcess (fun info ->
      info.FileName <- typings
      info.Arguments <- "install")
      TimeSpan.MaxValue
  if exitCode <> 0 then failwith "Failed: typings install"
  Npm (fun p ->
    {
      p with
        Command = Run "pack"
        WorkingDirectory = currentDirectory
        NpmFilePath = npm
    })
)

// --------------------------------------------------------------------------------------
// Deploy

Target "Deploy" (fun _ ->
  let artifacts = currentDirectory @@ ".." @@ "artifacts"
  let kuduSync = findToolInSubPath "KuduSync.NET.exe" (currentDirectory @@ "packages")
  let deploymentSource =
    currentDirectory @@ "bin" @@ project
  let deploymentTarget =
    match environVarOrNone "DEPLOYMENT_TARGET" with
    | Some v -> v
    | None -> artifacts @@ "wwwroot"
  let nextManifestPath =
    match environVarOrNone "NEXT_MANIFEST_PATH" with
    | Some v -> v
    | None -> artifacts @@ "manifest"
  let previousManifestPath =
    match environVarOrNone "PREVIOUS_MANIFEST_PATH" with
    | Some v -> v
    | None -> nextManifestPath
  if environVarOrNone "IN_PLACE_DEPLOYMENT" <> Some "1" then
    let args =
      sprintf "-v 50 -f \"%s\" -t \"%s\" -n \"%s\" -p \"%s\" -i \".git;.hg;.deployment;\""
        deploymentSource deploymentTarget nextManifestPath previousManifestPath
    let exitCode =
      ExecProcess (fun info ->
        info.FileName <- kuduSync
        info.Arguments <- args)
        TimeSpan.MaxValue
    if exitCode <> 0 then failwithf "Failed KuduSync: %s" args
    environVarOrNone "POST_DEPLOYMENT_ACTION"
    |> Option.iter (fun c ->
      let exitCode = ExecProcess (fun info -> info.FileName <- c) TimeSpan.MaxValue
      if exitCode <> 0 then failwithf "Failed: post deployment action"
    )
)

Target "All" DoNothing

"Clean"
  ==> "Build"
  ==> "CopyBinaries"
  ==> "BuildFront"
  ==> "CopyWebConfig"
  ==> "All"

"CopyWebConfig"
  ==> "Deploy"

RunTargetOrDefault "All"
