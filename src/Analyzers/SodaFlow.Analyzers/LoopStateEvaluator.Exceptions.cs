using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SodaFlow.Analyzers;

// The part of the evaluator for an object that a callee throws through code of the platform. The platform can put
// the object into a different exception, or keep it in a task.
internal sealed partial class LoopStateEvaluator
{
    /// <summary>
    ///     The result of a call that the runtime runs. The runtime replaces each
    ///     object that the callee throws with a new object of the type of the
    ///     wrapper. The caller gets that object, whose <c>InnerException</c> is
    ///     the first object.
    /// </summary>
    /// <param name="followed">The result of the callee.</param>
    /// <param name="wrapper">The metadata name of the type of the wrapper.</param>
    private Followed? Wrapped(Followed? followed, string wrapper)
    {
        if (followed == null || followed.Thrown.IsEmpty)
        {
            return followed;
        }

        INamedTypeSymbol? type = this.region.TypeOfThePlatform(wrapper);

        return WithThrown(
            followed: followed,
            thrown: followed.Thrown
                .Select(one => (one.Call, (ITypeSymbol?)type, WrapperOf(type: type, state: one.State)))
                .ToImmutableArray());
    }

    /// <summary>
    ///     The state of a new exception of the given type whose
    ///     <c>InnerException</c> is the object. The <c>InnerExceptions</c> of an
    ///     <c>AggregateException</c> contain the object too.
    /// </summary>
    /// <param name="type">The type of the new exception.</param>
    /// <param name="state">The state of the object.</param>
    private static LoopState WrapperOf(INamedTypeSymbol? type, LoopState state)
    {
        Construction? members = type == null ? null : Construction.Of(type);

        for (INamedTypeSymbol? owner = type; owner != null && members != null; owner = owner.BaseType)
        {
            foreach (IPropertySymbol inner in owner.GetMembers().OfType<IPropertySymbol>())
            {
                members =
                    inner.Name switch
                    {
                        "InnerException" when !members.Has(inner) => members.With(member: inner, state: state),
                        "InnerExceptions" when !members.Has(inner) => members.With(
                            member: inner,
                            state: state.AsContainer),
                        _ => members
                    };
            }
        }

        return members == null || !members.HasState ? LoopState.None : LoopState.None.WithMembers(members);
    }

    /// <summary>
    ///     Each object that a read of a task throws again. They are the objects
    ///     that the functions of the task threw, and the objects that the state
    ///     of the task keeps. Empty for a task whose result is a task, which
    ///     gives that second task.
    /// </summary>
    /// <param name="read">The read of the task, which throws the objects.</param>
    /// <param name="task">The expression of the task.</param>
    /// <param name="value">The type of the value of the read.</param>
    private ImmutableArray<(SyntaxNode Call, ITypeSymbol? Type, LoopState State)> ObjectsOfATask(
        SyntaxNode read,
        ExpressionSyntax task,
        ITypeSymbol value)
    {
        if (GivesATask(this.region.TypeOf(task)))
        {
            return ImmutableArray<(SyntaxNode Call, ITypeSymbol? Type, LoopState State)>.Empty;
        }

        LoopState state = this.Eval(task);

        ImmutableArray<(SyntaxNode Call, ITypeSymbol? Type, LoopState State)> kept =
            state.Faults.Select(fault => (read, fault.Type, fault.State)).ToImmutableArray();

        return this.FollowLazy(access: read, written: task, lazy: state.AsOneOfItsElements, value: value)
            is { Thrown.IsEmpty: false } followed
            ? followed.Thrown.AddRange(kept)
            : kept;
    }

    /// <summary>
    ///     The objects that <c>Wait</c> of a task throws, each in a new
    ///     <c>AggregateException</c>. Null for each other call.
    /// </summary>
    /// <remarks>
    ///     A receiver that is not a task keeps no object of a task. Thus, the
    ///     walk does not test the type of the receiver.
    /// </remarks>
    /// <param name="invocation">The call.</param>
    /// <param name="method">The method of the call.</param>
    private Followed? FaultOfAWait(InvocationExpressionSyntax invocation, IMethodSymbol method) =>
        invocation.Expression is MemberAccessExpressionSyntax { Expression: var task }
        && this.ObjectsOfATask(read: invocation, task: task, value: method.ReturnType) is { IsEmpty: false } thrown
            ? this.Wrapped(
                followed: WithThrown(followed: Followed.Nothing, thrown: thrown),
                wrapper: "System.AggregateException")
            : null;

