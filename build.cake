// Build script for the .NET implementation under src.
//
// The steps live here rather than in the workflow that calls them, and so does the reasoning behind
// each one, because a change to a step and a change to its explanation should be the same edit.
// What is left to the workflow is the part that is genuinely the CI system's: which machine to run
// on, what the environment holds, what to keep, and what to report where.
//
// Run it locally with:
//
//     dotnet tool restore
//     dotnet cake
//
// which restores, builds, tests with coverage, packs, and runs the inspection. Publishing is
// deliberately not part of the default target; see the Publish task.
//
// On CI the tasks are driven one phase at a time, with --exclusive, so that a failure is
// attributed to the phase it happened in rather than all of them reading as a build failure. The
// dependencies below are therefore what a local run follows, not what CI relies on; keep them
// accurate anyway, since `dotnet cake --target=Pack` on a clean tree has to work.
//
// .github/workflows/build.yml is what drives them that way. It used to be that and appveyor.yml
// both, running the same targets on the same commits while the two were compared; Actions won, on
// queue time above all, and AppVeyor is gone. Nothing in this file knows which CI is running it any
// more, which is the state it was always meant to reach.
//
// Package versions are NOT set here. Each packable project derives its own version from git tags
// via MinVer (see src/Directory.Build.props), so pushing sodaflow-async-2.1.0 releases only
// SodaFlow.Async and leaves every other package on its own last tag.

// Reading the SARIF only. Nothing here reports it: the workflow hands the same file to code
// scanning. Keeping these two to the reading half is what lets them track the Cake version rather
// than being held at 5.9.1 to agree with a reporting addin that has no Cake 6 release.
#addin nuget:?package=Cake.Issues&version=6.0.0
#addin nuget:?package=Cake.Issues.Sarif&version=6.0.0

using System.Xml.Linq;

var target = Argument("target", "Default");
var configuration = Argument("configuration", "Release");

// Which sample Inspect-Sample looks at. Empty for every other target.
var sampleName = Argument("sample", string.Empty);

var solution = File("./src/SodaFlow.slnx");
var artifactsDirectory = Directory("./artifacts");
var coverageDirectory = Directory("./coverage");
var inspectionDirectory = Directory("./inspection");
// Where inspectcode keeps its analysis caches, named explicitly so they land somewhere stable
// rather than in a temporary directory it picks itself. That is what makes them worth keeping
// between runs: a cold inspection of this solution takes 135 seconds and a warm one 26, and even a
// cache taken from before an edit comes in at 36 - it revalidates what changed rather than
// trusting itself, which is the property that makes reusing one safe against a zero threshold.
//
// Deliberately not inspectionDirectory: that one is cleaned at the start of every inspection,
// which is exactly what must not happen to this.
var inspectionCacheDirectory = Directory("./.inspectcode-cache");
// The canonical inspection settings, and the only ones CI reads. Rider pairs a .DotSettings file
// with the solution beside it, so every solution needs a copy of its own; CI is under no such
// constraint and points every inspection here. The copies therefore exist for the editor alone,
// which is what makes them worth guarding: drift between them is drift between what CI enforces
// and what Rider shows, and that disagreement is the thing this whole setup exists to prevent.
var inspectionSettings = File("./src/SodaFlow.sln.DotSettings");

// The editor-only copies, which must match it byte for byte.
var mirroredInspectionSettings = new[]
{
    File("./samples/Counter/SodaFlow.Samples.Counter.sln.DotSettings"),
    File("./samples/Search/SodaFlow.Samples.Search.sln.DotSettings"),
    File("./samples/Bounce/SodaFlow.Samples.Bounce.sln.DotSettings"),
};
// Overridable so that a release can be rehearsed against a local folder feed - pass
// --nuget-source=<path> - without the rehearsal being one typo away from a real publish. nuget.org
// does not allow a version to be deleted or reused, so the default being the only reachable value
// was worth giving up.
var nugetSource = Argument("nuget-source", "https://api.nuget.org/v3/index.json");

//////////////////////////////////////////////////////////////////////
// SETUP
//////////////////////////////////////////////////////////////////////

Setup(context =>
{
    Information("Building SodaFlow in {0}.", configuration);
});

//////////////////////////////////////////////////////////////////////
// TASKS
//////////////////////////////////////////////////////////////////////

