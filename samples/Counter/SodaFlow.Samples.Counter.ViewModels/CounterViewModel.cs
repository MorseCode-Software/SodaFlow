using System;
using System.Collections.Generic;
using System.Globalization;
using SodaFlow.Bindable.ObjectModel;
using SodaFlow.Functional;

namespace SodaFlow.Samples.Counter.ViewModels;

/// <summary>
///     A counter, in approximately twenty lines of graph.
/// </summary>
/// <remarks>
///     <para>
///         See the code that is not here. There is no <c>count</c> field, no
///         <c>OnPropertyChanged("Count")</c>, and no code that must remember to examine the
///         enabled state of Reset again. The count is a fold across a stream of edits, the label
///         is a function of the count, and the enabled state of Reset is a second function of the
///         count. Thus, the three values are always in agreement.
///     </para>
///     <para>
///         The view binds to <c>SomeProperty.Value</c> and never to <c>SomeProperty</c>. That is
///         the operation of the bindable object model. Each property is an object that raises
///         PropertyChanged for "Value".
///     </para>
/// </remarks>
// ReSharper disable once InheritdocConsiderUsage
public sealed class CounterViewModel
    : ICounterViewModel
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

    /// <inheritdoc />
    public IOneWayBindableValue<int> Count { get; }

    /// <inheritdoc />
    public IOneWayBindableValue<string> CountText { get; }

    /// <inheritdoc />
    public IBindableAction Increment { get; }

    /// <inheritdoc />
    public IBindableAction Decrement { get; }

    /// <inheritdoc />
    public IBindableAction Reset { get; }

    /// <inheritdoc />
    /// <remarks>
    ///     Each entry holds a subscription into the graph, and its disposal releases that
    ///     subscription.
    ///     <para />
    ///     The list holds <see cref="IDisposable" /> and not bindables, because the disposables of
    ///     a view model are not always bindables. A graph with MapAsync also holds an
    ///     AsyncMapStatus, as the search sample shows. Disposal is the only operation on them
    ///     here.
    /// </remarks>
    public void Dispose()
    {
        foreach (IDisposable disposable in this.disposables)
        {
            disposable.Dispose();
        }
    }

    public static ICounterViewModel Create() =>
        // There is one transaction for the full graph. No code here fires during the
        // construction, thus this transaction changes no behavior in this sample. It stays the
        // correct practice, because a graph with a Values() stream loses its first value without
        // a transaction, and gives no message.
        Transaction.Run(static () =>
        {
            StreamSink<Unit> increment = Stream.CreateSink<Unit>();
            StreamSink<Unit> decrement = Stream.CreateSink<Unit>();
            StreamSink<Unit> reset = Stream.CreateSink<Unit>();

            // Each button gives a function of the current count and not a number. Thus, Reset
            // uses the same stream as the other two buttons, and does not use a mechanism of its
            // own.
            Stream<Func<int, int>> edits =
                new[]
                {
                    increment.MapTo(static (int n) => n + 1),
                    decrement.MapTo(static (int n) => n - 1),
                    reset.MapTo(static (int _) => 0)
                }.OrElse();

            Cell<int> count = edits.Accum(initialState: 0, f: static (edit, n) => edit(n));

            return new CounterViewModel(
                count: count.ToOneWay(),
                countText: count.Map(static n => n.ToString(CultureInfo.CurrentCulture)).ToOneWay(),
                increment: increment.ToBindableAction(),
                decrement: decrement.ToBindableAction(),

                // The enabled state is one more cell. No code raises CanExecuteChanged
                // manually. The command follows the cell, and the cell follows the count.
                reset: reset.ToBindableAction(count.Map(static n => n != 0)));
        });
}
