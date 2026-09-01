using System;
using System.Collections.Generic;
using System.Globalization;
using JetBrains.Annotations;
using SodaFlow.Bindable.ObjectModel;
using SodaFlow.Functional;

namespace SodaFlow.Samples.Counter.ViewModels;

/// <summary>
///     A counter, in about twenty lines of graph.
/// </summary>
/// <remarks>
///     <para>
///         Worth noticing what is not here. There is no <c>count</c> field, no
///         <c>OnPropertyChanged("Count")</c>, and nothing that has to remember to re-evaluate
///         whether Reset should be enabled. The count is a fold over a stream of edits, the
///         label is a function of the count, and Reset's enablement is another function of it -
///         so none of them can disagree with each other.
///     </para>
///     <para>
///         The view binds to <c>SomeProperty.Value</c>, never to <c>SomeProperty</c>. That is
///         how the bindable object model works: each property is an object that raises
///         PropertyChanged for "Value".
///     </para>
/// </remarks>
public sealed class CounterViewModel
    : IDisposable
{
    private readonly IReadOnlyList<IDisposable> disposables;

    #region Constructor

    private CounterViewModel(
        IOneWayBindableValue<int> count,
        IOneWayBindableValue<string> countText,
        IBindableAction increment,
        IBindableAction decrement,
        IBindableAction reset)
    {
        this.Count = count;
        this.CountText = countText;
        this.Increment = increment;
        this.Decrement = decrement;
        this.Reset = reset;

        this.disposables =
            new IDisposable[] { count, countText, increment, decrement, reset };
    }

    #endregion

    /// <summary>The current count, for anything that wants the number itself.</summary>
    [UsedImplicitly] // This property is actually unused, but provided simply as a sample
    public IOneWayBindableValue<int> Count { get; }

    /// <summary>The count as text, formatted for the current culture.</summary>
    public IOneWayBindableValue<string> CountText { get; }

    public IBindableAction Increment { get; }

    public IBindableAction Decrement { get; }

    /// <summary>Enabled only when the count is not already zero.</summary>
    public IBindableAction Reset { get; }

    /// <summary>
    ///     Every entry holds a subscription into the graph, and disposing it is what releases
    ///     that subscription.
    /// </summary>
    /// <remarks>
    ///     The list is of <see cref="IDisposable" /> rather than of bindables because a view
    ///     model's disposables are not all bindables in general - a graph using MapAsync also
    ///     holds an AsyncMapStatus, as the search sample does - and disposal is the only thing
    ///     being asked of any of them here.
    /// </remarks>
    public void Dispose()
    {
        foreach (IDisposable disposable in this.disposables)
        {
            disposable.Dispose();
        }
    }

    public static CounterViewModel Create() =>
        // One transaction for the whole graph. Nothing here fires during construction, so it
        // changes no behavior in this sample - but it is the habit worth having: a graph
        // containing a Values() stream loses its first firing without it, silently.
        Transaction.Run(() =>
        {
            StreamSink<Unit> increment = Stream.CreateSink<Unit>();
            StreamSink<Unit> decrement = Stream.CreateSink<Unit>();
            StreamSink<Unit> reset = Stream.CreateSink<Unit>();

            // Each button contributes a function of the current count rather than a
            // number, which is what lets Reset join the same stream as the other two
            // instead of needing a mechanism of its own.
            Stream<Func<int, int>> edits =
                new[]
                {
                    increment.MapTo((int n) => n + 1), decrement.MapTo((int n) => n - 1), reset.MapTo((int _) => 0)
                }.OrElse();

            Cell<int> count = edits.Accum(initialState: 0, f: (edit, n) => edit(n));

            return new CounterViewModel(
                count: count.ToOneWay(),
                countText: count.Map(n => n.ToString(CultureInfo.CurrentCulture)).ToOneWay(),
                increment: increment.ToBindableAction(),
                decrement: decrement.ToBindableAction(),

                // Enablement is just another cell. Nothing raises CanExecuteChanged by
                // hand; the command follows the cell, and the cell follows the count.
                reset: reset.ToBindableAction(count.Map(n => n != 0)));
        });
}