Task("Info")
    .Description("Prints the SDK the build is running against.")
    .Does(() =>
{
    // Worth having in the log: a version difference between the agent and a developer's machine is
    // the first thing to check when a build reproduces locally but not on CI.
    var exitCode = StartProcess("dotnet", new ProcessSettings { Arguments = "--info" });
    if (exitCode != 0)
    {
        throw new Exception($"dotnet --info failed with exit code {exitCode}.");
    }
});

Task("Restore")
    .Description("Restores every project in the solution.")
    .IsDependentOn("Info")
    .Does(() =>
{
    // Every project here is SDK-style, so a plain restore covers all of them and there is nothing
    // left for nuget.exe to handle that this does not.
    DotNetRestore(solution);
});

Task("Build")
    .Description("Builds the solution.")
    .IsDependentOn("Verify-Inspection-Settings")
    .IsDependentOn("Restore")
    .Does(() =>
{
    // dotnet build rather than msbuild: with no legacy projects left there is no longer a reason to
    // reach for the one tool that could build both, and this handles the console benchmark too.
    DotNetBuild(
        solution,
        new DotNetBuildSettings
        {
            Configuration = configuration,
            NoRestore = true,
        });
});

Task("Test")
    .Description("Runs every test project, collecting coverage as it goes.")
    .IsDependentOn("Build")
    .Does(() =>
{
    CleanDirectory(coverageDirectory);

    // One run over the solution. The test projects run on Microsoft.Testing.Platform rather than
    // VSTest - global.json selects it - so each of them builds into an executable that hosts its own
    // run, and dotnet test starts them. Everything after the -- is theirs rather than the SDK's,
    // which is why none of it is expressed through DotNetTestSettings.
    //
    // Collection is still the Microsoft Code Coverage collector, reached now through
    // Microsoft.Testing.Extensions.CodeCoverage, which arrives with TUnit. It writes Cobertura
    // directly - the format the Coveralls reporter accepts - so nothing has to be converted and
    // nothing merged. Scoping and exclusions live in coverage.runsettings, which the platform takes
    // unchanged: --coverage-settings reads the same file --settings used to.
    //
    // SodaFlow.Benchmarks contributes no tests: it is a console application driving
    // BenchmarkDotNet, not a test project, and the run does not mind.
    //
    // Three earlier approaches are recorded so they are not retried blindly. The NUnit console
    // runner under OpenCover profiled cleanly but took close to ten minutes here and failed tests
    // that pass under dotnet test. OpenCover wrapped around dotnet test hangs, because dotnet.exe
    // is a CoreCLR host and OpenCover's profiler is a .NET Framework CLR profiler.
    //
    // Coverlet broke the F# suite, whose assemblies compiled against FSharp.Core 4.5.0.0 while
    // 10.0.0.0 was deployed; its instrumentation did not honor the binding redirect. That
    // particular reason is gone - every project here now compiles against the FSharp.Core it
    // deploys - so Coverlet is untried rather than ruled out. Nothing has been measured about it
    // since, and the collector in use costs nothing to keep.
    var resultsDirectory = MakeAbsolute(coverageDirectory).FullPath;
    var coverageSettings = MakeAbsolute(File("./coverage.runsettings")).FullPath;

    DotNetTest(
        solution,
        new DotNetTestSettings
        {
            Configuration = configuration,
            NoBuild = true,
            ArgumentCustomization = args => args
                .Append("--")
                .Append("--results-directory").AppendQuoted(resultsDirectory)
                .Append("--coverage")
                .Append("--coverage-output-format").Append("cobertura")
                .Append("--coverage-settings").AppendQuoted(coverageSettings)
                .Append("--report-trx"),
        });

    // --report-trx writes one file per test project per framework, named for both. Nothing is done
    // with them here: the workflow uploads them as an artifact, and test-report.yml turns them into
    // a check run listing every test. This file produces them and stops, which is the line it draws
    // everywhere - build steps here, reporting surfaces in the workflow that has one.

    // Checked here, where the reports are made, rather than wherever they are next read. Sending
    // them to Coveralls is the workflow's job now, and a run that submitted nothing because nothing
    // was written would look exactly like a build that got faster - so the check belongs to the step
    // that was supposed to produce them, and fails it.
    //
    // Not a recursive glob, and that matters to whoever globs these next: the collector writes every
    // report twice, once here and once under a machine-and-timestamp directory in the layout VSTest
    // used. The two copies are byte for byte the same file, so a `**` pattern finds every report
    // twice and submits it twice. .github/workflows/build.yml globs the same way for the same
    // reason.
    //
    // There is more than one report even so. A solution-level run writes one per test project per
    // framework, each covering only the assemblies that project touched, which is why they all have
    // to go up together rather than one standing in for the rest.
    var reports = GetFiles($"{coverageDirectory.Path}/*.cobertura.xml")
        .OrderBy(r => r.FullPath, StringComparer.Ordinal)
        .ToList();

    if (reports.Count == 0)
    {
        throw new Exception("No Cobertura report was produced.");
    }

    Information("Coverage reports ({0}):", reports.Count);
    foreach (var report in reports)
    {
        Information("  {0}", report.FullPath);
    }
});

