using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SodaFlow.Analyzers;

/// <summary>
///     The members of a compilation that run code where a lambda shows no call.
///     Such a member is a getter with a body, a constructor or an initializer,
///     or an operator. The walk gives a lambda with such a member to the
///     compiler, and drops each other lambda with no call.
/// </summary>
/// <remarks>
///     The walk reads the names in the declarations, with no binding. A name
///     is not a symbol, thus the walk also reads a lambda that names a member of
///     a different type with the same name. That costs time and changes no
///     rule.
/// </remarks>
internal sealed class DeclaredCode
{
    private readonly ImmutableHashSet<string> getters;
    private readonly ImmutableHashSet<string> madeWithCode;
    private readonly ImmutableHashSet<string> operators;

    private DeclaredCode(
        ImmutableHashSet<string> getters,
        ImmutableHashSet<string> madeWithCode,
        ImmutableHashSet<string> operators)
    {
        this.getters = getters;
        this.madeWithCode = madeWithCode;
        this.operators = operators;
    }

    /// <summary>
    ///     Reads the declarations of the types of a compilation. The read does
    ///     not read the body of a member.
    /// </summary>
    /// <param name="compilation">The compilation.</param>
    public static DeclaredCode Of(Compilation compilation)
    {
        ImmutableHashSet<string>.Builder getters = ImmutableHashSet.CreateBuilder<string>();
        ImmutableHashSet<string>.Builder madeWithCode = ImmutableHashSet.CreateBuilder<string>();
        ImmutableHashSet<string>.Builder operators = ImmutableHashSet.CreateBuilder<string>();
        List<(string Name, string[] Bases)> others = [];

        foreach (TypeDeclarationSyntax type in compilation.SyntaxTrees
                     .SelectMany(static tree => tree.GetRoot()
                         .DescendantNodes(static node =>
                             node is CompilationUnitSyntax or BaseNamespaceDeclarationSyntax or TypeDeclarationSyntax))
                     .OfType<TypeDeclarationSyntax>())
        {
            // A base type with arguments runs a constructor of the base. A primary constructor of a
            // class or a struct can give its parameters to initializers.
            bool runsCode =
                type.BaseList?.Types.Any(static baseType => baseType is PrimaryConstructorBaseTypeSyntax) == true;

            foreach (MemberDeclarationSyntax member in type.Members)
            {
                switch (member)
                {
                    case ConstructorDeclarationSyntax constructor
                        when !constructor.Modifiers.Any(SyntaxKind.StaticKeyword):
                        runsCode |= constructor.Body != null
                                    || constructor.ExpressionBody != null
                                    || constructor.Initializer != null;

                        break;
                    case FieldDeclarationSyntax field when !field.Modifiers.Any(SyntaxKind.StaticKeyword):
                        runsCode |= field.Declaration.Variables.Any(static variable => variable.Initializer != null);

                        break;
                    case PropertyDeclarationSyntax property:
                        if (HasCode(
                                expressionBody: property.ExpressionBody,
                                accessors: property.AccessorList,
                                kind: SyntaxKind.GetAccessorDeclaration))
                        {
                            getters.Add(property.Identifier.ValueText);
                        }

                        // An object initializer calls a setter, and an initializer of the property
                        // runs at construction.
                        runsCode |= !property.Modifiers.Any(SyntaxKind.StaticKeyword)
                                    && (property.Initializer != null
                                        || HasCode(
                                            expressionBody: null,
                                            accessors: property.AccessorList,
                                            kind: SyntaxKind.SetAccessorDeclaration)
                                        || HasCode(
                                            expressionBody: null,
                                            accessors: property.AccessorList,
                                            kind: SyntaxKind.InitAccessorDeclaration));

                        break;
                    case OperatorDeclarationSyntax declared:
                        operators.Add(declared.OperatorToken.ValueText);

                        break;
                    case ConversionOperatorDeclarationSyntax:
                        operators.Add("()");

                        break;
                }
            }

            if (runsCode)
            {
                madeWithCode.Add(type.Identifier.ValueText);
            }
            else if (type.BaseList is { } bases)
            {
                others.Add((type.Identifier.ValueText, [.. bases.Types.Select(static each => NameOf(each.Type))]));
            }
        }

        // A type with no code of its own runs the constructor of its base type.
        int found;

        do
        {
            found = others.RemoveAll(type => type.Bases.Any(madeWithCode.Contains) && madeWithCode.Add(type.Name));
        }
        while (found > 0);

        return new DeclaredCode(
            getters: getters.ToImmutable(),
            madeWithCode: madeWithCode.ToImmutable(),
            operators: operators.ToImmutable());
    }

    /// <summary>
    ///     True when a node can run code of the compilation with no call
    ///     syntax. Such a node names a getter, makes a type that runs code, or
    ///     uses an operator.
    /// </summary>
    /// <param name="node">A node of the body of a lambda.</param>
    public bool CanRunCode(SyntaxNode node) =>
        node switch
        {
            IdentifierNameSyntax name => this.getters.Contains(name.Identifier.ValueText),
            ObjectCreationExpressionSyntax creation => this.madeWithCode.Contains(NameOf(creation.Type)),
            ImplicitObjectCreationExpressionSyntax => this.madeWithCode.Count > 0,
            BinaryExpressionSyntax binary => this.operators.Contains(binary.OperatorToken.ValueText),
            PrefixUnaryExpressionSyntax prefix => this.operators.Contains(prefix.OperatorToken.ValueText),
            PostfixUnaryExpressionSyntax postfix => this.operators.Contains(postfix.OperatorToken.ValueText),
            // A compound assignment calls the operator before the equals sign.
            AssignmentExpressionSyntax { OperatorToken.ValueText: { Length: > 1 } compound } =>
                this.operators.Contains(compound.Substring(startIndex: 0, length: compound.Length - 1)),
            CastExpressionSyntax => this.operators.Contains("()"),
            _ => false
        };

    // True when an accessor of the given type has a body. An expression body of the property is
    // its getter.
    private static bool HasCode(
        ArrowExpressionClauseSyntax? expressionBody,
        AccessorListSyntax? accessors,
        SyntaxKind kind) =>
        (kind == SyntaxKind.GetAccessorDeclaration && expressionBody != null)
        || accessors?.Accessors.Any(accessor =>
            accessor.IsKind(kind) && (accessor.Body != null || accessor.ExpressionBody != null)) == true;

    // The name of the type that a creation names, with no namespace and no type arguments.
    private static string NameOf(TypeSyntax type) =>
        type switch
        {
            QualifiedNameSyntax qualified => NameOf(qualified.Right),
            AliasQualifiedNameSyntax alias => NameOf(alias.Name),
            GenericNameSyntax generic => generic.Identifier.ValueText,
            IdentifierNameSyntax name => name.Identifier.ValueText,
            _ => string.Empty
        };
}
