using System;
using System.Collections.Generic;
using System.Globalization;
using SodaFlow.Async;
using SodaFlow.Bindable.ObjectModel;
using SodaFlow.Functional;

namespace SodaFlow.Samples.Search
{
    /// <summary>
    ///     Search-as-you-type: every keystroke starts a search, a new one supersedes whatever was
    ///     in flight, and the results, the busy state and the error message are all functions of
    ///     the same graph.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         This is the case that is genuinely hard to write by hand. Typing faster than the
    ///         service responds means several searches are outstanding at once, and the usual bugs
    ///         are an older reply overwriting a newer one, a spinner that never stops because a
    ///         canceled request never decremented a counter, and a stale error left on screen
    ///         after a later search succeeded. None of those are possible here: SwitchLatest
    ///         guarantees only the newest search can publish, IsRunning is derived rather than
    ///         counted, and the error is cleared by the same stream that starts a search.
    ///     </para>
    ///     <para>
    ///         Type "fail" to see the error path. Type slowly and then quickly to see supersession.
    ///     </para>
    /// </remarks>
    public sealed class SearchViewModel : IDisposable
    {
        private static readonly IReadOnlyList<string> NoResults = new string[0];

        private readonly CellSink<string> query = Cell.CreateSink(string.Empty);
        private readonly StreamSink<Unit> cancel = Stream.CreateSink<Unit>();

        // MapAsync publishes into these two, and everything downstream reads them. Splitting
        // success from failure at the source is why no result ever has to be checked for an error.
        private readonly StreamSink<IReadOnlyList<string>> found =
            Stream.CreateSink<IReadOnlyList<string>>();

        private readonly StreamSink<Exception> failed = Stream.CreateSink<Exception>();

        private readonly AsyncMapStatus<string> status;
        private readonly IReadOnlyList<IBindable> bindables;

        public SearchViewModel()
        {
            var built = Transaction.Run(
                () =>
                {
                    // Calm first: holding a key down, or moving the caret, re-sends the same text,
                    // and there is no reason to search twice for it. Updates rather than Values
                    // because the empty initial query is not worth a round trip.
                    Stream<string> searches = this.query
                        .Calm()
                        .Updates()
                        .Filter(q => !string.IsNullOrWhiteSpace(q));

                    AsyncMapStatus<string> mapStatus = searches.MapAsync(
                        results: this.found,
                        errors: this.failed,
                        operation: Catalog.SearchAsync,

                        // The whole concurrency policy, in one argument. A new keystroke
                        // supersedes the search in flight, and the superseded one can never
                        // publish - which is the race that makes this screen hard to hand-write.
                        strategy: AsyncConcurrencyStrategy.SwitchLatest(),
                        cancelAll: this.cancel);

                    // Results survive until the next search replaces them.
                    Cell<IReadOnlyList<string>> results = this.found.Hold(NoResults);

                    // The error is cleared by the same stream that starts a search, so a stale
                    // message cannot outlive the request that produced it. Failures win a tie
                    // because OrElse prefers its left argument.
                    Cell<string> error = this.failed
                        .Map(e => e.Message)
                        .OrElse(searches.MapTo(string.Empty))
                        .Hold(string.Empty);

                    // Derived, not counted. There is no += 1 anywhere to get out of step.
                    Cell<bool> busy = mapStatus.IsRunning;

                    Cell<string> summary = results.Lift(
                        busy,
                        (r, isBusy) => isBusy
                            ? "Searching..."
                            : r.Count.ToString(CultureInfo.CurrentCulture) + " result(s)");

                    return (
                        Status: mapStatus,

                        // Two-way: the view writes here, and the cell stays authoritative.
                        Query: this.query.ToTwoWay(),
                        Results: results.ToOneWay(),
                        Summary: summary.ToOneWay(),
                        Error: error.ToOneWay(),
                        HasError: error.Map(e => e.Length > 0).ToOneWay(),
                        IsBusy: busy.ToOneWay(),

                        // Cancel is offered only while something is actually running.
                        Cancel: this.cancel.ToBindableAction(busy));
                });

            this.status = built.Status;
            this.Query = built.Query;
            this.Results = built.Results;
            this.Summary = built.Summary;
            this.Error = built.Error;
            this.HasError = built.HasError;
            this.IsBusy = built.IsBusy;
            this.Cancel = built.Cancel;

            this.bindables = new IBindable[]
            {
                this.Query, this.Results, this.Summary, this.Error, this.HasError, this.IsBusy,
                this.Cancel
            };
        }

        /// <summary>What the user has typed. Two-way, so the view both reads and writes it.</summary>
        public ITwoWayBindableValue<string> Query { get; }

        public IOneWayBindableValue<IReadOnlyList<string>> Results { get; }

        /// <summary>"Searching..." while a request is out, otherwise a count.</summary>
        public IOneWayBindableValue<string> Summary { get; }

        public IOneWayBindableValue<string> Error { get; }

        /// <summary>Separate from <see cref="Error" /> so the view can bind visibility to it.</summary>
        public IOneWayBindableValue<bool> HasError { get; }

        public IOneWayBindableValue<bool> IsBusy { get; }

        /// <summary>Enabled only while a search is running.</summary>
        public IBindableAction Cancel { get; }

        public void Dispose()
        {
            foreach (IBindable bindable in this.bindables)
            {
                bindable.Dispose();
            }

            // Tears down the pipeline and cancels anything still in flight.
            this.status.Dispose();
        }
    }
}
