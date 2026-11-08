using System;
using System.Collections.Generic;
using SodaFlow.Functional;
using OrderOrder =
    SodaFlow.Collections.KeyOrder<
        int,
        SodaFlow.Benchmarks.Blotter.OrderIdentity,
        SodaFlow.Benchmarks.Blotter.OrderState>;

namespace SodaFlow.Benchmarks.Blotter;

/// <summary>A column of the blotter that the list can sort on.</summary>
internal enum OrderColumn
{
    /// <summary>The identifier. It is part of the identity.</summary>
    Id,

    /// <summary>The symbol. It is part of the identity.</summary>
    Symbol,

    /// <summary>The remaining quantity. Fills and amendments change it.</summary>
    Remaining,

    /// <summary>The limit price. No edit here changes it.</summary>
    Limit
}

/// <summary>The column that the list sorts on, and the direction.</summary>
// ReSharper disable once InheritdocConsiderUsage - The summary says what this type is here, which the base does not.
internal sealed record SortSelection(OrderColumn Column, bool IsDescending)
{
    /// <summary>
    ///     This selection, as an order that the sort stage can hold. The orders sort on
    ///     an <c>int</c>, a <c>string</c>, and a <c>long</c>, and one cell holds each.
    /// </summary>
    /// <remarks>
    ///     A sort stage knows the same order in the opposite direction only from the
    ///     same selector and comparer instances. Thus, the selectors are static and
    ///     the comparers are the default instances.
    /// </remarks>
    internal OrderOrder Order =>
        this.Column switch
        {
            OrderColumn.Id => OrderOrder.ByIdentity(
                selector: static identity => identity.Id,
                sortComparer: Comparer<int>.Default,
                keyComparer: Comparer<int>.Default,
                isDescending: this.IsDescending),
            OrderColumn.Symbol => OrderOrder.ByIdentity(
                selector: static identity => identity.Symbol,
                sortComparer: StringComparer.Ordinal,
                keyComparer: Comparer<int>.Default,
                isDescending: this.IsDescending),
            OrderColumn.Remaining => OrderOrder.By(
                selector: static (_, state) => state.Remaining,
                sortComparer: Comparer<long>.Default,
                keyComparer: Comparer<int>.Default,
                isDescending: this.IsDescending),
            _ => OrderOrder.By(
                selector: static (_, state) => state.LimitCents,
                sortComparer: Comparer<long>.Default,
                keyComparer: Comparer<int>.Default,
                isDescending: this.IsDescending)
        };
}

/// <summary>The operations on a sort selection that can be missing.</summary>
internal static class SortSelectionExtensions
{
    /// <summary>The order that applies. It is arrival order until a click on a header.</summary>
    internal static OrderOrder Order(this Maybe<SortSelection> sortSelection) =>
        sortSelection.Map(static selection => selection.Order).ValueOr(OrderOrder.ByArrival);

    /// <summary>
    ///     The result of a click on a header. The same column changes the direction,
    ///     and a different column becomes the sort column in ascending order.
    /// </summary>
    internal static SortSelection UpdateSort(this Maybe<SortSelection> sortSelection, OrderColumn column) =>
        sortSelection.Match(
            onSome: selection =>
                column == selection.Column
                    ? selection with { IsDescending = !selection.IsDescending }
                    : new SortSelection(Column: column, IsDescending: false),
            onNone: () => new SortSelection(Column: column, IsDescending: false));

    /// <summary>The text of a header, with a mark when it is the sort column.</summary>
    internal static string Caption(this Maybe<SortSelection> sortSelection, OrderColumn column, string name) =>
        name
        + sortSelection.Match(
            onSome: selection =>
                column == selection.Column ? selection.IsDescending ? " ▼" : " ▲" : string.Empty,
            onNone: static () => string.Empty);
}
