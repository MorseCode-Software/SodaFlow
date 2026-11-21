using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SodaFlow.Analyzers;

internal sealed partial class LoopStateEvaluator
{
    /// <summary>
    ///     The state that a read in the transaction of the block gives for a hold
    ///     that the code of that transaction made. That state is the initial value
    ///     of the hold. Null for each other read.
    /// </summary>
    /// <remarks>
    ///     A hold gets the value of a firing at the end of the transaction. The
    ///     block and each send in it run in one transaction. Thus, a read in the
    ///     block gives the initial value, also after a send into the sink of the
    ///     hold. An accumulation and a cell sink are holds also. A function that
    ///     runs only in the transaction is a part of it. Each other function can
    ///     run after the transaction, thus a read in it gets each value. An
    ///     evaluator that changes nothing uses the same test. It reads a function
    ///     that the code gives by its name to a map. A map of a constant runs
    ///     that function in the transaction.
    /// </remarks>
    /// <param name="read">The call that reads.</param>
    /// <param name="receiver">The cell that the call reads.</param>
    private LoopState? InitialOfAHoldOfTheBlock(SyntaxNode read, ExpressionSyntax? receiver)
    {
        if (receiver == null)
        {
            return null;
        }

        ExpressionSyntax cell = Unwrapped(receiver);

        // An element of a literal list at a constant index is the expression of that element.
        while (cell is ElementAccessExpressionSyntax { ArgumentList.Arguments.Count: 1 } access
               && this.PositionOf(access) is { } element)
        {
            cell = Unwrapped(element);
        }

        if (this.InitialOfAHoldIn(cell: cell, reader: this, read: read) is { } initial)
        {
            return this.Eval(initial);
        }

        if (this.FirstValueThrough(cell: cell, read: read) is { } first)
        {
            return first;
        }

        // A member of the object of a method. The code of the call that runs the method made the
        // object, and has the hold.
        return this.HeldMemberAt(cell) is ({ } caller, { } given)
               && caller.InitialOfAHoldIn(cell: given, reader: this, read: read) is { } held
            ? caller.Eval(held)
            : null;
    }

    // The initial value of a hold that the code of this evaluator made in the transaction. The cell
    // is the call that makes the hold, or a local that no code writes. The reader is the evaluator
    // of the read. Null for each other cell, or for a read after the transaction.
    private ExpressionSyntax? InitialOfAHoldIn(ExpressionSyntax cell, LoopStateEvaluator reader, SyntaxNode read)
    {
        (ILocalSymbol Local, VariableDeclaratorSyntax Declarator)? named = null;

        // A local that no code writes has the hold of its initializer.
        if (cell is IdentifierNameSyntax name
            && this.region.SymbolOf(name) is ILocalSymbol { DeclaringSyntaxReferences.Length: 1 } local
            && local.DeclaringSyntaxReferences[0].GetSyntax()
                is VariableDeclaratorSyntax { Initializer.Value: { } value } declarator)
        {
            cell = Unwrapped(value);
            named = (local, declarator);
        }

        return this.InitialValueOf(cell) is not { } initial
               || !reader.RunsInTheTransaction(read)
               || (named is { Local: var only, Declarator: var declaredAt }
                   && (!this.RunsInTheTransaction(declaredAt)
                       || NamesOf(local: only, declarator: declaredAt, region: this.region)?.Any(IsWritten) != false))
            ? null
            : initial;
    }