Task("Pack")
    .Description("Packs every publishable project.")
    .IsDependentOn("Test")
    .Does(() =>
{
    CleanDirectory(artifactsDirectory);

    // One pack over the whole solution. It packs the publishable projects and skips the test and
    // benchmark ones, each of which sets IsPackable false. That used to be inferred for the test
    // projects, from the reference to Microsoft.NET.Test.Sdk the SDK reads as IsTestProject; moving
    // to Microsoft.Testing.Platform removed that reference, so every one of them now says so.
    DotNetPack(
        solution,
        new DotNetPackSettings
        {
            Configuration = configuration,
            OutputDirectory = artifactsDirectory,
        });

    foreach (var package in GetFiles($"{artifactsDirectory.Path}/*.nupkg").OrderBy(p => p.FullPath))
    {
        Information(package.GetFilename().FullPath);
    }
});

// compiler(line): RuleId: text - the shape a compiler error takes, because it is the shape an
// editor, a log reader and a person all already know how to scan.
string Describe(IIssue issue) =>
    $"{issue.AffectedFileRelativePath?.FullPath ?? "<solution>"}"
    + $"({issue.Line?.ToString() ?? "-"}): {issue.RuleId}: {issue.MessageText}";

// A dependency of Build rather than a step of its own in the workflow. As a dependency it runs
// everywhere without being listed anywhere - a local `dotnet cake` and CI both reach it through
// Build - so there is no list of steps for it to quietly fall off.
//
// It needs nothing compiled, so as a dependency of Build it still runs before anything is built
// and costs milliseconds.
Task("Verify-Inspection-Settings")
    .Description("Checks that every solution's inspection settings match the canonical ones.")
    .Does(() =>
{
    var canonicalPath = MakeAbsolute(inspectionSettings.Path).FullPath;
    var expected = System.IO.File.ReadAllBytes(canonicalPath);
    var problems = new List<string>();

    foreach (var mirrored in mirroredInspectionSettings)
    {
        var path = MakeAbsolute(mirrored.Path).FullPath;

        if (!System.IO.File.Exists(path))
        {
            problems.Add($"{mirrored.Path} is missing.");
            continue;
        }

        if (!System.IO.File.ReadAllBytes(path).SequenceEqual(expected))
        {
            problems.Add($"{mirrored.Path} differs from {inspectionSettings.Path}.");
        }
    }

    foreach (var problem in problems)
    {
        Error("  {0}", problem);
    }

    if (problems.Count > 0)
    {
        throw new Exception(
            $"{problems.Count} inspection settings file(s) out of step with {inspectionSettings.Path}, "
            + "listed above. Copy that file over them - they are meant to be byte-identical.");
    }

    Information(
        "All {0} mirrored inspection settings match {1}.",
        mirroredInspectionSettings.Length,
        inspectionSettings.Path);
});