    /// <summary>
    ///     The objects that a read of <c>Result</c> of a task throws, each in a
    ///     new <c>AggregateException</c>. Null for each other read.
    /// </summary>
    /// <param name="read">The read of the property.</param>
    /// <param name="owner">The expression of the task.</param>
    /// <param name="property">The property.</param>
    public Followed? FaultOfAResult(SyntaxNode read, ExpressionSyntax owner, IPropertySymbol property) =>
        property.Name == "Result"
        && this.ObjectsOfATask(read: read, task: owner, value: property.Type) is { IsEmpty: false } thrown
            ? this.Wrapped(
                followed: WithThrown(followed: Followed.Nothing, thrown: thrown),
                wrapper: "System.AggregateException")
            : null;

    /// <summary>
    ///     The objects that <c>Parallel.Invoke</c> throws, each in a new
    ///     <c>AggregateException</c>. They are the objects that the functions
    ///     that it gets throw. The walk reads those functions for their objects
    ///     only, because they can run on a different thread.
    /// </summary>
    /// <param name="invocation">The call.</param>
    /// <param name="method">The method of the call.</param>
    private Followed? FaultOfAParallelInvoke(InvocationExpressionSyntax invocation, IMethodSymbol method)
    {
        if (method is not { IsStatic: true, ContainingType.Name: "Parallel" }
            || method.ContainingType.ContainingNamespace?.ToDisplayString() != "System.Threading.Tasks")
        {
            return null;
        }

        ImmutableArray<(SyntaxNode Call, ITypeSymbol? Type, LoopState State)> thrown =
            invocation.ArgumentList.Arguments
                .SelectMany(argument =>
                    this.ObjectsOfAFunction(function: argument.Expression, value: method.ReturnType)
                        .Select(one => ((SyntaxNode)invocation, one.Type, one.State)))
                .ToImmutableArray();

        return thrown.IsEmpty
            ? null
            : this.Wrapped(
                followed: WithThrown(followed: Followed.Nothing, thrown: thrown),
                wrapper: "System.AggregateException");
    }

    // The objects that a function throws, and the objects of the task that it returns. Empty for a value that is no
    // function that the walk knows.
    private ImmutableArray<(ITypeSymbol? Type, LoopState State)> ObjectsOfAFunction(
        ExpressionSyntax function,
        ITypeSymbol value) =>
        this.Eval(function) is { Functions.IsEmpty: false } functions
        && this.FollowLazy(access: function, written: function, lazy: functions, value: value) is { } followed
            ? followed.Thrown
                .Select(static one => (one.Type, one.State))
                .Concat(followed.State.Faults)
                .ToImmutableArray()
            : ImmutableArray<(ITypeSymbol? Type, LoopState State)>.Empty;

    /// <summary>
    ///     The state of a member of an object of the platform that holds the
    ///     objects of an exception or of a task. Null for each other member.
    /// </summary>
    /// <param name="owner">The state of the object.</param>
    /// <param name="member">The member that the code reads.</param>
    /// <param name="read">The read of the member.</param>
    private LoopState? MemberOfAPlatformObject(LoopState owner, ISymbol member, ExpressionSyntax read) =>
        // A test of the name comes before each other test, because each read of a member comes here.
        member.Name switch
        {
            "InnerException" or "InnerExceptions" => InnerExceptionsOf(owner: owner, member: member),
            "Task" when !owner.Faults.IsEmpty =>
                LoopState.None.WithFaults(owner.Faults),
            "Exception" when member is IPropertySymbol { Type: var type }
                             && OwnerOf(read) is { } task
                             && this.ObjectsOfATask(read: read, task: task, value: type)
                                 is { IsEmpty: false } objects =>
                objects.Aggregate(
                    seed: LoopState.None,
                    func: (all, one) => all.Join(
                        WrapperOf(type: this.region.TypeOfThePlatform("System.AggregateException"), state: one.State))),
            _ => null
        };

