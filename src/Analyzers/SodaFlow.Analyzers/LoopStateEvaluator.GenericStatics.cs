using System.Linq;
using Microsoft.CodeAnalysis;

namespace SodaFlow.Analyzers;

// The part of the evaluator for a static member of a generic type. Each closed type has its own location of the member:
// see LoopRegion.ClosedKeyOf.
internal sealed partial class LoopStateEvaluator
{
    // The static member of the closed type that the call of this code gives. It is for a member of a generic type whose
    // type argument is a type parameter of this code. Each other member is as it is.
    private ISymbol InTheTypeOfTheCall(ISymbol member)
    {
        if (member is not { IsStatic: true, ContainingType: { IsGenericType: true } type }
            || this.call is not { TypeArguments.Count: > 0 } call
            || LoopRegion.ClosedKeyOf(member) != null)
        {
            return member;
        }

        ITypeSymbol[] given =
        [
            .. type.TypeArguments.Select(argument =>
                argument is ITypeParameterSymbol parameter
                && call.TypeArguments.TryGetValue(key: parameter, value: out ITypeSymbol? actual)
                    ? actual
                    : argument)
        ];

        return type.OriginalDefinition.Construct(given)
                .GetMembers(member.Name)
                .FirstOrDefault(found =>
                    SymbolEqualityComparer.Default.Equals(x: found.OriginalDefinition, y: member.OriginalDefinition))
            ?? member;
    }

    // True for a static field with one location: a field of a type that is not generic, or of a closed type.
    private bool HasOneLocation(ISymbol member) =>
        !IsInAGenericType(member.ContainingType) || LoopRegion.ClosedKeyOf(this.InTheTypeOfTheCall(member)) != null;

    private static bool IsInAGenericType(INamedTypeSymbol? type) =>
        type != null && (type.IsGenericType || IsInAGenericType(type.ContainingType));
}
