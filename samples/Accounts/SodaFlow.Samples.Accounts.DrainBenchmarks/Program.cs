using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Toolchains.InProcess.Emit;
using SodaFlow.Samples.Accounts.DrainBenchmarks;

if (args.Length >= 2 && args[0] == "--footprint")
{
    Footprint.Run(args[1]);
    return;
}

if (args.Length >= 2 && args[0] == "--allocations")
{
    Allocations.Run(args[1]);
    return;
}

if (args.Length >= 2 && args[0] == "--growth")
{
    Growth.Run(args[1]);
    return;
}

// Out of process by default. --inprocess is the fallback for when BenchmarkDotNet's generated
// project will not build: every view model is then measured in this one process, in turn.
bool inProcess = args.Contains("--inprocess");

Job job =
    inProcess
        ? Job.Default.WithToolchain(InProcessEmitToolchain.Instance).WithId("InProcess")
        : Job.Default.WithId("OutOfProcess");

IConfig config =
    DefaultConfig.Instance
        .AddJob(job)
        .AddDiagnoser(MemoryDiagnoser.Default)
        .WithOptions(ConfigOptions.JoinSummary);

BenchmarkSwitcher
    .FromAssembly(typeof(PayBenchmarks).Assembly)
    .Run(args.Where(static a => a != "--inprocess").ToArray(), config);
