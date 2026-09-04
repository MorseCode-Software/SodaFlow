// Build script for the .NET implementation under src.
//
// This reproduces what appveyor.yml used to run inline, step for step. The reasoning behind each
// step lives here now rather than in the YAML, because the steps do.
//
// Run it locally with:
//
//     dotnet tool restore
//     dotnet cake
//
// which restores, builds, tests with coverage, packs, and runs the inspection. Publishing is
// deliberately not part of the default target; see the Publish task.
//
// On AppVeyor the tasks are driven one phase at a time, with --exclusive, so that a failure is
// attributed to the phase it happened in rather than all of them reading as a build failure. The
// dependencies below are therefore what a local run follows, not what CI relies on; keep them
// accurate anyway, since `dotnet cake --target=Pack` on a clean tree has to work.
//
// Package versions are NOT set here. Each packable project derives its own version from git tags
// via MinVer (see src/Directory.Build.props), so pushing sodaflow-async-2.1.0 releases only
// SodaFlow.Async and leaves every other package on its own last tag.

// The Cake.Issues family is pinned to 5.9.1 rather than the current 6.0.0 because
// Cake.Issues.PullRequests.AppVeyor - the piece that does the actual reporting - has no 6.x release,
// and the four have to agree on a version. Under Cake 6 they log one informational line apiece
// saying they were built against Cake.Core 5.0.0; they load and work regardless. Move the whole set
// to 6.x once the AppVeyor one ships.
#addin nuget:?package=Cake.Issues&version=5.9.1
#addin nuget:?package=Cake.Issues.Sarif&version=5.9.1
#addin nuget:?package=Cake.Issues.PullRequests&version=5.9.1
#addin nuget:?package=Cake.Issues.PullRequests.AppVeyor&version=5.9.1

using System.Xml.Linq;

var target = Argument("target", "Default");
var configuration = Argument("configuration", "Release");

var solution = File("./src/SodaFlow.slnx");
var artifactsDirectory = Directory("./artifacts");
var coverageDirectory = Directory("./coverage");
var inspectionDirectory = Directory("./inspection");
var inspectionSettings = File("./src/SodaFlow.sln.DotSettings");
var coverallsExecutable = File("./coveralls.exe");

const string CoverallsDownloadUrl =
    "https://github.com/coverallsapp/coverage-reporter/releases/latest/download/coveralls-windows.exe";

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

    if (BuildSystem.IsRunningOnAppVeyor)
    {
        Information(
            "AppVeyor build {0}, branch {1}{2}.",
            AppVeyor.Environment.Build.Number,
            AppVeyor.Environment.Repository.Branch,
            AppVeyor.Environment.Repository.Tag.IsTag
                ? ", tag " + AppVeyor.Environment.Repository.Tag.Name
                : string.Empty);
    }
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

    // One run over the solution. Collection is done by the Microsoft Code Coverage collector that
    // ships inside Microsoft.NET.Test.Sdk, which writes Cobertura directly - the format the
    // Coveralls reporter accepts - so nothing has to be converted and nothing merged. Scoping and
    // exclusions live in coverage.runsettings.
    //
    // SodaFlow.Tests.Performance contributes no tests: it is a console benchmark, not a test
    // project, and the run does not mind.
    //
    // Three earlier approaches are recorded so they are not retried blindly. The NUnit console
    // runner under OpenCover profiled cleanly but took close to ten minutes here and failed tests
    // that pass under dotnet test. Coverlet breaks the F# suite, whose assemblies compile against
    // FSharp.Core 4.5.0.0 while 10.0.0.0 is deployed; its instrumentation does not honor the
    // binding redirect. OpenCover wrapped around dotnet test hangs, because dotnet.exe is a CoreCLR
    // host and OpenCover's profiler is a .NET Framework CLR profiler.
    DotNetTest(
        solution,
        new DotNetTestSettings
        {
            Configuration = configuration,
            NoBuild = true,
            Settings = File("./coverage.runsettings"),
            ResultsDirectory = coverageDirectory,
            Loggers = new[] { "trx" },
            ArgumentCustomization = args => args.Append("--collect:\"Code Coverage\""),
        });

    // AppVeyor shows a Tests tab only for results handed to its API; a passing or failing phase on
    // its own says how many suites ran, not which test failed. The trx logger writes one file per
    // test project and AppVeyor reads that format as MSTest.
    //
    // Uploaded here rather than in a later task because a failing test run stops the build, and the
    // results of the run that failed are exactly the ones worth having.
    if (BuildSystem.IsRunningOnAppVeyor)
    {
        foreach (var results in GetFiles($"{coverageDirectory.Path}/**/*.trx"))
        {
            Information("Uploading {0}", results.GetFilename());
            AppVeyor.UploadTestResults(results, AppVeyorTestResultsType.MSTest);
        }
    }
});

