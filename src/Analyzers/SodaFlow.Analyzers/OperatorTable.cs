using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace SodaFlow.Analyzers;

/// <summary>
///     What an operator does with the pending bit, the linked bit, and the
///     content of its inputs. <see cref="LoopStateEvaluator" /> applies the rule.
/// </summary>
internal enum OperatorRule
{
    /// <summary>
    ///     No row. The result keeps the state of its inputs where its type can
    ///     hold one.
    /// </summary>
    Default,

    /// <summary>
    ///     The lambda runs in the block, and the result is what the lambda
    ///     returns.
    /// </summary>
    RunsNow,

    /// <summary>
    ///     A read of the current value. The result is the value that the receiver
    ///     holds, with the state of that value.
    /// </summary>
    Read,

    /// <summary>
    ///     A deferred read of the receiver. Pending when the receiver is, with no
    ///     link.
    /// </summary>
    SampleLazy,

    /// <summary>A map of a lazy sample. Pending when the receiver is, with no link.</summary>
    MapLazy,

    /// <summary>
    ///     A map of a stream. Linked when the receiver is, and the content is
    ///     what the function returns.
    /// </summary>
    MapStream,

    /// <summary>
    ///     A map of a cell or behavior. Pending and linked as the receiver, and
    ///     the content is what the function returns.
    /// </summary>
    MapValue,

    /// <summary>A lift of lazy samples. Pending when any input is, with no link.</summary>
    LiftLazy,

    /// <summary>
    ///     A cell or behavior from cells or behaviors. Pending and linked when
    ///     any input is.
    /// </summary>
    Combine,

    /// <summary>
    ///     A switch to a cell or behavior. Pending and linked through the outer
    ///     and through its content.
    /// </summary>
    SwitchValue,

    /// <summary>A switch to a stream. Linked through the content of the outer only.</summary>
    SwitchStream,

    /// <summary>A hold. Linked when the stream is, and the value is content.</summary>
    Hold,

    /// <summary>
    ///     A hold or fold from a lazy sample. Pending when the sample is, linked
    ///     when the stream is.
    /// </summary>
    HoldLazy,

    /// <summary>A constant from a lazy sample. Pending when the sample is.</summary>
    ConstantLazy,

    /// <summary>A constant or a sink. The value given is content.</summary>
    Content,

    /// <summary>A fold with a given value. Linked when the stream is.</summary>
    Fold,

    /// <summary>
    ///     The updates or values of a cell or behavior. Linked when the source
    ///     is.
    /// </summary>
    Updates,

    /// <summary>
    ///     A stream that fires a given value. Linked when the receiver is, and
    ///     the value is content.
    /// </summary>
    MapTo,

    /// <summary>
    ///     A stream from the receiver. Linked when the receiver is. A cell
    ///     argument is sampled, not linked.
    /// </summary>
    StreamFromReceiver,

    /// <summary>A merge of streams. Linked when any stream input is.</summary>
    MergeStreams,

    /// <summary>A deferred function. Pending when the function reads a pending value.</summary>
    DeferLazy,

    /// <summary>A new transaction, a listener, or a send. No state.</summary>
    Break
}

/// <summary>
///     The table of the library's operators: the rule for each, by name and
///     by the type of its receiver. A name with no row falls to
///     <see cref="OperatorRule.Default" />, which keeps the state of the inputs.
///     Thus, it reports too much and not too few.
/// </summary>
/// <remarks>
///     The completeness test in the analyzer's tests holds the table to the
///     public operators of the library. Each one is a row here, or is listed in
///     <see cref="DefaultIsSound" />. A new operator fails that test until it has
///     a row or that listing. That is the moment to say what it does with a loop.
/// </remarks>
internal static class OperatorTable
{
    /// <summary>The names that have a row.</summary>
    public static readonly ImmutableHashSet<string> Names =
        ImmutableHashSet.Create(
            "Run",
            "RunVoid",
            "WithCaptures",
            "WithoutCaptures",
            "Sample",
            "Value",
            "CanExecute",
            "SampleLazy",
            "Map",
            "Lift",
            "Apply",
            "Calm",
            "AsCell",
            "AsBehavior",
            "AsStream",
            "SwitchC",
            "SwitchB",
            "SwitchS",
            "Hold",
            "HoldLazy",
            "AccumLazy",
            "CollectLazy",
            "ConstantLazy",
            "Constant",
            "CreateSink",
            "CreateStreamSink",
            "Accum",
            "Collect",
            "Updates",
            "Values",
            "MapTo",
            "Snapshot",
            "SnapshotLatest",
            "Gate",
            "Filter",
            "FilterSome",
            "Once",
            "Merge",
            "OrElse",
            "Defer",
            "Split",
            "At",
            "Debounce",
            "Post",
            "Listen",
            "ListenStrong",
            "ListenOnce",
            "ListenOnceStrong",
            "ListenOnceAsync",
            "Send",
            "Unlisten",
            "Loop");

