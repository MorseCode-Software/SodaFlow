using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SodaFlow.Analyzers;

// The part of the walker for the nodes of System.Text.Json.Nodes. A JsonValue keeps an object of the
// code. When the code writes the node, the node calls the getters of that object.
internal sealed partial class SinkWalker
{
    /// <summary>
    ///     Reads a write of a node of <c>System.Text.Json.Nodes</c> as a call of
    ///     the serializer with the node. The node has the state of each value
    ///     that <c>JsonValue.Create</c> kept in it. The result is false for each
    ///     other method.
    /// </summary>
    /// <remarks>
    ///     A JsonValue writes its value as the type argument of the creation.
    ///     The state of the node does not know that type. Thus, the walk reads
    ///     the members of the type of the object, which is the careful side.
    /// </remarks>
    private bool WritesANode(
        IMethodSymbol method,
        IMethodSymbol callee,
        ExpressionSyntax receiver,
        SeparatedSyntaxList<ArgumentSyntax> arguments)
    {
        if (!SodaFlowSymbols.WritesAJsonNode(callee))
        {
            return false;
        }

        this.Serialize(
            written: receiver,
            state: this.evaluator.Eval(receiver),
            serializer: callee,
            options: SerializerOptions.Of(region: this.region, method: method, arguments: arguments),
            member: null,
            declared: null,
            depth: 0,
            seen: new HashSet<(Construction, INamedTypeSymbol?)>(SerializedObjects.Instance));

        return true;
    }
}
