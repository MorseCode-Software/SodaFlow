using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SodaFlow.Analyzers;

/// <summary>
///     The fields of a compilation that no code gives a different object after
///     the initializer. For an array in a static field, no code gives an element
///     a different object either. The search reads the names in the code of the
///     type only. Only that code can use a private member or give a readonly
///     field a value.
/// </summary>
/// <remarks>
///     The search reads syntax only. A use of a different symbol with the same
///     name counts as a use of the field. Thus, such a use can make the answer
///     false, and never true.
/// </remarks>
internal sealed class UnchangedStatics
{
    // The names in the code of each type, for each text. The first question about a field of a type makes them.
    private readonly ConcurrentDictionary<INamedTypeSymbol, ILookup<string, IdentifierNameSyntax>> namesIn =
        new(SymbolEqualityComparer.Default);

    private readonly ConcurrentDictionary<ISymbol, ImmutableArray<BaseObjectCreationExpressionSyntax>> elements =
        new(SymbolEqualityComparer.Default);

    /// <summary>
    ///     The creation of each element of the array of a static field, in
    ///     order. Empty when code can give the field or an element a different
    ///     object, or when an element of the initializer is not a creation.
    /// </summary>
    /// <remarks>
    ///     Each creation makes a different object. Thus, two elements are never
    ///     one object, and an element keeps the object of its creation.
    /// </remarks>
    public ImmutableArray<BaseObjectCreationExpressionSyntax> CreationsIn(IFieldSymbol field) =>
        field is { IsStatic: true, DeclaredAccessibility: Accessibility.Private, Type: IArrayTypeSymbol { Rank: 1 } }
            ? this.elements.GetOrAdd(key: field.OriginalDefinition, valueFactory: _ => this.FindTheCreationsIn(field))
            : ImmutableArray<BaseObjectCreationExpressionSyntax>.Empty;

    private readonly ConcurrentDictionary<ISymbol, BaseObjectCreationExpressionSyntax?> objects =
        new(SymbolEqualityComparer.Default);

    /// <summary>
    ///     The creation in the initializer of a static field that is not an
    ///     array, where no code gives the field a different object. Null for each
    ///     other field.
    /// </summary>
    /// <remarks>
    ///     A field with readonly gets a different object only in a static
    ///     constructor. Thus, a static constructor must not name it. Each other
    ///     field must be private, and each use of it must be a read.
    /// </remarks>
    public BaseObjectCreationExpressionSyntax? CreationOf(IFieldSymbol field) =>
        field is { IsStatic: true, Type: not IArrayTypeSymbol }
        && (field.IsReadOnly || field.DeclaredAccessibility == Accessibility.Private)
            ? this.objects.GetOrAdd(key: field.OriginalDefinition, valueFactory: _ => this.FindTheCreationOf(field))
            : null;

    private BaseObjectCreationExpressionSyntax? FindTheCreationOf(IFieldSymbol field)
    {
        if (field.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax()
            is not VariableDeclaratorSyntax { Initializer.Value: BaseObjectCreationExpressionSyntax creation })
        {
            return null;
        }

        bool keepsIt =
            field.IsReadOnly
                ? field.ContainingType.StaticConstructors
                    .SelectMany(static constructor => constructor.DeclaringSyntaxReferences)
                    .SelectMany(static declared => declared.GetSyntax().DescendantNodes())
                    .OfType<IdentifierNameSyntax>()
                    .All(name => name.Identifier.ValueText != field.Name)
                : this.UsesOf(field)
                    .All(static use =>
                    {
                        ExpressionSyntax named =
                            use.Parent is MemberAccessExpressionSyntax access && access.Name == use ? access : use;

                        return named.Parent is not RefExpressionSyntax && !IsWritten(named);
                    });

        return keepsIt ? creation : null;
    }

    private readonly ConcurrentDictionary<ISymbol, bool> keepTheirObjects = new(SymbolEqualityComparer.Default);

    /// <summary>
    ///     True for a readonly field of an object whose initializer makes its
    ///     object with <c>new</c>. No constructor of its type can give it a
    ///     different value. Each object then keeps the object of its creation in
    ///     the field.
    /// </summary>
    /// <remarks>
    ///     Only a constructor of the type can give a readonly field a value. A
    ///     constructor can read the field, as in a build from it.
    /// </remarks>
    public bool KeepsItsObject(IFieldSymbol field) =>
        field is { IsStatic: false, IsReadOnly: true }
        && this.keepTheirObjects.GetOrAdd(key: field.OriginalDefinition, valueFactory: _ => FindIfItKeeps(field));

    private static bool FindIfItKeeps(ISymbol field) =>
        field.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax()
            is VariableDeclaratorSyntax { Initializer.Value: BaseObjectCreationExpressionSyntax }
        && field.ContainingType.InstanceConstructors
            .SelectMany(static constructor => constructor.DeclaringSyntaxReferences)
            .SelectMany(static declared => declared.GetSyntax().DescendantNodes())
            .OfType<IdentifierNameSyntax>()
            .Where(name => name.Identifier.ValueText == field.Name)
            .All(static use =>
            {
                ExpressionSyntax named =
                    use.Parent is MemberAccessExpressionSyntax access && access.Name == use ? access : use;

                return named.Parent is not RefExpressionSyntax && !IsWritten(named);
            });

