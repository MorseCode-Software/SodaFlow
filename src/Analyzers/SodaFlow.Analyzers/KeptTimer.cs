using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SodaFlow.Analyzers;

/// <summary>
///     A timer that <c>SetTimer</c> of a clock of the library base gave the
///     clock. The clock keeps it until <c>RunTimersTo</c> runs it or the code
///     cancels it.
/// </summary>
/// <param name="set">The call of <c>SetTimer</c>.</param>
/// <param name="time">The time of the timer.</param>
/// <param name="callback">The expression of the callback, where the code gave it.</param>
/// <param name="functions">The state of the callback at the call of <c>SetTimer</c>.</param>
/// <param name="keeper">The walk of the code that called <c>SetTimer</c>.</param>
internal sealed class KeptTimer(
    in SyntaxNode set,
    in ExpressionSyntax time,
    in ExpressionSyntax callback,
    in LoopState functions,
    in SinkWalker keeper)
{
    public SyntaxNode Set { get; } = set;

    public ExpressionSyntax Time { get; } = time;

    public ExpressionSyntax Callback { get; } = callback;

    public LoopState Functions { get; } = functions;

    public SinkWalker Keeper { get; } = keeper;
}
