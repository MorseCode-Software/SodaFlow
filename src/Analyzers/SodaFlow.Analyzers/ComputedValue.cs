using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
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
/// <param name="manyCells">
///     True when the build can run two or more times for one run of the code
///     that the walk reads. This object then stands for each cell that the
///     build makes.
/// </param>
internal sealed class ComputedValue(
    in InvocationExpressionSyntax build,
    in LoopStateEvaluator builder,
    in ImmutableArray<(ExpressionSyntax Written, LoopState Functions)> functions,
    in LoopState given,
    in bool manyTimes,
    in bool writes,
    in bool manyCells)
{
    public InvocationExpressionSyntax Build { get; } = build;

    public LoopStateEvaluator Builder { get; } = builder;

    public ImmutableArray<(ExpressionSyntax Written, LoopState Functions)> Functions { get; } = functions;

    public LoopState Given { get; } = given;

    public bool ManyTimes { get; } = manyTimes;

    public bool Writes { get; } = writes;

    public bool ManyCells { get; } = manyCells;

    // For a build that makes two or more cells, each name through which a read ran the functions.
    private List<ReadKey>? runsAt;

    // The number of turn marks of the loops that the walk reads now. See RunsInATurn.
    private int turns;

    // True after a read that the walk knows to run the functions.
    private bool hasRun;

    /// <summary>
    ///     True after a read that the walk knows to run the functions. Also true
    ///     after such a read in each turn of a loop that the walk reads now.
    /// </summary>
    private bool HasRunNow => this.hasRun || this.turns > 0;

    /// <summary>Records a read that runs the functions.</summary>
    public void Runs() => this.hasRun = true;

    /// <summary>
    ///     True after a read that the walk knows to run the functions. For a
    ///     build that makes two or more cells, the read was through the given
    ///     name.
    /// </summary>
    public bool HasRunAt(ReadKey? key) =>
        this.ManyCells ? key != null && this.runsAt?.Exists(key.IsTheSameAs) == true : this.HasRunNow;

    /// <summary>Records a read through a name that runs the functions of one of the cells.</summary>
    public void RunsAt(ReadKey key) => (this.runsAt ??= []).Add(key);

    /// <summary>Removes each record of a read through a name that now holds a different cell.</summary>
    public void Forgets(ISymbol name) =>
        this.runsAt?.RemoveAll(key => SymbolEqualityComparer.Default.Equals(x: key.Name, y: name));

    /// <summary>
    ///     Records a read that runs the functions in each turn of a loop. The
    ///     walk is then in the loop. Until the end of the loop, the walk reads
    ///     each next read in that turn or in a turn after it.
    /// </summary>
    /// <remarks>
    ///     The loop can have no turn. Thus, the walker removes the mark with
    ///     <see cref="LeavesATurn" /> at the end of the loop.
    /// </remarks>
    public void RunsInATurn() => this.turns++;

    /// <summary>Removes a mark of <see cref="RunsInATurn" />.</summary>
    public void LeavesATurn() => this.turns--;
}
