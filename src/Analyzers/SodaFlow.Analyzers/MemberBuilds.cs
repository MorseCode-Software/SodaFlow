using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SodaFlow.Analyzers;

/// <summary>
///     The assignments in the code of a type that can give a member a function.
///     A <c>Map</c>, a <c>Lazy</c>, and a lambda are such values. The walk
///     reads the value of such an assignment where it reads a member whose
///     value it does not know.
/// </summary>
/// <remarks>
///     The walk reads the names in the code, with no binding. Thus, a local
///     with the name of a member also gives an assignment. That costs time,
///     and the walk reads the value for the member that the read names.
/// </remarks>
internal static class MemberBuilds
{
    // The assignments of each type declaration, while its tree is alive. The IDE makes a new
    // compilation after each edit, and the timing tool makes the symbols again for each file. A
    // declaration does not change, thus its assignments do not change.
    //
    // The table is static state. The rules of this code accept static state only for a good cause.
    // The cause is that Roslyn gives an analyzer no other location to keep the part of a tree from
    // one compilation to the next. The table holds a part only while something else holds the tree.
    private static readonly ConditionalWeakTable<TypeDeclarationSyntax, Part> Parts = new();

    /// <summary>
    ///     The values that the code of a type gives a member of this, where the
    ///     value can be a function. The code of a derived type can give a value
    ///     to a member of its base type.
    /// </summary>
    /// <param name="member">A member that is not static.</param>
    /// <param name="type">
    ///     The type of the object. The search starts at this type, and stops at
    ///     the type that declares the member.
    /// </param>
    public static ImmutableArray<ExpressionSyntax> Of(ISymbol member, ITypeSymbol? type)
    {
        INamedTypeSymbol declares = member.ContainingType.OriginalDefinition;
        ImmutableArray<ExpressionSyntax> all = ImmutableArray<ExpressionSyntax>.Empty;

        // A name can give a delegate member a method group or the delegate of a different name.
        bool takesNames =
            member is IFieldSymbol { Type.TypeKind: TypeKind.Delegate }
                or IPropertySymbol { Type.TypeKind: TypeKind.Delegate };

        for (INamedTypeSymbol? each = (type as INamedTypeSymbol)?.OriginalDefinition ?? declares;
             each != null;
             each = each.BaseType?.OriginalDefinition)
        {
            foreach (SyntaxReference reference in each.DeclaringSyntaxReferences)
            {
                if (reference.GetSyntax() is not TypeDeclarationSyntax code)
                {
                    continue;
                }

                Part part = Parts.GetValue(key: code, createValueCallback: static part => PartOf(part));

                if (part.Values.TryGetValue(key: member.Name, value: out ImmutableArray<ExpressionSyntax> values))
                {
                    all = all.AddRange(values);
                }

                if (takesNames
                    && part.Names.TryGetValue(key: member.Name, value: out ImmutableArray<ExpressionSyntax> names))
                {
                    all = all.AddRange(names);
                }
            }

            if (SymbolEqualityComparer.Default.Equals(x: each, y: declares))
            {
                break;
            }
        }

        return all;
    }

    // Reads the code of the members of one type declaration, and not that of a nested type.
    private static Part PartOf(TypeDeclarationSyntax type)
    {
        Dictionary<string, ImmutableArray<ExpressionSyntax>.Builder> values = [];
        Dictionary<string, ImmutableArray<ExpressionSyntax>.Builder> names = [];

        // A nested type has members of its own, with names that can be the same.
        foreach (MemberDeclarationSyntax code in type.Members.Where(static member =>
                     member is not BaseTypeDeclarationSyntax))
        {
            foreach ((string name, ExpressionSyntax given) in WritesIn(code))
            {
                (ExpressionSyntax value, bool onlyAName) = ValueOf(code: code, value: given);
                Dictionary<string, ImmutableArray<ExpressionSyntax>.Builder> to = onlyAName ? names : values;

                if (!to.TryGetValue(key: name, value: out ImmutableArray<ExpressionSyntax>.Builder? of))
                {
                    of = ImmutableArray.CreateBuilder<ExpressionSyntax>();
                    to[name] = of;
                }

                of.Add(value);
            }
        }

        return new Part(values: Built(values), names: Built(names));
    }

