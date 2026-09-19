using System;
using SodaFlow.Samples.Accounts.ViewModels;

namespace SodaFlow.Samples.Accounts.DrainBenchmarks;

/// <summary>
///     The retained managed memory, which BenchmarkDotNet does not report. This is the memory that
///     a live view model holds, and not the memory that an operation allocates. This runs one time
///     for each view model, each in its own process, thus the memory of one view model is never in
///     the baseline of a second view model.
/// </summary>
/// <remarks>
///     This code builds a first view model and disposes it before the measurement. Thus the one
///     hundred thousand items of the seed, the JIT, and the static caches are in the baseline, and
///     the numbers are the memory that one view model adds. Each measurement is after a full,
///     compacting collection. The totals are also a test that each view model drains the same
///     accounts.
/// </remarks>
internal static class Footprint
{
    public static void Run(string name)
    {
        using (ViewModels.Create(name))
        {
        }

        long baseline = Settled();

        IAccountsViewModel viewModel = ViewModels.Create(name);
        long afterCreate = Settled();

        viewModel.DrainFrozenAccounts.Execute(null);
        long afterDrain = Settled();

        IAccountRowViewModel row = viewModel.Rows.Cell.Sample()[0];

        for (int i = 0; i < 1_000; i++)
        {
            row.Deposit.Execute(null);
        }

        long afterPays = Settled();

        Console.WriteLine(
            $"{name,-15} create {Megabytes(afterCreate - baseline)}  drain {Megabytes(afterDrain - baseline)}  "
            + $"+1,000 pays {Megabytes(afterPays - baseline)}  | {viewModel.Total.Cell.Sample()} | "
            + $"drain enabled after: {viewModel.DrainFrozenAccounts.IsEnabledCell.Sample()}");

        GC.KeepAlive(viewModel);
        viewModel.Dispose();
    }

    private static long Settled()
    {
        GC.Collect(generation: GC.MaxGeneration, mode: GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(generation: GC.MaxGeneration, mode: GCCollectionMode.Forced, blocking: true, compacting: true);
        return GC.GetTotalMemory(forceFullCollection: true);
    }

    private static string Megabytes(long bytes) => $"{bytes / 1024.0 / 1024.0,6:F2} MB";
}
