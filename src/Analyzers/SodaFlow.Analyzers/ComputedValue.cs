using System.Collections.Immutable;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SodaFlow.Analyzers;

/// <summary>
///     The functions that calculate the value of a cell at the first read of
///     the cell. A map, a lift, a view, and a deferred lazy sample have them.
/// </summary>
/// <remarks>
///     The cell is an identity, as a query is. Each name of the cell has this
///     object in its state. The functions run at the first read only, and the
///     cell keeps the value after that read. A read through a second name is
///     then no first read.
/// </remarks>
/// <param name="build">The call that makes the cell.</param>
/// <param name="builder">
///     The evaluator of the code that makes the cell. The functions read the
///     locals of that code as they are at the read.
/// </param>
/// <param name="functions">
///     Each argument that gives the operator a function that calculates the
///     value, with the state of that argument.
/// </param>
/// <param name="given">The state that each parameter of a function gets.</param>
/// <param name="manyTimes">True when the cell can run a function two or more times at the read.</param>
/// <param name="writes">True when a lambda of the functions gives a location a value.</param>
internal sealed class ComputedValue(
    in InvocationExpressionSyntax build,
    in LoopStateEvaluator builder,
    in ImmutableArray<(ExpressionSyntax Written, LoopState Functions)> functions,
    in LoopState given,
    in bool manyTimes,
    in bool writes)
{
    public InvocationExpressionSyntax Build { get; } = build;

    public LoopStateEvaluator Builder { get; } = builder;

    public ImmutableArray<(ExpressionSyntax Written, LoopState Functions)> Functions { get; } = functions;

    public LoopState Given { get; } = given;

    public bool ManyTimes { get; } = manyTimes;

    public bool Writes { get; } = writes;

    /// <summary>True after a read that the walk knows to run the functions.</summary>
    public bool HasRun { get; private set; }

    /// <summary>Records a read that runs the functions.</summary>
    public void Runs() => this.HasRun = true;
}