// The whole of an inspection run, shared by the solution under src and by each sample. Extracted
// rather than duplicated because the reporting is the interesting part - the zero threshold, the
// listing before the throw - and two copies of that would drift.
void RunInspection(FilePath solutionPath, FilePath reportPath, string description)
{
    // inspectcode creates this itself, but only after deciding it is usable; making it first keeps
    // a first run on a clean tree from differing from every run after it.
    EnsureDirectoryExists(inspectionCacheDirectory);

    // inspectcode comes from the jetbrains.resharper.globaltools local tool, pinned alongside Cake
    // in .config/dotnet-tools.json, so the agent inspects with the version a developer does. There
    // is no Cake alias for it; a process call is the whole of the integration.
    var arguments = new ProcessArgumentBuilder()
        .Append("jb")
        .Append("inspectcode")
        .AppendQuoted(MakeAbsolute(solutionPath).FullPath)
        .AppendSwitchQuoted("--output", "=", MakeAbsolute(reportPath).FullPath)
        .Append("--format=Sarif")
        // The same settings Rider applies, named explicitly rather than left to inspectcode's
        // lookup: that lookup pairs a .DotSettings file with a solution of the same name, and the
        // solutions here are .slnx while the settings are .sln.DotSettings.
        //
        // Absolute, and that is load-bearing rather than tidy: inspectcode ignores a relative
        // --settings path without saying so, and inspects with its own defaults instead.
        .AppendSwitchQuoted("--settings", "=", MakeAbsolute(inspectionSettings.Path).FullPath)
        // See the declaration of this directory for what it buys and why reusing it is safe. It is
        // given here rather than left to inspectcode's own temporary location so that CI can carry
        // it between runs and a developer keeps one between invocations.
        .AppendSwitchQuoted("--caches-home", "=", MakeAbsolute(inspectionCacheDirectory).FullPath)
        // Absolute paths in the SARIF, which is what lets the issues be reported against paths from
        // the repository root. Left relative, they come out relative to the solution directory -
        // CSharp/SodaFlow/Foo.cs for a file that lives at src/CSharp/SodaFlow/Foo.cs - because the
        // reader takes the URI as written rather than rebasing it.
        .Append("--absolute-paths")
        // Already built by whatever depends on this. Building it again would double the cost of the
        // phase for no gain, so tell inspectcode which configuration it is looking at instead of
        // letting it pick one and build it.
        .Append("--no-build")
        .Append($"--properties:Configuration={configuration}")
        .Append("--verbosity=WARN");

    var exitCode = StartProcess("dotnet", new ProcessSettings { Arguments = arguments });
    if (exitCode != 0)
    {
        throw new Exception($"inspectcode failed (exit {exitCode}).");
    }

    var issues = ReadIssues(
            SarifIssuesFromFilePath(reportPath),
            Context.Environment.WorkingDirectory)
        .OrderBy(i => i.AffectedFileRelativePath?.FullPath ?? string.Empty, StringComparer.Ordinal)
        .ThenBy(i => i.Line ?? 0)
        .ToList();

    // Listed before the throw below, not after, because a build that fails on the inspection is
    // exactly the build that needs to say what the inspection found. This log is the whole of the
    // reporting done here; the SARIF it was read from is handed to code scanning by the workflow,
    // which puts the findings on the lines they are about.
    //
    // There used to be a second copy of this, pushed to AppVeyor's messages tab through Cake's own
    // AppVeyor provider. Worth recording why it was that and not Cake.Issues.PullRequests.AppVeyor,
    // in case anyone reaches for the equivalent again: that addin has no release built against Cake
    // 6, and under Cake 6 it dies with MissingMethodException on
    // Spectre.Console.Text..ctor(String, Style) as soon as it formats anything. Keeping it would
    // have meant holding the entire build at Cake 5 to satisfy one package.
    Information("InspectCode found {0} issue(s) in {1}.", issues.Count, description);
    foreach (var issue in issues)
    {
        Information("  {0}", Describe(issue));
    }

    // Anything at all fails the build, suggestions included - inspectcode reports SUGGESTION and
    // above by default, so this gates on everything it is willing to say. The threshold is zero
    // rather than a count because a count is a number that only ever goes up: it has to be raised
    // to land the change that raised it, and raising it is easier than fixing the thing.
    //
    // The way to make an inspection stop failing the build, other than fixing it, is to turn the
    // rule off or lower it in src/SodaFlow.sln.DotSettings, where Rider will then agree with CI.
    // Silencing something here would put CI and the editor into disagreement, which is the problem
    // this whole task exists to avoid.
    if (issues.Count > 0)
    {
        throw new Exception(
            $"InspectCode found {issues.Count} issue(s) in {description}, listed above. Fix them, or "
            + "change the rule in src/SodaFlow.sln.DotSettings.");
    }
}

