// --------------------------------------------------------------------------------------
// FAKE build script
// --------------------------------------------------------------------------------------

#r "System.Xml.Linq"
#if MONO
#else
#r "Microsoft.VisualBasic"
#endif
#r @"packages/FAKE/tools/FakeLib.dll"
open Fake
open Fake.Git
open Fake.AssemblyInfoFile
open Fake.ReleaseNotesHelper
open Fake.UserInputHelper
open System
open System.IO
open System.Xml.Linq
open System.Collections.Concurrent
open System.Collections.Generic
#if MONO
#else
#load "packages/SourceLink.Fake/tools/Fake.fsx"
open SourceLink
#r @"packages/LibGit2Sharp/lib/net40/LibGit2Sharp.dll"
open LibGit2Sharp
#endif

// --------------------------------------------------------------------------------------
// START TODO: Provide project-specific details below
// --------------------------------------------------------------------------------------

// Information about the project are used
//  - for version and project name in generated AssemblyInfo file
//  - by the generated NuGet package
//  - to run tests and to publish documentation on GitHub gh-pages
//  - for documentation, you also need to edit info in "docs/tools/generate.fsx"

// The name of the project
// (used by attributes in AssemblyInfo, name of a NuGet package and directory in 'src')
let project = "Prajna"

// Short summary of the project
// (used as description in AssemblyInfo and as a short summary for NuGet package)
let summary = "Prajna: A Distributed Functional Programming Platform for Interactive Big Data Analytics and Cloud Service Building"

// Longer description of the project
// (used as a description for NuGet package; line breaks are automatically cleaned up)
let description = "Prajna: A Distributed Functional Programming Platform for Interactive Big Data Analytics and Cloud Service Building"

// List of author names (for NuGet package)
let authors = [ "Cloud Computing & Storage Group, Microsoft Research" ]

// Tags for your project (for NuGet package)
let tags = "Prajna Distributed-Computing Functional-Programming Data-Analytics Cloud-Service"

// File system information 
#if MONO
let solutionFile  = "Prajna-Mono.sln"
#else
let solutionFile  = "Prajna.sln"
#endif

// Pattern specifying assemblies to be tested using NUnit
let testAssemblies = "tests/**/bin/{0}/*Tests*.dll"

let curDir = __SOURCE_DIRECTORY__

let isTravisBuild =
    let isTravis = Environment.GetEnvironmentVariable("TRAVIS")
    let r = isTravis <> null && isTravis = "true"
    trace(sprintf "Travis build: %b" r)
    r

// --------------------------------------------------------------------------------------
// Git helpers
// --------------------------------------------------------------------------------------

// Try to find out the owner from the remote URL that "origin" points to, if fails, return None
let getGitOwnerFromOrigin () = 
#if MONO
    None
#else
    try
      if Repository.IsValid(curDir) then
              use repo = new Repository(curDir)
              let origin = repo.Network.Remotes |> Seq.tryFind (fun r -> r.Name = "origin")
              match origin with
              | Some v -> let uri = System.Uri(v.Url)
                          let isValid = uri.Segments.Length = 3 &&
                                          let seg2 = uri.Segments.[2]
                                          String.Compare(seg2, project, StringComparison.InvariantCultureIgnoreCase) = 0 ||
                                          String.Compare(seg2, project + ".git", StringComparison.InvariantCultureIgnoreCase) = 0
                          if not isValid then failwith( sprintf "Unexpected segments: %A" uri.Segments)
                          let owner = uri.Segments.[1].TrimEnd([| '/' |])
                          owner |> Some
              | None -> None
      else
          None
    with
    | e ->  trace(sprintf "Fail to get the owner from origin: %s" e.Message)
            None
#endif

// Git configuration (used for publishing documentation in gh-pages branch)
// The profile where the project is posted
let gitDefaultOwner = "MSRCCS" 
let gitOwner = match getGitOwnerFromOrigin() with
               | Some o -> o
               | None -> gitDefaultOwner
let gitHome = "https://github.com/" + gitOwner