    // The value that a read in the transaction gives for an operator of a hold of the block. A map,
    // a lift, and a calm cell compute their first value from the value of the input, at the first
    // read. A cell as a cell is its receiver. A bindable reads its cell, and a constant of a lazy
    // sample forces the sample at the read. A switch takes the sent cell at the end of the
    // transaction, thus it is not such an operator. Null for each other cell.
    private LoopState? FirstValueThrough(ExpressionSyntax cell, SyntaxNode read)
    {
        if (this.ValueOfALocalThatNoCodeWrites(cell) is not InvocationExpressionSyntax
            {
                Expression: MemberAccessExpressionSyntax
                {
                    Name.Identifier.ValueText: "Map" or "Calm" or "AsCell" or "AsBehavior" or "Lift" or "ConstantLazy"
                    or "CreateOneWay" or "CreateTwoWay"
                }
            } call
            || this.region.SymbolOf(call) is not IMethodSymbol method
            || !this.region.Symbols.IsSodaFlow(method))
        {
            return null;
        }

        SodaFlowSymbols symbols = this.region.Symbols;
        ExpressionSyntax? input = ReceiverOf(symbols: symbols, invocation: call, method: method);
        SeparatedSyntaxList<ArgumentSyntax> arguments = call.ArgumentList.Arguments;

        return OperatorTable.Rule(
                symbols: symbols,
                method: method,
                receiverType: input == null ? null : this.region.TypeOf(input)) switch
            {
                OperatorRule.SameValues => this.InitialOfAHoldOfTheBlock(read: read, receiver: input),
                OperatorRule.MapValue => this.InitialOfAHoldOfTheBlock(read: read, receiver: input) is { } value
                    ? this.MappedContent(invocation: call, parameterState: value)
                    : null,
                OperatorRule.LiftValue when input != null =>
                    this.LiftedFirstValue(lift: call, method: method, receiver: input, read: read),
                OperatorRule.ConstantLazy =>
                    arguments.FirstOrDefault()?.Expression is { } given
                    && this.ValueOfALocalThatNoCodeWrites(Unwrapped(given)) is InvocationExpressionSyntax
                    {
                        Expression: MemberAccessExpressionSyntax { Name.Identifier.ValueText: "SampleLazy" }
                    } lazy
                    && this.region.SymbolOf(lazy) is IMethodSymbol sample
                    && symbols.IsSodaFlow(sample)
                        ? this.InitialOfAHoldOfTheBlock(
                            read: read,
                            receiver: ReceiverOf(symbols: symbols, invocation: lazy, method: sample))
                        : null,
                OperatorRule.Default when method.Name is "CreateOneWay" or "CreateTwoWay" =>
                    arguments
                        .Where((argument, position) =>
                            symbols.IsCellOrBehaviorType(
                                ParameterFor(method: method, argument: argument, position: position)?.Type))
                        .Select(argument => this.InitialOfAHoldOfTheBlock(read: read, receiver: argument.Expression))
                        .FirstOrDefault(),
                _ => null
            };
    }

    // The value that a read in the transaction gives for a lift with a hold of the block as an
    // input. Each such input gives its initial value, and each other input gives what it holds.
    // Null when no input is such a hold.
    private LoopState? LiftedFirstValue(
        InvocationExpressionSyntax lift,
        IMethodSymbol method,
        ExpressionSyntax receiver,
        SyntaxNode read)
    {
        if (!this.HoldsContent(method))
        {
            return null;
        }

        bool anyInitial = false;
        LoopState receiverState = First(receiver);

        List<LoopState> argumentStates =
        [
            .. lift.ArgumentList.Arguments
                .Where(argument => argument.Expression != receiver)
                .Select(argument => First(argument.Expression))
        ];

        return anyInitial
            ? this.LiftedContent(
                invocation: lift,
                receiver: receiver,
                receiverState: receiverState,
                argumentStates: argumentStates)
            : null;

        LoopState First(ExpressionSyntax input)
        {
            if (this.InitialOfAHoldOfTheBlock(read: read, receiver: input) is not { } initial)
            {
                return this.EvalArgument(argument: input, runsNow: false);
            }

            anyInitial = true;

            return initial.AsContent;
        }
    }

    // The value of a local that no code writes. Each other expression is itself. The read of a
    // hold checks that the code of the transaction made the hold.
    private ExpressionSyntax ValueOfALocalThatNoCodeWrites(ExpressionSyntax cell) =>
        cell is IdentifierNameSyntax name
        && this.region.SymbolOf(name) is ILocalSymbol { DeclaringSyntaxReferences.Length: 1 } local
        && local.DeclaringSyntaxReferences[0].GetSyntax()
            is VariableDeclaratorSyntax { Initializer.Value: { } value } declarator
        && NamesOf(local: local, declarator: declarator, region: this.region)?.Any(IsWritten) == false
            ? Unwrapped(value)
            : cell;

    // The initial value of the hold, of the accumulation, or of the cell sink that a call makes.
    // Null for each other expression.
    private ExpressionSyntax? InitialValueOf(ExpressionSyntax made)
    {
        if (made is not InvocationExpressionSyntax
            {
                Expression: MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Hold" or "Accum" or "CreateSink" }
            } creation
            || this.region.SymbolOf(creation) is not IMethodSymbol method
            || !this.region.Symbols.IsSodaFlow(method))
        {
            return null;
        }

        IMethodSymbol definition = (method.ReducedFrom ?? method).OriginalDefinition;

        bool isAHold =
            method.Name == "CreateSink"
                ? this.region.Symbols.IsCellOrBehaviorType(method.ReturnType)
                : definition is { IsExtensionMethod: true, Parameters.Length: > 1 }
                  && this.region.Symbols.IsStreamType(definition.Parameters[0].Type);

        // The first parameter of an extension method is the stream.
        int ordinal = definition.IsExtensionMethod ? 1 : 0;

        return isAHold
            ? creation.ArgumentList.Arguments
                .Where((argument, position) =>
                    ParameterFor(method: method, argument: argument, position: position)?.Ordinal == ordinal)
                .Select(static argument => argument.Expression)
                .FirstOrDefault()
            : null;
    }