Task("Inspect-Code")
    .Description("Runs JetBrains InspectCode over the solution and reports what it finds.")
    .IsDependentOn("Build")
    .Does(() =>
{
    CleanDirectory(inspectionDirectory);

    RunInspection(
        solution.Path,
        (inspectionDirectory + File("inspectcode.sarif")).Path,
        solution.Path.FullPath);
});

// One sample per run, named by --sample, because that is the shape of the samples workflow: a job
// per sample, so a failure says which sample rather than only which repository.
//
// Not dependent on Build, which compiles the solution under src. A sample does not use it - the
// samples reference published packages - and the workflow has already built the sample itself by
// the time this runs.
Task("Inspect-Sample")
    .Description("Runs JetBrains InspectCode over one sample solution. Pass --sample=Counter.")
    .Does(() =>
{
    if (string.IsNullOrWhiteSpace(sampleName))
    {
        throw new Exception("Pass which sample to inspect, for example --sample=Counter.");
    }

    var sampleSolution = File($"./samples/{sampleName}/SodaFlow.Samples.{sampleName}.slnx");

    if (!FileExists(sampleSolution))
    {
        throw new Exception($"No sample solution at {sampleSolution.Path}.");
    }

    // Not cleaned, unlike the inspection above: each sample writes its own report, and running all
    // three locally should end with all three rather than only the last.
    EnsureDirectoryExists(inspectionDirectory);

    RunInspection(
        sampleSolution.Path,
        (inspectionDirectory + File($"inspectcode-{sampleName}.sarif")).Path,
        $"the {sampleName} sample");
});

