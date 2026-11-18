using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SodaFlow.Analyzers;

// The part of the evaluator that limits the keys of a location for a read of a member of the result of
// a call. See Construction.StateWithTheAliases and Construction.StateImplementedTogether.
internal sealed partial class LoopStateEvaluator
{
    /// <summary>
    ///     A test of each other key of the location, for a read of a member of
    ///     the result of a call. Null for each other read, which joins each key.
    /// </summary>
    /// <remarks>
    ///     The region keeps one state for each key, for all the objects that the
    ///     walk does not know. Thus, a pending cell of an object of one class can
    ///     go to the read of an object of a different class. When the callee
    ///     returns only an expression of a sealed type, each object of the result
    ///     has that type. A key of a type that the object does not have is then
    ///     not a name of the member that the code reads.
    /// </remarks>
    private Func<ISymbol, bool>? KeysOfTheResult(ExpressionSyntax read) =>
        read is MemberAccessExpressionSyntax { Expression: var owner }
        && Unwrapped(owner) is InvocationExpressionSyntax call
            ? key => this.CanHave(call: call, key: key)
            : null;

    // The test runs only for a key that the region joins, thus the questions to the compiler come
    // after that gate. A structure becomes a new object at each return, which no other write can
    // change.
    private bool CanHave(InvocationExpressionSyntax call, ISymbol key) =>
        this.ReturnedBy(call).Returned is not { } returned
        || this.region.TypeOf(returned) is not INamedTypeSymbol { IsSealed: true } exact
        || Has(type: exact, other: key.ContainingType);

    // True when the type is the other type, derives from it, or implements it. A key is a
    // definition, thus the test compares definitions.
    private static bool Has(ITypeSymbol type, ITypeSymbol other)
    {
        if (other.TypeKind == TypeKind.Interface)
        {
            return type.AllInterfaces.Any(contract =>
                SymbolEqualityComparer.Default.Equals(x: contract.OriginalDefinition, y: other));
        }

        for (ITypeSymbol? own = type; own != null; own = own.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(x: own.OriginalDefinition, y: other))
            {
                return true;
            }
        }

        return false;
    }
}