    /// <summary>
    ///     Reads a function that runs after the transaction of the block, as the
    ///     function of <c>Transaction.Post</c> does. A send in it gives its value
    ///     to the sink after the close. Thus, each value that the block made from
    ///     the sink gets that value also.
    /// </summary>
    /// <remarks>
    ///     An evaluator that changes nothing reads the function, and the walk
    ///     reports nothing from it. A read after the close does not throw. A post
    ///     in a posted function runs after the close also, thus the walk reads
    ///     its function with this procedure. A method of an object gets the
    ///     members that the object had at the post.
    /// </remarks>
    /// <param name="function">The expression of the function.</param>
    public void RunsAfterTheTransaction(ExpressionSyntax function)
    {
        if (this.quiet && !this.region.ReadsCodeAfterTheTransaction)
        {
            return;
        }

        ImmutableArray<FunctionValue> functions = this.Eval(function).Functions;

        if (functions.IsEmpty)
        {
            return;
        }

        // In a posted function, this evaluator is the one that changes nothing. The walk of that
        // function continues after this read, thus the evaluator gets its walker again.
        LoopStateEvaluator later = this.Quiet();
        SinkWalker? walking = later.walker;
        SinkWalker walker = later.ReadBy(new SinkWalker(region: this.region, evaluator: later, sink: static _ => { }));

        try
        {
            foreach (FunctionValue each in functions)
            {
                this.region.ReadAfterTheTransaction(
                    function: each.Declaration,
                    read: () =>
                    {
                        switch (each.Declaration)
                        {
                            case AnonymousFunctionExpressionSyntax lambda:
                                walker.Visit(lambda.Body);

                                break;
                            case LocalFunctionStatementSyntax local:
                                walker.Visit((SyntaxNode?)local.Body ?? local.ExpressionBody);

                                break;
                            case MethodDeclarationSyntax:
                                later.FollowFunction(
                                    call: function,
                                    name: NameOf(function),
                                    function: each,
                                    target: LoopState.None,
                                    arguments: default,
                                    each: null);

                                break;
                        }
                    });
            }
        }
        finally
        {
            later.walker = walking;
        }
    }

    /// <summary>
    ///     Reads a write to a two-way bindable of a sink as a send of the value
    ///     into that sink after the transaction. The setter posts that send, as
    ///     <c>Transaction.Post</c> does. <c>Execute</c> of a bindable action posts
    ///     a send of its parameter into the sink of its firings also. The bindable
    ///     is the object that the receiver has. A local, a member, a parameter,
    ///     or a callee can give the receiver that object.
    /// </summary>
    /// <param name="bindable">The expression of the bindable.</param>
    /// <param name="value">The value that the code writes, or the parameter of <c>Execute</c>.</param>
    /// <param name="write">The assignment, or the call of <c>Execute</c>.</param>
    public void PostsAWrite(ExpressionSyntax bindable, ExpressionSyntax value, SyntaxNode write)
    {
        if (this.quiet && !this.region.ReadsCodeAfterTheTransaction)
        {
            return;
        }

        // A local that the creation made names the sink. A sink in a field or in a parameter has no
        // object of the store. Thus, the send then goes to the name of the sink.
        if (this.ValueOfALocalThatNoCodeWrites(Unwrapped(bindable)) is not InvocationExpressionSyntax
            {
                Expression: MemberAccessExpressionSyntax
                {
                    Name.Identifier.ValueText: "CreateTwoWay" or "CreateBindableAction"
                }
            } creation
            || this.region.SymbolOf(creation) is not IMethodSymbol method
            || !this.region.Symbols.IsSodaFlow(method))
        {
            this.PostsAWriteToItsObject(bindable: bindable, value: value, write: write);

            return;
        }

        ExpressionSyntax? sink = this.SinkArgumentOf(creation: creation, method: method);

        if (sink == null)
        {
            return;
        }

        LoopState content = this.Eval(value).AsContent;
        LoopStateEvaluator later = this.Quiet();

        this.region.ReadAfterTheTransaction(function: write, read: () => later.Sends(sink: sink, content: content));
    }

    // A write to the bindable that the receiver has. A member, a callee, or a second name can give
    // the receiver that bindable. An action carries the sink of its firings as a member. The store
    // has the sink of each two-way bindable that the code made.
    private void PostsAWriteToItsObject(ExpressionSyntax bindable, ExpressionSyntax value, SyntaxNode write)
    {
        LoopState carried = this.Eval(bindable);

        LoopState sinks =
            this.region.Objects.Now(carried.Objects)
                .Select(made => this.region.Objects.SinkOfABindable(made))
                .OfType<LoopState>()
                .Concat(
                    carried.Members?.Members
                        .Where(member =>
                            member.Key.Name == "FiringsStream"
                            && this.region.Symbols.IsBindableAction(member.Key.ContainingType))
                        .Select(static member => member.Value)
                    ?? [])
                .Aggregate(seed: LoopState.None, func: static (all, each) => all.Join(each));

        if (sinks.Objects.IsEmpty)
        {
            return;
        }

        LoopState content = this.Eval(value).AsContent;
        LoopStateEvaluator later = this.Quiet();

        this.region.ReadAfterTheTransaction(
            function: write,
            read: () => later.SendsInto(sinks: sinks, content: content));
    }

