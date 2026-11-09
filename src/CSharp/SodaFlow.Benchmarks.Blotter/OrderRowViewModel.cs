using System;
using System.Collections.Generic;
using SodaFlow.Bindable.ObjectModel;
using SodaFlow.Collections;
using SodaFlow.Functional;

namespace SodaFlow.Benchmarks.Blotter;

/// <summary>One row. It holds the cells that follow one order through the page that shows it.</summary>
// ReSharper disable once InheritdocConsiderUsage - The summary says what this type is here, which the base does not.
internal sealed class OrderRowViewModel : IOrderRowViewModel
{
    private readonly IReadOnlyList<IDisposable> disposables;

    private OrderRowViewModel(
        IOneWayBindableValue<string> id,
        IOneWayBindableValue<string> symbol,
        IOneWayBindableValue<string> side,
        IOneWayBindableValue<string> trader,
        IOneWayBindableValue<string> quantity,
        IOneWayBindableValue<string> filled,
        IOneWayBindableValue<string> remaining,
        IOneWayBindableValue<string> limit,
        IOneWayBindableValue<string> lastPrice,
        IOneWayBindableValue<string> notional,
        IOneWayBindableValue<string> edge,
        IOneWayBindableValue<string> status,
        IOneWayBindableValue<bool> isWorking,
        IBindableAction cancel,
        IBindableAction amendUp,
        IBindableAction amendDown,
        IBindableAction select,
        Stream<CollectionEdit<int, OrderIdentity, OrderState>> editsStream,
        Stream<int> selectionsStream)
    {
        this.Id = id;
        this.Symbol = symbol;
        this.Side = side;
        this.Trader = trader;
        this.Quantity = quantity;
        this.Filled = filled;
        this.Remaining = remaining;
        this.Limit = limit;
        this.LastPrice = lastPrice;
        this.Notional = notional;
        this.Edge = edge;
        this.Status = status;
        this.IsWorking = isWorking;
        this.Cancel = cancel;
        this.AmendUp = amendUp;
        this.AmendDown = amendDown;
        this.Select = select;
        this.EditsStream = editsStream;
        this.SelectionsStream = selectionsStream;

        this.disposables =
        [
            id, symbol, side, trader, quantity, filled, remaining, limit, lastPrice, notional, edge, status,
            isWorking, cancel, amendUp, amendDown, select
        ];
    }

    /// <summary>The edits that the commands of this row make. The blotter sends them back in.</summary>
    internal Stream<CollectionEdit<int, OrderIdentity, OrderState>> EditsStream { get; }

    /// <summary>The key of this order, each time a user selects the row.</summary>
    internal Stream<int> SelectionsStream { get; }

    public IOneWayBindableValue<string> Id { get; }

    public IOneWayBindableValue<string> Symbol { get; }

    public IOneWayBindableValue<string> Side { get; }

    public IOneWayBindableValue<string> Trader { get; }

    public IOneWayBindableValue<string> Quantity { get; }

    public IOneWayBindableValue<string> Filled { get; }

    public IOneWayBindableValue<string> Remaining { get; }

    public IOneWayBindableValue<string> Limit { get; }

    public IOneWayBindableValue<string> LastPrice { get; }

    public IOneWayBindableValue<string> Notional { get; }

    public IOneWayBindableValue<string> Edge { get; }

    public IOneWayBindableValue<string> Status { get; }

    public IOneWayBindableValue<bool> IsWorking { get; }

    public IBindableAction Cancel { get; }

    public IBindableAction AmendUp { get; }

    public IBindableAction AmendDown { get; }

    public IBindableAction Select { get; }

    public void Dispose()
    {
        foreach (IDisposable disposable in this.disposables)
        {
            disposable.Dispose();
        }
    }

