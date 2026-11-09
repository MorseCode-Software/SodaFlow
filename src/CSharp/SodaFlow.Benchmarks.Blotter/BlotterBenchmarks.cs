using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Windows.Input;
using BenchmarkDotNet.Attributes;
using JetBrains.Annotations;
using SodaFlow.Bindable.ObjectModel;

namespace SodaFlow.Benchmarks.Blotter;

/// <summary>The cost to build a blotter and to release it.</summary>
[MemoryDiagnoser]
// Not sealed, and not private to this file: BenchmarkDotNet derives from this and finds it by
// reflection. See BindableValueBenchmarks in SodaFlow.Benchmarks.
// ReSharper disable once ClassCanBeSealed.Global
// ReSharper disable once MemberCanBeFileLocal
public class BlotterBuildBenchmarks
{
    private readonly IBindableFactory bindableFactory = new BindableFactory(BindingScheduler.Immediate);
    private readonly StreamSink<Fill> fills = Stream.CreateSink<Fill>();
    private readonly StreamSink<Tick> ticks = Stream.CreateSink<Tick>();

    [Params(1_000, 10_000)]
    public int OrderCount { get; [UsedImplicitly] set; }

    [GlobalSetup]
    public void Setup() => Library.Report();

    /// <summary>
    ///     Builds the blotter and disposes it. The disposal is part of the
    ///     measurement, because a screen that opens also closes.
    /// </summary>
    [Benchmark]
    public void BuildAndDispose()
    {
        using IBlotterViewModel viewModel =
            BlotterViewModel.Create(
                bindableFactory: this.bindableFactory,
                orderCount: this.OrderCount,
                ticksStream: this.ticks,
                fillsStream: this.fills);
    }
}

/// <summary>
///     The cost of each thing that a user or a feed does to a blotter that is open.
///     Each benchmark puts the blotter back in the state that it found it in, thus
///     each invocation measures the same work.
/// </summary>
/// <remarks>
///     <para>
///         The setup submits <see cref="LargeOrderCount" /> large orders on one
///         symbol and sorts the list by identifier, with the newest order first. Thus,
///         the first page shows only those orders. A fill takes one lot from an order
///         whose quantity is too large for a run to fill. A tick on the symbol of
///         those orders changes each row, and a tick on a different symbol changes no
///         row.
///     </para>
///     <para>
///         The setup also clears each order that is not working, thus the order
///         lifecycle starts from the same collection at each invocation.
///     </para>
/// </remarks>
[MemoryDiagnoser]
// Not sealed, and not private to this file: BenchmarkDotNet derives from this and finds it by
// reflection. See BindableValueBenchmarks in SodaFlow.Benchmarks.
// ReSharper disable once ClassCanBeSealed.Global
// ReSharper disable once MemberCanBeFileLocal
public class BlotterSendBenchmarks
{
    /// <summary>The number of large orders. It is more than a page, thus two of them are off the page.</summary>
    private const int LargeOrderCount = BlotterViewModel.PageSize + 2;

    private const long LargeQuantity = 1_000_000_000_000;

    private readonly IBindableFactory bindableFactory = new BindableFactory(BindingScheduler.Immediate);
    private readonly StreamSink<Fill> fills = Stream.CreateSink<Fill>();
    private readonly StreamSink<Tick> ticks = Stream.CreateSink<Tick>();

    private int offPageOrder;
    private string offPageSymbol = string.Empty;
    private int onPageOrder;
    private string onPageSymbol = string.Empty;
    private long tickCents;
    private IBlotterViewModel? viewModel;

    [Params(1_000, 10_000)]
    public int OrderCount { get; [UsedImplicitly] set; }

    private IBlotterViewModel ViewModel =>
        this.viewModel ?? throw new InvalidOperationException("The setup did not build the blotter.");

    [GlobalSetup]
    public void Setup()
    {
        Library.Report();

        IBlotterViewModel built =
            BlotterViewModel.Create(
                bindableFactory: this.bindableFactory,
                orderCount: this.OrderCount,
                ticksStream: this.ticks,
                fillsStream: this.fills);

        this.viewModel = built;
        this.onPageSymbol = Orders.Symbols[0];
        this.offPageSymbol = Orders.Symbols[1];
        this.tickCents = Orders.InitialLastCents(this.onPageSymbol);

        Execute(built.ClearInactive);

        built.EntrySymbol.Value = this.onPageSymbol;
        built.EntryLimit.Value = "12.34";
        built.EntryIsBuy.Value = true;

        for (int index = 0; index < LargeOrderCount; index++)
        {
            built.EntryQuantity.Value = LargeQuantity.ToString(CultureInfo.InvariantCulture);
            Execute(built.Submit);
        }

        // Two clicks: ascending, then descending.
        Execute(built.SortById);
        Execute(built.SortById);

        this.onPageOrder = this.OrderCount + LargeOrderCount;
        this.offPageOrder = this.OrderCount + 1;

        this.Check();
    }