    // Gives content to each sink of the store that a state has, as a send into a name of it does.
    private void SendsInto(LoopState sinks, LoopState content)
    {
        content = content.AfterTheClose;

        foreach (MadeObject made in this.region.Objects.Now(sinks.Objects))
        {
            if (this.region.Objects.CreationOfASink(made) is { } creation)
            {
                this.region.Sent.Add(creation: creation, content: content);
            }
        }
    }

    // The argument of the creation of a bindable that is its sink. The write of a bindable of a
    // cell sink sends into that sink. The write of a bindable of a cell sends into its sink of
    // edits. An action sends into its sink of firings.
    private ExpressionSyntax? SinkArgumentOf(InvocationExpressionSyntax creation, IMethodSymbol method) =>
        creation.ArgumentList.Arguments
            .Where((argument, position) =>
                ParameterFor(method: method, argument: argument, position: position)?.Type is INamedTypeSymbol
                {
                    OriginalDefinition.Name: "CellSink" or "StreamSink"
                } type
                && this.region.Symbols.IsSodaFlow(type))
            .Select(static argument => argument.Expression)
            .FirstOrDefault();

    // The parts of a value that a call of the library makes, which the operator table does not
    // give it.
    private LoopState WithTheLibraryParts(InvocationExpressionSyntax call, IMethodSymbol method, LoopState state) =>
        method.Name == "CreateTwoWay"
            ? this.ATwoWayBindable(creation: call, method: method, state: state)
            : this.AConstructedResult(construct: call, method: method, state: state);

    // The result of Construct of a map through a task holds what its function returns. The function
    // runs in the transaction of the send of the result, after the close. Thus, an evaluator that
    // changes nothing reads it. The library has no other method of that name.
    private LoopState AConstructedResult(InvocationExpressionSyntax construct, IMethodSymbol method, LoopState state)
    {
        if (construct.ArgumentList.Arguments.FirstOrDefault()?.Expression is not { } function)
        {
            return state;
        }

        LoopState functions = this.Eval(function);
        LoopStateEvaluator reader = this.Quiet();

        return functions.Functions.Aggregate(
            seed: state,
            func: (all, each) =>
                all.Join(
                    reader.FollowFunction(
                            call: construct,
                            name: NameOf(function),
                            function: each,
                            target: functions,
                            arguments: default,
                            each: null)
                        ?.State
                    ?? LoopState.None));
    }

    // A two-way bindable that a call makes is an object of the store. The store has the sink that
    // the bindable sends a write into.
    private LoopState ATwoWayBindable(InvocationExpressionSyntax creation, IMethodSymbol method, LoopState state)
    {
        if (this.SinkArgumentOf(creation: creation, method: method) is not { } sink)
        {
            return state;
        }

        LoopState made = this.MadeHere(place: creation, state: state, exact: false);
        LoopState ofTheSink = this.Eval(sink);

        foreach (MadeObject bindable in made.Objects)
        {
            this.region.Objects.IsABindableOf(made: bindable, sink: ofTheSink);
        }

        return made;
    }

    /// <summary>
    ///     Reads a map through a task as a send of the result of its operation
    ///     into its sink of results, after the transaction. The result is the
    ///     value that the operation gives to <c>FromValue</c>, or the value that
    ///     the function of <c>Construct</c> returns.
    /// </summary>
    /// <remarks>
    ///     The operation gets each value that the source fires. The walk reads
    ///     what the operation returns, with that value for its first parameter.
    ///     It also finds each such call in the code of the operation.
    /// </remarks>
    /// <param name="call">The call of <c>MapAsync</c>.</param>
    /// <param name="method">The method of the call.</param>
    /// <param name="arguments">The arguments of the call.</param>
    public void SendsTheResults(
        InvocationExpressionSyntax call,
        IMethodSymbol method,
        SeparatedSyntaxList<ArgumentSyntax> arguments) =>
        this.SendsTheResults(
            call: call,
            method: method,
            arguments: arguments,
            input: ReceiverOf(symbols: this.region.Symbols, invocation: call, method: method) is { } source
                ? this.Eval(source).Content
                : LoopState.None);

