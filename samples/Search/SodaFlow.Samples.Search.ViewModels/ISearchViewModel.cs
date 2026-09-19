using System;
using System.Collections.Generic;
using SodaFlow.Bindable.ObjectModel;

namespace SodaFlow.Samples.Search.ViewModels;

/// <summary>
///     The contract that the views bind to.
/// </summary>
/// <remarks>
///     <para>
///         XAML binds by name to the data context, at each type of that data context. Thus a type
///         for the data context has a value. The type shows the bindable members, gives the
///         designer and the compiler a target for the binding paths, and keeps the construction of
///         the view model away from a view.
///     </para>
///     <para>
///         For that cause this interface has only the bound members. <c>Create</c> is not on it,
///         because a view does not build a view model.
///     </para>
///     <para>
///         <see cref="IDisposable" /> is here because it is part of the contract and not a part
///         of the implementation. The code that built one must release it, through this interface
///         and through the class. Disposal here removes more than the bindables, because the
///         asynchronous pipeline goes with them, but that is a property of the implementation and
///         not of this contract.
///     </para>
/// </remarks>
// ReSharper disable once InheritdocConsiderUsage
public interface ISearchViewModel : IDisposable
{
    /// <summary>The text from the user. This is two-way, thus the view reads it and writes
    /// it.</summary>
    ITwoWayBindableValue<string> Query { get; }

    IOneWayBindableValue<IReadOnlyList<string>> Results { get; }

    /// <summary>"Searching..." during a search, and a count at each other time.</summary>
    IOneWayBindableValue<string> Summary { get; }

    IOneWayBindableValue<string> Error { get; }

    /// <summary>This is not part of <see cref="Error" />, thus the view can bind the visibility
    /// to it.</summary>
    IOneWayBindableValue<bool> HasError { get; }

    IOneWayBindableValue<bool> IsBusy { get; }

    /// <summary>This is enabled only during a search.</summary>
    IBindableAction Cancel { get; }
}