    [GlobalCleanup]
    public void Cleanup() => this.viewModel?.Dispose();

    /// <summary>A trade on the symbol of each row on the page.</summary>
    [Benchmark]
    public void TickOnPage() => this.ticks.Send(new Tick(Symbol: this.onPageSymbol, LastCents: this.NextTickCents()));

    /// <summary>A trade on a symbol that no row on the page shows.</summary>
    [Benchmark]
    public void TickOffPage() => this.ticks.Send(new Tick(Symbol: this.offPageSymbol, LastCents: this.NextTickCents()));

    /// <summary>A fill of the order in the first row.</summary>
    [Benchmark]
    public void FillOnPage() => this.fills.Send(new Fill(OrderId: this.onPageOrder, Quantity: Orders.Lot));

    /// <summary>A fill of an order that the page does not show.</summary>
    [Benchmark]
    public void FillOffPage() => this.fills.Send(new Fill(OrderId: this.offPageOrder, Quantity: Orders.Lot));

    /// <summary>One lot more on the first row, and then one lot less.</summary>
    [Benchmark]
    public void AmendRow()
    {
        IOrderRowViewModel row = this.ViewModel.Rows.Value[0];
        Execute(row.AmendUp);
        Execute(row.AmendDown);
    }

    /// <summary>A selection of the first row, and then of the second row.</summary>
    [Benchmark]
    public void SelectRows()
    {
        IReadOnlyList<IOrderRowViewModel> rows = this.ViewModel.Rows.Value;
        Execute(rows[0].Select);
        Execute(rows[1].Select);
    }

    /// <summary>A search for "A", and then a clear of the search.</summary>
    [Benchmark]
    public void SearchAndClear()
    {
        this.ViewModel.Search.Value = "A";
        this.ViewModel.Search.Value = string.Empty;
    }

    /// <summary>Working orders only, and then all orders.</summary>
    [Benchmark]
    public void ToggleWorkingOnly()
    {
        this.ViewModel.WorkingOnly.Value = true;
        this.ViewModel.WorkingOnly.Value = false;
    }

    /// <summary>Two clicks on the header that the list sorts on: two reversals.</summary>
    [Benchmark]
    public void ReverseSort()
    {
        Execute(this.ViewModel.SortById);
        Execute(this.ViewModel.SortById);
    }

    /// <summary>
    ///     A sort on the remaining quantity, and then two clicks to go back to the
    ///     identifier, newest first.
    /// </summary>
    [Benchmark]
    public void ChangeSortColumn()
    {
        Execute(this.ViewModel.SortByRemaining);
        Execute(this.ViewModel.SortById);
        Execute(this.ViewModel.SortById);
    }

    /// <summary>The next page, and then the previous page.</summary>
    [Benchmark]
    public void PageForwardAndBack()
    {
        Execute(this.ViewModel.NextPage);
        Execute(this.ViewModel.PreviousPage);
    }

    /// <summary>
    ///     An order from the form to the clear. The user types a quantity and submits.
    ///     Then the user cancels the selected order and clears the inactive orders.
    /// </summary>
    [Benchmark]
    public void OrderLifecycle()
    {
        this.ViewModel.EntryQuantity.Value = "500";
        Execute(this.ViewModel.Submit);
        Execute(this.ViewModel.CancelSelected);
        Execute(this.ViewModel.ClearInactive);
    }

    private static void Execute(ICommand action)
    {
        if (!action.CanExecute(null))
        {
            throw new InvalidOperationException("A command that the benchmark needs is disabled.");
        }

        action.Execute(null);
    }

    private static void Expect(bool condition, string what)
    {
        if (!condition)
        {
            throw new InvalidOperationException("The check failed: " + what);
        }
    }

