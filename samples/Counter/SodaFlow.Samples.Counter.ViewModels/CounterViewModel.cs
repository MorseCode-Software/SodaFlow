using System;
using System.Collections.Generic;
using System.Globalization;
using SodaFlow.Bindable.ObjectModel;
using SodaFlow.Functional;

namespace SodaFlow.Samples.Counter.ViewModels
{
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
        private readonly IReadOnlyList<IBindable> bindables;

        #region Constructor
        
        private CounterViewModel(
            in IOneWayBindableValue<int> count,
            in IOneWayBindableValue<string> countText,
            in IBindableAction increment,
            in IBindableAction decrement,
            in IBindableAction reset)
        {
            this.Count = count;
            this.CountText = countText;
            this.Increment = increment;
            this.Decrement = decrement;
            this.Reset = reset;

            this.bindables =
                new IBindable[]
                {
                    count, countText, increment, decrement, reset
                };
        }
        
        #endregion Constructor

        /// <summary>The current count, for anything that wants the number itself.</summary>
        public IOneWayBindableValue<int> Count { get; }

        /// <summary>The count as text, formatted for the current culture.</summary>
        public IOneWayBindableValue<string> CountText { get; }

        public IBindableAction Increment { get; }

        public IBindableAction Decrement { get; }

        /// <summary>Enabled only when the count is not already zero.</summary>
        public IBindableAction Reset { get; }

        public static CounterViewModel Create() =>
            // One transaction for the whole graph. Nothing here fires during construction, so it
            // changes no behavior in this sample - but it is the habit worth having: a graph
            // containing a Values() stream loses its first firing without it, silently.
            Transaction.Run(
                () =>
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
                            increment.MapTo<Unit, Func<int, int>>(n => n + 1),
                            decrement.MapTo<Unit, Func<int, int>>(n => n - 1),
                            reset.MapTo<Unit, Func<int, int>>(_ => 0)
                        }.OrElse();

                    Cell<int> count = edits.Accum(0, (edit, n) => edit(n));

                    return new CounterViewModel(
                        count: count.ToOneWay(),
                        countText: count.Map(n => n.ToString(CultureInfo.CurrentCulture)).ToOneWay(),
                        increment: increment.ToBindableAction(),
                        decrement: decrement.ToBindableAction(),

                        // Enablement is just another cell. Nothing raises CanExecuteChanged by
                        // hand; the command follows the cell, and the cell follows the count.
                        reset: reset.ToBindableAction(count.Map(n => n != 0)));
                });

        /// <summary>
        ///     Every bindable holds a subscription into the graph. Disposing them through the one
        ///     IBindable interface is what that interface is for.
        /// </summary>
        public void Dispose()
        {
            foreach (IBindable bindable in this.bindables)
            {
                bindable.Dispose();
            }
        }
    }
}
