using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SodaFlow.Analyzers;

// The part of the evaluator that gives the content of the result of a library operator, such as a hold, a
// snapshot, or a collect.
internal sealed partial class LoopStateEvaluator
{
    /// <summary>
    ///     The state of what a lambda given to an operator returns, for the
    ///     content of a cell of cells or a cell of streams.
    /// </summary>
    private LoopState LambdaContent(InvocationExpressionSyntax invocation, LoopState parameterState)
    {
        AnonymousFunctionExpressionSyntax? lambda =
            invocation.ArgumentList.Arguments
                .Select(static a => a.Expression)
                .OfType<AnonymousFunctionExpressionSyntax>()
                .FirstOrDefault();

        return lambda == null
            ? LoopState.None
            : this.EvalLambdaResult(lambda: lambda, boundParameters: ImmutableArray.Create(parameterState));
    }

    /// <summary>
    ///     True when the value that the result of an operator holds can hold a
    ///     state, as a cell of cells does. The walk reads the function of the
    ///     operator for its content only then.
    /// </summary>
    private bool HoldsContent(IMethodSymbol method) =>
        method.ReturnType is INamedTypeSymbol { TypeArguments.Length: 1 } result
        && this.region.Symbols.CanHoldState(type: result.TypeArguments[0], compilation: this.region.Assembly);

    /// <summary>
    ///     The state of what the function of a lift returns. Each parameter has
    ///     the content of its input: the receiver first, and then each argument.
    /// </summary>
    private LoopState LiftedContent(
        InvocationExpressionSyntax invocation,
        ExpressionSyntax? receiver,
        LoopState receiverState,
        IEnumerable<LoopState> argumentStates)
    {
        AnonymousFunctionExpressionSyntax? lambda =
            invocation.ArgumentList.Arguments
                .Select(static a => a.Expression)
                .OfType<AnonymousFunctionExpressionSyntax>()
                .FirstOrDefault();

        IEnumerable<LoopState> given = argumentStates.Select(static state => state.Content);

        ImmutableArray<LoopState> inputs =
            (receiver != null ? given.Prepend(receiverState.Content) : given).ToImmutableArray();

        // Each lift with a function has an input for the function. A lift with no function is a lift
        // of a list of cells, and its value is the list of their values.
        if (inputs.Length == 1)
        {
            return inputs[0];
        }

        return lambda == null
            ? this.ContentOfANamedFunction(invocation: invocation, given: inputs)
            : this.EvalLambdaResult(lambda: lambda, boundParameters: inputs);
    }

    /// <summary>
    ///     The state of what the functions in the cell of an apply return. Each
    ///     function gets the value of the receiver.
    /// </summary>
    /// <remarks>
    ///     The functions are in the content of the cell, and not at the call.
    ///     The operator runs them at a time that the walk does not know. Thus,
    ///     an evaluator that changes nothing reads them, as for a named function.
    /// </remarks>
    private LoopState AppliedContent(
        InvocationExpressionSyntax invocation,
        IMethodSymbol method,
        ExpressionSyntax? receiver,
        LoopState receiverState,
        LoopState functionCell)
    {
        if (functionCell.Content.Functions.IsEmpty
            || !this.HoldsContent(method)
            || invocation.ArgumentList.Arguments.FirstOrDefault(argument => argument.Expression != receiver)
                is not { } written)
        {
            return LoopState.None;
        }

        return this.ResultOfTheFunctions(
            call: invocation,
            written: written.Expression,
            functions: functionCell.Content,
            given: ImmutableArray.Create(receiverState.Content));
    }

    // What the functions of a state return, for an operator that runs them at a time that the walk
    // does not know. Thus, an evaluator that changes nothing reads them.
    private LoopState ResultOfTheFunctions(
        SyntaxNode call,
        ExpressionSyntax written,
        LoopState functions,
        ImmutableArray<LoopState> given)
    {
        if (functions.Functions.IsEmpty)
        {
            return LoopState.None;
        }

        LoopStateEvaluator quiet = this.Quiet();

        return functions.Functions.Aggregate(
            seed: LoopState.None,
            func: (all, function) =>
                all.Join(
                    quiet.FollowFunction(
                            call: call,
                            name: NameOf(written),
                            function: function,
                            target: functions,
                            arguments: default,
                            each: (written, LoopState.None, ManyTimes: true),
                            byPosition: given)
                        ?.State
                    ?? LoopState.None));
    }