trace ("GitHome: " + gitHome)

// The name of the project on GitHub
let gitName = "Prajna"

// The url for the raw files hosted
let gitRaw = environVarOrDefault "gitRaw" "https://raw.github.com/" + gitOwner

// --------------------------------------------------------------------------------------
// END TODO: The rest of the file includes standard build steps
// --------------------------------------------------------------------------------------

// Read additional information from the release notes document
let release = LoadReleaseNotes "RELEASE_NOTES.md"

// Helper active pattern for project types
let (|Fsproj|Csproj|Vbproj|) (projFileName:string) = 
    match projFileName with
    | f when f.EndsWith("fsproj") -> Fsproj
    | f when f.EndsWith("csproj") -> Csproj
    | f when f.EndsWith("vbproj") -> Vbproj
    | _                           -> failwith (sprintf "Project file %s not supported. Unknown project type." projFileName)

Target "PrintBuildMachineConfiguration" ( fun _ ->
    printfn "Build Machine Configuration: "
    printfn "    Machine Name: %s" Environment.MachineName
    printfn "    # of Logical Processors: %d" Environment.ProcessorCount
#if MONO
#else
    let ci = Microsoft.VisualBasic.Devices.ComputerInfo()
    printfn "    Total Physical Memory: %.2f GB" ((float ci.TotalPhysicalMemory)/1e9)
    printfn "    Available Physical Memory: %.2f GB" ((float ci.AvailablePhysicalMemory)/1e9)
    printfn "    Total Virtual Memory: %.2f GB" ((float ci.TotalVirtualMemory)/1e9)
    printfn "    Available Virtual Memory: %.2f GB" ((float ci.AvailableVirtualMemory)/1e9)
    printfn "    OS: %s" ci.OSFullName
#endif
)

// Replace a file's CRLF with LF
let replaceCrLfWithLf file =
   let content = File.ReadAllText(file);
   let newContent = content.Replace("\r\n", "\n");
   if String.Compare(content, newContent, StringComparison.InvariantCulture) <> 0 then
       File.WriteAllText(file, newContent)

// Remove the UTF-8 BOM (EF BB BF) from file
let removeBom file = 
    let bytes = File.ReadAllBytes(file)
    if bytes.Length > 2 && bytes.[0] = 0xEFuy && bytes.[1] = 0xBBuy && bytes.[2] = 0xBFuy then
        File.WriteAllBytes(file, bytes |> Seq.skip 3 |> Array.ofSeq)

// Generate assembly info files with the right version & up-to-date information
Target "AssemblyInfo" (fun _ ->
    let getAssemblyInfoAttributes projectName =
        [ Attribute.Title (projectName)
          Attribute.Product project
          Attribute.Description summary
          Attribute.Version release.AssemblyVersion
          Attribute.FileVersion release.AssemblyVersion ]

    let getProjectDetails projectPath =
        let projectFileName = System.IO.Path.GetFileNameWithoutExtension(projectPath)
        let projectName = 
            if projectFileName.StartsWith(project, StringComparison.InvariantCultureIgnoreCase) 
            then projectFileName
            else project + "." + projectFileName
        ( projectPath, 
          projectName,
          System.IO.Path.GetDirectoryName(projectPath),
          (getAssemblyInfoAttributes projectName)
        )

    !! "src/**/*.??proj"
    |> Seq.map getProjectDetails
    |> Seq.iter (fun (projFileName, projectName, folderName, attributes) ->
        let createAssemblyInfoFunc, asmInfoFile =
            match projFileName with
            | Fsproj -> CreateFSharpAssemblyInfo, (folderName @@ "AssemblyInfo.fs")
            | Csproj -> CreateCSharpAssemblyInfo, ((folderName @@ "Properties") @@ "AssemblyInfo.cs")
            | Vbproj -> CreateVisualBasicAssemblyInfo, ((folderName @@ "My Project") @@ "AssemblyInfo.vb")
        createAssemblyInfoFunc asmInfoFile attributes
        replaceCrLfWithLf asmInfoFile
        removeBom asmInfoFile
        )
)