    /// <summary>
    ///     The public operators with no row, for which the default is the correct
    ///     rule. Each gives its result the state of its inputs. None crosses a
    ///     transaction or reads a current value at construction.
    /// </summary>
    public static readonly ImmutableHashSet<string> DefaultIsSound =
        ImmutableHashSet.Create(
            // A stream of the values a function keeps, from a stream: linked as the stream is.
            "Choose",
            // A stream or sink with no inputs.
            "Never",
            "AsStreamSink",
            // The cells and streams of a reactive collection. Their input is the collection, which is
            // not a placeholder. A fold starts from the snapshot of the collection.
            "Fold",
            "FoldByIdentity",
            "FromAdds",
            "FromRemoves",
            "FromStates",
            "FromUpdates",
            "IdentityCell",
            "ItemCell",
            "StateCell",
            // A collection, and the views of a collection. A view is pending when its upstream is. A
            // cell that the view reads also makes the view pending. Such a cell is the criteria of a
            // filter, the order of a sort, or the limits of a slice. Filter and Map have a row for a
            // stream or a cell. The methods of those names on a collection get this rule also.
            "Create",
            "FilterByIdentity",
            "Slice",
            "SortBy",
            "SortByArrival",
            "SortByDescending",
            "SortByIdentity",
            "SortByIdentityDescending",
            "SortByKey",
            "SortByKeyDescending",
            "Take",
            // A bindable is pending when its cell is. A read of its value is a read of the cell.
            "CreateBindableAction",
            "CreateOneWay",
            "CreateTwoWay");

    /// <summary>
    ///     The library methods that run a function in the call, before the loop
    ///     closes. Each has the names of the parameters whose function it runs. An
    ///     empty set is all of them.
    /// </summary>
    /// <remarks>
    ///     <c>Fold</c> and <c>FoldByIdentity</c> of a collection run <c>select</c>
    ///     and <c>add</c> over the items that the collection has at construction.
    ///     They keep <c>subtract</c> for an edit after the close.
    /// </remarks>
    private static readonly ImmutableDictionary<string, ImmutableHashSet<string>> RunsInTheCall =
        ImmutableDictionary<string, ImmutableHashSet<string>>.Empty
            .Add(key: "Run", value: ImmutableHashSet<string>.Empty)
            .Add(key: "RunVoid", value: ImmutableHashSet<string>.Empty)
            .Add(key: "WithCaptures", value: ImmutableHashSet<string>.Empty)
            .Add(key: "WithoutCaptures", value: ImmutableHashSet<string>.Empty)
            .Add(key: "Fold", value: ImmutableHashSet.Create("select", "add"))
            .Add(key: "FoldByIdentity", value: ImmutableHashSet.Create("select", "add"));

    /// <summary>The names of the methods that run a function in the call.</summary>
    public static ImmutableHashSet<string> FunctionsInTheCall { get; } = RunsInTheCall.Keys.ToImmutableHashSet();

