using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SodaFlow.Analyzers;

/// <summary>
///     A function that a map of a collection runs in its <c>Dispose</c>, as its
///     eviction handler.
/// </summary>
/// <param name="written">The argument of the call of <c>Map</c>.</param>
/// <param name="parameter">The parameter that gets the argument.</param>
/// <param name="handler">The state of the argument.</param>
internal sealed class MapHandler(in ExpressionSyntax written, in IParameterSymbol? parameter, in LoopState handler)
{
    public ExpressionSyntax Written { get; } = written;

    public IParameterSymbol? Parameter { get; } = parameter;

    public LoopState Handler { get; } = handler;
}
