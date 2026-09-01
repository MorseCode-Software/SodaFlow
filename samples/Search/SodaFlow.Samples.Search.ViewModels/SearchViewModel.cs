using System;
using System.Collections.Generic;
using System.Globalization;
using JetBrains.Annotations;
using SodaFlow.Async;
using SodaFlow.Bindable.ObjectModel;
using SodaFlow.Functional;

namespace SodaFlow.Samples.Search.ViewModels;

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
    private static readonly IReadOnlyList<string> NoResults = Array.Empty<string>();

    private readonly IReadOnlyList<IDisposable> disposables;

    #region Constructor

    private SearchViewModel(
        AsyncMapStatus<string> status,
        ITwoWayBindableValue<string> query,
        IOneWayBindableValue<IReadOnlyList<string>> results,
        IOneWayBindableValue<string> summary,
        IOneWayBindableValue<string> error,
        IOneWayBindableValue<bool> hasError,
        IOneWayBindableValue<bool> isBusy,
        IBindableAction cancel)
    {
        this.Query = query;
        this.Results = results;
        this.Summary = summary;
        this.Error = error;
        this.HasError = hasError;
        this.IsBusy = isBusy;
        this.Cancel = cancel;

        // The bindables each hold a subscription into the graph, and status is the async
        // pipeline itself: disposing it tears that down and cancels anything still in flight.
        // They differ in kind but not in what disposal asks of them, so one list holds both.
        this.disposables = new IDisposable[] { query, results, summary, error, hasError, isBusy, cancel, status };
    }

    #endregion

    /// <summary>What the user has typed. Two-way, so the view both reads and writes it.</summary>
    public ITwoWayBindableValue<string> Query { get; }

    public IOneWayBindableValue<IReadOnlyList<string>> Results { get; }

    /// <summary>"Searching..." while a request is out, otherwise a count.</summary>
    public IOneWayBindableValue<string> Summary { get; }

    public IOneWayBindableValue<string> Error { get; }

    /// <summary>Separate from <see cref="Error" /> so the view can bind visibility to it.</summary>
    public IOneWayBindableValue<bool> HasError { get; }

    [UsedImplicitly] // This property is actually unused, but provided simply as a sample
    public IOneWayBindableValue<bool> IsBusy { get; }

    /// <summary>Enabled only while a search is running.</summary>
    public IBindableAction Cancel { get; }

    /// <summary>
    ///     Every entry holds a subscription into the graph, and disposing it is what releases
    ///     that subscription.
    /// </summary>
    /// <remarks>
    ///     This is the case the counter sample's remarks point at: the list holds the async
    ///     pipeline's status alongside the bindables, which is why it is typed as
    ///     <see cref="IDisposable" />. The constructor says what disposing that one does.
    /// </remarks>
    public void Dispose()
    {
        foreach (IDisposable disposable in this.disposables)
        {
            disposable.Dispose();
        }
    }

    public static SearchViewModel Create() =>
        Transaction.Run(() =>
        {
            CellSink<string> query = Cell.CreateSink(string.Empty);
            StreamSink<Unit> cancel = Stream.CreateSink<Unit>();

            // MapAsync publishes into these two, and everything downstream reads them. Splitting
            // success from failure at the source is why no result ever has to be checked for an error.
            StreamSink<IReadOnlyList<string>> found =
                Stream.CreateSink<IReadOnlyList<string>>();

            StreamSink<Exception> failed = Stream.CreateSink<Exception>();

            // Calm first: holding a key down, or moving the caret, re-sends the same text,
            // and there is no reason to search twice for it. Updates rather than Values
            // because the empty initial query is not worth a round trip.
            Stream<string> searches =
                query
                    .Calm()
                    .Updates()
                    .Filter(q => !string.IsNullOrWhiteSpace(q));

            AsyncMapStatus<string> mapStatus =
                searches.MapAsync(
                    results: found,
                    errors: failed,
                    operation: Catalog.SearchAsync,

                    // The whole concurrency policy, in one argument. A new keystroke
                    // supersedes the search in flight, and the superseded one can never
                    // publish - which is the race that makes this screen hard to hand-write.
                    strategy: AsyncConcurrencyStrategy.SwitchLatest(),
                    cancelAll: cancel);

            // Results survive until the next search replaces them.
            Cell<IReadOnlyList<string>> results = found.Hold(SearchViewModel.NoResults);

            // The error is cleared by the same stream that starts a search, so a stale
            // message cannot outlive the request that produced it. Failures win a tie
            // because OrElse prefers its left argument.
            Cell<string> error =
                failed
                    .Map(e => e.Message)
                    .OrElse(searches.MapTo(string.Empty))
                    .Hold(string.Empty);

            // Derived, not counted. There is no += 1 anywhere to get out of step.
            Cell<bool> busy = mapStatus.IsRunning;

            Cell<string> summary =
                results.Lift(
                    c2: busy,
                    f: (r, isBusy) =>
                        isBusy
                            ? "Searching..."
                            : r.Count.ToString(CultureInfo.CurrentCulture) + " result(s)");

            return new SearchViewModel(
                status: mapStatus,

                // Two-way: the view writes here, and the cell stays authoritative.
                query: query.ToTwoWay(),
                results: results.ToOneWay(),
                summary: summary.ToOneWay(),
                error: error.ToOneWay(),
                hasError: error.Map(e => e.Length > 0).ToOneWay(),
                isBusy: busy.ToOneWay(),

                // Cancel is offered only while something is actually running.
                cancel: cancel.ToBindableAction(busy));
        });
}