    /// <summary>
    ///     Reads <c>Execute</c> of the status of a map through a task as a value
    ///     that the operation gets. The pipeline then sends the result into its
    ///     sink of results, after the transaction. The status is a local that no
    ///     code writes, and that <c>MapAsync</c> made.
    /// </summary>
    /// <param name="execute">The method of the call of <c>Execute</c>.</param>
    /// <param name="status">The receiver of the call.</param>
    /// <param name="value">The value, or the cell whose value the pipeline gets.</param>
    public void SendsTheResultsOf(IMethodSymbol execute, ExpressionSyntax status, ExpressionSyntax value)
    {
        if (this.MadeValueOf(status) is not InvocationExpressionSyntax
            {
                Expression: MemberAccessExpressionSyntax { Name.Identifier.ValueText: "MapAsync" }
            } map
            || this.region.SymbolOf(map) is not IMethodSymbol mapAsync
            || !this.region.Symbols.IsSodaFlow(mapAsync))
        {
            return;
        }

        IMethodSymbol definition = (execute.ReducedFrom ?? execute).OriginalDefinition;
        LoopState given = this.Eval(value);

        // Execute of a cell gives the operation the value of the cell.
        this.SendsTheResults(
            call: map,
            method: mapAsync,
            arguments: map.ArgumentList.Arguments,
            input: definition.Parameters[definition.Parameters.Length - 1].Type is ITypeParameterSymbol
                ? given
                : given.Content);
    }

    private void SendsTheResults(
        InvocationExpressionSyntax call,
        IMethodSymbol method,
        SeparatedSyntaxList<ArgumentSyntax> arguments,
        LoopState input)
    {
        if ((this.quiet && !this.region.ReadsCodeAfterTheTransaction)
            || ArgumentFor(method: method, arguments: arguments, parameter: "results") is not { } results
            || ArgumentFor(method: method, arguments: arguments, parameter: "operation") is not { } operation)
        {
            return;
        }

        LoopState functions = this.Eval(operation);
        LoopStateEvaluator reader = this.Quiet();

        // The operation returns a task of its result.
        LoopState returned =
            functions.Functions.Aggregate(
                seed: LoopState.None,
                func: (all, function) =>
                    all.Join(
                        reader.FollowFunction(
                                call: call,
                                name: NameOf(operation),
                                function: function,
                                target: functions,
                                arguments: default,
                                each: (operation, input, ManyTimes: true),
                                byPosition: ImmutableArray.Create(input))
                            ?.State
                        ?? LoopState.None));

        LoopState content =
            functions.Functions
                .SelectMany(static function => function.Declaration.DescendantNodes())
                .OfType<InvocationExpressionSyntax>()
                .Where(static result =>
                    result.Expression is MemberAccessExpressionSyntax
                    {
                        Name.Identifier.ValueText: "FromValue" or "Construct"
                    })
                .Where(result =>
                    this.region.SymbolOf(result) is IMethodSymbol { ContainingType.Name: "ResultFactory" } factory
                    && this.region.Symbols.IsSodaFlow(factory))
                .Aggregate(seed: LoopState.None, func: (all, result) => all.Join(this.ResultOf(result)))
                .Join(returned.AsContent);

        if (content.IsNone)
        {
            return;
        }

        LoopStateEvaluator later = this.Quiet();

        this.region.ReadAfterTheTransaction(function: call, read: () => later.Sends(sink: results, content: content));
    }

    // The content that a call of FromValue or Construct gives to the sink of results. Each of the
    // two has one parameter. The function of Construct runs in the transaction of the send, and
    // its return value is the result.
    private LoopState ResultOf(InvocationExpressionSyntax result)
    {
        ExpressionSyntax value = result.ArgumentList.Arguments[0].Expression;

        if (result.Expression is MemberAccessExpressionSyntax { Name.Identifier.ValueText: "FromValue" })
        {
            return this.Eval(value).AsContent;
        }

        return this.Eval(value).Functions
            .Select(static function => function.Declaration)
            .OfType<AnonymousFunctionExpressionSyntax>()
            .Aggregate(
                seed: LoopState.None,
                func: (all, function) =>
                    all.Join(this.EvalBody(expressionBody: function.ExpressionBody, block: function.Block).AsContent));
    }

    // True when a node runs only in the transaction of the block. That is the code of the block,
    // and a function in it that the library or the platform runs only in the call. The function of
    // a map of a constant cell is such a function also, because the transaction computes the first
    // value of the map. A function that this evaluator reads for a call in the transaction is such a
    // function also. Each other function, a query, and a member can run after the transaction.
    private bool RunsInTheTransaction(SyntaxNode node)
    {
        for (SyntaxNode? each = node; each != null; each = each.Parent)
        {
            if (each == this.region.Root)
            {
                return true;
            }

            bool runsLater =
                each switch
                {
                    AnonymousFunctionExpressionSyntax
                    {
                        Parent: ArgumentSyntax { Parent.Parent: InvocationExpressionSyntax call }
                    } => !this.RunsOnlyInTheCall(call),
                    AnonymousFunctionExpressionSyntax
                        or LocalFunctionStatementSyntax
                        or QueryExpressionSyntax
                        or MemberDeclarationSyntax => true,
                    _ => false
                };

            if (runsLater)
            {
                return this.IsReadForACallInTheTransaction(each);
            }
        }

        return false;
    }