    /// <summary>
    ///     What the transforms of an update of a collection return. The transform
    ///     of <c>Update</c> is an argument. The transforms of <c>FromUpdates</c>
    ///     are in the content of its stream. A transform runs when the edit fires,
    ///     after the close.
    /// </summary>
    /// <remarks>
    ///     A transform gets the state that the item has at that time. The content
    ///     of the collection holds that state. Thus, the parameter of the transform
    ///     gets no state here.
    /// </remarks>
    private LoopState TransformedContent(
        InvocationExpressionSyntax invocation,
        IMethodSymbol method,
        IReadOnlyList<LoopState> argumentStates)
    {
        ImmutableArray<ITypeSymbol> types = method.ContainingType.TypeArguments;

        if (types.Length != 3
            || !this.region.Symbols.CanHoldState(type: types[2], compilation: this.region.Assembly))
        {
            return LoopState.None;
        }

        SeparatedSyntaxList<ArgumentSyntax> arguments = invocation.ArgumentList.Arguments;
        ImmutableArray<LoopState> given = ImmutableArray.Create(LoopState.None);
        LoopState transformed = LoopState.None;

        for (int i = 0; i < arguments.Count && i < argumentStates.Count; i++)
        {
            ITypeSymbol? type = ParameterFor(method: method, argument: arguments[i], position: i)?.Type;

            LoopState functions =
                type?.TypeKind == TypeKind.Delegate
                    ? this.Eval(arguments[i].Expression)
                    : this.region.Symbols.IsStreamType(type)
                        ? argumentStates[i].Content
                        : LoopState.None;

            transformed =
                transformed.Join(
                    this.ResultOfTheFunctions(
                        call: invocation,
                        written: arguments[i].Expression,
                        functions: functions,
                        given: given));
        }

        return transformed.AfterTheClose;
    }

    /// <summary>
    ///     The transforms that an update of a collection gets. The edit keeps
    ///     them in its <c>Updates</c>, beside what they return. Thus, a call of a
    ///     transform that the code reads back through that member runs it.
    /// </summary>
    /// <remarks>
    ///     The functions are a member, and not a part of the state of the edit.
    ///     An operator does not use the state of an argument with functions as
    ///     an input, and <c>CombineWith</c> gets an edit as an argument.
    /// </remarks>
    private LoopState TransformsOf(InvocationExpressionSyntax invocation, IMethodSymbol method)
    {
        SeparatedSyntaxList<ArgumentSyntax> arguments = invocation.ArgumentList.Arguments;
        LoopState transforms = LoopState.None;

        for (int i = 0; i < arguments.Count; i++)
        {
            if (ParameterFor(method: method, argument: arguments[i], position: i)
                is { Type.TypeKind: TypeKind.Delegate })
            {
                transforms = OnlyTheFunctions(state: this.Eval(arguments[i].Expression), seed: transforms);
            }
        }

        return KeptInTheUpdates(edit: method.ContainingType, transforms: transforms);
    }

    // The state of an edit whose Updates member holds the functions.
    private static LoopState KeptInTheUpdates(INamedTypeSymbol edit, LoopState transforms) =>
        transforms.Functions.IsEmpty
        || edit.GetMembers("Updates").OfType<IPropertySymbol>().FirstOrDefault() is not { } updates
            ? LoopState.None
            : LoopState.None.WithMembers(Construction.Empty.With(member: updates, state: transforms));

    // The functions of a state, joined to the seed. A name that holds a function can have other
    // state also, which is not a transform.
    private static LoopState OnlyTheFunctions(LoopState state, LoopState seed) =>
        state.Functions.Aggregate(seed: seed, func: static (all, function) => all.Join(LoopState.OfFunction(function)));