    /// <summary>The row for one order, built from the page that shows it.</summary>
    /// <remarks>
    ///     The page gives the identity and the state. Thus, an order that left the
    ///     page shows empty text, and its commands stay disabled. The quotes collection
    ///     gives the quote, through a switch on the symbol of the order.
    /// </remarks>
    internal static OrderRowViewModel Create(
        IBindableFactory bindableFactory,
        ReactiveCollection<int, OrderIdentity, OrderState> page,
        ReactiveCollection<string, SymbolIdentity, Quote> quotes,
        int key)
    {
        Cell<Maybe<OrderIdentity>> identity = page.IdentityCell(key);
        Cell<Maybe<OrderState>> state = page.StateCell(key);

        Cell<Maybe<Quote>> quote =
            identity
                .Map(current =>
                    current.Match(
                        onSome: value => quotes.StateCell(value.Symbol),
                        onNone: static () => Cell.Constant(Maybe<Quote>.None)))
                .SwitchC();

        Cell<bool> isWorking = state.Map(static current => current.Match(onSome: static value => value.IsWorking, onNone: static () => false));

        Cell<bool> canAmendDown =
            state.Map(static current =>
                current.Match(onSome: static value => value.CanAmend(-Orders.Lot), onNone: static () => false));

        StreamSink<Unit> cancel = Stream.CreateSink<Unit>();
        StreamSink<Unit> amendUp = Stream.CreateSink<Unit>();
        StreamSink<Unit> amendDown = Stream.CreateSink<Unit>();
        StreamSink<Unit> select = Stream.CreateSink<Unit>();

        return new OrderRowViewModel(
            id: bindableFactory.CreateOneWay(IdentityText(identity: identity, text: static value => Orders.Quantity(value.Id))),
            symbol: bindableFactory.CreateOneWay(IdentityText(identity: identity, text: static value => value.Symbol)),
            side: bindableFactory.CreateOneWay(IdentityText(identity: identity, text: static value => value.Side == Blotter.Side.Buy ? "Buy" : "Sell")),
            trader: bindableFactory.CreateOneWay(IdentityText(identity: identity, text: static value => value.Trader)),
            quantity: bindableFactory.CreateOneWay(StateText(state: state, text: static value => Orders.Quantity(value.Quantity))),
            filled: bindableFactory.CreateOneWay(StateText(state: state, text: static value => Orders.Quantity(value.Filled))),
            remaining: bindableFactory.CreateOneWay(StateText(state: state, text: static value => Orders.Quantity(value.Remaining))),
            limit: bindableFactory.CreateOneWay(StateText(state: state, text: static value => Orders.Money(value.LimitCents))),
            lastPrice: bindableFactory.CreateOneWay(
                quote.Map(static current =>
                    current.Match(onSome: static value => Orders.Money(value.LastCents), onNone: static () => string.Empty))),
            notional: bindableFactory.CreateOneWay(
                state.Lift(
                    c2: quote,
                    f: static (currentState, currentQuote) =>
                        currentState.Match(
                            onSome: value =>
                                currentQuote.Match(
                                    onSome: market => Orders.Money(value.Remaining * market.LastCents),
                                    onNone: static () => string.Empty),
                            onNone: static () => string.Empty))),
            edge: bindableFactory.CreateOneWay(identity.Lift(c2: state, c3: quote, f: EdgeText)),
            status: bindableFactory.CreateOneWay(StateText(state: state, text: static value => value.Status.ToString())),
            isWorking: bindableFactory.CreateOneWay(isWorking),
            cancel: bindableFactory.CreateBindableAction(firingsStreamSink: cancel, isEnabledCell: isWorking),
            amendUp: bindableFactory.CreateBindableAction(firingsStreamSink: amendUp, isEnabledCell: isWorking),
            amendDown: bindableFactory.CreateBindableAction(firingsStreamSink: amendDown, isEnabledCell: canAmendDown),
            select: bindableFactory.CreateBindableAction(select),
            editsStream: CollectionEdit<int, OrderIdentity, OrderState>.FromUpdates(
                key: key,
                transformsStream: new[]
                    {
                        cancel.MapTo(static (OrderState current) => current.Cancel()),
                        amendUp.MapTo(static (OrderState current) => current.Amend(Orders.Lot)),
                        amendDown.MapTo(static (OrderState current) => current.Amend(-Orders.Lot))
                    }
                    .OrElse()),
            selectionsStream: select.MapTo(key));
    }

    private static Cell<string> IdentityText(Cell<Maybe<OrderIdentity>> identity, Func<OrderIdentity, string> text) =>
        identity.Map(current => current.Match(onSome: text, onNone: static () => string.Empty));

    private static Cell<string> StateText(Cell<Maybe<OrderState>> state, Func<OrderState, string> text) =>
        state.Map(current => current.Match(onSome: text, onNone: static () => string.Empty));

    /// <summary>
    ///     The distance from the limit to the last price. It is positive when the market
    ///     is better than the limit: below it for a buy, and above it for a sell.
    /// </summary>
    private static string EdgeText(Maybe<OrderIdentity> identity, Maybe<OrderState> state, Maybe<Quote> quote) =>
        identity.Match(
            onSome: order =>
                state.Match(
                    onSome: current =>
                        quote.Match(
                            onSome: market =>
                                Orders.Money(
                                    order.Side == Blotter.Side.Buy
                                        ? current.LimitCents - market.AskCents
                                        : market.BidCents - current.LimitCents),
                            onNone: static () => string.Empty),
                    onNone: static () => string.Empty),
            onNone: static () => string.Empty);
}