    // True when this evaluator reads a function for a call that runs in the transaction of the
    // block. RunsTheFunctionInTheCall says which calls run the function in the call. Thus, a
    // function that the code gives by its name is a part of the transaction, as a lambda at
    // Transaction.Run is. Each other run of the function has an evaluator of its own, with its own
    // call.
    private bool IsReadForACallInTheTransaction(SyntaxNode function)
    {
        if (this.call is not { Caller: var caller, Site: var site }
            || (function is MethodDeclarationSyntax ? this.function != null : function != this.function))
        {
            return false;
        }

        // The first read of the Value of a Lazy runs its function. Each read before it is in the
        // transaction also, because the code of the transaction made the hold that the function
        // reads.
        return site switch
        {
            InvocationExpressionSyntax invocation => caller.RunsTheFunctionInTheCall(invocation)
                                                     && caller.RunsInTheTransaction(invocation),
            MemberAccessExpressionSyntax or MemberBindingExpressionSyntax =>
                caller.region.SymbolOf(site) is IPropertySymbol property
                && SodaFlowSymbols.IsLazyValue(property)
                && caller.RunsInTheTransaction(site),
            _ => false
        };
    }

    // True when a call runs in the call a function that the walk follows from it. A call of a
    // delegate runs it. A method of this compilation runs it also, but an iterator runs it only
    // when the code enumerates the result at once. For a call of the library or of the platform,
    // RunsOnlyInTheCall decides.
    private bool RunsTheFunctionInTheCall(InvocationExpressionSyntax site)
    {
        if (this.region.SymbolOf(site) is not IMethodSymbol called)
        {
            return false;
        }

        if (called.MethodKind == MethodKind.DelegateInvoke || IsDynamicInvoke(called))
        {
            return true;
        }

        IReadOnlyList<IMethodSymbol> definitions =
            this.region.Symbols.IsSodaFlow(called) ? [] : this.DefinitionsOf(invocation: site, method: called);

        return definitions.Count > 0 && definitions.All(static definition => BodyOf(definition) != null)
            ? !definitions.Any(IsIterator) || this.IsEnumeratedAtOnce(site)
            : this.RunsOnlyInTheCall(site);
    }

    // True when the library or the platform runs a function that a call gets only in that call. A
    // transaction and a loop block run it one time. A map of a constant cell runs it for the first value, and has
    // no update that runs it again.
    private bool RunsOnlyInTheCall(InvocationExpressionSyntax call)
    {
        if (this.region.SymbolOf(call) is not IMethodSymbol method)
        {
            return false;
        }

        if (!this.region.Symbols.IsSodaFlow(method))
        {
            return this.RunsOnlyInTheCallOfThePlatform(call: call, method: method);
        }

        if (OperatorTable.RunsFunctionOneTime(method))
        {
            return true;
        }

        return method.Name == "Map"
               && ReceiverOf(symbols: this.region.Symbols, invocation: call, method: method) is { } receiver
               && this.region.Symbols.IsCellOrBehaviorType(this.region.TypeOf(receiver))
               && this.IsAConstant(receiver);
    }

    // True for a call of Constant of the library. The call can also be the initializer of a
    // readonly field, or of a local that no code writes.
    private bool IsAConstant(ExpressionSyntax cell)
    {
        ExpressionSyntax made = Unwrapped(cell);

        switch (this.region.SymbolOf(made))
        {
            case IFieldSymbol { IsReadOnly: true } field
                when this.region.InitializerOf(field.OriginalDefinition) is { } value:
                made = Unwrapped(value);

                break;
            case ILocalSymbol { DeclaringSyntaxReferences.Length: 1 } local
                when local.DeclaringSyntaxReferences[0].GetSyntax()
                         is VariableDeclaratorSyntax { Initializer.Value: { } value } declarator
                     && NamesOf(local: local, declarator: declarator, region: this.region)?.Any(IsWritten) == false:
                made = Unwrapped(value);

                break;
        }

        return made is InvocationExpressionSyntax
                   {
                       Expression: MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Constant" }
                   } constant
               && this.region.SymbolOf(constant) is IMethodSymbol method
               && this.region.Symbols.IsSodaFlow(method);
    }