    /// <summary>
    ///     What the transforms of an edit that the constructor of
    ///     <c>CollectionEdit</c> makes return. The transforms are the values of
    ///     the dictionary that the constructor gets, and each one runs as the
    ///     transform of <c>Update</c> does.
    /// </summary>
    private LoopState TransformedByTheConstructor(
        SyntaxNode creation,
        BaseArgumentListSyntax arguments,
        IMethodSymbol constructor)
    {
        ImmutableArray<ITypeSymbol> types = constructor.ContainingType.TypeArguments;

        if (constructor.ContainingType.Name != "CollectionEdit" || types.Length != 3)
        {
            return LoopState.None;
        }

        // Only a transform of a state that can hold a state can return one. The edit keeps each
        // transform in its Updates, as the edit of Update does.
        bool holdsState = this.region.Symbols.CanHoldState(type: types[2], compilation: this.region.Assembly);
        ImmutableArray<LoopState> given = ImmutableArray.Create(LoopState.None);
        LoopState transformed = LoopState.None;
        LoopState kept = LoopState.None;

        for (int i = 0; i < arguments.Arguments.Count; i++)
        {
            if (ParameterFor(method: constructor, argument: arguments.Arguments[i], position: i) is { Name: "updates" })
            {
                ExpressionSyntax written = arguments.Arguments[i].Expression;
                LoopState functions = this.Eval(written);

                if (holdsState)
                {
                    transformed =
                        transformed.Join(
                            this.ResultOfTheFunctions(
                                call: creation,
                                written: written,
                                functions: functions,
                                given: given));
                }

                kept = OnlyTheFunctions(state: functions, seed: kept);
            }
        }

        return transformed.AfterTheClose.Join(KeptInTheUpdates(edit: constructor.ContainingType, transforms: kept));
    }

    /// <summary>
    ///     What the function of a merge returns. The first parameter gets the
    ///     content of the receiver. The second gets the content of the second
    ///     stream, or of the list for a merge of a list of streams.
    /// </summary>
    /// <remarks>
    ///     The function runs only when two streams fire in one transaction. The
    ///     walk does not know when that occurs. Thus, it reads the function as a
    ///     function that runs, as for a read that occurs only if a condition is
    ///     true.
    /// </remarks>
    private LoopState MergedContent(
        InvocationExpressionSyntax invocation,
        IMethodSymbol method,
        ITypeSymbol? receiverType,
        LoopState receiverState,
        IReadOnlyList<LoopState> argumentStates)
    {
        // Of the operators of this rule, only Merge takes a function, and each Merge takes one. The
        // name is the same test with no read of the parameters, which the compiler makes anew.
        if (method.Name != "Merge" || !this.HoldsContent(method))
        {
            return LoopState.None;
        }

        ImmutableArray<LoopState> given =
            ImmutableArray.Create(
                item1: receiverState.Content,
                item2: this.region.Symbols.IsStreamType(receiverType) && argumentStates.Count > 0
                    ? argumentStates[0].Content
                    : receiverState.Content);

        return invocation.ArgumentList.Arguments
                .Select(static a => a.Expression)
                .OfType<AnonymousFunctionExpressionSyntax>()
                .FirstOrDefault() is { } lambda
            ? this.EvalLambdaResult(lambda: lambda, boundParameters: given)
            : this.ContentOfANamedFunction(invocation: invocation, given: given);
    }

