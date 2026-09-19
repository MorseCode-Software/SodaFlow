using System.Linq;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Toolchains.InProcess.Emit;

namespace SodaFlow.Samples.Accounts.DrainBenchmarks;

file static class Program
{
    public static void Main(string[] args)
    {
        switch (args.Length)
        {
            case >= 2 when args[0] == "--footprint":
                Footprint.Run(args[1]);
                return;
            case >= 2 when args[0] == "--allocations":
                Allocations.Run(args[1]);
                return;
            case >= 2 when args[0] == "--growth":
                Growth.Run(args[1]);
                return;
        }

        // ReSharper disable once CommentTypo
        // The default is a different process. Use --inprocess when the generated project of
        // BenchmarkDotNet does not build. This process then measures each view model in sequence.
        // ReSharper disable once StringLiteralTypo
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
            // ReSharper disable once StringLiteralTypo
            .Run(args: [.. args.Where(static a => a != "--inprocess")], config: config);
    }
}
