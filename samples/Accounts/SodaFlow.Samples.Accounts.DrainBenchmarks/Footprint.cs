using SodaFlow.Samples.Accounts.ViewModels;

namespace SodaFlow.Samples.Accounts.DrainBenchmarks;

/// <summary>
///     Retained managed memory, which BenchmarkDotNet does not report: what a live view model holds
///     on to, rather than what an operation allocates on the way. Run once per view model, each in
///     its own process, so one view model's leftovers are never in another's baseline.
/// </summary>
/// <remarks>
///     A first view model is built and disposed before anything is measured, so the seed's hundred
///     thousand items, JIT and static caches are already in the baseline and the numbers are what
///     one view model adds. Every reading is after a full, compacting collection. The totals double
///     as a check that every view model drains the same accounts.
/// </remarks>
public static class Footprint
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

        IAccountRowViewModel row = viewModel.Rows.Value[0];

        for (int i = 0; i < 1_000; i++)
        {
            row.Deposit.Execute(null);
        }

        long afterPays = Settled();

        Console.WriteLine(
            $"{name,-15} create {Megabytes(afterCreate - baseline)}  drain {Megabytes(afterDrain - baseline)}  "
            + $"+1,000 pays {Megabytes(afterPays - baseline)}  | {viewModel.Total.Value} | "
            + $"drain enabled after: {viewModel.DrainFrozenAccounts.CanExecute(null)}");

        GC.KeepAlive(viewModel);
        viewModel.Dispose();
    }

    private static long Settled()
    {
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        return GC.GetTotalMemory(forceFullCollection: true);
    }

    private static string Megabytes(long bytes) => $"{bytes / 1024.0 / 1024.0,6:F2} MB";
}