Target "SearchCrLf" (fun _ ->
    let excludedExts = set [ ".dll"; ".exe"; ".pdb" ; ".png"; ".jpg"; ".doc"; ".docm"; ".docx"; ".pdf" ]
    let excludedDirs = set [ ".git"; ".paket"; "packages"; "paket-files" ; ".fake" ] 
    let dirs = Directory.GetDirectories(curDir, "*", SearchOption.TopDirectoryOnly)
               |> Seq.filter (fun d -> 
                     let di = DirectoryInfo(d)
                     not (excludedDirs.Contains(di.Name))
                  )
    let files = dirs 
                |> Seq.collect (fun d -> Directory.GetFiles((curDir @@ d), "*", SearchOption.AllDirectories))
                |> Seq.filter (fun f ->
                       if not (excludedExts.Contains(Path.GetExtension(f))) then 
                           File.ReadAllText(f).Contains("\r\n")
                       else false)
    
    if not (Seq.isEmpty files) then
        traceImportant "Found files that uses CRLF: "
        files 
        |> Seq.iter( fun f -> trace (sprintf "    %s" f))
)

// Copies binaries from default VS location to expected bin folder
// But keeps a subdirectory structure for each project in the 
// src folder to support multiple project outputs
let CopyBinariesFun target =
    !! "src/**/*.??proj"
    |>  Seq.map (fun f -> ((System.IO.Path.GetDirectoryName f) @@ "bin" @@ target, "bin" @@ target @@ (System.IO.Path.GetFileNameWithoutExtension f)))
    |>  Seq.iter (fun (fromDir, toDir) -> if Directory.Exists(fromDir) then                                            
                                            CopyDir toDir fromDir (fun _ -> true)
#if MONO
                                          // MONO does not build WPF related projects
#else
                                          else
                                            failwith (sprintf "Cannot find directory %s" fromDir)
#endif                                          
                 )
    // A customized step to copy PrajnaClientExt.exe to the folder of PrajnaClient, so they are co-located
    let clientFolder = "bin" @@ target @@ "Client"
    !! ( ("bin" @@ target @@ "ClientExt") + "/PrajnaClientExt*" )
    |> Seq.iter (fun f -> CopyFile clientFolder f)

Target "CopyBinaries" (fun _ -> CopyBinariesFun("Debugx64"))

Target "CopyDebugBinaries" (fun _ -> CopyBinariesFun("Debugx64"))

Target "CopyReleaseBinaries" (fun _ -> CopyBinariesFun("Releasex64"))

// Targets for the D/R dependence chains
Target "CopyBinariesD" (fun _ -> CopyBinariesFun("Debugx64"))
Target "CopyBinariesR" (fun _ -> CopyBinariesFun("Releasex64"))

// --------------------------------------------------------------------------------------
// Clean build results
let cleanFunc dir =
    let pattern = sprintf "%s/**/*.??proj" dir
    !! pattern 
    |> Seq.iter (fun f ->  let dir = (System.IO.Path.GetDirectoryName f)
                           CleanDirs [ dir @@ "bin"; dir @@ "obj" ])

Target "Clean" (fun _ ->
    CleanDirs ["bin"; "temp"]
    cleanFunc "src"
    cleanFunc "tests"
    cleanFunc "samples"
)

Target "CleanDocs" (fun _ ->
    CleanDirs ["docs/output"]
)