    /// <summary>
    ///     The state of a reactive collection that its factory makes. A pending
    ///     cell of initial items makes the collection pending, and a linked stream
    ///     of edits links it. The items that the code gives in a list are content
    ///     only.
    /// </summary>
    /// <remarks>
    ///     The collection holds the state of each item, and does not read it or
    ///     listen to it. Thus, the state of an item does not make the collection
    ///     pending or linked. A read of the state, as the select of a fold does,
    ///     gets the content.
    /// </remarks>
    private LoopState MadeCollection(
        LoopState inputs,
        IReadOnlyList<LoopState> argumentStates,
        IReadOnlyList<ITypeSymbol?> argumentTypes)
    {
        bool pending = false;
        bool linked = false;
        LoopState items = LoopState.None;

        for (int i = 0; i < argumentStates.Count; i++)
        {
            if (this.region.Symbols.IsCellOrBehaviorType(argumentTypes[i]))
            {
                pending |= argumentStates[i].Pending;
            }
            else if (this.region.Symbols.IsStreamType(argumentTypes[i])
                     || (argumentTypes[i] is IArrayTypeSymbol { ElementType: var element }
                         && this.region.Symbols.IsStreamType(element)))
            {
                linked |= argumentStates[i].Linked;
            }
            else
            {
                items = items.Join(argumentStates[i]);
            }
        }

        return inputs.WithPending(pending).WithLinked(linked).WithContent(inputs.Content.Join(items));
    }

    /// <summary>
    ///     What the sends of the block give to the sinks that a value can be. The
    ///     walk knows the sends into a sink that it saw made only. Thus, the result
    ///     is null when the value can be a different object.
    /// </summary>
    private LoopState? SentToTheSinks(LoopState value)
    {
        ImmutableArray<MadeObject> objects = this.region.Objects.Now(value.Objects);

        if (objects.IsEmpty)
        {
            return null;
        }

        LoopState sent = LoopState.None;

        foreach (MadeObject made in objects)
        {
            if (this.region.Objects.CreationOfASink(made) is not { } creation)
            {
                return null;
            }

            sent = sent.Join(this.region.SentBefore.To(creation).Content);
        }

        return sent;
    }

    /// <summary>
    ///     The values that a hold or an accumulation holds. The initial value is
    ///     one. A hold gets the values of its stream after the close, and an
    ///     accumulation gets what its function returns then. A collect keeps no
    ///     value that it gives.
    /// </summary>
    private LoopState HeldContent(
        InvocationExpressionSyntax invocation,
        IMethodSymbol method,
        LoopState receiverState,
        LoopState initial) =>
        !this.HoldsContent(method)
            ? LoopState.None
            : OperatorName(method) switch
            {
                "HoldLazy" => initial.Join(receiverState.Content.AfterTheClose),
                "CollectLazy" => this.CollectedContent(
                    invocation: invocation,
                    method: method,
                    receiverState: receiverState,
                    initial: initial),
                "Accum" or "AccumLazy" => initial.Join(
                    this.AccumulatesValuesOfTheGraph(method)
                    || this.FunctionCanGiveAState(
                        invocation: invocation,
                        given: receiverState.Content.AfterTheClose)
                        ? this.LambdaContent(
                                invocation: invocation,
                                parameterState: receiverState.Content.AfterTheClose)
                            .AfterTheClose
                        : LoopState.None),
                _ => LoopState.None
            };

    /// <summary>
    ///     The values that a snapshot fires: what its function returns, or else
    ///     the value of its cell. The function gets the value of the stream and of
    ///     each cell at a firing, which is after the close. The walk reads the
    ///     function only for a result that can hold a state.
    /// </summary>
    private LoopState SnapshotContent(
        InvocationExpressionSyntax invocation,
        IMethodSymbol method,
        LoopState receiverState,
        IReadOnlyList<LoopState> argumentStates)
    {
        if (!this.HoldsContent(method))
        {
            return LoopState.None;
        }

        AnonymousFunctionExpressionSyntax? lambda =
            invocation.ArgumentList.Arguments
                .Select(static a => a.Expression)
                .OfType<AnonymousFunctionExpressionSyntax>()
                .FirstOrDefault();

        if (lambda != null)
        {
            return this.EvalLambdaResult(
                lambda: lambda,
                boundParameters: argumentStates.Select(static state => state.Content.AfterTheClose)
                    .Prepend(receiverState.Content.AfterTheClose)
                    .ToImmutableArray());
        }

        // A function that is not a lambda gives nothing that the walk knows.
        return method.Parameters.Any(static parameter => parameter.Type.TypeKind == TypeKind.Delegate)
               || argumentStates.Count == 0
            ? LoopState.None
            : argumentStates[0].Content.AfterTheClose;
    }