    // True when a method of the platform runs a function that it gets only in the call. A method
    // of Enumerable that is not a query runs it, and so does a query that the code enumerates at
    // once. So does a method of an array or of a List that runs its function, as ForEach and Find
    // do. Each other method can keep the function for a later time, as Add, Array.Fill, and
    // CancellationToken.Register do.
    private bool RunsOnlyInTheCallOfThePlatform(ExpressionSyntax call, IMethodSymbol method)
    {
        INamedTypeSymbol type = (method.ReducedFrom ?? method).OriginalDefinition.ContainingType;

        if (SodaFlowSymbols.IsLinqEnumerable(type))
        {
            return !SodaFlowSymbols.MakesQuery(method) || this.IsEnumeratedAtOnce(call);
        }

        // BinarySearch gets a comparer and no function, thus no lambda is its argument.
        return method.Name is "ForEach" or "Find" or "FindAll" or "FindIndex" or "FindLast" or "FindLastIndex"
                   or "Exists" or "TrueForAll" or "RemoveAll" or "ConvertAll" or "Sort"
               && (type.SpecialType == SpecialType.System_Array
                   || (type is { Name: "List", Arity: 1 }
                       && type.ContainingNamespace?.ToDisplayString() == "System.Collections.Generic"));
    }

    // True when the code of the transaction enumerates the result of a call. A method of
    // Enumerable that is not a query enumerates its receiver, as in Select(...).First(), and a
    // foreach enumerates its collection. A query of Enumerable passes the test to its own result.
    // A local that no code writes passes it to each of its names, and each name must run in the
    // transaction.
    private bool IsEnumeratedAtOnce(ExpressionSyntax made)
    {
        while (true)
        {
            switch (made.Parent)
            {
                case MemberAccessExpressionSyntax { Parent: InvocationExpressionSyntax next } access
                    when access.Expression == made
                         && this.region.SymbolOf(next) is IMethodSymbol method
                         && SodaFlowSymbols.IsLinqEnumerable(
                             (method.ReducedFrom ?? method).OriginalDefinition.ContainingType):
                    if (!SodaFlowSymbols.MakesQuery(method))
                    {
                        return true;
                    }

                    made = next;

                    continue;
                case CommonForEachStatementSyntax loop when loop.Expression == made:
                    return true;
                case EqualsValueClauseSyntax { Parent: VariableDeclaratorSyntax declarator }
                    when this.region.ModelFor(declarator).GetDeclaredSymbol(declarator) is ILocalSymbol local:
                    return NamesOf(local: local, declarator: declarator, region: this.region)?.ToList()
                               is { Count: > 0 } names
                           && names.All(name =>
                               !IsWritten(name) && this.RunsInTheTransaction(name) && this.IsEnumeratedAtOnce(name));
                default:
                    return false;
            }
        }
    }

    // The hold that a member of the object of this method has, with the evaluator of the code that
    // made the object. The member is one that only the constructor can set. The object is a
    // creation at the call that runs this method, directly or through a local that no code writes.
    private (LoopStateEvaluator Caller, ExpressionSyntax Given)? HeldMemberAt(SyntaxNode cell)
    {
        if (cell is not (IdentifierNameSyntax or MemberAccessExpressionSyntax { Expression: ThisExpressionSyntax })
            || this.function != null
            || this.call is not { Caller: var caller, Site: InvocationExpressionSyntax site }
            || this.region.SymbolOf(cell) is not { IsStatic: false } member
            || member is not (IPropertySymbol or IFieldSymbol)
            || this.ParameterThatSets(member) is not
            {
                ContainingSymbol: IMethodSymbol { MethodKind: MethodKind.Constructor } constructor
            } parameter
            || cell.FirstAncestorOrSelf<MethodDeclarationSyntax>() is not { } declaration
            || this.region.ModelFor(declaration).GetDeclaredSymbol(declaration) is not { } method
            || caller.OwnerAt(site: site, method: method) is not { } owner
            || caller.CreationIn(owner) is not { ArgumentList: { } arguments } creation
            || caller.region.SymbolOf(creation) is not IMethodSymbol made
            || !SymbolEqualityComparer.Default.Equals(x: made.OriginalDefinition, y: constructor.OriginalDefinition)
            || !caller.RunsInTheTransaction(creation))
        {
            return null;
        }

        ExpressionSyntax? given =
            arguments.Arguments
                .Where((argument, position) =>
                    ParameterFor(method: made, argument: argument, position: position)?.Ordinal == parameter.Ordinal)
                .Select(static argument => argument.Expression)
                .FirstOrDefault();

        return given == null ? null : (caller, Unwrapped(given));
    }

    // The parameter of a constructor that gives a member its value, for a member that no code
    // after the creation can set. That is a property of a positional record, or a readonly field
    // or a property with no setter whose initializer is the parameter. An init accessor can set a
    // member in a creation with an initializer only, and CreationIn does not accept one.
    private IParameterSymbol? ParameterThatSets(ISymbol member)
    {
        if (member is IPropertySymbol { SetMethod.IsInitOnly: false } or IFieldSymbol { IsReadOnly: false })
        {
            return null;
        }

        ISymbol? source =
            member.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() is ParameterSyntax positional
                ? this.region.ModelFor(positional).GetDeclaredSymbol(positional)
                : this.region.InitializerOf(member.OriginalDefinition) is IdentifierNameSyntax named
                    ? this.region.SymbolOf(named)
                    : null;

        return source as IParameterSymbol;
    }

