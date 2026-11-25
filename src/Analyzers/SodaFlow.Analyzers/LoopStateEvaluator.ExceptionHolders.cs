using System.Linq;
using Microsoft.CodeAnalysis;

namespace SodaFlow.Analyzers;

// The part of the evaluator for a value of the platform that holds exceptions, such as an ImmutableArray of exceptions.
internal sealed partial class LoopStateEvaluator
{
    // True for an array of exceptions, and for a generic type with an exception as a type argument. An exception of
    // the platform cannot hold a state by its type. But an exception of this compilation can have that type, and an
    // exception of the platform can hold such an object. Thus, a method of the platform that gives such a value keeps
    // the objects that it got. Each call of the platform whose result holds no state comes here. Thus, the test reads
    // only the types in the type of the result.
    private static bool HoldsExceptions(ITypeSymbol type) =>
        type switch
        {
            IArrayTypeSymbol { ElementType: INamedTypeSymbol element } => IsOfAnException(element),
            INamedTypeSymbol { IsGenericType: true } named => named.TypeArguments.Any(static argument =>
                argument is INamedTypeSymbol each && IsOfAnException(each)),
            _ => false
        };

    // True for a member of a generic type whose type is an exception that a type argument names, as Result of a
    // Task<Exception> is. The member gives one of the values that its owner holds. A member of an exception type of
    // its own, as Exception of a task, is not such a member.
    private static bool GivesAnExceptionThatItHolds(ISymbol member, ITypeSymbol type) =>
        type is INamedTypeSymbol named
        && IsOfAnException(named)
        && member.OriginalDefinition is IPropertySymbol { Type: ITypeParameterSymbol }
            or IFieldSymbol { Type: ITypeParameterSymbol };
}