    private static ExpressionSyntax? OwnerOf(ExpressionSyntax read) =>
        read switch
        {
            MemberAccessExpressionSyntax access => access.Expression,
            MemberBindingExpressionSyntax binding => ConditionalReceiver(binding),
            _ => null
        };

    /// <summary>
    ///     The state of <c>InnerException</c> or <c>InnerExceptions</c> of an
    ///     exception of the platform. Such an exception contains each object
    ///     that its constructor got. Null for each other member, and for an
    ///     owner that contains nothing, as an object of this compilation.
    /// </summary>
    /// <remarks>
    ///     The walk reads an element of the objects as one of the objects. Thus,
    ///     the same state serves for the one object and for the collection.
    /// </remarks>
    /// <param name="owner">The state of the exception.</param>
    /// <param name="member">The member that the code reads.</param>
    private static LoopState? InnerExceptionsOf(LoopState owner, ISymbol member) =>
        member is IPropertySymbol { Name: "InnerException" or "InnerExceptions" } && owner.IsContainer
            ? owner.AsElement
            : null;

    /// <summary>
    ///     The state of what a method of code that cannot know the library
    ///     returns. A method of the platform that gives an exception or a task
    ///     can have a rule of its own. Each other method gives a value that has
    ///     no object of a task that it got. A continuation is such a value.
    /// </summary>
    /// <param name="invocation">The call.</param>
    /// <param name="method">The method of the call.</param>
    private LoopState ResultOfAPlatformCall(InvocationExpressionSyntax invocation, IMethodSymbol method) =>
        this.ExceptionOfACall(invocation: invocation, method: method)
        ?? this.TaskOfACall(invocation: invocation, method: method)
        ?? this.MovedResult(invocation: invocation, method: method).WithoutFaults;

    // The object of the one argument of a call that gets an exception, with the type of the argument. Empty for a call
    // with a different count of arguments.
    private ImmutableArray<(ITypeSymbol? Type, LoopState State)> ObjectOfAnArgument(
        InvocationExpressionSyntax invocation) =>
        invocation.ArgumentList.Arguments is { Count: 1 } arguments
        && this.Eval(arguments[0].Expression) is { } thrown
            ? ImmutableArray.Create((this.region.TypeOf(arguments[0].Expression), thrown))
            : ImmutableArray<(ITypeSymbol? Type, LoopState State)>.Empty;

    /// <summary>
    ///     The state of a call that gives one exception of an exception of the
    ///     platform. <c>Flatten</c> and <c>GetBaseException</c> give the
    ///     exception itself or one of the exceptions that it holds, and the walk
    ///     reads one state for each of them. A method that gives one element of
    ///     a sequence of exceptions, as <c>First</c> does, gives one of those
    ///     exceptions. Null for each other call.
    /// </summary>
    /// <remarks>
    ///     The walk gives no state to the result of a method of the platform
    ///     whose type is an exception of the platform. But an object of this
    ///     compilation can have that type.
    /// </remarks>
    /// <param name="invocation">The call.</param>
    /// <param name="method">The method of the call.</param>
    private LoopState? ExceptionOfACall(InvocationExpressionSyntax invocation, IMethodSymbol method) =>
        // A test of the name comes before each other test, because each call of the platform comes here.
        method.Name switch
        {
            "Flatten" or "GetBaseException" when invocation.Expression is MemberAccessExpressionSyntax
                {
                    Expression: var receiver
                } => this.Eval(receiver),
            "First" or "FirstOrDefault" or "Last" or "LastOrDefault" or "Single" or "SingleOrDefault" or "ElementAt"
                or "ElementAtOrDefault"
                when method.ReturnType is INamedTypeSymbol element
                     && IsOfAnException(element)
                     && SequenceOf(invocation: invocation, method: method) is { } sequence =>
                this.Eval(sequence).AsOneOfItsElements,
            _ => null
        };

    // The sequence that an extension method gets, also where the code calls it as a static method.
    private static ExpressionSyntax? SequenceOf(InvocationExpressionSyntax invocation, IMethodSymbol method) =>
        method.ReducedFrom != null
            ? (invocation.Expression as MemberAccessExpressionSyntax)?.Expression
            : method.IsExtensionMethod && invocation.ArgumentList.Arguments is { Count: > 0 } arguments
                ? arguments[0].Expression
                : null;