Task("Upload-Coverage")
    .Description("Sends the Cobertura report to Coveralls.")
    .IsDependentOn("Test")
    .Does(() =>
{
    // The collector names each report after the machine and timestamp and puts it in a GUID
    // subdirectory, so they have to be found rather than assumed.
    //
    // There is more than one. A solution-level run writes a report per test project, each covering
    // only the assemblies that project touched - eight of them here, ranging from two assemblies to
    // seven. Sending the first, which is what this did until now, uploaded a partial view, and
    // which partial view depended on the order the filesystem happened to return those GUID
    // directories in. The reporter takes several files in one invocation and merges them, so all of
    // them go up as a single submission.
    var reports = GetFiles($"{coverageDirectory.Path}/**/*.cobertura.xml")
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

    if (!BuildSystem.IsRunningOnAppVeyor)
    {
        Information("Not running on AppVeyor - skipping the coverage upload.");
        return;
    }

    var repoToken = EnvironmentVariable("COVERALLS_REPO_TOKEN");
    if (string.IsNullOrEmpty(repoToken))
    {
        // Secure variables are withheld from pull requests raised on forks, so this logs and skips
        // rather than failing a build that could never have had the token.
        Information("COVERALLS_REPO_TOKEN is not set - skipping the coverage upload.");
        return;
    }

    DownloadFile(CoverallsDownloadUrl, coverallsExecutable);

    // AppVeyor is not one of the CI services the reporter auto-detects, so every piece of build
    // metadata is supplied explicitly. Without it the upload lands with no job, branch or commit
    // attached.
    // Taken from the environment rather than through Cake's typed AppVeyor properties: the three
    // parts of this URL are the ones Cake either does not surface or names differently, and a URL
    // assembled half one way and half the other is harder to check against AppVeyor's own docs.
    var buildUrl =
        $"https://ci.appveyor.com/project/{EnvironmentVariable("APPVEYOR_ACCOUNT_NAME")}" +
        $"/{EnvironmentVariable("APPVEYOR_PROJECT_SLUG")}/builds/{EnvironmentVariable("APPVEYOR_BUILD_ID")}";

    var arguments = new ProcessArgumentBuilder().Append("report");

    foreach (var report in reports)
    {
        arguments.AppendQuoted(report.FullPath);
    }

    arguments
        .Append("--format=cobertura")
        .AppendSwitchQuotedSecret("--repo-token", "=", repoToken)
        .AppendSwitchQuoted("--base-path", "=", Context.Environment.WorkingDirectory.FullPath)
        .Append("--service-name=appveyor")
        .AppendSwitchQuoted("--service-job-id", "=", AppVeyor.Environment.JobId)
        .AppendSwitchQuoted("--service-branch", "=", AppVeyor.Environment.Repository.Branch)
        .AppendSwitchQuoted("--service-build-url", "=", buildUrl);

    if (AppVeyor.Environment.PullRequest.IsPullRequest)
    {
        arguments.AppendSwitchQuoted(
            "--service-pull-request",
            "=",
            AppVeyor.Environment.PullRequest.Number.ToString());
    }

    // RenderSafe rather than Render: the repo token is appended as a secret and comes back
    // redacted, so this is safe to leave in a public build log.
    Verbose("coveralls {0}", arguments.RenderSafe());

    var exitCode = StartProcess(coverallsExecutable, new ProcessSettings { Arguments = arguments });
    if (exitCode != 0)
    {
        throw new Exception($"Coveralls upload failed (exit {exitCode}).");
    }
});

