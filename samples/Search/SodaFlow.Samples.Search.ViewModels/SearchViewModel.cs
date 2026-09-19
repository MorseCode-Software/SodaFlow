using System;
using System.Collections.Generic;
using System.Globalization;
using JetBrains.Annotations;
using SodaFlow.Async;
using SodaFlow.Bindable.ObjectModel;
using SodaFlow.Functional;

namespace SodaFlow.Samples.Search.ViewModels;

/// <summary>
///     A search at each keystroke. Each keystroke starts a search, a new search replaces the
///     search in operation, and the results, the busy state, and the error message are functions
///     of the same graph.
/// </summary>
/// <remarks>
///     <para>
///         This condition is difficult to write manually. A user that types faster than Catalog
///         replies makes some searches operate at the same time. The usual defects are a previous
///         reply that replaces a new reply, a spinner that does not stop because a canceled search
///         did not decrease a counter, and a previous error on the screen after a subsequent
///         search gave results. None of those defects is possible here. SwitchLatest lets only
///         the newest search publish, the graph calculates IsRunning and does not count it, and
///         the same stream that starts a search removes the error.
///     </para>
///     <para>
///         Type "fail" to see the error path. Type slowly and then quickly to see a new search
///         replace a previous search.
///     </para>
/// </remarks>
// ReSharper disable once InheritdocConsiderUsage
public sealed class SearchViewModel : ISearchViewModel
{
    private static readonly IReadOnlyList<string> NoResults = [];

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

        // Each bindable holds a subscription into the graph, and the status is the async
        // pipeline. Disposal of the pipeline removes it and cancels each operation in it. The two
        // types are different, but the disposal of each is the same, thus one list holds the two
        // types.
        this.disposables = [query, results, summary, error, hasError, isBusy, cancel, status];
    }

    #endregion

    /// <inheritdoc />
    public ITwoWayBindableValue<string> Query { get; }

    /// <inheritdoc />
    public IOneWayBindableValue<IReadOnlyList<string>> Results { get; }

    /// <inheritdoc />
    public IOneWayBindableValue<string> Summary { get; }

    /// <inheritdoc />
    public IOneWayBindableValue<string> Error { get; }

    /// <inheritdoc />
    public IOneWayBindableValue<bool> HasError { get; }

    /// <inheritdoc />
    [UsedImplicitly] // This property is actually unused, but provided simply as a sample
    public IOneWayBindableValue<bool> IsBusy { get; }

    /// <inheritdoc />
    public IBindableAction Cancel { get; }

    /// <inheritdoc />
    /// <remarks>
    ///     Each entry holds a subscription into the graph, and its disposal releases that
    ///     subscription.
    ///     <para />
    ///     The remarks of the counter sample name this condition. The list holds the status of the
    ///     async pipeline and the bindables, thus its type is <see cref="IDisposable" />. The
    ///     constructor gives the result of the disposal of the status.
    /// </remarks>
    public void Dispose()
    {
        foreach (IDisposable disposable in this.disposables)
        {
            disposable.Dispose();
        }
    }

    public static ISearchViewModel Create() =>
        Transaction.Run(static () =>
        {
            CellSink<string> query = Cell.CreateSink(string.Empty);
            StreamSink<Unit> cancel = Stream.CreateSink<Unit>();

            // MapAsync publishes into these two streams, and the code after them reads the two
            // streams. This code divides the successes from the failures at the source, thus no
            // result needs a test for an error.
            StreamSink<IReadOnlyList<string>> found =
                Stream.CreateSink<IReadOnlyList<string>>();

            StreamSink<Exception> failed = Stream.CreateSink<Exception>();

            // Calm is first. A key that stays down, or a move of the caret, sends the same text
            // again, and a second search for that text has no value. This code uses Updates and
            // not Values, because a round-trip is not necessary for the empty initial query.
            Stream<string> searches =
                query
                    .Calm()
                    .Updates()
                    .Filter(static q => !string.IsNullOrWhiteSpace(q));

            AsyncMapStatus<string> searchStatus =
                searches.MapAsync(
                    results: found,
                    errors: failed,
                    operation: Catalog.SearchAsync,

                    // The full concurrency policy is in one argument. A new keystroke replaces
                    // the search in operation, and the search that it replaces cannot publish.
                    // That is the race condition that makes this screen difficult to write
                    // manually.
                    strategy: AsyncConcurrencyStrategy.SwitchLatest(),
                    cancelAll: cancel);

            // The results stay until the next search replaces them.
            Cell<IReadOnlyList<string>> results = found.Hold(NoResults);

            // The same stream that starts a search removes the error, thus a previous message
            // cannot stay after its search. A failure has priority when the two streams fire
            // together, because OrElse uses its left argument first.
            Cell<string> error =
                failed
                    .Map(static e => e.Message)
                    .OrElse(searches.MapTo(string.Empty))
                    .Hold(string.Empty);

            // The graph calculates this and does not count it. There is no += 1 that can become
            // incorrect.
            Cell<bool> busy = searchStatus.IsRunning;

            Cell<string> summary =
                results.Lift(
                    c2: busy,
                    f: static (r, isBusy) =>
                        isBusy
                            ? "Searching..."
                            : r.Count.ToString(CultureInfo.CurrentCulture) + " result(s)");

            return new SearchViewModel(
                status: searchStatus,
                query: query.ToTwoWay(), // Two-way: the view writes here, and the cell stays authoritative.
                results: results.ToOneWay(),
                summary: summary.ToOneWay(),
                error: error.ToOneWay(),
                hasError: error.Map(static e => e.Length > 0).ToOneWay(),
                isBusy: busy.ToOneWay(),
                cancel: cancel.ToBindableAction(busy)); // Cancel is offered only while something is actually running.
        });
}
