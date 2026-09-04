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
// which restores, builds, tests with coverage, and packs. Publishing is deliberately not part of
// the default target; see the Publish task.
//
// On AppVeyor the tasks are driven one phase at a time, with --exclusive, so that a failure is
// attributed to the phase it happened in rather than all of them reading as a build failure. The
// dependencies below are therefore what a local run follows, not what CI relies on; keep them
// accurate anyway, since `dotnet cake --target=Pack` on a clean tree has to work.
//
// Package versions are NOT set here. Each packable project derives its own version from git tags
// via MinVer (see src/Directory.Build.props), so pushing sodaflow-async-2.1.0 releases only
// SodaFlow.Async and leaves every other package on its own last tag.

using System.Xml.Linq;

var target = Argument("target", "Default");
var configuration = Argument("configuration", "Release");

var solution = File("./src/SodaFlow.slnx");
var artifactsDirectory = Directory("./artifacts");
var coverageDirectory = Directory("./coverage");
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
    // The collector names the report after the machine and timestamp and puts it in a GUID
    // subdirectory, so it has to be found rather than assumed.
    var report = GetFiles($"{coverageDirectory.Path}/**/*.cobertura.xml").FirstOrDefault();
    if (report == null)
    {
        throw new Exception("No Cobertura report was produced.");
    }

    Information("Coverage report: {0}", report.FullPath);

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

    var arguments = new ProcessArgumentBuilder()
        .Append("report")
        .AppendQuoted(report.FullPath)
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

Task("Default")
    .Description("Restore, build, test with coverage, and pack.")
    .IsDependentOn("Pack");

RunTarget(target);
