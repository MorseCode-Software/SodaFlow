using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SodaFlow.Bindable.ObjectModel;
using SodaFlow.Collections;
using SodaFlow.Functional;
using OrderEdit = SodaFlow.Collections.CollectionEdit<int, SodaFlow.Benchmarks.Blotter.OrderIdentity, SodaFlow.Benchmarks.Blotter.OrderState>;
using OrderItem = SodaFlow.Collections.Item<SodaFlow.Benchmarks.Blotter.OrderIdentity, SodaFlow.Benchmarks.Blotter.OrderState>;
using QuoteEdit = SodaFlow.Collections.CollectionEdit<string, SodaFlow.Benchmarks.Blotter.SymbolIdentity, SodaFlow.Benchmarks.Blotter.Quote>;

namespace SodaFlow.Benchmarks.Blotter;

/// <summary>The value of the working orders on each side, at their limits, in cents.</summary>
// ReSharper disable once InheritdocConsiderUsage - The summary says what this type is here, which the base does not.
internal readonly record struct Exposure(long BuyCents, long SellCents)
{
    internal static Exposure Of(OrderIdentity identity, OrderState state)
    {
        if (!state.IsWorking)
        {
            return default;
        }

        long cents = state.Remaining * state.LimitCents;

        return identity.Side == Side.Buy ? new Exposure(BuyCents: cents, SellCents: 0) : new Exposure(BuyCents: 0, SellCents: cents);
    }

    internal static Exposure Add(Exposure x, Exposure y) => new(BuyCents: x.BuyCents + y.BuyCents, SellCents: x.SellCents + y.SellCents);

    internal static Exposure Subtract(Exposure x, Exposure y) =>
        new(BuyCents: x.BuyCents - y.BuyCents, SellCents: x.SellCents - y.SellCents);
}

/// <summary>The result of the entry form: an order to submit, or the cause that it cannot submit.</summary>
// ReSharper disable once InheritdocConsiderUsage - The summary says what this type is here, which the base does not.
internal sealed record EntryResult(Maybe<OrderRequest> Request, string Error)
{
    internal static EntryResult Validate(string symbol, string quantityText, string limitText, bool isBuy)
    {
        if (!KnownSymbols.Contains(symbol))
        {
            return Invalid("Enter a symbol of the market.");
        }

        if (!long.TryParse(s: quantityText, style: NumberStyles.Integer, provider: CultureInfo.InvariantCulture, result: out long quantity)
            || quantity <= 0
            || quantity % Orders.Lot != 0)
        {
            return Invalid("Enter a quantity that is a multiple of " + Orders.Quantity(Orders.Lot) + ".");
        }

        if (!decimal.TryParse(s: limitText, style: NumberStyles.Number, provider: CultureInfo.InvariantCulture, result: out decimal limit)
            || limit <= 0)
        {
            return Invalid("Enter a limit price that is more than zero.");
        }

        return new EntryResult(
            Request: Maybe.Some(
                new OrderRequest(
                    Symbol: symbol,
                    Side: isBuy ? Side.Buy : Side.Sell,
                    Quantity: quantity,
                    LimitCents: (long)decimal.Round(limit * 100))),
            Error: string.Empty);

        static EntryResult Invalid(string error) => new(Request: Maybe<OrderRequest>.None, Error: error);
    }

    private static readonly HashSet<string> KnownSymbols = new(collection: Orders.Symbols, comparer: StringComparer.Ordinal);
}

/// <summary>
///     A blotter over a collection of orders and a collection of quotes. The market data
///     feed and the exchange are streams that the caller gives.
/// </summary>
/// <remarks>
///     The list goes through a search, a filter, a sort, and a page, as a grid on a
///     trading desk does. Each row follows the quote of its symbol through a switch.
///     The totals fold over each change of the orders, and the selected order is a
///     switch on the key that the user selected.
/// </remarks>
// ReSharper disable once InheritdocConsiderUsage - The summary says what this type is here, which the base does not.
internal sealed class BlotterViewModel : IBlotterViewModel
{
    /// <summary>The number of rows on a page.</summary>
    internal const int PageSize = 25;

    private readonly Stream<OrderItem> accepted;
    private readonly Stream<OrderEdit> cancelSelectedEdits;
    private readonly Stream<OrderEdit> clears;
    private readonly IReadOnlyList<IDisposable> disposables;
    private readonly Stream<OrderEdit> rowEdits;