    private static string Text(int value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    ///     Reads each value and each command state that a view binds, as the first
    ///     render of a view does. Gives the number of characters that it read.
    /// </summary>
    private static int Render(IBlotterViewModel blotter) =>
        new[]
            {
                blotter.IdHeader.Value, blotter.SymbolHeader.Value, blotter.RemainingHeader.Value,
                blotter.LimitHeader.Value, blotter.Search.Value, blotter.Page.Value, blotter.WorkingCount.Value,
                blotter.BuyExposure.Value, blotter.SellExposure.Value, blotter.LastFill.Value,
                blotter.SelectedOrder.Value, blotter.EntrySymbol.Value, blotter.EntryQuantity.Value,
                blotter.EntryLimit.Value, blotter.EntryError.Value
            }
            .Sum(static text => text.Length)
        + Enabled(
            blotter.SortById, blotter.SortBySymbol, blotter.SortByRemaining, blotter.SortByLimit, blotter.NextPage,
            blotter.PreviousPage, blotter.CancelSelected, blotter.Submit, blotter.ClearInactive)
        + (blotter.WorkingOnly.Value ? 1 : 0)
        + (blotter.EntryIsBuy.Value ? 1 : 0)
        + blotter.Rows.Value.Sum(RenderRow);

    private static int RenderRow(IOrderRowViewModel row) =>
        new[]
            {
                row.Id.Value, row.Symbol.Value, row.Side.Value, row.Trader.Value, row.Quantity.Value, row.Filled.Value,
                row.Remaining.Value, row.Limit.Value, row.LastPrice.Value, row.Notional.Value, row.Edge.Value,
                row.Status.Value
            }
            .Sum(static text => text.Length)
        + (row.IsWorking.Value ? 1 : 0)
        + Enabled(row.Cancel, row.AmendUp, row.AmendDown, row.Select);

    private static int Enabled(params ICommand[] commands) => commands.Count(static command => command.CanExecute(null));

    private long NextTickCents()
    {
        // Two prices in turn, thus each tick changes the quote.
        this.tickCents ^= 1;
        return this.tickCents;
    }

    /// <summary>
    ///     Does each operation one time and checks what it changed. A failure here
    ///     stops the run before a measurement of the wrong work.
    /// </summary>
    private void Check()
    {
        IBlotterViewModel built = this.ViewModel;
        IReadOnlyList<IOrderRowViewModel> rows = built.Rows.Value;

        Expect(condition: Render(built) > 0, what: "a view can read each bound value");
        Expect(condition: rows.Count == BlotterViewModel.PageSize, what: "the page shows a full page of rows");
        Expect(condition: rows[0].Id.Value == Orders.Quantity(this.onPageOrder), what: "the newest order is in the first row");

        Expect(
            condition: built.SelectedOrder.Value.StartsWith(
                value: Text(this.onPageOrder),
                comparisonType: StringComparison.Ordinal),
            what: "the last submit selected its order");

        string price = rows[0].LastPrice.Value;
        this.TickOnPage();
        Expect(condition: rows[0].LastPrice.Value != price, what: "a tick on the symbol of the page changes the first row");

        price = rows[0].LastPrice.Value;
        this.TickOffPage();
        Expect(condition: rows[0].LastPrice.Value == price, what: "a tick on a different symbol does not change the first row");

        string remaining = rows[0].Remaining.Value;
        this.FillOnPage();
        Expect(condition: rows[0].Remaining.Value != remaining, what: "a fill on the page changes the first row");

        Expect(
            condition: built.LastFill.Value.Contains(Orders.Quantity(this.onPageOrder)),
            what: "the last fill names the order");

        this.FillOffPage();

        Expect(
            condition: built.LastFill.Value.Contains(Orders.Quantity(this.offPageOrder)),
            what: "the last fill names the order off the page");

        string quantity = rows[0].Quantity.Value;
        this.AmendRow();
        Expect(condition: built.Rows.Value[0].Quantity.Value == quantity, what: "an amendment up and down keeps the quantity");

        this.SelectRows();

        Expect(
            condition: built.SelectedOrder.Value.StartsWith(
                value: rows[1].Id.Value.Replace(oldValue: ",", newValue: string.Empty),
                comparisonType: StringComparison.Ordinal),
            what: "the second selection wins");

        string page = built.Page.Value;
        this.SearchAndClear();

        Expect(
            condition: built.Page.Value == page && built.Rows.Value[0].Id.Value == Orders.Quantity(this.onPageOrder),
            what: "a search and a clear give the same page");

        this.ToggleWorkingOnly();
        Expect(condition: built.Page.Value == page, what: "the filter on and off gives the same page");

        this.ReverseSort();

        Expect(
            condition: built.Rows.Value[0].Id.Value == Orders.Quantity(this.onPageOrder),
            what: "two reversals give the same first row");

        this.ChangeSortColumn();

        Expect(
            condition: built.Rows.Value[0].Id.Value == Orders.Quantity(this.onPageOrder),
            what: "the sort is back on the identifier");

        this.PageForwardAndBack();
        Expect(condition: built.Page.Value == page, what: "forward and back gives the same page");

        string working = built.WorkingCount.Value;
        this.OrderLifecycle();
        Expect(condition: built.WorkingCount.Value == working, what: "the lifecycle leaves the same number of working orders");
        Expect(condition: !built.ClearInactive.CanExecute(null), what: "the lifecycle leaves no inactive order");
        Expect(condition: Render(built) > 0, what: "a view can read each bound value after each operation");
    }
}

/// <summary>Which copy of the libraries this run measures.</summary>
file static class Library
{
    /// <summary>
    ///     Writes the copy of the libraries to the log of the run. BenchmarkDotNet
    ///     builds the benchmark again in a different process, and this line shows that
    ///     the build used the copy that the run asked for.
    /// </summary>
    internal static void Report()
    {
#if SODAFLOW_RELEASE
        const string source = "Release";
#else
        const string source = "Projects";
#endif

        Assembly core = typeof(Cell<>).Assembly;

        Console.WriteLine(
            "// SodaFlowSource: " + source + ", " + core.GetName().Name + " "
            + (core.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown"));
    }
}
