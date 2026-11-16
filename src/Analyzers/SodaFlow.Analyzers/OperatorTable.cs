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

    /// <summary>
    ///     A lift of lazy samples. Pending when any input is, with no link, and
    ///     the content is what the function returns.
    /// </summary>
    LiftLazy,

    /// <summary>
    ///     A lift of cells or behaviors. Pending and linked when any input is, and
    ///     the content is what the function returns.
    /// </summary>
    LiftValue,

    /// <summary>
    ///     The same values as the receiver, as a calm cell or a sink as a cell
    ///     has. Pending and linked as the receiver, with its content.
    /// </summary>
    SameValues,

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

    /// <summary>
    ///     A fold with a given value. Linked when the stream is. The content of
    ///     an accumulation is its initial state and what its function returns.
    /// </summary>
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

    /// <summary>
    ///     A stream from the receiver that takes the updates of each cell or
    ///     behavior also. Linked when any input is.
    /// </summary>
    SnapshotLatest,

    /// <summary>A merge of streams. Linked when any stream input is.</summary>
    MergeStreams,

    /// <summary>
    ///     A bindable action. It has the state of its inputs, as with
    ///     <see cref="Default" />. Its firings have the state of its sink only, and
    ///     its enablement has the state of its cell.
    /// </summary>
    BindableAction,

    /// <summary>
    ///     A reactive collection. Pending when an input is. It samples its cell
    ///     of initial items lazily and does not listen to it. Thus, only a linked
    ///     edit stream links it.
    /// </summary>
    CollectionCreate,

    /// <summary>A deferred function. Pending when the function reads a pending value.</summary>
    DeferLazy,

    /// <summary>
    ///     A fold of a collection. It reads the snapshot of the receiver at
    ///     construction. The result keeps the state of the inputs, as with
    ///     <see cref="Default" />.
    /// </summary>
    FoldCollection,

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
            "Loop",
            "Fold",
            "FoldByIdentity",
            "CreateBindableAction",
            "Create");

    /// <summary>
    ///     The public members of the library with no row, for which the default
    ///     is the correct rule. An operator here gives its result the state of its
    ///     inputs. None crosses a transaction or reads a current value at
    ///     construction.
    /// </summary>
    /// <remarks>
    ///     A member that is not an operator is here also, with the reason that
    ///     the default is correct for it. A property of the library that is not a
    ///     read member has the state of its owner. A result whose type cannot hold
    ///     a pending read, as a bool or a task, has no state.
    /// </remarks>
    public static readonly ImmutableHashSet<string> DefaultIsSound =
        ImmutableHashSet.Create(
            // A stream of the values a function keeps, from a stream: linked as the stream is.
            "Choose",
            // A stream or sink with no inputs.
            "Never",
            "AsStreamSink",
            // The cells and streams of a reactive collection. Their input is the collection, which is
            // not a placeholder.
            "FromAdds",
            "FromRemoves",
            "FromStates",
            "FromUpdates",
            "IdentityCell",
            "ItemCell",
            "StateCell",
            // The views of a collection. A view is pending when its upstream is. A cell that the view
            // reads also makes the view pending. Such a cell is the criteria of a filter, the order
            // of a sort, or the limits of a slice. Filter and Map have a row for a stream or a cell.
            // The methods of those names on a collection get this rule also.
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
            "CreateOneWay",
            "CreateTwoWay",
            // The values that a collection gives: a snapshot, a change, an edit, an item, the keys
            // of a view, and the parts of each. Each is data that holds what the code or the graph
            // gave it. A read of a part runs no code of the graph and reads no cell.
            "Add",
            "Added",
            "Adds",
            "After",
            "Before",
            "ChangeFor",
            "ChangedKeys",
            "CombineWith",
            "Contains",
            "ContainsKey",
            "Count",
            "Empty",
            "FromIndex",
            "GetEnumerator",
            "Identities",
            "Identity",
            "Index",
            "IndexOf",
            "IsReset",
            "IsStructural",
            "Item",
            "Key",
            "Keys",
            "Lookup",
            "NewStates",
            "Operations",
            "Pairs",
            "Remove",
            "Removed",
            "Removes",
            "State",
            "States",
            "ToIndex",
            "TryGetItem",
            "TryGetNewState",
            "TryGetState",
            "Update",
            "WasChanged",
            // The cells and streams that a collection holds. They have the state of the collection,
            // thus a cell of a pending collection is pending.
            "ItemChangesStream",
            "KeyChangesStream",
            "KeysCell",
            "ShapeCell",
            "SnapshotCell",
            // The cell of a bindable, and the members of a bindable action, which CreateBindableAction
            // gives their own state. An event of a bindable keeps its handler and reads no cell.
            "Cell",
            "FiringsStream",
            "IsEnabledCell",
            "CanExecuteChanged",
            "PropertyChanged",
            // Execute of ICommand and Dispose of IDisposable. Each reads the enablement of a bindable
            // action, and the walk checks that read. Each other Dispose releases listeners. Execute of
            // an AsyncMapStatus reads its cell in a transaction of its own, after the close.
            "Dispose",
            "Execute",
            // The sort orders of a collection. Each keeps its function for a view that sorts by it.
            // An order whose function reads a pending value is pending, because each order with a
            // function is in ValueFromFunction.
            "By",
            "ByArrival",
            "ByDescending",
            "ByIdentity",
            "ByIdentityDescending",
            "ByKey",
            "ByKeyDescending",
            "ThenBy",
            "ThenByDescending",
            "ThenByIdentity",
            "ThenByIdentityDescending",
            // Listeners from listeners. A listener holds no value and has no state.
            "Append",
            "AppendStrong",
            "AppendWeak",
            "CreateComposite",
            "CreateStrongComposite",
            "CreateWeakComposite",
            // IsActive of a transaction reads no cell. OnStart keeps its function for the start of
            // each transaction after this one.
            "IsActive",
            "OnStart",
            // A clock and its timers. Each member gives the time, moves it, or keeps a callback for a
            // time. A timer sends into a sink, and reads no cell. Time is the behavior of the clock,
            // which no loop gives a state.
            "Cancel",
            "Now",
            "RunTimersTo",
            "SetTimer",
            "Start",
            "Stop",
            "Time",
            // The scheduler of the binding thread. It holds no value of the graph.
            "Capture",
            "CheckAccess",
            "Immediate",
            // The map of a stream through a task. MapAsync keeps its function for a firing, and its
            // status has the state of its inputs. The strategies, the results, and the status of
            // one item are data.
            "MapAsync",
            "Construct",
            "FromValue",
            "IsRunning",
            "Items",
            "Parallel",
            "Queue",
            "QueuePerGroup",
            "Status",
            "SwitchLatest",
            "UndeliveredError");

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

    /// <summary>
    ///     True for a library method that runs its function one time in the call:
    ///     a transaction and a loop block. A fold of a collection runs its function
    ///     one time for each item, which can be no time.
    /// </summary>
    public static bool RunsFunctionOneTime(IMethodSymbol method) =>
        LoopStateEvaluator.OperatorName(method) is "Run" or "RunVoid" or "WithCaptures" or "WithoutCaptures";

    /// <summary>
    ///     True for the name of a library method that reads the current value of
    ///     its receiver at construction, as a read member does. A fold of a
    ///     collection samples the snapshot of the collection.
    /// </summary>
    public static bool ReadsReceiverAtConstruction(string name) => name is "Fold" or "FoldByIdentity";

    /// <summary>
    ///     True for a library method that does not enumerate a sequence that it
    ///     gets in the call. A reactive collection keeps the sequence of its
    ///     initial items in a constant cell. It enumerates the sequence in a lazy
    ///     sample, which the transaction forces after the close.
    /// </summary>
    public static bool EnumeratesAfterTheClose(IMethodSymbol method) =>
        method.Name == "Create" && method.ContainingType.Name == "ReactiveCollection";

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

    // The operators whose function computes the current value of the result. Map, Lift, and
    // Defer are for a cell, a behavior, and a lazy sample. Apply runs the function that its cell
    // holds. The filters and the sorts are the views of a collection. The next eight make a sort
    // order. A view that sorts by that order runs the function of the order, and the pending state
    // of the order tells the view so. The last three get a comparer or a key selector, which the
    // collection calls at its first read.
    private static readonly ImmutableHashSet<string> ValueFromFunction =
        ImmutableHashSet.Create(
            "Map",
            "Lift",
            "Apply",
            "Defer",
            "Filter",
            "FilterByIdentity",
            "SortBy",
            "SortByDescending",
            "SortByIdentity",
            "SortByIdentityDescending",
            "By",
            "ByIdentity",
            "ThenBy",
            "ThenByDescending",
            "ThenByIdentity",
            "ThenByIdentityDescending",
            "ByDescending",
            "ByIdentityDescending",
            "ByKey",
            "SortByKey",
            "Create");

    /// <summary>
    ///     True for an operator that computes the current value of its result
    ///     with its function, at the first read of that value. A function that
    ///     samples a pending value then makes the result pending. An operator of a
    ///     stream runs its function at a firing, which is after the close.
    /// </summary>
    /// <remarks>
    ///     A sort order has no current value. It is pending because a view that
    ///     sorts by it runs its function, at the first read of the view. A comparer
    ///     that such an operator gets is a function of the operator also.
    /// </remarks>
    public static bool ComputesValueWithFunction(
        SodaFlowSymbols symbols,
        IMethodSymbol method,
        ITypeSymbol? receiverType) =>
        ValueFromFunction.Contains(LoopStateEvaluator.OperatorName(method))
        && !symbols.IsStreamType(receiverType)
        && !symbols.IsStreamType(method.ReturnType);

    /// <summary>
    ///     True for a parameter that gets a cell of sort orders. A view sorts by
    ///     the order that the cell holds, and runs the function of that order at
    ///     the first read of the view.
    /// </summary>
    public static bool IsOrderCell(SodaFlowSymbols symbols, IParameterSymbol? parameter) =>
        parameter?.Type is INamedTypeSymbol cell
        && symbols.IsCellOrBehaviorType(cell)
        && cell.TypeArguments.FirstOrDefault() is INamedTypeSymbol { Name: "KeyOrder" } order
        && symbols.IsSodaFlow(order);

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
            "Lift" => OperatorRule.LiftValue,
            "Calm" or "AsCell" or "AsBehavior" or "AsStream" => OperatorRule.SameValues,
            "Apply" => OperatorRule.Combine,
            "SwitchC" or "SwitchB" => OperatorRule.SwitchValue,
            "SwitchS" => OperatorRule.SwitchStream,
            "Hold" when ofStreams => OperatorRule.Hold,
            "HoldLazy" or "AccumLazy" or "CollectLazy" when ofStreams => OperatorRule.HoldLazy,
            "ConstantLazy" => OperatorRule.ConstantLazy,
            "Constant" or "CreateSink" or "CreateStreamSink" => OperatorRule.Content,
            "Accum" or "Collect" when ofStreams => OperatorRule.Fold,
            "Updates" or "Values" => OperatorRule.Updates,
            "MapTo" when ofStreams => OperatorRule.MapTo,
            // The output of SnapshotLatest listens to the updates of each cell, thus it cannot close a
            // loop.
            "SnapshotLatest" when ofStreams => OperatorRule.SnapshotLatest,
            "Snapshot" or "Gate" or "Filter" or "FilterSome" or "Once" when ofStreams =>
                OperatorRule.StreamFromReceiver,
            "Merge" or "OrElse" when ofStreams => OperatorRule.MergeStreams,
            "Defer" when method.ContainingType.Name == "LazySample" => OperatorRule.DeferLazy,
            "Fold" or "FoldByIdentity" => OperatorRule.FoldCollection,
            "CreateBindableAction" => OperatorRule.BindableAction,
            "Create" when method.ContainingType.Name == "ReactiveCollection" => OperatorRule.CollectionCreate,
            "Defer" or "Split" or "At" or "Debounce" or "Post" or "Listen" or "ListenStrong" or "ListenOnce" or "ListenOnceStrong" or "ListenOnceAsync" or "Send" or "Unlisten" or "Loop" => OperatorRule.Break,
            _ => OperatorRule.Default
        };
    }
}