    private static bool IsOfAnException(INamedTypeSymbol type)
    {
        for (INamedTypeSymbol? owner = type; owner != null; owner = owner.BaseType)
        {
            if (owner is
                {
                    Name: "Exception",
                    ContainingNamespace: { Name: "System", ContainingNamespace.IsGlobalNamespace: true }
                })
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    ///     The objects that a call of the platform throws again. Null for a
    ///     call that throws no object that the walk knows.
    /// </summary>
    /// <param name="invocation">The call.</param>
    /// <param name="method">The method of the call.</param>
    private Followed? FaultOfAPlatformCall(InvocationExpressionSyntax invocation, IMethodSymbol method) =>
        method.Name switch
        {
            "GetResult" => this.FaultOfATask(invocation: invocation, method: method),
            "Throw" => this.DispatchedAgain(invocation: invocation, method: method),
            "Wait" => this.FaultOfAWait(invocation: invocation, method: method),
            "Invoke" => this.FaultOfAParallelInvoke(invocation: invocation, method: method),
            _ => null
        };

    /// <summary>
    ///     The object that <c>Throw</c> of <c>ExceptionDispatchInfo</c> throws
    ///     again. The static <c>Throw</c> throws its argument. The other
    ///     <c>Throw</c> throws the argument of the <c>Capture</c> that made the
    ///     receiver. Each one throws as a <c>throw</c> statement does.
    /// </summary>
    /// <remarks>
    ///     The walk knows the object only where the receiver is a call of
    ///     <c>ExceptionDispatchInfo</c>. <c>Capture</c> is the only such method
    ///     that gives an <c>ExceptionDispatchInfo</c>.
    /// </remarks>
    /// <param name="invocation">The call of <c>Throw</c>.</param>
    /// <param name="method">The method of the call.</param>
    private Followed? DispatchedAgain(InvocationExpressionSyntax invocation, IMethodSymbol method)
    {
        if (!IsADispatchInfo(method.ContainingType))
        {
            return null;
        }

        ExpressionSyntax? thrown =
            method.IsStatic
                ? invocation.ArgumentList.Arguments is { Count: 1 } arguments ? arguments[0].Expression : null
                : invocation.Expression is MemberAccessExpressionSyntax
                  {
                      Expression: InvocationExpressionSyntax { ArgumentList.Arguments.Count: 1 } capture
                  }
                  && this.region.SymbolOf(capture) is IMethodSymbol captures
                  && IsADispatchInfo(captures.ContainingType)
                    ? capture.ArgumentList.Arguments[0].Expression
                    : null;

        return thrown == null
            ? null
            : WithThrown(
                followed: Followed.Nothing,
                thrown: ImmutableArray.Create<(SyntaxNode Call, ITypeSymbol? Type, LoopState State)>(
                    (invocation, this.region.TypeOf(thrown), this.Eval(thrown))));

        static bool IsADispatchInfo(INamedTypeSymbol? type) =>
            type is { Name: "ExceptionDispatchInfo", ContainingNamespace.Name: "ExceptionServices" };
    }

    /// <summary>
    ///     The objects that the functions of a task threw, for a call of
    ///     <c>GetResult</c> of the awaiter of the task. The call throws each of
    ///     them again. Null where the task has no function that the walk can
    ///     read.
    /// </summary>
    /// <remarks>
    ///     The value of the call comes from the read of the result of the task.
    ///     The walk reported the reads of the functions where the code ran them.
    ///     Thus, this result has only the objects.
    /// </remarks>
    /// <param name="invocation">The call of <c>GetResult</c>.</param>
    /// <param name="method">The method of the call.</param>
    private Followed? FaultOfATask(InvocationExpressionSyntax invocation, IMethodSymbol method)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax
            {
                Expression: InvocationExpressionSyntax
                {
                    Expression: MemberAccessExpressionSyntax { Name.Identifier.ValueText: "GetAwaiter" } awaiter
                } awaited
            }
            || this.TheTaskOfAnAwaiter(awaited: awaited, awaitable: awaiter.Expression) is not { } task)
        {
            return null;
        }

        ImmutableArray<(SyntaxNode Call, ITypeSymbol? Type, LoopState State)> thrown =
            this.ObjectsOfATask(read: invocation, task: task, value: method.ReturnType);

        return thrown.IsEmpty ? null : WithThrown(followed: Followed.Nothing, thrown: thrown);
    }

