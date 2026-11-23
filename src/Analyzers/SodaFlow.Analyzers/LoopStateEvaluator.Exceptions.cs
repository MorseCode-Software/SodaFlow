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
        IPropertySymbol? inner = type == null ? null : InnerExceptionOf(type);

        return WithThrown(
            followed: followed,
            thrown: followed.Thrown
                .Select(one => (
                    one.Call,
                    (ITypeSymbol?)type,
                    type != null && inner != null
                        ? LoopState.None.WithMembers(Construction.Of(type).With(member: inner, state: one.State))
                        : LoopState.None))
                .ToImmutableArray());

        static IPropertySymbol? InnerExceptionOf(INamedTypeSymbol exception)
        {
            for (INamedTypeSymbol? owner = exception; owner != null; owner = owner.BaseType)
            {
                if (owner.GetMembers("InnerException").OfType<IPropertySymbol>().FirstOrDefault() is { } found)
                {
                    return found;
                }
            }

            return null;
        }
    }

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
    private Followed? FaultOfATask(InvocationExpressionSyntax invocation, IMethodSymbol method) =>
        invocation.Expression is MemberAccessExpressionSyntax
        {
            Expression: InvocationExpressionSyntax
            {
                Expression: MemberAccessExpressionSyntax { Name.Identifier.ValueText: "GetAwaiter" } awaiter
            } awaited
        }
        && this.TheTaskOfAnAwaiter(awaited: awaited, awaitable: awaiter.Expression) is { } task
        && this.FollowLazy(
                access: invocation,
                written: task,
                lazy: this.Eval(task).AsOneOfItsElements,
                value: method.ReturnType)
            is { } followed
            ? WithThrown(followed: Followed.Nothing, thrown: followed.Thrown)
            : null;

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