// --------------------------------------------------------------------------------------
// Close handles that are locked by Visual Studio
// VS sometimes locks the file handle for output files such as the XML documents
Target "FreeHandlesFromVS" ( fun _ ->
    let identity = System.Security.Principal.WindowsIdentity.GetCurrent();
    let principal = System.Security.Principal.WindowsPrincipal(identity);
    // Only works if the build script is elevated
    if principal.IsInRole (System.Security.Principal.WindowsBuiltInRole.Administrator) then
        let dir = curDir
        let handle = Path.Combine(dir, @"paket-files\download.sysinternals.com\Handle.exe")
        let vsName = "devenv"    
        let args = sprintf "-p %s %s -accepteula" vsName dir
        let p = new System.Diagnostics.Process()
        p.StartInfo.UseShellExecute <- false
        p.StartInfo.RedirectStandardOutput <- true
        p.StartInfo.FileName <- handle
        p.StartInfo.Arguments <- args
        p.Start() |> ignore
        let output = p.StandardOutput.ReadToEnd()
        p.WaitForExit()
        let lines = output.Split([| '\r'; '\n' |], StringSplitOptions.RemoveEmptyEntries)
        Array.sub lines 3 (lines.Length - 3)
              |> Array.iter( fun l -> 
                    let fields = l.Split([|':';' '|], StringSplitOptions.RemoveEmptyEntries)
                    // "devenv.exe"; "pid"; "25664"; "type"; "File"; "E74"; "C"; "Path"
                    let pid = fields.[2]
                    let handleType = fields.[4]
                    let hex = fields.[5]   
                    if handleType = "File" then
                        let args = sprintf "-p %s -c %s -y -accepteula" pid hex
                        Shell.Exec(handle, args) |> ignore
                 )
)

// --------------------------------------------------------------------------------------
// Build library & test project

let buildFunc rebuild configuration platform = 
    let logfilePrefix = "msbuild_" + configuration + "_" + platform
    let setParams defaults =
            { defaults with
                Verbosity = Some(Quiet)
#if MONO
                // Issue: with "Clean", somehow gets lots of compilation errors
                Targets = if rebuild then ["Rebuild"] else ["Build"]
#else                
                Targets = if rebuild then ["Clean"; "Rebuild"] else ["Build"]
#endif
                MaxCpuCount = Environment.ProcessorCount |> Some |> Some
                NodeReuse = false
                NoConsoleLogger = false
#if MONO
#else
                FileLoggers = [
                                { Number = 1; 
                                  Filename = (logfilePrefix + ".log") |> Some; 
                                  Verbosity = Normal |> Some; 
                                  Parameters = [ShowTimestamp; ShowCommandLine] |> Some }
                                { Number = 2; 
                                  Filename = logfilePrefix + ".wrn" |> Some; 
                                  Verbosity = Diagnostic |> Some; 
                                  Parameters = [WarningsOnly; ShowTimestamp; ShowCommandLine] |> Some }
                                { Number = 3; 
                                  Filename = logfilePrefix + ".err" |> Some; 
                                  Verbosity = Diagnostic |> Some; 
                                  Parameters = [ErrorsOnly; ShowTimestamp; ShowCommandLine] |> Some }
                                { Number = 4; 
                                  Filename = logfilePrefix + ".sum" |> Some; 
                                  Verbosity = Minimal |> Some
                                  Parameters = [Summary; PerformanceSummary] |> Some }
                              ] |> Some
#endif
                Properties =
                    [
                        "Configuration",configuration
                        "Platform", platform
                        "DebugSymbols", "True"
                        "TreatWarningsAsErrors", "True"
                    ]
             }
    !! solutionFile
    |> Seq.iter (fun f -> build setParams f)

Target "Build" (fun _ -> buildFunc false "Debug" "x64")

Target "Rebuild" (fun _ -> buildFunc true "Debug" "x64")

Target "BuildRelease" (fun _ -> buildFunc false "Release" "x64")

Target "RebuildRelease" (fun _ -> buildFunc true "Release" "x64")

