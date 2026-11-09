using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SodaFlow.Collections;

namespace SodaFlow.Benchmarks.Blotter;

/// <summary>The side of an order.</summary>
internal enum Side
{
    /// <summary>An order to buy.</summary>
    Buy,

    /// <summary>An order to sell.</summary>
    Sell
}

/// <summary>The status of an order.</summary>
internal enum OrderStatus
{
    /// <summary>The market can fill the remaining quantity.</summary>
    Working,

    /// <summary>The market filled all the quantity.</summary>
    Filled,

    /// <summary>A trader canceled the order before the market filled it.</summary>
    Canceled
}

/// <summary>The part of an order that no edit can change.</summary>
// ReSharper disable once InheritdocConsiderUsage - The summary says what this type is here, which the base does not.
internal sealed record OrderIdentity(int Id, string Symbol, Side Side, string Trader) : IIdentity<int>
{
    /// <inheritdoc />
    public int Key => this.Id;

    /// <summary>
    ///     True when the search text is empty, starts the symbol, or is in the name of
    ///     the trader.
    /// </summary>
    internal bool Matches(string search) =>
        search.Length == 0
        || this.Symbol.StartsWith(value: search, comparisonType: StringComparison.OrdinalIgnoreCase)
        || this.Trader.Contains(value: search, comparisonType: StringComparison.OrdinalIgnoreCase);
}

/// <summary>The part of an order that edits change.</summary>
/// <param name="Quantity">The quantity that the trader asked for.</param>
/// <param name="Filled">The quantity that the market filled.</param>
/// <param name="LimitCents">The worst price that the trader accepts, in cents.</param>
/// <param name="Status">The status of the order.</param>
// ReSharper disable once InheritdocConsiderUsage - The summary says what this type is here, which the base does not.
internal sealed record OrderState(long Quantity, long Filled, long LimitCents, OrderStatus Status)
{
    /// <summary>The quantity that the market can still fill.</summary>
    internal long Remaining => this.Quantity - this.Filled;

    internal bool IsWorking => this.Status == OrderStatus.Working;

    /// <summary>
    ///     A fill of up to <paramref name="quantity" />. An order that is not working
    ///     does not change.
    /// </summary>
    internal OrderState Fill(long quantity)
    {
        if (!this.IsWorking)
        {
            return this;
        }

        long filled = this.Filled + Math.Min(val1: quantity, val2: this.Remaining);

        return this with
        {
            Filled = filled,
            Status = filled == this.Quantity ? OrderStatus.Filled : OrderStatus.Working
        };
    }

    internal OrderState Cancel() => this.IsWorking ? this with { Status = OrderStatus.Canceled } : this;

    /// <summary>
    ///     A change of the quantity. The quantity cannot go below the filled quantity
    ///     plus one lot.
    /// </summary>
    internal OrderState Amend(long change) =>
        this.CanAmend(change) ? this with { Quantity = this.Quantity + change } : this;

    internal bool CanAmend(long change) => this.IsWorking && this.Quantity + change >= this.Filled + Orders.Lot;
}

/// <summary>A symbol of the market, the identity of a quote.</summary>
// ReSharper disable once InheritdocConsiderUsage - The summary says what this type is here, which the base does not.
internal sealed record SymbolIdentity(string Symbol) : IIdentity<string>
{
    /// <inheritdoc />
    public string Key => this.Symbol;
}

/// <summary>The prices of a symbol, in cents.</summary>
// ReSharper disable once InheritdocConsiderUsage - The summary says what this type is here, which the base does not.
internal sealed record Quote(long BidCents, long AskCents, long LastCents)
{
    /// <summary>The quote after a trade at <paramref name="lastCents" />.</summary>
    internal Quote Trade(long lastCents) =>
        new(BidCents: lastCents - Orders.Spread, AskCents: lastCents + Orders.Spread, LastCents: lastCents);
}

/// <summary>A trade on the market. The market data feed sends one for each trade.</summary>
// ReSharper disable once InheritdocConsiderUsage - The summary says what this type is here, which the base does not.
internal readonly record struct Tick(string Symbol, long LastCents);

/// <summary>A fill of an order. The exchange sends one for each fill.</summary>
// ReSharper disable once InheritdocConsiderUsage - The summary says what this type is here, which the base does not.
internal readonly record struct Fill(int OrderId, long Quantity);

/// <summary>An order that the entry form accepted, which has no identifier yet.</summary>
// ReSharper disable once InheritdocConsiderUsage - The summary says what this type is here, which the base does not.
internal sealed record OrderRequest(string Symbol, Side Side, long Quantity, long LimitCents)
{
    internal Item<OrderIdentity, OrderState> ToItem(int id) =>
        new(
            identity: new OrderIdentity(Id: id, Symbol: this.Symbol, Side: this.Side, Trader: Orders.EntryTrader),
            state: new OrderState(
                Quantity: this.Quantity,
                Filled: 0,
                LimitCents: this.LimitCents,
                Status: OrderStatus.Working));
}

