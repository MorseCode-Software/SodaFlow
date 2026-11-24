using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SodaFlow.Analyzers;

// The part of the evaluator for the owner of a write to a member that the region keeps for the field. A read through a
// different owner does not get the write: see LoopRegion.SetFieldOfAnOwner.
internal sealed partial class LoopStateEvaluator
{
    /// <summary>
    ///     The one owner of a member write or a member read, when the walk knows
    ///     that object. Null when the owner can be an object that the walk does
    ///     not know.
    /// </summary>
    /// <remarks>
    ///     An owner is an element of the array of a static field, where no code
    ///     changes the object of an element: see StaticOwnerOf.
    /// </remarks>
    /// <param name="owner">The expression of the owner, or null.</param>
    private object? KnownOwnerOf(ExpressionSyntax? owner) =>
        owner == null ? null : this.StaticOwnerOf(Unwrapped(owner));

    // The owner of a read of a member: the expression before the name.
    private object? OwnerOfARead(ExpressionSyntax read) =>
        read is MemberAccessExpressionSyntax { Expression: var owner } ? this.KnownOwnerOf(owner) : null;

    // An element of the array of a static field whose objects no code changes, at a constant index: see
    // UnchangedStatics.
    private StaticOwner? StaticOwnerOf(SyntaxNode written) =>
        written is ElementAccessExpressionSyntax { ArgumentList.Arguments: { Count: 1 } arguments } element
        && Unwrapped(element.Expression) is (IdentifierNameSyntax or MemberAccessExpressionSyntax) and var named
        && this.TheArrayOfAStaticField(named) is { } field
        && this.region.Symbols.Unchanged.CreationsIn(field) is { IsEmpty: false } creations
        && this.ConstantIndexOf(arguments[0]) is { } index
        && index < creations.Length
            ? new StaticOwner(field: field, index: index)
            : null;

    // The static field that a name of an array names. A local whose declaration gets the field, and whose each use only
    // reads an element, names the field also: see UnchangedStatics.LocalOfOnlyReads.
    private IFieldSymbol? TheArrayOfAStaticField(SyntaxNode named) =>
        this.region.SymbolOf(named) switch
        {
            IFieldSymbol { IsStatic: true, Type: IArrayTypeSymbol { Rank: 1 } } field => field,
            ILocalSymbol { RefKind: RefKind.None, Type: IArrayTypeSymbol { Rank: 1 } } local
                when local.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax()
                         is VariableDeclaratorSyntax { Initializer.Value: { } value }
                     && Unwrapped(value) is (IdentifierNameSyntax or MemberAccessExpressionSyntax) and var initial
                     && this.region.SymbolOf(initial)
                         is IFieldSymbol { IsStatic: true, Type: IArrayTypeSymbol { Rank: 1 } } field => field,
            _ => null
        };
}
