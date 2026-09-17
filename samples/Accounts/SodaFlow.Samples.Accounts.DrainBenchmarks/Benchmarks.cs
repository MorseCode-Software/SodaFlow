using System.Globalization;
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

    public static IAccountsViewModel Create(string name) =>
        name switch
        {
            Original => AccountsViewModel.Create(),
            OptimizedDrain => AccountsViewModelOptimizedDrain.Create(),
            _ => throw new ArgumentOutOfRangeException(nameof(name), name, "Unknown view model."),
        };
}

/// <summary>
///     One click of a row's Pay button, into the first row of the first page, before any drain and
///     after one.
/// </summary>
/// <remarks>
///     Every invocation pays into the same view model, so the check that the clicks did something
///     is made once, in global cleanup: the row's balance has to have risen by exactly one deposit for
///     every Pay, which catches Pays that stopped landing partway through a run as well as ones that
///     never did. The deposit is learned from a Pay in setup, outside the measurement, rather than
///     copied from the view models.
/// </remarks>
[MemoryDiagnoser]
public class PayBenchmarks
{
    private static readonly CultureInfo UsDollars = CultureInfo.GetCultureInfo("en-US");

    private IAccountRowViewModel row = null!;
    private IAccountsViewModel viewModel = null!;
    private decimal startingBalance;
    private decimal deposit;
    private long pays;

    [Params(ViewModels.Original, ViewModels.OptimizedDrain)]
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

            if (this.viewModel.DrainFrozenAccounts.IsEnabledCell.Sample())
            {
                throw new InvalidOperationException("The drain did not empty the frozen accounts.");
            }
        }

        // The first row in the default order, which is arrival order - no column is sorted until a
        // header is clicked. That order does not read balances, so paying into this row never
        // moves it: it stays on the page and its command stays live for the whole run.
        this.row = this.viewModel.Rows.Cell.Sample()[0];

        if (!this.row.Deposit.IsEnabledCell.Sample())
        {
            throw new InvalidOperationException("The first row cannot be paid into.");
        }

        decimal beforeSetupPay = BalanceOf(this.row);

        this.row.Deposit.Execute(null);

        this.startingBalance = BalanceOf(this.row);
        this.deposit = this.startingBalance - beforeSetupPay;
        this.pays = 0;

        if (this.deposit <= 0)
        {
            throw new InvalidOperationException("A Pay in setup did not raise the balance.");
        }
    }

    [Benchmark]
    public void Pay()
    {
        this.row.Deposit.Execute(null);
        this.pays++;
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        decimal expected = this.startingBalance + (this.pays * this.deposit);
        decimal actual = BalanceOf(this.row);

        this.viewModel.Dispose();

        if (actual != expected)
        {
            throw new InvalidOperationException(
                string.Format(
                    provider: UsDollars,
                    format: "{0:N0} Pays of {1:C} should have left the balance at {2:C}, but it is {3:C}.",
                    this.pays,
                    this.deposit,
                    expected,
                    actual));
        }
    }

    /// <summary>
    ///     The row's balance, read from the cell rather than the bindable, and parsed back from the fixed
    ///     US-dollar format every balance on screen is written in.
    /// </summary>
    private static decimal BalanceOf(IAccountRowViewModel row) =>
        decimal.Parse(s: row.Balance.Cell.Sample(), style: NumberStyles.Currency, provider: UsDollars);
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

    [Params(ViewModels.Original, ViewModels.OptimizedDrain)]
    public string ViewModel { get; set; } = ViewModels.Original;

    [IterationSetup]
    public void Setup() => this.viewModel = ViewModels.Create(this.ViewModel);

    [Benchmark]
    public void Drain() => this.viewModel!.DrainFrozenAccounts.Execute(null);

    [IterationCleanup]
    public void Cleanup()
    {
        if (this.viewModel!.DrainFrozenAccounts.IsEnabledCell.Sample())
        {
            throw new InvalidOperationException("The drain did not empty the frozen accounts.");
        }

        this.viewModel.Dispose();
        this.viewModel = null;
    }
}