    /// <summary>
    ///     The public methods that have a function parameter and keep the
    ///     function for a firing, an edit, or a read after the call. A read in such
    ///     a function is not a read in the block.
    /// </summary>
    /// <remarks>
    ///     The completeness test in the analyzer's tests holds the two lists to
    ///     the public methods of the library with a delegate parameter. A run of
    ///     each method with a function that samples a loop is the source of this
    ///     list.
    ///     The exceptions are <c>Debounce</c>, <c>MapAsync</c>, <c>OnStart</c>,
    ///     <c>SetTimer</c>, and <c>Start</c>, whose documentation is the source.
    /// </remarks>
    public static readonly ImmutableHashSet<string> FunctionsAfterTheCall =
        ImmutableHashSet.Create(
            "Accum",
            "AccumLazy",
            "By",
            "ByDescending",
            "ByIdentity",
            "ByIdentityDescending",
            "Calm",
            "Choose",
            "Collect",
            "CollectLazy",
            "Create",
            "CreateSink",
            "CreateStreamSink",
            "Debounce",
            "Defer",
            "Filter",
            "FilterByIdentity",
            "Lift",
            "Listen",
            "ListenOnce",
            "ListenOnceStrong",
            "ListenStrong",
            "Map",
            "MapAsync",
            "Merge",
            "OnStart",
            "Post",
            "SetTimer",
            "Snapshot",
            "SnapshotLatest",
            "SortBy",
            "SortByDescending",
            "SortByIdentity",
            "SortByIdentityDescending",
            "Start",
            "ThenBy",
            "ThenByDescending",
            "ThenByIdentity",
            "ThenByIdentityDescending",
            "Update");

    /// <summary>
    ///     True when a library method runs, in the call, the function that it
    ///     gets for the given parameter.
    /// </summary>
    public static bool RunsFunction(IMethodSymbol method, IParameterSymbol? parameter) =>
        RunsInTheCall.TryGetValue(
            key: LoopStateEvaluator.OperatorName(method),
            value: out ImmutableHashSet<string>? named)
        && (named.IsEmpty || (parameter != null && named.Contains(parameter.Name)));

    /// <summary>
    ///     The rule for a library method, by its name and the type of its
    ///     receiver.
    /// </summary>
    public static OperatorRule Rule(SodaFlowSymbols symbols, IMethodSymbol method, ITypeSymbol? receiverType)
    {
        string name = LoopStateEvaluator.OperatorName(method);
        bool lazy = symbols.IsLazySampleType(receiverType);
        bool stream = symbols.IsStreamType(receiverType);

        // The rows for the operators of a stream apply to a stream. A method of a different type
        // can have the same name, as the Filter of a collection view has. That method gets the
        // default rule.
        bool ofStreams = stream || symbols.IsStreamType(method.ReturnType);

        return name switch
        {
            "Run" or "RunVoid" or "WithCaptures" or "WithoutCaptures" => OperatorRule.RunsNow,
            "Value" when method.ContainingType.Name == "Operational" => OperatorRule.Updates,
            "Sample" or "Value" or "CanExecute" => OperatorRule.Read,
            "SampleLazy" => OperatorRule.SampleLazy,
            "Map" when lazy => OperatorRule.MapLazy,
            "Map" when stream => OperatorRule.MapStream,
            "Map" => OperatorRule.MapValue,
            "Lift" when lazy => OperatorRule.LiftLazy,
            "Calm" when stream => OperatorRule.StreamFromReceiver,
            "Lift" or "Apply" or "Calm" or "AsCell" or "AsBehavior" or "AsStream" => OperatorRule.Combine,
            "SwitchC" or "SwitchB" => OperatorRule.SwitchValue,
            "SwitchS" => OperatorRule.SwitchStream,
            "Hold" when ofStreams => OperatorRule.Hold,
            "HoldLazy" or "AccumLazy" or "CollectLazy" when ofStreams => OperatorRule.HoldLazy,
            "ConstantLazy" => OperatorRule.ConstantLazy,
            "Constant" or "CreateSink" or "CreateStreamSink" => OperatorRule.Content,
            "Accum" or "Collect" when ofStreams => OperatorRule.Fold,
            "Updates" or "Values" => OperatorRule.Updates,
            "MapTo" when ofStreams => OperatorRule.MapTo,
            "Snapshot" or "SnapshotLatest" or "Gate" or "Filter" or "FilterSome" or "Once" when ofStreams =>
                OperatorRule.StreamFromReceiver,
            "Merge" or "OrElse" when ofStreams => OperatorRule.MergeStreams,
            "Defer" when method.ContainingType.Name == "LazySample" => OperatorRule.DeferLazy,
            "Defer" or "Split" or "At" or "Debounce" or "Post" or "Listen" or "ListenStrong" or "ListenOnce" or "ListenOnceStrong" or "ListenOnceAsync" or "Send" or "Unlisten" or "Loop" => OperatorRule.Break,
            _ => OperatorRule.Default
        };
    }
}