    private BlotterViewModel(
        IOneWayBindableValue<IReadOnlyList<IOrderRowViewModel>> rows,
        IOneWayBindableValue<string> idHeader,
        IOneWayBindableValue<string> symbolHeader,
        IOneWayBindableValue<string> remainingHeader,
        IOneWayBindableValue<string> limitHeader,
        IBindableAction sortById,
        IBindableAction sortBySymbol,
        IBindableAction sortByRemaining,
        IBindableAction sortByLimit,
        ITwoWayBindableValue<string> search,
        ITwoWayBindableValue<bool> workingOnly,
        IOneWayBindableValue<string> page,
        IBindableAction nextPage,
        IBindableAction previousPage,
        IOneWayBindableValue<string> workingCount,
        IOneWayBindableValue<string> buyExposure,
        IOneWayBindableValue<string> sellExposure,
        IOneWayBindableValue<string> lastFill,
        IOneWayBindableValue<string> selectedOrder,
        IBindableAction cancelSelected,
        ITwoWayBindableValue<string> entrySymbol,
        ITwoWayBindableValue<string> entryQuantity,
        ITwoWayBindableValue<string> entryLimit,
        ITwoWayBindableValue<bool> entryIsBuy,
        IOneWayBindableValue<string> entryError,
        IBindableAction submit,
        IBindableAction clearInactive,
        Stream<OrderEdit> rowEdits,
        Stream<OrderEdit> cancelSelectedEdits,
        Stream<OrderEdit> clears,
        Stream<OrderItem> accepted,
        IDisposable projectedRows)
    {
        this.Rows = rows;
        this.IdHeader = idHeader;
        this.SymbolHeader = symbolHeader;
        this.RemainingHeader = remainingHeader;
        this.LimitHeader = limitHeader;
        this.SortById = sortById;
        this.SortBySymbol = sortBySymbol;
        this.SortByRemaining = sortByRemaining;
        this.SortByLimit = sortByLimit;
        this.Search = search;
        this.WorkingOnly = workingOnly;
        this.Page = page;
        this.NextPage = nextPage;
        this.PreviousPage = previousPage;
        this.WorkingCount = workingCount;
        this.BuyExposure = buyExposure;
        this.SellExposure = sellExposure;
        this.LastFill = lastFill;
        this.SelectedOrder = selectedOrder;
        this.CancelSelected = cancelSelected;
        this.EntrySymbol = entrySymbol;
        this.EntryQuantity = entryQuantity;
        this.EntryLimit = entryLimit;
        this.EntryIsBuy = entryIsBuy;
        this.EntryError = entryError;
        this.Submit = submit;
        this.ClearInactive = clearInactive;
        this.rowEdits = rowEdits;
        this.cancelSelectedEdits = cancelSelectedEdits;
        this.clears = clears;
        this.accepted = accepted;

        // The projection releases each row that it still holds.
        this.disposables =
        [
            rows, idHeader, symbolHeader, remainingHeader, limitHeader, sortById, sortBySymbol, sortByRemaining,
            sortByLimit, search, workingOnly, page, nextPage, previousPage, workingCount, buyExposure, sellExposure,
            lastFill, selectedOrder, cancelSelected, entrySymbol, entryQuantity, entryLimit, entryIsBuy, entryError,
            submit, clearInactive, projectedRows
        ];
    }

    public IOneWayBindableValue<IReadOnlyList<IOrderRowViewModel>> Rows { get; }

    public IOneWayBindableValue<string> IdHeader { get; }

    public IOneWayBindableValue<string> SymbolHeader { get; }

    public IOneWayBindableValue<string> RemainingHeader { get; }

    public IOneWayBindableValue<string> LimitHeader { get; }

    public IBindableAction SortById { get; }

    public IBindableAction SortBySymbol { get; }

    public IBindableAction SortByRemaining { get; }

    public IBindableAction SortByLimit { get; }

    public ITwoWayBindableValue<string> Search { get; }

    public ITwoWayBindableValue<bool> WorkingOnly { get; }

    public IOneWayBindableValue<string> Page { get; }

    public IBindableAction NextPage { get; }

    public IBindableAction PreviousPage { get; }

    public IOneWayBindableValue<string> WorkingCount { get; }

    public IOneWayBindableValue<string> BuyExposure { get; }

    public IOneWayBindableValue<string> SellExposure { get; }

    public IOneWayBindableValue<string> LastFill { get; }

    public IOneWayBindableValue<string> SelectedOrder { get; }

    public IBindableAction CancelSelected { get; }

    public ITwoWayBindableValue<string> EntrySymbol { get; }

    public ITwoWayBindableValue<string> EntryQuantity { get; }

    public ITwoWayBindableValue<string> EntryLimit { get; }

