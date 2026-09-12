using BenchmarkDotNet.Attributes;
using SodaFlow.Samples.Accounts.ViewModels;

namespace SodaFlow.Samples.Accounts.DrainBenchmarks;

/// <summary>The view models under test, by the name the benchmarks and the footprint mode use.</summary>
public static class ViewModels
{
    /// <summary>b1398a3's AccountsViewModel: drainable accounts are a filtered ReactiveCollection.</summary>
    public const string Original = "Original";

    /// <summary>b1398a3's AccountsViewModelOptimizedDrain: an ImmutableHashSet folded over item changes.</summary>
    public const string OptimizedDrain = "OptimizedDrain";

    /// <summary>Review variant: the same hash set, updated in one pass with a builder only when needed.</summary>
    public const string TunedSet = "TunedSet";

    /// <summary>Review variant: a count folded over item changes, and the keys found at drain time.</summary>
    public const string CountAndScan = "CountAndScan";

    public static IAccountsViewModel Create(string name) =>
        name switch
        {
            Original => AccountsViewModel.Create(),
            OptimizedDrain => AccountsViewModelOptimizedDrain.Create(),
            TunedSet => AccountsViewModelTunedSet.Create(),
            CountAndScan => AccountsViewModelCountAndScan.Create(),
            _ => throw new ArgumentOutOfRangeException(nameof(name), name, "Unknown view model."),
        };
}

/// <summary>
///     One click of a row's Pay button, into the first row of the first page, before any drain and
///     after one.
/// </summary>
[MemoryDiagnoser]
public class PayBenchmarks
{
    private IAccountRowViewModel row = null!;
    private IAccountsViewModel viewModel = null!;

    [Params(ViewModels.Original, ViewModels.OptimizedDrain, ViewModels.TunedSet, ViewModels.CountAndScan)]
    public string ViewModel { get; set; } = ViewModels.Original;

    [Params(false, true)]
    public bool AfterDrain { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        this.viewModel = ViewModels.Create(this.ViewModel);

        if (this.AfterDrain)
        {
            this.viewModel.DrainFrozenAccounts.Execute(null);

            if (this.viewModel.DrainFrozenAccounts.CanExecute(null))
            {
                throw new InvalidOperationException("The drain did not empty the frozen accounts.");
            }
        }

        // The first row in the default order, which is arrival order - no column is sorted until a
        // header is clicked. That order does not read balances, so paying into this row never
        // moves it: it stays on the page and its command stays live for the whole run.
        this.row = this.viewModel.Rows.Value[0];

        if (!this.row.Deposit.CanExecute(null))
        {
            throw new InvalidOperationException("The first row cannot be paid into.");
        }
    }

    [Benchmark]
    public void Pay() => this.row.Deposit.Execute(null);

    [GlobalCleanup]
    public void Cleanup() => this.viewModel.Dispose();
}

/// <summary>One click of Drain frozen accounts, on a view model nobody has drained yet.</summary>
/// <remarks>
///     A drain empties every frozen account, so it cannot be repeated on the same view model: each
///     iteration builds a fresh one first, outside the measurement, and checks afterwards that the
///     drain really happened.
/// </remarks>
[MemoryDiagnoser]
[InvocationCount(1)]
[WarmupCount(3)]
[IterationCount(20)]
public class DrainBenchmarks
{
    private IAccountsViewModel? viewModel;

    [Params(ViewModels.Original, ViewModels.OptimizedDrain, ViewModels.TunedSet, ViewModels.CountAndScan)]
    public string ViewModel { get; set; } = ViewModels.Original;

    [IterationSetup]
    public void Setup() => this.viewModel = ViewModels.Create(this.ViewModel);

    [Benchmark]
    public void Drain() => this.viewModel!.DrainFrozenAccounts.Execute(null);

    [IterationCleanup]
    public void Cleanup()
    {
        if (this.viewModel!.DrainFrozenAccounts.CanExecute(null))
        {
            throw new InvalidOperationException("The drain did not empty the frozen accounts.");
        }

        this.viewModel.Dispose();
        this.viewModel = null;
    }
}
