using System;
using System.Collections.Generic;
using SodaFlow.Bindable.ObjectModel;

namespace SodaFlow.Samples.Bounce.ViewModels;

/// <summary>
///     What the views bind to.
/// </summary>
/// <remarks>
///     <para>
///         XAML binds by name against whatever the data context happens to be, so the data context
///         is worth stating as a type: it is what makes the bindable members discoverable, gives
///         the designer and the compiler something to check the binding paths against, and keeps a
///         view from depending on how the view model is built.
///     </para>
///     <para>
///         Which is why this is only the bound surface. <c>Create</c> is not on it, because
///         building a view model is not something a view does with one, and neither is anything
///         else a view has no business calling.
///     </para>
///     <para>
///         <see cref="IDisposable" /> is here because it is part of the contract rather than an
///         implementation detail: whoever built one has to release it, and that is as true through
///         this interface as through the class.
///     </para>
/// </remarks>
public interface IBounceViewModel : IDisposable
{
    /// <summary>The scenes to offer, smallest first.</summary>
    IReadOnlyList<IScene> Scenes { get; }

    /// <summary>
    ///     Which scene is showing.
    /// </summary>
    /// <remarks>
    ///     Two-way, so it is a value rather than a fact about a control: bind a tab control's
    ///     selected item to <c>SelectedScene.Value</c>, and write it to change which tab is
    ///     showing.
    /// </remarks>
    ITwoWayBindableValue<IScene> SelectedScene { get; }

    /// <summary>The selected scene's description, as a function of the selection.</summary>
    IOneWayBindableValue<string> SelectedSummary { get; }
}