    /// <summary>
    ///     True for a task whose result is a task, as the task of
    ///     <c>WhenAny</c> is. An await of it gives the second task.
    /// </summary>
    /// <param name="type">The type of the awaited value.</param>
    public static bool GivesATask(ITypeSymbol? type) =>
        type is INamedTypeSymbol { TypeArguments: { Length: 1 } arguments }
        && SodaFlowSymbols.IsTask(arguments[0] as INamedTypeSymbol);

    /// <summary>
    ///     The state of the values that a method of the platform got. For
    ///     <c>SetException</c> and <c>TrySetException</c> of a
    ///     <c>TaskCompletionSource</c>, that is the task of the source with the
    ///     object in it. A source holds what the walk knows of its task.
    /// </summary>
    /// <remarks>
    ///     Each overload has one parameter, which is an exception or a sequence
    ///     of them. An object of a sequence can be of each type.
    /// </remarks>
    /// <param name="method">The method of the call.</param>
    /// <param name="moved">The state of the values that the method got.</param>
    private static LoopState ObjectsGivenToASource(IMethodSymbol method, LoopState moved) =>
        method is { Name: "SetException" or "TrySetException", Parameters: { Length: 1 } parameters }
            ? LoopState.None.WithFaults(
                ImmutableArray.Create(
                    (SodaFlowSymbols.IsEnumerated(parameters[0].Type) ? null : parameters[0].Type, moved)))
            : moved;

    // WhenAll throws the objects of each task that it gets, and the task of WhenAny is one of the tasks that it gets.
    // ConfigureAwait, AsTask, and WaitAsync give the same task. Only a task has objects of a task, so the walk does not
    // test the type of the method.
    private static bool PassesTheFaults(IMethodSymbol method) =>
        method.Name is "WhenAll" or "WhenAny" or "ConfigureAwait" or "AsTask" or "WaitAsync";

    /// <summary>
    ///     The state of a call of the platform that gives a task with objects
    ///     in it. <c>FromException</c> gives a task that keeps its argument. A
    ///     method that gives a task with the objects of the tasks that it gets,
    ///     as <c>WhenAll</c> does, keeps those objects. Null for each other
    ///     call, and where no task has an object.
    /// </summary>
    /// <param name="invocation">The call.</param>
    /// <param name="method">The method of the call.</param>
    private LoopState? TaskOfACall(InvocationExpressionSyntax invocation, IMethodSymbol method)
    {
        // A test of the name comes before each other test, because each call of the platform comes here.
        ImmutableArray<(ITypeSymbol? Type, LoopState State)> faults =
            method.Name == "FromException"
                ? this.ObjectOfAnArgument(invocation)
                : method.Name == "Run"
                    ? invocation.ArgumentList.Arguments
                        .SelectMany(argument =>
                            this.ObjectsOfAFunction(function: argument.Expression, value: method.ReturnType))
                        .ToImmutableArray()
                : PassesTheFaults(method)
                    ? this.Moved(invocation: invocation, method: method, withReceiver: true).Faults
                    : ImmutableArray<(ITypeSymbol? Type, LoopState State)>.Empty;

        return faults.IsEmpty ? null : this.MovedResult(invocation: invocation, method: method).WithFaults(faults);
    }

    /// <summary>
    ///     The result of the function of a task that the call runs. The task
    ///     keeps each object that the function throws, and the call throws none.
    /// </summary>
    private static Followed? KeptByTheTask(Followed? followed) =>
        followed == null || followed.Thrown.IsEmpty
            ? followed
            : WithThrown(
                followed: followed,
                thrown: ImmutableArray<(SyntaxNode Call, ITypeSymbol? Type, LoopState State)>.Empty);

    private static Followed WithThrown(
        Followed followed,
        ImmutableArray<(SyntaxNode Call, ITypeSymbol? Type, LoopState State)> thrown) =>
        new(
            state: followed.State,
            findings: followed.Findings,
            outs: followed.Outs,
            sets: followed.Sets,
            through: followed.Through,
            alwaysGives: followed.AlwaysGives,
            thrown: thrown);
}