/// <summary>
///     One click of Show frozen accounts, which changes the filter's predicate rather than any
///     account.
/// </summary>
/// <remarks>
///     <para>
///         The filter re-tests every account, and lists the ones that entered or left only while
///         there are few enough to be worth listing. Showing the frozen accounts adds about 25,000
///         to a filter holding about 75,000, past its budget of a tenth of what it holds, so the
///         filter rebuilds and reports a reset, and the sort, the page and the rows below it
///         rebuild too. This is the cost of that for the whole chain down to the rows on screen.
///     </para>
///     <para>
///         Nothing is waited for, because nothing is left to arrive once the click returns. The row
///         list is a cell, so it changes inside the transaction the click opens, and with no binding
///         scheduler or synchronization context in this process the bindables fall back to the
///         immediate scheduler, which delivers their values and change notifications as that
///         transaction closes - still inside the click. What a dispatcher adds in a real UI happens
///         after that, and a benchmark here cannot see it.
///     </para>
///     <para>
///         Each iteration builds a fresh view model, outside the measurement, so the toggle always
///         goes the same way. Cleanup checks that it switched and that the row list changed, so a
///         click that did nothing fails the run rather than timing as fast. The checks read the cells
///         rather than the bindables' values, which a dispatcher-backed scheduler would deliver only
///         after the click, so they hold whichever scheduler is in use.
///     </para>
/// </remarks>
[MemoryDiagnoser]
[InvocationCount(1)]
[WarmupCount(3)]
[IterationCount(20)]
public class ToggleFrozenBenchmarks
{
    private IAccountsViewModel? viewModel;
    private IReadOnlyList<IAccountRowViewModel>? rowsBefore;
    private bool initialShowFrozen;

    [Params(ViewModels.Original, ViewModels.OptimizedDrain)]
    public string ViewModel { get; set; } = ViewModels.Original;

    [IterationSetup]
    public void Setup()
    {
        this.viewModel = ViewModels.Create(this.ViewModel);
        this.initialShowFrozen = this.viewModel.ShowFrozen.Cell.Sample();
        this.rowsBefore = this.viewModel.Rows.Cell.Sample();
    }

    [Benchmark]
    public void ToggleFrozen() => this.viewModel!.ShowFrozen.Value = !this.initialShowFrozen;

    [IterationCleanup]
    public void Cleanup()
    {
        if (this.viewModel!.ShowFrozen.Cell.Sample() == this.initialShowFrozen)
        {
            throw new InvalidOperationException("The frozen toggle was not switched.");
        }

        RowList.CheckChanged(before: this.rowsBefore!, after: this.viewModel.Rows.Cell.Sample(), action: "The frozen toggle");

        this.viewModel.Dispose();
        this.viewModel = null;
        this.rowsBefore = null;
    }
}

/// <summary>
///     One click of the balance header on a list already sorted by balance, which reverses the
///     sort.
/// </summary>
/// <remarks>
///     <para>
///         A sort stage handed its own order run the other way turns the list it holds around rather
///         than filing every account again, so this measures that path. Setup sorts by balance first,
///         outside the measurement, so that the click measured is the reversal and not the first
///         sort.
///     </para>
///     <para>
///         Each iteration builds a fresh view model, so the reversal always goes the same way. As on
///         <see cref="ToggleFrozenBenchmarks" />, nothing is waited for, and cleanup checks the row
///         list's cell to see that it changed.
///     </para>
/// </remarks>
[MemoryDiagnoser]
[InvocationCount(1)]
[WarmupCount(3)]
[IterationCount(20)]
public class ToggleBalanceSortBenchmarks
{
    private IAccountsViewModel? viewModel;
    private IReadOnlyList<IAccountRowViewModel>? rowsBefore;

    [Params(ViewModels.Original, ViewModels.OptimizedDrain)]
    public string ViewModel { get; set; } = ViewModels.Original;

    [IterationSetup]
    public void Setup()
    {
        this.viewModel = ViewModels.Create(this.ViewModel);

        IReadOnlyList<IAccountRowViewModel> unsorted = this.viewModel.Rows.Cell.Sample();

        this.viewModel.SortByBalance.Execute(null);

        RowList.CheckChanged(before: unsorted, after: this.viewModel.Rows.Cell.Sample(), action: "Sorting by balance");

        this.rowsBefore = this.viewModel.Rows.Cell.Sample();
    }

    [Benchmark]
    public void ToggleSort() => this.viewModel!.SortByBalance.Execute(null);

    [IterationCleanup]
    public void Cleanup()
    {
        RowList.CheckChanged(before: this.rowsBefore!, after: this.viewModel!.Rows.Cell.Sample(), action: "Reversing the balance sort");

        this.viewModel.Dispose();
        this.viewModel = null;
        this.rowsBefore = null;
    }
}

/// <summary>The check the toggle benchmarks make, outside the measurement, that a click did something.</summary>
internal static class RowList
{
    /// <summary>Throws unless the row list is a different list than it was before the click.</summary>
    /// <remarks>
    ///     By reference: the rows are projected into a new list whenever the keys on the page change,
    ///     and the same list instance is kept when they do not.
    /// </remarks>
    internal static void CheckChanged(
        IReadOnlyList<IAccountRowViewModel> before,
        IReadOnlyList<IAccountRowViewModel> after,
        string action)
    {
        if (ReferenceEquals(objA: before, objB: after))
        {
            throw new InvalidOperationException($"{action} did not change the row list.");
        }
    }
}
