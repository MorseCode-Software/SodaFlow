using System.Linq;
using Microsoft.CodeAnalysis;

namespace SodaFlow.Analyzers;

// The part of the evaluator for a callee whose result can hold a function.
internal sealed partial class LoopStateEvaluator
{
    // True for a delegate type, and for a tuple type with a delegate element. A callee with no state from the loop
    // can return a lambda there. A call of that lambda can write a local of the callee.
    private static bool CanReturnAFunction(ITypeSymbol type) =>
        type.TypeKind == TypeKind.Delegate
        || (type is INamedTypeSymbol { IsTupleType: true } tuple
            && tuple.TupleElements.Any(static element => element.Type.TypeKind == TypeKind.Delegate));
}
