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

    /// <summary>
    ///     Whether the selected scene is one the damping applies to.
    /// </summary>
    /// <remarks>
    ///     The one-ball scene is deliberately elastic, so the controls appear only for the other
    ///     two. A function of the selection, like the summary.
    /// </remarks>
    IOneWayBindableValue<bool> IsDampingAvailable { get; }

    /// <summary>
    ///     Whether a bounce changes speed rather than keeping it. Two-way, for a checkbox.
    /// </summary>
    ITwoWayBindableValue<bool> DampingEnabled { get; }

    /// <summary>
    ///     The same answer as <see cref="DampingEnabled" />, for anything that is not the checkbox.
    /// </summary>
    /// <remarks>
    ///     A two-way value is written by the control that owns the input, and does not announce
    ///     that write back at it - which is right for the writer and leaves a second reader of the
    ///     same value hearing nothing. The slider is that second reader: it is enabled by the
    ///     checkbox's answer without being the control that supplies it, so it binds here. Anything
    ///     else that comes to depend on the checkbox should bind here too.
    /// </remarks>
    IOneWayBindableValue<bool> IsDampingAdjustable { get; }

    /// <summary>
    ///     What a bounce multiplies speed by while damping is on. Two-way, for a slider.
    /// </summary>
    /// <remarks>
    ///     Below one a body loses speed at every bounce and comes to rest; at one it bounces
    ///     forever; above one it gains speed and climbs. Read at the moment of each bounce, so
    ///     moving the slider changes the next bounce rather than the flight already under way.
    /// </remarks>
    ITwoWayBindableValue<double> Damping { get; }

    /// <summary>The smallest value <see cref="Damping" /> takes.</summary>
    double MinimumDamping { get; }

    /// <summary>The largest value <see cref="Damping" /> takes.</summary>
    double MaximumDamping { get; }
}