/// <summary>The data that a blotter starts with, and the constants of its market.</summary>
/// <remarks>
///     Each value comes from a hash of the index. Thus, two runs and two copies of
///     the library start from the same data.
/// </remarks>
internal static class Orders
{
    /// <summary>The smallest quantity of an order.</summary>
    internal const long Lot = 100;

    /// <summary>The distance from the last price to the bid and to the ask, in cents.</summary>
    internal const long Spread = 1;

    /// <summary>The trader of each order that the entry form makes.</summary>
    internal const string EntryTrader = "Entry Desk";

    private const int SymbolCount = 200;

    private static readonly string[] Traders =
    [
        // ReSharper disable StringLiteralTypo
        "Ackroyd", "Bhatt", "Calloway", "Dimitrova", "Eze", "Fairbairn", "Gruber", "Haddad", "Ivanov", "Jarrett",
        "Kowalski", "Lindqvist", "Moreau", "Nakamura", "Okonkwo", "Pereira", "Quill", "Rasmussen", "Tanaka",
        "Urquhart", "Varga", "Whitlock", "Xu", "Yilmaz", "Zielinski", "Abernathy", "Brennan", "Castellano",
        "Delacroix", "Eriksson"
        // ReSharper restore StringLiteralTypo
    ];

    /// <summary>
    ///     Three letters for each symbol. The step through the 17,576 codes is
    ///     prime to that count, thus no two symbols are equal.
    /// </summary>
    internal static IReadOnlyList<string> Symbols { get; } =
    [
        .. Enumerable.Range(start: 0, count: SymbolCount).Select(static index => Code(index * 7_919 % 17_576))
    ];

    internal static IReadOnlyList<Item<SymbolIdentity, Quote>> Quotes { get; } =
    [
        .. Symbols.Select(static symbol =>
            new Item<SymbolIdentity, Quote>(
                identity: new SymbolIdentity(symbol),
                state: new Quote(BidCents: 0, AskCents: 0, LastCents: 0).Trade(InitialLastCents(symbol))))
    ];

    /// <summary>
    ///     Orders with the identifiers one to <paramref name="count" />. About seven
    ///     in ten work, two in ten have no remaining quantity, and one in ten has the
    ///     canceled status.
    /// </summary>
    internal static IReadOnlyList<Item<OrderIdentity, OrderState>> Seed(int count) =>
        [.. Enumerable.Range(start: 1, count: count).Select(Create)];

    /// <remarks>
    ///     The hash is of the letters, and not <see cref="string.GetHashCode()" />,
    ///     which changes from one process to the next on .NET.
    /// </remarks>
    internal static long InitialLastCents(string symbol) =>
        1_000 + Scramble(symbol.Aggregate(seed: 17u, func: static (hash, letter) => hash * 31 + letter)) % 90_000;

    internal static string Money(long cents) =>
        (cents / 100m).ToString(format: "N2", provider: CultureInfo.InvariantCulture);

    internal static string Quantity(long quantity) =>
        quantity.ToString(format: "N0", provider: CultureInfo.InvariantCulture);

    private static Item<OrderIdentity, OrderState> Create(int id)
    {
        uint hash = Scramble((uint)id);
        string symbol = Symbols[(int)(hash % SymbolCount)];
        long quantity = (hash >> 8) % 50 * Lot + Lot;
        uint kind = (hash >> 16) % 10;

        OrderStatus status =
            kind switch
            {
                < 7 => OrderStatus.Working,
                < 9 => OrderStatus.Filled,
                _ => OrderStatus.Canceled
            };

        long filled =
            status switch
            {
                OrderStatus.Filled => quantity,
                OrderStatus.Working => (hash >> 20) % 3 == 0 ? quantity / Lot / 2 * Lot : 0,
                _ => 0
            };

        // The limit is near the last price, thus some orders are better than the market and some
        // are worse.
        long last = InitialLastCents(symbol);
        long limit = last + (hash >> 4) % 200 - 100;

        return new Item<OrderIdentity, OrderState>(
            identity: new OrderIdentity(
                Id: id,
                Symbol: symbol,
                Side: (hash & 1) == 0 ? Side.Buy : Side.Sell,
                Trader: Traders[(hash >> 24) % Traders.Length]),
            state: new OrderState(Quantity: quantity, Filled: filled, LimitCents: limit, Status: status));
    }

    private static string Code(int value) =>
        new([(char)('A' + value / 676), (char)('A' + value / 26 % 26), (char)('A' + value % 26)]);

    private static uint Scramble(uint value)
    {
        unchecked
        {
            value ^= value >> 16;
            value *= 0x7feb352d;
            value ^= value >> 15;
            value *= 0x846ca68b;
            value ^= value >> 16;
            return value;
        }
    }
}