    /// <summary>
    ///     The values that a collect fires: the first element of the tuple that
    ///     its function returns. The function gets the value of the stream and the
    ///     state, at a firing after the close.
    /// </summary>
    private LoopState CollectedContent(
        InvocationExpressionSyntax invocation,
        IMethodSymbol method,
        LoopState receiverState,
        LoopState initial)
    {
        if (!this.HoldsContent(method))
        {
            return LoopState.None;
        }

        ImmutableArray<LoopState> given =
            ImmutableArray.Create(item1: receiverState.Content.AfterTheClose, item2: initial.AfterTheClose);

        // A function that is not a lambda at the call is a lambda in a local, a local function, or a
        // method.
        LoopState returned =
            invocation.ArgumentList.Arguments
                    .Select(static a => a.Expression)
                    .OfType<AnonymousFunctionExpressionSyntax>()
                    .FirstOrDefault() is { } lambda
                ? this.EvalLambdaResult(lambda: lambda, boundParameters: given)
                : this.ContentOfANamedFunction(invocation: invocation, given: given);

        return returned.Members?.PartsAt(ofATuple: true).ToList() is { Count: > 0 } parts
            ? parts.Where(static part => part.Position == 0).Select(static part => part.State).FirstOrDefault()
              ?? LoopState.None
            : returned;
    }

    // The walk of the function of an accumulation costs much in a view model, which accumulates
    // records. Thus, the walk always reads the function only for an accumulation of cells or
    // streams. For each other accumulation, FunctionCanGiveAState decides.
    private bool AccumulatesValuesOfTheGraph(IMethodSymbol method) =>
        method.ReturnType is INamedTypeSymbol { TypeArguments.Length: 1 } result
        && (this.region.Symbols.IsCellOrBehaviorType(result.TypeArguments[0])
            || this.region.Symbols.IsStreamType(result.TypeArguments[0]));

    /// <summary>
    ///     True when the function of an accumulation of other values, such as
    ///     records, can return a value with a state. This is a check of the syntax
    ///     only, as <see cref="CanUseState" /> is. The walk reads the function only
    ///     then, and an accumulation that cannot give a state stays cheap.
    /// </summary>
    /// <remarks>
    ///     A function gets a value with a state through its parameters, when the
    ///     stream has content. A static lambda has no other source. Each other
    ///     lambda can also name a local, a parameter, or a member that has a state.
    ///     It can also call a local function or a member of this object.
    /// </remarks>
    private bool FunctionCanGiveAState(InvocationExpressionSyntax invocation, LoopState given)
    {
        if (!given.IsNone || this.region.HasFields)
        {
            return true;
        }

        AnonymousFunctionExpressionSyntax? lambda =
            invocation.ArgumentList.Arguments
                .Select(static a => a.Expression)
                .OfType<AnonymousFunctionExpressionSyntax>()
                .FirstOrDefault();

        if (lambda == null || lambda.Modifiers.Any(SyntaxKind.StaticKeyword))
        {
            return false;
        }

        HashSet<string> names = this.NamesWithState();

        return lambda.Body.DescendantNodesAndSelf()
            .Any(node =>
                node switch
                {
                    ThisExpressionSyntax => this.thisMembers.Count > 0,
                    InvocationExpressionSyntax { Expression: SimpleNameSyntax called } =>
                        this.thisMembers.Count > 0
                        || NamesALocalFunction(from: lambda, name: called.Identifier.ValueText),
                    IdentifierNameSyntax name => names.Contains(name.Identifier.ValueText),
                    _ => false
                });

        static bool NamesALocalFunction(SyntaxNode from, string name) =>
            from.Ancestors()
                .OfType<BlockSyntax>()
                .SelectMany(static block => block.Statements.OfType<LocalFunctionStatementSyntax>())
                .Any(function => function.Identifier.ValueText == name);
    }
}