    private ImmutableArray<BaseObjectCreationExpressionSyntax> FindTheCreationsIn(ISymbol field)
    {
        IEnumerable<ExpressionSyntax>? written =
            field.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() switch
            {
                VariableDeclaratorSyntax { Initializer.Value: { } value } => value switch
                {
                    CollectionExpressionSyntax collection =>
                        collection.Elements.All(static element => element is ExpressionElementSyntax)
                            ? collection.Elements.Cast<ExpressionElementSyntax>()
                                .Select(static element => element.Expression)
                            : null,
                    ArrayCreationExpressionSyntax { Initializer: { } initializer } => initializer.Expressions,
                    ImplicitArrayCreationExpressionSyntax { Initializer: var initializer } => initializer.Expressions,
                    InitializerExpressionSyntax { RawKind: (int)SyntaxKind.ArrayInitializerExpression } initializer =>
                        initializer.Expressions,
                    _ => null
                },
                _ => null
            };

        ImmutableArray<BaseObjectCreationExpressionSyntax> creations =
            written?.Select(static element => element as BaseObjectCreationExpressionSyntax).ToList() is { } all
            && all.All(static creation => creation != null)
                ? all.Cast<BaseObjectCreationExpressionSyntax>().ToImmutableArray()
                : ImmutableArray<BaseObjectCreationExpressionSyntax>.Empty;

        return !creations.IsEmpty
               && this.UsesOf(field).All(static use => IsARead(ElementRead(use)) || IsALocalOfOnlyReads(use))
            ? creations
            : ImmutableArray<BaseObjectCreationExpressionSyntax>.Empty;
    }

    /// <summary>
    ///     The declaration of a local that gets the array of the field, where
    ///     each use of the local only reads an element. The local then names the
    ///     array of the field at each use. Null for each other expression.
    /// </summary>
    private static VariableDeclaratorSyntax? LocalOfOnlyReads(SyntaxNode value) =>
        value.Parent is EqualsValueClauseSyntax
        {
            Parent: VariableDeclaratorSyntax { Parent.Parent: LocalDeclarationStatementSyntax } local
        }
        && CodeOf(local) is { } code
        && code.DescendantNodes()
            .OfType<IdentifierNameSyntax>()
            .Where(name => name.Identifier.ValueText == local.Identifier.ValueText)
            .All(static name => IsARead(ElementRead(name)))
            ? local
            : null;

    private static bool IsALocalOfOnlyReads(ExpressionSyntax use) =>
        LocalOfOnlyReads(use.Parent is MemberAccessExpressionSyntax access && access.Name == use ? access : use)
            != null;

    // The function that declares a local.
    private static SyntaxNode? CodeOf(SyntaxNode local) =>
        local.FirstAncestorOrSelf<SyntaxNode>(static code =>
            code is BaseMethodDeclarationSyntax
                or AccessorDeclarationSyntax
                or AnonymousFunctionExpressionSyntax
                or LocalFunctionStatementSyntax);

    // Each name in the code of the type of the field with the text of the name of the field.
    private IEnumerable<IdentifierNameSyntax> UsesOf(ISymbol field) =>
        this.namesIn.GetOrAdd(key: field.ContainingType.OriginalDefinition, valueFactory: NamesIn)[field.Name];

    private static ILookup<string, IdentifierNameSyntax> NamesIn(INamedTypeSymbol type) =>
        type.DeclaringSyntaxReferences
            .SelectMany(static reference => reference.GetSyntax().DescendantNodes())
            .OfType<IdentifierNameSyntax>()
            .ToLookup(static name => name.Identifier.ValueText);

    // The element access of a use of the field, as in Field[1] or Owner.Field[1]. Null for each other use.
    private static ElementAccessExpressionSyntax? ElementRead(ExpressionSyntax use)
    {
        ExpressionSyntax named =
            use.Parent is MemberAccessExpressionSyntax access && access.Name == use ? access : use;

        return named.Parent is ElementAccessExpressionSyntax element && element.Expression == named ? element : null;
    }

    // True for an element that the code only reads. A ref local of it is a read when each use of the local is the owner
    // of a member, as in slot.Share.
    private static bool IsARead(SyntaxNode? element)
    {
        if (element == null || IsWritten(element))
        {
            return false;
        }

        if (element.Parent is not RefExpressionSyntax reference)
        {
            return true;
        }

        if (reference.Parent is not EqualsValueClauseSyntax { Parent: VariableDeclaratorSyntax local }
            || CodeOf(local) is not { } code)
        {
            return false;
        }

        return code.DescendantNodes()
            .OfType<IdentifierNameSyntax>()
            .Where(name => name.Identifier.ValueText == local.Identifier.ValueText)
            .All(static name => name.Parent is MemberAccessExpressionSyntax owner && owner.Expression == name);
    }

    /// <summary>
    ///     True for an expression that gets a value. It is the target of an
    ///     assignment, also in a tuple, or of an increment. A ref or an out
    ///     argument gets a value also.
    /// </summary>
    public static bool IsWritten(SyntaxNode expression)
    {
        SyntaxNode at = expression;

        while (at.Parent is ParenthesizedExpressionSyntax
               or ArgumentSyntax { Parent: TupleExpressionSyntax }
               or TupleExpressionSyntax)
        {
            at = at.Parent;
        }

        return at.Parent switch
        {
            AssignmentExpressionSyntax assignment => assignment.Left == at,
            PrefixUnaryExpressionSyntax or PostfixUnaryExpressionSyntax => true,
            ArgumentSyntax argument => !argument.RefKindKeyword.IsKind(SyntaxKind.None),
            _ => false
        };
    }
}