    // The object of a method at a call. It is the receiver of a call of the method, or the owner
    // of the one argument that names the method. Null for each other call.
    private ExpressionSyntax? OwnerAt(InvocationExpressionSyntax site, ISymbol method)
    {
        if (site.Expression is MemberAccessExpressionSyntax called && IsTheMethod(called))
        {
            return called.Expression;
        }

        ExpressionSyntax[] owners =
        [
            .. site.ArgumentList.Arguments
                .Select(static argument => argument.Expression)
                .OfType<MemberAccessExpressionSyntax>()
                .Where(IsTheMethod)
                .Select(static named => named.Expression)
        ];

        return owners.Length == 1 ? owners[0] : null;

        bool IsTheMethod(ExpressionSyntax named) =>
            this.region.SymbolOf(named) is IMethodSymbol found
            && SymbolEqualityComparer.Default.Equals(x: found.OriginalDefinition, y: method.OriginalDefinition);
    }

    // The creation that an expression is: the expression itself, or the initializer of a local
    // that no code writes. A creation with an initializer can set a member with init, thus it is
    // not one. Null for each other expression.
    private BaseObjectCreationExpressionSyntax? CreationIn(ExpressionSyntax owner)
    {
        ExpressionSyntax made = Unwrapped(owner);

        if (made is IdentifierNameSyntax name
            && this.region.SymbolOf(name) is ILocalSymbol { DeclaringSyntaxReferences.Length: 1 } local
            && local.DeclaringSyntaxReferences[0].GetSyntax()
                is VariableDeclaratorSyntax { Initializer.Value: { } value } declarator
            && NamesOf(local: local, declarator: declarator, region: this.region)?.Any(IsWritten) == false)
        {
            made = Unwrapped(value);
        }

        return made is BaseObjectCreationExpressionSyntax { Initializer: null } creation ? creation : null;
    }

    /// <summary>
    ///     What the sends of the read before give to the sink that the call
    ///     made. A sink with a coalesce function fires what that function returns
    ///     when two sends go into it in one transaction.
    /// </summary>
    /// <remarks>
    ///     The function replaces the values of the sends only where each send is
    ///     in the block and two of them always run. Otherwise, the sink can also
    ///     fire the value of a send.
    /// </remarks>
    private LoopState SentBefore(InvocationExpressionSyntax creation, IMethodSymbol method)
    {
        LoopState sent = this.region.SentBefore.To(creation);

        // A sink of a stream takes only the function, and a sink of a cell takes its initial value
        // also. The count of the arguments is a test that reads no parameter. Only a sink of values
        // that can hold a state can get a state from the function.
        int withAFunction = method.ContainingType.Name == "Stream" || method.Name == "CreateStreamSink" ? 1 : 2;

        if (creation.ArgumentList.Arguments.Count != withAFunction
            || !this.HoldsContent(method)
            || creation.ArgumentList.Arguments
                    .Where((argument, position) =>
                        ParameterFor(method: method, argument: argument, position: position) is { Name: "coalesce" })
                    .Select(static argument => argument.Expression)
                    .FirstOrDefault()
                is not { } coalesce)
        {
            return sent;
        }

        this.region.Sent.AddCoalescing(creation);

        if (!this.region.SentBefore.CanCoalesce(creation))
        {
            return sent;
        }

        // The function runs after the close. Each parameter gets the value of a send.
        LoopState coalesced =
            this.ResultOfTheFunctions(
                    call: creation,
                    written: coalesce,
                    functions: this.Eval(coalesce),
                    given: ImmutableArray.Create(item1: sent.Content, item2: sent.Content))
                .AfterTheClose
                .AsContent;

        return this.region.SentBefore.AlwaysCoalesces(creation) ? coalesced : sent.Join(coalesced);
    }

    // The send of a statement of the block with no return or goto statement before it. The send
    // then always runs before the close. Null for each other send.
    private SyntaxNode? SendOfTheBlock(SyntaxNode sink) =>
        sink.Parent is MemberAccessExpressionSyntax
        {
            Parent: InvocationExpressionSyntax
            {
                Parent: ExpressionStatementSyntax { Parent: BlockSyntax block } statement
            } send
        }
        && block == this.region.Root
        && !block.Statements.TakeWhile(each => each != statement).Any(static each => each
            .DescendantNodesAndSelf(descendIntoChildren: static node =>
                node is not (AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax))
            .Any(static node => node is ReturnStatementSyntax or GotoStatementSyntax))
            ? send
            : null;
}
