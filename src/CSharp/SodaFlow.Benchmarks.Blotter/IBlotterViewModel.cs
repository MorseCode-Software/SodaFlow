using System;
using System.Collections.Generic;
using SodaFlow.Bindable.ObjectModel;

namespace SodaFlow.Benchmarks.Blotter;

/// <summary>One row of the blotter: one order on the page.</summary>
// ReSharper disable once InheritdocConsiderUsage - The summary says what this type is here, which the base does not.
internal interface IOrderRowViewModel : IDisposable
{
    IOneWayBindableValue<string> Id { get; }

    IOneWayBindableValue<string> Symbol { get; }

    IOneWayBindableValue<string> Side { get; }

    IOneWayBindableValue<string> Trader { get; }

    IOneWayBindableValue<string> Quantity { get; }

    IOneWayBindableValue<string> Filled { get; }

    IOneWayBindableValue<string> Remaining { get; }

    IOneWayBindableValue<string> Limit { get; }

    /// <summary>The last price of the symbol on the market.</summary>
    IOneWayBindableValue<string> LastPrice { get; }

    /// <summary>The remaining quantity at the last price.</summary>
    IOneWayBindableValue<string> Notional { get; }

    /// <summary>The distance from the limit to the last price, in the direction that is good for the trader.</summary>
    IOneWayBindableValue<string> Edge { get; }

    IOneWayBindableValue<string> Status { get; }

    IOneWayBindableValue<bool> IsWorking { get; }

    IBindableAction Cancel { get; }

    /// <summary>Adds one lot to the quantity.</summary>
    IBindableAction AmendUp { get; }

    /// <summary>Removes one lot from the quantity.</summary>
    IBindableAction AmendDown { get; }

    /// <summary>Makes this order the selected order.</summary>
    IBindableAction Select { get; }
}

/// <summary>
///     A blotter of orders. It has a list with a search, a filter, a sort, and pages.
///     It also has totals, the details of a selected order, and a form for a new order.
/// </summary>
// ReSharper disable once InheritdocConsiderUsage - The summary says what this type is here, which the base does not.
internal interface IBlotterViewModel : IDisposable
{
    IOneWayBindableValue<IReadOnlyList<IOrderRowViewModel>> Rows { get; }

    IOneWayBindableValue<string> IdHeader { get; }

    IOneWayBindableValue<string> SymbolHeader { get; }

    IOneWayBindableValue<string> RemainingHeader { get; }

    IOneWayBindableValue<string> LimitHeader { get; }

    IBindableAction SortById { get; }

    IBindableAction SortBySymbol { get; }

    IBindableAction SortByRemaining { get; }

    IBindableAction SortByLimit { get; }

    /// <summary>Text that a symbol starts with, or that is in the name of a trader.</summary>
    ITwoWayBindableValue<string> Search { get; }

    ITwoWayBindableValue<bool> WorkingOnly { get; }

    IOneWayBindableValue<string> Page { get; }

    IBindableAction NextPage { get; }

    IBindableAction PreviousPage { get; }

    IOneWayBindableValue<string> WorkingCount { get; }

    /// <summary>The remaining quantity of the working buy orders at their limits.</summary>
    IOneWayBindableValue<string> BuyExposure { get; }

    /// <summary>The remaining quantity of the working sell orders at their limits.</summary>
    IOneWayBindableValue<string> SellExposure { get; }

    IOneWayBindableValue<string> LastFill { get; }

    IOneWayBindableValue<string> SelectedOrder { get; }

    IBindableAction CancelSelected { get; }

    ITwoWayBindableValue<string> EntrySymbol { get; }

    ITwoWayBindableValue<string> EntryQuantity { get; }

    ITwoWayBindableValue<string> EntryLimit { get; }

    ITwoWayBindableValue<bool> EntryIsBuy { get; }

    /// <summary>Why the form cannot submit, or an empty string when it can.</summary>
    IOneWayBindableValue<string> EntryError { get; }

    IBindableAction Submit { get; }

    /// <summary>Removes each filled order and each canceled order.</summary>
    IBindableAction ClearInactive { get; }
}