    // Each member of this that the code of a member writes, with the value of the write. An
    // initializer, an assignment, a part of a deconstruction, and Interlocked.Exchange or
    // CompareExchange each give a value.
    private static IEnumerable<(string Name, ExpressionSyntax Value)> WritesIn(SyntaxNode code)
    {
        switch (code)
        {
            case FieldDeclarationSyntax field:
                foreach (VariableDeclaratorSyntax variable in field.Declaration.Variables)
                {
                    if (variable.Initializer is { Value: var initial })
                    {
                        yield return (variable.Identifier.ValueText, initial);
                    }
                }

                break;
            case PropertyDeclarationSyntax { Initializer.Value: var initial } property:
                yield return (property.Identifier.ValueText, initial);

                break;
        }

        foreach (SyntaxNode node in code.DescendantNodes())
        {
            switch (node)
            {
                case AssignmentExpressionSyntax
                    {
                        RawKind: (int)SyntaxKind.SimpleAssignmentExpression,
                        Left: TupleExpressionSyntax parts,
                        Right: TupleExpressionSyntax given
                    }
                    when parts.Arguments.Count == given.Arguments.Count:
                    for (int i = 0; i < parts.Arguments.Count; i++)
                    {
                        if (NameOnThis(parts.Arguments[i].Expression) is { } part)
                        {
                            yield return (part, given.Arguments[i].Expression);
                        }
                    }

                    break;
                // A `+=` gives a delegate one more function.
                case AssignmentExpressionSyntax assignment
                    when assignment.Kind() is SyntaxKind.SimpleAssignmentExpression
                             or SyntaxKind.CoalesceAssignmentExpression
                             or SyntaxKind.AddAssignmentExpression
                         && NameOnThis(assignment.Left) is { } name:
                    yield return (name, assignment.Right);

                    break;
                // Exchange and CompareExchange put their second argument into the first one.
                case InvocationExpressionSyntax
                    {
                        Expression: MemberAccessExpressionSyntax
                        {
                            Name.Identifier.ValueText: "Exchange" or "CompareExchange"
                        },
                        ArgumentList.Arguments: { Count: >= 2 } arguments
                    }
                    when arguments[0].RefKindKeyword.IsKind(SyntaxKind.RefKeyword)
                         && NameOnThis(arguments[0].Expression) is { } exchanged:
                    yield return (exchanged, arguments[1].Expression);

                    break;
            }
        }
    }

    private static ImmutableDictionary<string, ImmutableArray<ExpressionSyntax>> Built(
        Dictionary<string, ImmutableArray<ExpressionSyntax>.Builder> values) =>
        values.ToImmutableDictionary(
            keySelector: static pair => pair.Key,
            elementSelector: static pair => pair.Value.ToImmutable());

    // The name of the member of this that an assignment writes. Null for each other target.
    private static string? NameOnThis(ExpressionSyntax target) =>
        target switch
        {
            MemberAccessExpressionSyntax
            {
                Expression: ThisExpressionSyntax or BaseExpressionSyntax,
                Name: IdentifierNameSyntax name
            } => name.Identifier.ValueText,
            IdentifierNameSyntax name => name.Identifier.ValueText,
            _ => null
        };

    // The value of an assignment. A local gives the value of its declaration in the same member,
    // also through more locals. A value that is only a name can be a method group. Each other
    // value can be a function also, as a cast or a conditional of a method group is.
    private static (ExpressionSyntax Value, bool OnlyAName) ValueOf(SyntaxNode code, ExpressionSyntax value)
    {
        HashSet<string> seen = [];

        while (value is IdentifierNameSyntax local
               && seen.Add(local.Identifier.ValueText)
               && code.DescendantNodes()
                       .OfType<VariableDeclaratorSyntax>()
                       .FirstOrDefault(declared => declared.Identifier.ValueText == local.Identifier.ValueText)
                       ?.Initializer?.Value
                   is { } declared)
        {
            value = declared;
        }

        return (value, value is SimpleNameSyntax or MemberAccessExpressionSyntax);
    }

    // The assignments of one type declaration, by the name of the member. A value that is only a
    // name is in Names, because only a delegate member takes it.
    private sealed class Part(
        in ImmutableDictionary<string, ImmutableArray<ExpressionSyntax>> values,
        in ImmutableDictionary<string, ImmutableArray<ExpressionSyntax>> names)
    {
        public ImmutableDictionary<string, ImmutableArray<ExpressionSyntax>> Values { get; } = values;

        public ImmutableDictionary<string, ImmutableArray<ExpressionSyntax>> Names { get; } = names;
    }
}