    public ITwoWayBindableValue<bool> EntryIsBuy { get; }

    public IOneWayBindableValue<string> EntryError { get; }

    public IBindableAction Submit { get; }

    public IBindableAction ClearInactive { get; }

    public void Dispose()
    {
        foreach (IDisposable disposable in this.disposables)
        {
            disposable.Dispose();
        }
    }

    /// <summary>Builds a blotter over <paramref name="orderCount" /> orders.</summary>
    /// <param name="bindableFactory">Makes the bindable values and the commands.</param>
    /// <param name="orderCount">The number of orders at the start.</param>
    /// <param name="ticksStream">The trades of the market data feed.</param>
    /// <param name="fillsStream">The fills of the exchange.</param>
    public static IBlotterViewModel Create(
        IBindableFactory bindableFactory,
        int orderCount,
        Stream<Tick> ticksStream,
        Stream<Fill> fillsStream) =>
        Transaction.Run(() =>
            ForwardReference<BlotterViewModel>.WithoutCaptures(viewModelLoop =>
            {
                StreamSink<Unit> sortById = Stream.CreateSink<Unit>();
                StreamSink<Unit> sortBySymbol = Stream.CreateSink<Unit>();
                StreamSink<Unit> sortByRemaining = Stream.CreateSink<Unit>();
                StreamSink<Unit> sortByLimit = Stream.CreateSink<Unit>();
                StreamSink<Unit> nextPage = Stream.CreateSink<Unit>();
                StreamSink<Unit> previousPage = Stream.CreateSink<Unit>();
                StreamSink<Unit> cancelSelected = Stream.CreateSink<Unit>();
                StreamSink<Unit> submit = Stream.CreateSink<Unit>();
                StreamSink<Unit> clearInactive = Stream.CreateSink<Unit>();
                StreamSink<string> entryQuantityEdits = Stream.CreateSink<string>();

                CellSink<string> search = Cell.CreateSink(string.Empty);
                CellSink<bool> workingOnly = Cell.CreateSink(false);
                CellSink<string> entrySymbol = Cell.CreateSink(string.Empty);
                CellSink<string> entryLimit = Cell.CreateSink(string.Empty);
                CellSink<bool> entryIsBuy = Cell.CreateSink(true);

                // The edits that the parts below make go into the collection that those parts
                // read. The forward reference closes each of these cycles.
                Stream<OrderEdit> rowEditsLoop = viewModelLoop.Map(static viewModel => viewModel.rowEdits).SwitchS();

                Stream<OrderEdit> cancelSelectedLoop =
                    viewModelLoop.Map(static viewModel => viewModel.cancelSelectedEdits).SwitchS();

                Stream<OrderEdit> clearsLoop = viewModelLoop.Map(static viewModel => viewModel.clears).SwitchS();
                Stream<OrderItem> acceptedLoop = viewModelLoop.Map(static viewModel => viewModel.accepted).SwitchS();

                ReactiveCollection<string, SymbolIdentity, Quote> quotes =
                    ReactiveCollection.Create(
                        initialEntries: Orders.Quotes,
                        ticksStream.Map(static tick =>
                            QuoteEdit.Update(key: tick.Symbol, transform: quote => quote.Trade(tick.LastCents))));

                ReactiveCollection<int, OrderIdentity, OrderState> orders =
                    ReactiveCollection.Create(
                        initialEntries: Orders.Seed(orderCount),
                        fillsStream.Map(static fill =>
                            OrderEdit.Update(key: fill.OrderId, transform: state => state.Fill(fill.Quantity))),
                        rowEditsLoop,
                        cancelSelectedLoop,
                        clearsLoop,
                        acceptedLoop.Map(static item => OrderEdit.Add(item)));

                Cell<Maybe<SortSelection>> sort =
                    new[]
                        {
                            sortById.MapTo(OrderColumn.Id),
                            sortBySymbol.MapTo(OrderColumn.Symbol),
                            sortByRemaining.MapTo(OrderColumn.Remaining),
                            sortByLimit.MapTo(OrderColumn.Limit)
                        }
                        .OrElse()
                        .Accum(
                            initialState: Maybe<SortSelection>.None,
                            f: static (column, current) => Maybe.Some(current.UpdateSort(column)));

                // The search reads only the identity, thus a fill or an amendment costs it no
                // test. The filter on the status reads the state.
                ReactiveCollection<int, OrderIdentity, OrderState> visible =
                    orders
                        .FilterByIdentity(
                            criteriaCell: search,
                            predicate: static (text, identity) => identity.Matches(text))
                        .Filter(
                            criteriaCell: workingOnly,
                            predicate: static (only, _, state) => !only || state.IsWorking)
                        .SortBy(sort.Map(static selection => selection.Order()));

                // A change of the search or of the filter goes back to the first page. A change
                // of the sort keeps the page, because it keeps the members.
                Cell<int> offset =
                    new[]
                        {
                            nextPage.MapTo(static (int at) => at + PageSize),
                            previousPage.MapTo(static (int at) => at - PageSize),
                            search.Updates().MapTo(static (int _) => 0),
                            workingOnly.Updates().MapTo(static (int _) => 0)
                        }
                        .OrElse()
                        .Accum(initialState: 0, f: static (move, at) => Math.Max(val1: 0, val2: move(at)));

                ReactiveCollection<int, OrderIdentity, OrderState> page =
                    visible.Slice(offsetCell: offset, limitCell: Cell.Constant(PageSize));

                MappedItems<OrderRowViewModel> rows =
                    page.Map(
                        project: key =>
                            OrderRowViewModel.Create(
                                bindableFactory: bindableFactory,
                                page: page,
                                quotes: quotes,
                                key: key),
                        onEvicted: static row => row.Dispose());

                Stream<OrderEdit> rowEdits =
                    rows.Items
                        .Map(static items => items.Select(static row => row.EditsStream).OrElse())
                        .SwitchS();

                Stream<int> rowSelections =
                    rows.Items
                        .Map(static items => items.Select(static row => row.SelectionsStream).OrElse())
                        .SwitchS();

                // The entry form. A submit that the form accepts gets the next identifier, and the
                // identifier is a fold over the accepted orders. That fold reads the identifier
                // that it makes, thus it is a loop.
                Cell<string> entryQuantity =
                    entryQuantityEdits.OrElse(acceptedLoop.MapTo(string.Empty)).Hold(string.Empty);

                Cell<EntryResult> entry =
                    entrySymbol.Lift(
                        c2: entryQuantity,
                        c3: entryLimit,
                        c4: entryIsBuy,
                        f: EntryResult.Validate);

                (Cell<int> _, Stream<OrderItem> accepted) =
                    Cell.Loop<int>()
                        .WithCaptures(nextId =>
                        {
                            Stream<OrderItem> acceptedOrders =
                                submit
                                    .Snapshot(c: entry, f: static (_, result) => result.Request)
                                    .FilterSome()
                                    .Snapshot(c: nextId, f: static (request, id) => request.ToItem(id));

                            return (
                                Cell: acceptedOrders.Accum(initialState: orderCount + 1, f: static (_, id) => id + 1),
                                Captures: acceptedOrders);
                        });

                // The selected order is the row that the user selected last, or the order that the
                // form submitted last.
                Cell<Maybe<int>> selected =
                    rowSelections
                        .OrElse(accepted.Map(static item => item.Identity.Id))
                        .Map(static key => Maybe.Some(key))
                        .Hold(Maybe<int>.None);

                Cell<Maybe<OrderItem>> selectedItem =
                    selected
                        .Map(key =>
                            key.Match(
                                onSome: orders.ItemCell,
                                onNone: static () => Cell.Constant(Maybe<OrderItem>.None)))
                        .SwitchC();

                Stream<OrderEdit> cancelSelectedEdits =
                    cancelSelected
                        .Snapshot(c: selected, f: static (_, key) => key)
                        .FilterSome()
                        .Map(static key => OrderEdit.Update(key: key, transform: static state => state.Cancel()));

                // Each order that is not working, across the full collection, and not across the
                // page.
                ReactiveCollection<int, OrderIdentity, OrderState> inactive =
                    orders.Filter(static (_, state) => !state.IsWorking);

                Stream<OrderEdit> clears =
                    clearInactive.Snapshot(c: inactive.KeysCell, f: static (_, keys) => OrderEdit.Remove([.. keys]));

                Cell<int> working =
                    orders.Fold(
                        select: static (_, state) => state.IsWorking ? 1 : 0,
                        zero: 0,
                        add: static (x, y) => x + y,
                        subtract: static (x, y) => x - y);

                Cell<Exposure> exposure =
                    orders.Fold(
                        select: Exposure.Of,
                        zero: default,
                        add: Exposure.Add,
                        subtract: Exposure.Subtract);

                Cell<int> pageCount =
                    visible.KeysCell.Map(static keys => Math.Max(val1: 1, val2: (keys.Count + PageSize - 1) / PageSize));

                return new BlotterViewModel(
                    rows: bindableFactory.CreateOneWay(
                        rows.Items.Map(static IReadOnlyList<IOrderRowViewModel> (items) => items)),
                    idHeader: bindableFactory.CreateOneWay(
                        sort.Map(static selection => selection.Caption(column: OrderColumn.Id, name: "Order"))),
                    symbolHeader: bindableFactory.CreateOneWay(
                        sort.Map(static selection => selection.Caption(column: OrderColumn.Symbol, name: "Symbol"))),
                    remainingHeader: bindableFactory.CreateOneWay(
                        sort.Map(static selection => selection.Caption(column: OrderColumn.Remaining, name: "Remaining"))),
                    limitHeader: bindableFactory.CreateOneWay(
                        sort.Map(static selection => selection.Caption(column: OrderColumn.Limit, name: "Limit"))),
                    sortById: bindableFactory.CreateBindableAction(sortById),
                    sortBySymbol: bindableFactory.CreateBindableAction(sortBySymbol),
                    sortByRemaining: bindableFactory.CreateBindableAction(sortByRemaining),
                    sortByLimit: bindableFactory.CreateBindableAction(sortByLimit),
                    search: bindableFactory.CreateTwoWay(search),
                    workingOnly: bindableFactory.CreateTwoWay(workingOnly),
                    page: bindableFactory.CreateOneWay(
                        offset.Lift(
                            c2: pageCount,
                            f: static (at, count) =>
                                string.Format(
                                    provider: CultureInfo.InvariantCulture,
                                    format: "Page {0:N0} of {1:N0}",
                                    arg0: at / PageSize + 1,
                                    arg1: count))),
                    nextPage: bindableFactory.CreateBindableAction(
                        firingsStreamSink: nextPage,
                        isEnabledCell: offset.Lift(c2: visible.KeysCell, f: static (at, keys) => at + PageSize < keys.Count)),
                    previousPage: bindableFactory.CreateBindableAction(
                        firingsStreamSink: previousPage,
                        isEnabledCell: offset.Map(static at => at > 0)),
                    workingCount: bindableFactory.CreateOneWay(
                        working.Map(static count => Orders.Quantity(count) + " working")),
                    buyExposure: bindableFactory.CreateOneWay(
                        exposure.Map(static value => "Buy " + Orders.Money(value.BuyCents))),
                    sellExposure: bindableFactory.CreateOneWay(
                        exposure.Map(static value => "Sell " + Orders.Money(value.SellCents))),
                    lastFill: bindableFactory.CreateOneWay(
                        fillsStream
                            .Map(static fill => "Order " + Orders.Quantity(fill.OrderId) + " filled " + Orders.Quantity(fill.Quantity))
                            .Hold("No fills")),
                    selectedOrder: bindableFactory.CreateOneWay(selectedItem.Map(Describe)),
                    cancelSelected: bindableFactory.CreateBindableAction(
                        firingsStreamSink: cancelSelected,
                        isEnabledCell: selectedItem.Map(static item =>
                            item.Match(onSome: static value => value.State.IsWorking, onNone: static () => false))),
                    entrySymbol: bindableFactory.CreateTwoWay(entrySymbol),
                    entryQuantity: bindableFactory.CreateTwoWay(cell: entryQuantity, editsStreamSink: entryQuantityEdits),
                    entryLimit: bindableFactory.CreateTwoWay(entryLimit),
                    entryIsBuy: bindableFactory.CreateTwoWay(entryIsBuy),
                    entryError: bindableFactory.CreateOneWay(entry.Map(static result => result.Error)),
                    submit: bindableFactory.CreateBindableAction(
                        firingsStreamSink: submit,
                        isEnabledCell: entry.Map(static result =>
                            result.Request.Match(onSome: static _ => true, onNone: static () => false))),
                    clearInactive: bindableFactory.CreateBindableAction(
                        firingsStreamSink: clearInactive,
                        isEnabledCell: inactive.KeysCell.Map(static keys => keys.Count > 0)),
                    rowEdits: rowEdits,
                    cancelSelectedEdits: cancelSelectedEdits,
                    clears: clears,
                    accepted: accepted,
                    projectedRows: rows);
            }));

    private static string Describe(Maybe<OrderItem> item) =>
        item.Match(
            onSome: static value =>
                string.Format(
                    provider: CultureInfo.InvariantCulture,
                    format: "{0} {1} {2:N0} {3} at {4}, {5:N0} filled, {6}",
                    value.Identity.Id,
                    value.Identity.Side,
                    value.State.Quantity,
                    value.Identity.Symbol,
                    Orders.Money(value.State.LimitCents),
                    value.State.Filled,
                    value.State.Status),
            onNone: static () => "No order selected");
}
