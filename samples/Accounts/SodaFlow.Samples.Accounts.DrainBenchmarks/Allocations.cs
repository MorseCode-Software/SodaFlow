using System;
using SodaFlow.Samples.Accounts.ViewModels;

namespace SodaFlow.Samples.Accounts.DrainBenchmarks;

/// <summary>
///     Allocations counted on the calling thread only, which is where a Pay and a Drain run.
/// </summary>
/// <remarks>
///     The memory diagnoser of BenchmarkDotNet counts each thread. Thus tiered JIT compilation on
///     its background thread is in the numbers and hides a difference of some kilobytes. This
///     counts only the allocation of the operation, after sufficient warm-up Pays to complete the
///     tiered compilation.
/// </remarks>
internal static class Allocations
{
    private const int Warmup = 5_000;
    private const int Measured = 20_000;

    public static void Run(string name)
    {
        IAccountsViewModel viewModel = ViewModels.Create(name);
        double beforeDrain = PerPay(viewModel.Rows.Cell.Sample()[0]);

        viewModel.DrainFrozenAccounts.Execute(null);
        double afterDrain = PerPay(viewModel.Rows.Cell.Sample()[0]);

        IAccountsViewModel fresh = ViewModels.Create(name);
        long start = GC.GetAllocatedBytesForCurrentThread();
        fresh.DrainFrozenAccounts.Execute(null);
        long drain = GC.GetAllocatedBytesForCurrentThread() - start;

        Console.WriteLine(
            $"{name,-15} pay before drain {beforeDrain,8:F0} B   pay after drain {afterDrain,8:F0} B   "
            + $"drain {drain / 1024.0 / 1024.0,6:F2} MB");

        viewModel.Dispose();
        fresh.Dispose();
    }

    private static double PerPay(IAccountRowViewModel row)
    {
        for (int i = 0; i < Warmup; i++)
        {
            row.Deposit.Execute(null);
        }

        long start = GC.GetAllocatedBytesForCurrentThread();

        for (int i = 0; i < Measured; i++)
        {
            row.Deposit.Execute(null);
        }

        return (GC.GetAllocatedBytesForCurrentThread() - start) / (double)Measured;
    }
}