Task("Publish")
    .Description("Pushes the one package this build's tag names to nuget.org.")
    .Does(() =>
{
    // Publishing is gated on a tag, which is what makes the release schedule per-package: MinVer
    // gives a tagged build a stable version and every other build a prerelease one, so only
    // deliberate tags can ever produce something publishable.
    //
    // A tag build publishes exactly one package: the one its own tag names. A run sees the whole
    // artifacts directory, holding every package the solution produces, so pushing all of them from
    // every tag build made the publish order the order the builds happened to run in - which is not
    // something a release can control, and which let a package reach nuget.org before the
    // dependency it was built against.
    //
    // Note what this does and does not do. It makes the order controllable; it does not impose one.
    // Push the tags in dependency order, and wait for each run to publish before pushing the next.
    // That advice does not depend on how many runs a multi-tag push produces, because each run
    // publishes only the package named by the tag it was started for.
    //
    // GitHub Actions is the only place this pushes from, and the only CI there is. AppVeyor
    // published until the main build moved, and stopped in the same commit that gave the workflow
    // its publish step, so no commit ever had both able to push one tag.
    if (!BuildSystem.IsRunningOnGitHubActions ||
        GitHubActions.Environment.Workflow.RefType != GitHubActionsRefType.Tag)
    {
        Information("Not a tag build - skipping NuGet push.");
        return;
    }

    var tag = GitHubActions.Environment.Workflow.RefName;
    if (string.IsNullOrEmpty(tag))
    {
        throw new Exception(
            "This is a tag build but the tag name is empty, so there is no way to tell which " +
            "package it releases.");
    }

    // Read from the environment, and deliberately incurious about where it came from. Nothing here
    // stores a key: the workflow trades this run's OIDC token with nuget.org for one that expires
    // shortly afterwards - trusted publishing - and hands it to this task the same way a stored
    // secret used to be handed over. That is why moving off a stored key changed nothing here.
    //
    // Thrown rather than skipped, unlike every other missing-credential check here. Those guard
    // work that is worth doing anyway; this one guards the release itself, and a tag build that
    // quietly published nothing is the failure that is hardest to notice - the tag exists, the run
    // is green, and only nuget.org disagrees.
    var apiKey = EnvironmentVariable("NUGET_API_KEY");
    if (string.IsNullOrEmpty(apiKey))
    {
        throw new Exception(
            "NUGET_API_KEY is not set. It is minted by the NuGet login step in "
            + ".github/workflows/build.yml, which exchanges this run's OIDC token for a short-lived "
            + "key. An empty value means that exchange did not happen or was refused - check that a "
            + "trusted publishing policy for this repository and workflow exists on nuget.org under "
            + "the MorseCodeSoftware account.");
    }

    // The tag prefix to package id map is read from the projects rather than written out here. Both
    // halves already live in every packable project, as MinVerTagPrefix and PackageId, and a second
    // copy would be free to drift from them - which has happened before, in a comment.
    var packageIdByPrefix = new Dictionary<string, string>(StringComparer.Ordinal);
    foreach (var project in GetFiles("./src/**/*.csproj") + GetFiles("./src/**/*.fsproj"))
    {
        var document = XDocument.Load(project.FullPath);
        var prefix = document.Descendants("MinVerTagPrefix").FirstOrDefault();
        var id = document.Descendants("PackageId").FirstOrDefault();
        if (prefix != null && id != null)
        {
            packageIdByPrefix[prefix.Value] = id.Value;
        }
    }

    if (packageIdByPrefix.Count == 0)
    {
        throw new Exception("Found no project under src declaring both MinVerTagPrefix and PackageId.");
    }

    // A tag belongs to the package whose prefix it starts with, the rest of it being the version.
    // The rest has to start with a digit, or sodaflow- would claim sodaflow-core-3.0.0.
    //
    // More than one match is refused rather than tie-broken. It takes one prefix extending another
    // by something starting with a digit, which none of these do, so this cannot fire today; if a
    // prefix added later made it fire, picking a winner would mean guessing which package the tag
    // meant. The costs of the two failures are not comparable. Throwing loses a release until
    // someone renames a prefix, and the packages are still sitting in the build artifacts. Guessing
    // wrong publishes the wrong package, and nuget.org does not allow a version to be deleted or
    // reused - only unlisted.
    var prefixes = packageIdByPrefix.Keys
        .Where(p =>
            tag.StartsWith(p, StringComparison.Ordinal) &&
            tag.Length > p.Length &&
            char.IsDigit(tag[p.Length]))
        .OrderBy(p => p, StringComparer.Ordinal)
        .ToList();

    if (prefixes.Count == 0)
    {
        Information("Tag '{0}' does not name a package in this repository - skipping NuGet push.", tag);
        Information(
            "Known prefixes: {0}",
            string.Join(", ", packageIdByPrefix.Keys.OrderBy(p => p, StringComparer.Ordinal)));
        return;
    }

    if (prefixes.Count > 1)
    {
        throw new Exception(
            $"Tag '{tag}' matches more than one package prefix: {string.Join(", ", prefixes)}. " +
            "Rename one of them so that neither extends the other.");
    }

    var packageId = packageIdByPrefix[prefixes[0]];
    Information("Tag '{0}' releases {1}.", tag, packageId);

    // Matched by name rather than by glob: SodaFlow.* would also match SodaFlow.Core and every
    // other package here. dotnet pack names a file Id.Version.nupkg, so what follows the id and its
    // dot is the start of the version.
    //
    // *.symbols.nupkg is excluded: these projects set IncludeSource and IncludeSymbols, which emits
    // the legacy symbols format that nuget.org rejects.
    var packages = GetFiles($"{artifactsDirectory.Path}/*.nupkg")
        .Where(p => !p.GetFilename().FullPath.EndsWith(".symbols.nupkg", StringComparison.OrdinalIgnoreCase))
        .Where(p =>
        {
            var name = p.GetFilename().FullPath;
            return name.StartsWith(packageId + ".", StringComparison.OrdinalIgnoreCase) &&
                   name.Length > packageId.Length + 1 &&
                   char.IsDigit(name[packageId.Length + 1]);
        })
        .ToList();

    if (packages.Count != 1)
    {
        throw new Exception(
            $"Expected exactly one {packageId} package in artifacts, found {packages.Count}.");
    }

    Information("Pushing {0} to {1}", packages[0].GetFilename(), nugetSource);

    // --skip-duplicate means re-running a build over an already published version is a no-op rather
    // than a failure, so re-running a tag build does not fail on the package it already pushed.
    DotNetNuGetPush(
        packages[0].FullPath,
        new DotNetNuGetPushSettings
        {
            ApiKey = apiKey,
            Source = nugetSource,
            SkipDuplicate = true,
        });
});

// Inspect-Code depends on Build, which is the truth, rather than being chained behind Pack the way
// the rest of these are. It is listed second here so that a default run still packs first: the
// inspection fails the build, and leaving the packages and the coverage from the run behind is
// worth more than failing a few seconds earlier.
Task("Default")
    .Description("Restore, build, test with coverage, pack, and inspect.")
    .IsDependentOn("Pack")
    .IsDependentOn("Inspect-Code");

RunTarget(target);