// --------------------------------------------------------------------------------------
// Check whether the generated XML documents are well formed
let checkXmlDocs target =
    !! "src/**/*.??proj"
    |>  Seq.map (fun f -> 
                    let file = (System.IO.Path.GetFileNameWithoutExtension f)
                    let dir = (System.IO.Path.GetDirectoryName f) @@ "bin" @@ target
                    // CoreLib is the only exception that the project name and the DLL name does not match (maybe we should fix it)
                    if String.Compare(file, "CoreLib", StringComparison.OrdinalIgnoreCase) = 0 then 
                        dir @@ "Prajna.xml" 
                    else 
                        let doc = dir @@ (file + ".xml")                                                            
                        if File.Exists(doc) then
                            doc
                        else
                            let f = dir @@ ("Prajna." + file + ".xml")
                            if File.Exists(f) then
                                f
                            else
                                let sf = dir @@ ("Prajna.Service." + file + ".xml")
                                if File.Exists(sf) then
                                    sf
                                else
                                    dir @@ ("Prajna" + file + ".xml")
                )
    |>  Seq.iter (fun f -> 
                    try
                        if File.Exists(f) then
                            // the load will verify whether the XML doc is well formed                        
                            XDocument.Load(f) |> ignore
#if MONO
                        // Mono does not build WPF related projects
#else
                        else
                            failwith (sprintf "Cannot find file %s" f)
#endif
                    with
                        // match any execptions
                        | ex -> failwith(sprintf "Error in '%s': %s" f ex.Message)
                  )

// Targets for the dependence chain of All/AllRelease
Target "CheckXmlDocs" (fun _ -> checkXmlDocs "Debugx64")
Target "CheckReleaseXmlDocs" (fun _ -> checkXmlDocs "Releasex64")

// Targets for the dependence chain of D/R
Target "CheckXmlDocsD" (fun _ -> checkXmlDocs "Debugx64")
Target "CheckXmlDocsR" (fun _ -> checkXmlDocs "Releasex64")
// --------------------------------------------------------------------------------------
// Run the unit tests using test runner

let runTests (target:string) =
    let timer : System.Timers.Timer = null
    try
        if isTravisBuild then
            // For travis build, if there's no output for 10 minutes, the build will be cancelled,
            // and addons etc will not be executed. To keep the build alive, use a timer to output 
            // to stdout every 5 minutes. So the test suite timeout can eventually kick in.
            // Note: if NUnit below supports per test timeout, then there's no need to do this, just
            // need to set the per test timeout less than 10 minutes.
            let timer = new System.Timers.Timer(5000.0 * 60.0)
            timer.Elapsed.Add(fun _ -> trace("tests in progress ..."))
            timer.Start()
        try
            let pattern = String.Format(testAssemblies, target)
            traceImportant (sprintf "Running NUnit with parameter %s" pattern)
            !! pattern
            |> NUnit (fun p ->
                { p with
                    ExcludeCategory = "Performance"
                    DisableShadowCopy = true
                    ProcessModel = SeparateProcessModel
                    Domain = SingleDomainModel
                    // Though from the doc, this looks like per test-case timeout, but it works as whole test suite timeout
                    // We will need to increase the timeout, as otherwise, the test timeout in Linux container
                    TimeOut = TimeSpan.FromMinutes 60. 
                    OutputFile = sprintf "TestResults_%s.xml" target})    
        with
        | ex -> traceImportant(sprintf "Test run fails: %A" ex)
    finally
        if timer <> null then
            timer.Dispose()

        traceImportant(sprintf "runTests %s completed: execute final tasks" target)
        // Kill nunit-agent.exe if it is alive (it can sometimes happen when the test timeouts)
        let mutable cnt = 0;
        let mutable procs = Diagnostics.Process.GetProcessesByName("nunit-agent") |> Array.filter (fun p -> not p.HasExited)
        while (not (procs |> Array.isEmpty)) && cnt < 6 do
            Threading.Thread.Sleep(TimeSpan.FromSeconds(10.))
            procs <- Diagnostics.Process.GetProcessesByName("nunit-agent") 
            procs |> Array.iter (fun p -> if not p.HasExited then
                                              printfn "Kill process %s (%i)" p.ProcessName p.Id
                                              try p.Kill() with | _ -> ()) // tolerate failure
            cnt <- cnt + 1;
            procs <- Diagnostics.Process.GetProcessesByName("nunit-agent") |> Array.filter (fun p -> not p.HasExited)
        if (not (procs |> Array.isEmpty)) then trace "fail to kill nunit-agent"
        

