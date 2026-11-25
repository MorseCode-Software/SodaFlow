using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SodaFlow.Analyzers;

// The part of the walker for the sequence that an await foreach enumerates through ConfigureAwait or WithCancellation
// of the platform. Each of them gives a struct whose enumerator calls the members of the sequence that it got.
internal sealed partial class SinkWalker
{
    /// <summary>
    ///     The sequence that a foreach enumerates. For an await foreach, it is
    ///     the sequence in each <c>ConfigureAwait</c> and
    ///     <c>WithCancellation</c> of the platform. Otherwise, it is the
    ///     expression of the foreach.
    /// </summary>
    /// <param name="node">The foreach statement.</param>
    private ExpressionSyntax SequenceOf(CommonForEachStatementSyntax node)
    {
        ExpressionSyntax sequence = node.Expression;

        while (node.AwaitKeyword.IsKind(SyntaxKind.AwaitKeyword)
               && sequence is InvocationExpressionSyntax
               {
                   Expression: MemberAccessExpressionSyntax
                   {
                       Name.Identifier.ValueText: "ConfigureAwait" or "WithCancellation"
                   } access
               } call
               && this.region.SymbolOf(call) is IMethodSymbol
               {
                   ContainingType.Name: "TaskAsyncEnumerableExtensions" or "ConfiguredCancelableAsyncEnumerable"
               })
        {
            sequence = access.Expression;
        }

        return sequence;
    }

    /// <summary>
    ///     The methods that a foreach calls on the sequence and on its
    ///     enumerator. For the sequence in a <c>ConfigureAwait</c> or a
    ///     <c>WithCancellation</c>, they are the members of
    ///     <c>IAsyncEnumerable</c>, <c>IAsyncEnumerator</c>, and
    ///     <c>IAsyncDisposable</c>. The struct of the platform calls those
    ///     members.
    /// </summary>
    /// <param name="node">The foreach statement.</param>
    /// <param name="sequence">The sequence that <see cref="SequenceOf" /> gives.</param>
    private (IMethodSymbol? Start, IMethodSymbol? Next, IPropertySymbol? Current, IMethodSymbol? Dispose) CallsOf(
        CommonForEachStatementSyntax node,
        ExpressionSyntax sequence)
    {
        if (sequence == node.Expression)
        {
            ForEachStatementInfo calls = this.region.ModelFor(node).GetForEachStatementInfo(node);

            return (calls.GetEnumeratorMethod, calls.MoveNextMethod, calls.CurrentProperty, calls.DisposeMethod);
        }

        IMethodSymbol? start =
            this.region.TypeOf(sequence) is INamedTypeSymbol type
                ? type.AllInterfaces.Add(type)
                    .Where(SodaFlowSymbols.IsAsyncEnumerated)
                    .SelectMany(static enumerable => enumerable.GetMembers("GetAsyncEnumerator"))
                    .OfType<IMethodSymbol>()
                    .FirstOrDefault()
                : null;

        return start == null
            ? (null, null, null, null)
            : (
                start,
                start.ReturnType.GetMembers("MoveNextAsync").OfType<IMethodSymbol>().FirstOrDefault(),
                start.ReturnType.GetMembers("Current").OfType<IPropertySymbol>().FirstOrDefault(),
                this.region.Symbols.AsyncDisposableDisposeAsync);
    }
}