Task("Pack")
    .Description("Packs every publishable project.")
    .IsDependentOn("Upload-Coverage")
    .Does(() =>
{
    CleanDirectory(artifactsDirectory);

    // One pack over the whole solution. It packs the publishable projects and skips the test and
    // benchmark ones: the test projects reference Microsoft.NET.Test.Sdk, which the SDK reads as
    // IsTestProject and defaults IsPackable to false for, and SodaFlow.Tests.Performance sets
    // IsPackable false explicitly because it has no test adapter reference to be read that way.
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

Task("Inspect-Code")
    .Description("Runs JetBrains InspectCode over the solution and reports what it finds.")
    .IsDependentOn("Build")
    .Does(() =>
{
    CleanDirectory(inspectionDirectory);

    var report = inspectionDirectory + File("inspectcode.sarif");

    // inspectcode comes from the jetbrains.resharper.globaltools local tool, pinned alongside Cake
    // in .config/dotnet-tools.json, so the agent inspects with the version a developer does. There
    // is no Cake alias for it; a process call is the whole of the integration.
    var arguments = new ProcessArgumentBuilder()
        .Append("jb")
        .Append("inspectcode")
        .AppendQuoted(MakeAbsolute(solution.Path).FullPath)
        .AppendSwitchQuoted("--output", "=", MakeAbsolute(report.Path).FullPath)
        .Append("--format=Sarif")
        // The same settings Rider applies, named explicitly rather than left to inspectcode's
        // lookup: that lookup pairs a .DotSettings file with a solution of the same name, and this
        // solution is SodaFlow.slnx while the settings are SodaFlow.sln.DotSettings.
        .AppendSwitchQuoted("--settings", "=", MakeAbsolute(inspectionSettings.Path).FullPath)
        // Absolute paths in the SARIF, which is what lets the issues be reported against paths from
        // the repository root. Left relative, they come out relative to the solution directory -
        // CSharp/SodaFlow/Foo.cs for a file that lives at src/CSharp/SodaFlow/Foo.cs - because the
        // reader takes the URI as written rather than rebasing it.
        .Append("--absolute-paths")
        // The solution was built by the Build task this depends on. Building it again would double
        // the cost of the phase for no gain, so tell inspectcode which configuration it is looking
        // at instead of letting it pick one and build it.
        .Append("--no-build")
        .Append($"--properties:Configuration={configuration}")
        .Append("--verbosity=WARN");

    var exitCode = StartProcess("dotnet", new ProcessSettings { Arguments = arguments });
    if (exitCode != 0)
    {
        throw new Exception($"inspectcode failed (exit {exitCode}).");
    }

    var issues = ReadIssues(
            SarifIssuesFromFilePath(report),
            Context.Environment.WorkingDirectory)
        .OrderBy(i => i.AffectedFileRelativePath?.FullPath ?? string.Empty, StringComparer.Ordinal)
        .ThenBy(i => i.Line ?? 0)
        .ToList();

    // Logged as well as reported. The AppVeyor messages tab is the readable form, but it is only
    // populated on AppVeyor, and a local run should not have to guess what was found.
    Information("InspectCode found {0} issue(s).", issues.Count);
    foreach (var issue in issues)
    {
        Information(
            "  {0}({1}): {2}: {3}",
            issue.AffectedFileRelativePath?.FullPath ?? "<solution>",
            issue.Line?.ToString() ?? "-",
            issue.RuleId,
            issue.MessageText);
    }

    // Reported before the throw below, not after, because a build that fails on the inspection is
    // exactly the build that needs to say what the inspection found.
    if (BuildSystem.IsRunningOnAppVeyor && issues.Count > 0)
    {
        ReportIssuesToPullRequest(
            issues,
            AppVeyorBuilds(),
            Context.Environment.WorkingDirectory);
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
            $"InspectCode found {issues.Count} issue(s), listed above. Fix them, or change the rule "
            + "in src/SodaFlow.sln.DotSettings.");
    }
});

Task("Publish")
    .Description("Pushes the one package this build's tag names to nuget.org.")
    .Does(() =>
{
    // Publishing is gated on a tag, which is what makes the release schedule per-package: MinVer
    // gives a tagged build a stable version and every other build a prerelease one, so only
    // deliberate tags can ever produce something publishable.
    //
    // A tag build publishes exactly one package: the one its own tag names. AppVeyor starts a
    // separate build per tag even when several are pushed together, and each of those builds sees
    // the same artifacts directory holding every package. Pushing all of them from every build made
    // the publish order the order the builds happened to run in, which is not something a release
    // can control - so a package could reach nuget.org before the dependency it was built against.
    //
    // Note what this does and does not do. It makes the order controllable; it does not impose one.
    // Push the tags in dependency order, and wait for each build to publish before pushing the next.
    if (!BuildSystem.IsRunningOnAppVeyor || !AppVeyor.Environment.Repository.Tag.IsTag)
    {
        Information("Not a tag build - skipping NuGet push.");
        return;
    }

    var tag = AppVeyor.Environment.Repository.Tag.Name;
    if (string.IsNullOrEmpty(tag))
    {
        throw new Exception(
            "This is a tag build but the tag name is empty, so there is no way to tell which " +
            "package it releases.");
    }

    var apiKey = EnvironmentVariable("NUGET_API_KEY");
    if (string.IsNullOrEmpty(apiKey))
    {
        throw new Exception("NUGET_API_KEY is not set. Add it as a secure variable in AppVeyor.");
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