Target "RunTests" (fun _ -> runTests "Debugx64")
Target "RunReleaseTests" (fun _ -> runTests "Releasex64")

#if MONO
#else
// --------------------------------------------------------------------------------------
// SourceLink allows Source Indexing on the PDB generated by the compiler, this allows
// the ability to step through the source code of external libraries http://ctaggart.github.io/SourceLink/ 

Target "SourceLink" (fun _ ->
    let baseUrl = (sprintf "%s/%s/" gitRaw project) + "%var2%/{0}"

    // Collect all the PDBs that have been generated before being source indexed
    let dic = ConcurrentDictionary<_,List<_>>(StringComparer.OrdinalIgnoreCase)
    !! "**/**/*.pdb"
    |> Seq.filter( fun pdb -> not (pdb.Contains("Nano")) )
    |> Seq.iter ( fun pdb -> let shortName = Path.GetFileName( pdb )
                             let entry = dic.GetOrAdd( shortName, fun _ -> List<_>() )
                             entry.Add( pdb )
                        )

    !! "src/**/*.??proj"
    |> Seq.filter( fun projFile -> not (projFile.Contains("Nano")) )
    |> Seq.iter (fun projFile -> 
        //trace (sprintf "Start to work on projFile %s" projFile)
        let proj = VsProj.LoadRelease projFile 
        try
            //trace (sprintf "Start to sourceLink file %A %A %A %A" proj.CompilesNotLinked proj.OutputFilePdb __SOURCE_DIRECTORY__ baseUrl)
            SourceLink.Index proj.CompilesNotLinked proj.OutputFilePdb __SOURCE_DIRECTORY__ baseUrl 
            //trace (sprintf "Done SourceLink.Index" )
            // Replace the same PDB in all directories with the source indexed PDB
            let pdbShortName = Path.GetFileName( proj.OutputFilePdb )
            let bExist, entry = dic.TryGetValue( pdbShortName )
            if bExist then 
                for file1 in entry do 
                    if String.Compare( Path.GetFullPath(proj.OutputFilePdb), Path.GetFullPath(file1), true )<>0 then 
                        // trace ( sprintf "To copy file %s to %s " proj.OutputFilePdb file1 )
                        File.Copy( proj.OutputFilePdb, file1, true )
            //trace (sprintf "Done sourceLink" )
        with 
        | ex -> trace (sprintf "Fail to sourceLink file '%s': %s" proj.OutputFilePdb ex.Message)
    )

    // Make sure the bin folder contains source indexed PDB
    //trace (sprintf "Start copy binary" )
    CopyBinariesFun("Releasex64")
    //trace (sprintf "End copy binary" )
)
#endif

// --------------------------------------------------------------------------------------
// Build a NuGet package

Target "NuGet" (fun _ ->
    Paket.Pack(fun p -> 
        { p with
            OutputPath = "bin"
            Version = release.NugetVersion
            ReleaseNotes = toLines release.Notes})
)

Target "PublishNuget" (fun _ ->
    Paket.Push(fun p -> 
        { p with
            WorkingDir = "bin" })
)


// --------------------------------------------------------------------------------------
// Generate the documentation

let fakePath = "packages" @@ "FAKE" @@ "tools" @@ "FAKE.exe"
let fakeStartInfo script workingDirectory args fsiargs environmentVars =
    (fun (info: System.Diagnostics.ProcessStartInfo) ->
        info.FileName <- System.IO.Path.GetFullPath fakePath
        info.Arguments <- sprintf "%s --fsiargs -d:FAKE %s \"%s\"" args fsiargs script
        info.WorkingDirectory <- workingDirectory
        let setVar k v =
            info.EnvironmentVariables.[k] <- v
        for (k, v) in environmentVars do
            setVar k v
        setVar "MSBuild" msBuildExe
        setVar "GIT" Git.CommandHelper.gitPath
        setVar "FSI" fsiPath)

/// Run the given buildscript with FAKE.exe
let executeFAKEWithOutput workingDirectory script fsiargs envArgs =
    let exitCode =
        ExecProcessWithLambdas
            (fakeStartInfo script workingDirectory "" fsiargs envArgs)
            TimeSpan.MaxValue false ignore ignore
    System.Threading.Thread.Sleep 1000
    exitCode

// Documentation
let buildDocumentationTarget fsiargs target =
    trace (sprintf "Building documentation (%s), this could take some time, please wait..." target)
    let exit = executeFAKEWithOutput "docs/tools" "generate.fsx" fsiargs ["target", target]
    if exit <> 0 then
        failwith "generating reference documentation failed"
    ()
Target "GenerateReferenceDocs" (fun _ ->
    buildDocumentationTarget "-d:RELEASE -d:REFERENCE" "Default"
)

let generateHelp' fail debug =
    let args =
        if debug then "--define:HELP"
        else "--define:RELEASE --define:HELP"
    try
        buildDocumentationTarget args "Default"
        traceImportant "Help generated"
    with
    | e when not fail ->
        traceImportant "generating help documentation failed"

let generateHelp fail =
    generateHelp' fail false

Target "GenerateHelp" (fun _ ->
    DeleteFile "docs/content/release-notes.md"
    CopyFile "docs/content/" "RELEASE_NOTES.md"
    Rename "docs/content/release-notes.md" "docs/content/RELEASE_NOTES.md"

    DeleteFile "docs/content/license.md"
    CopyFile "docs/content/" "LICENSE.txt"
    Rename "docs/content/license.md" "docs/content/LICENSE.txt"

    generateHelp true
)

Target "GenerateHelpDebug" (fun _ ->
    DeleteFile "docs/content/release-notes.md"
    CopyFile "docs/content/" "RELEASE_NOTES.md"
    Rename "docs/content/release-notes.md" "docs/content/RELEASE_NOTES.md"

    DeleteFile "docs/content/license.md"
    CopyFile "docs/content/" "LICENSE.txt"
    Rename "docs/content/license.md" "docs/content/LICENSE.txt"

    generateHelp' true true
)

Target "KeepRunning" (fun _ ->    
    use watcher = !! "docs/content/**/*.*" |> WatchChanges (fun changes ->
         generateHelp false
    )

    traceImportant "Waiting for help edits. Press any key to stop."

    System.Console.ReadKey() |> ignore

    watcher.Dispose()
)

Target "GenerateDocs" DoNothing

let createIndexFsx lang =
    let content = """(*** hide ***)
// This block of code is omitted in the generated HTML documentation. Use 
// it to define helpers that you do not want to show in the documentation.
#I "../../../bin"

(**
F# Project Scaffold ({0})
=========================
*)
"""
    let targetDir = "docs/content" @@ lang
    let targetFile = targetDir @@ "index.fsx"
    ensureDirectory targetDir
    System.IO.File.WriteAllText(targetFile, System.String.Format(content, lang))

Target "AddLangDocs" (fun _ ->
    let args = System.Environment.GetCommandLineArgs()
    if args.Length < 4 then
        failwith "Language not specified."

    args.[3..]
    |> Seq.iter (fun lang ->
        if lang.Length <> 2 && lang.Length <> 3 then
            failwithf "Language must be 2 or 3 characters (ex. 'de', 'fr', 'ja', 'gsw', etc.): %s" lang

        let templateFileName = "template.cshtml"
        let templateDir = "docs/tools/templates"
        let langTemplateDir = templateDir @@ lang
        let langTemplateFileName = langTemplateDir @@ templateFileName

        if System.IO.File.Exists(langTemplateFileName) then
            failwithf "Documents for specified language '%s' have already been added." lang

        ensureDirectory langTemplateDir
        Copy langTemplateDir [ templateDir @@ templateFileName ]

        createIndexFsx lang)
)

// --------------------------------------------------------------------------------------
// Release Scripts

Target "ReleaseDocs" (fun _ ->
    let tempDocsDir = "temp/gh-pages"
    CleanDir tempDocsDir
    Repository.cloneSingleBranch "" (gitHome + "/" + gitName + ".git") "gh-pages" tempDocsDir

    CopyRecursive "docs/output" tempDocsDir true |> tracefn "%A"
    StageAll tempDocsDir
    Git.Commit.Commit tempDocsDir (sprintf "Update generated documentation for version %s" release.NugetVersion)
    Branches.push tempDocsDir
)

#load "paket-files/fsharp/FAKE/modules/Octokit/Octokit.fsx"
open Octokit

Target "PublicRelease" (fun _ ->
    let user =
        match getBuildParam "github-user" with
        | s when not (String.IsNullOrWhiteSpace s) -> s
        | _ -> getUserInput "Username: "
    let pw =
        match getBuildParam "github-pw" with
        | s when not (String.IsNullOrWhiteSpace s) -> s
        | _ -> getUserPassword "Password: "
    let remote =
        Git.CommandHelper.getGitResult "" "remote -v"
        |> Seq.filter (fun (s: string) -> s.EndsWith("(push)"))
        |> Seq.tryFind (fun (s: string) -> s.Contains(gitOwner + "/" + gitName))
        |> function None -> gitHome + "/" + gitName | Some (s: string) -> s.Split().[0]
    StageAll ""
    Git.Commit.Commit "" (sprintf "Bump version to %s" release.NugetVersion)
    Branches.pushBranch "" remote (Information.getBranchName "")

    Branches.tag "" release.NugetVersion
    Branches.pushTag "" remote release.NugetVersion
    
    // release on github
    createClient user pw
    |> createDraft gitOwner gitName release.NugetVersion (release.SemVer.PreRelease <> None) release.Notes 
    // TODO: |> uploadFile "PATH_TO_FILE"    
    |> releaseDraft
    |> Async.RunSynchronously
)

Target "BuildPackage" DoNothing

// --------------------------------------------------------------------------------------
// Run all targets by default. Invoke 'build <Target>' to override

Target "Debug" DoNothing  // Default, clean build of Debug
Target "Release" DoNothing // Clean build of Release
Target "D" DoNothing // Incremental build of Debug
Target "R" DoNothing // Incremental build of Release

"Clean" 
  ==> "SearchCrLf"

"Build" // incremental build of debug
  ==> "CheckXmlDocsD"
  ==> "CopyBinariesD"
  ==> "D"

"BuildRelease" // incremental build of release
  ==> "CheckXmlDocsR"
  ==> "CopyBinariesR"
  ==> "R"   

"PrintBuildMachineConfiguration"
  ==> "Clean"
  ==> "AssemblyInfo"
  ==> "Rebuild"
  ==> "CheckXmlDocs"
  ==> "CopyBinaries"
  ==> "RunTests"
  ==> "Debug"

"PrintBuildMachineConfiguration"
  ==> "Clean"
  ==> "AssemblyInfo"
  ==> "RebuildRelease"
  ==> "CheckReleaseXmlDocs"
  ==> "CopyReleaseBinaries"
  ==> "RunReleaseTests"
  =?> ("GenerateReferenceDocs",isLocalBuild)
  =?> ("GenerateDocs",isLocalBuild)
  ==> "Release"
  =?> ("ReleaseDocs",isLocalBuild)

"Release" 
#if MONO
#else
  // =?> ("SourceLink", Pdbstr.tryFind().IsSome )
  ==> "SourceLink"
#endif
  ==> "NuGet"
  ==> "BuildPackage"

"CleanDocs"
  ==> "GenerateHelp"
  ==> "GenerateReferenceDocs"
  ==> "GenerateDocs"

"CleanDocs"
  ==> "GenerateHelpDebug"

"GenerateHelp"
  ==> "KeepRunning"
    
"ReleaseDocs"
  ==> "PublicRelease"

"BuildPackage"
  ==> "PublishNuget"
  ==> "PublicRelease"

RunTargetOrDefault "Debug"
