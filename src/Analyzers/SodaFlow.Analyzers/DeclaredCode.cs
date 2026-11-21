using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.CompilerServices;
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
    // The key of operators for an implicit conversion, which has no operator token.
    private const string ImplicitConversion = "implicit";

    // The part of each syntax tree, while the tree is alive. The IDE makes a new compilation after
    // each edit, and the timing tool makes the symbols again for each file. Without this table, each
    // new compilation reads all its trees again, also the trees that did not change.
    //
    // The table is static state. The rules of this code accept static state only for a good cause.
    // The cause is that Roslyn gives an analyzer no other location to keep the part of a tree from
    // one compilation to the next. A tree does not change, thus its part does not change. The table
    // holds a part only while something else holds the tree.
    private static readonly ConditionalWeakTable<SyntaxTree, TreePart> Parts = new();

    private readonly ImmutableHashSet<string> getters;
    private readonly ImmutableHashSet<string> madeWithCode;
    private readonly ImmutableHashSet<string> operators;

    // True when the compilation declares an implicit conversion. No syntax shows where such a
    // conversion runs, thus each name of a value can run one.
    private readonly bool convertsImplicitly;

    private DeclaredCode(
        ImmutableHashSet<string> getters,
        ImmutableHashSet<string> madeWithCode,
        ImmutableHashSet<string> operators)
    {
        this.getters = getters;
        this.madeWithCode = madeWithCode;
        this.operators = operators;
        this.convertsImplicitly = operators.Contains(ImplicitConversion);
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
        List<(string Name, ImmutableArray<string> Bases)> others = [];

        // A base type can be in a different tree. Thus, the parts do not find a base type that runs code,
        // and this method finds it.
        foreach (TreePart part in compilation.SyntaxTrees.Select(static tree =>
                     Parts.GetValue(key: tree, createValueCallback: static each => PartOf(each))))
        {
            getters.UnionWith(part.Getters);
            madeWithCode.UnionWith(part.MadeWithCode);
            operators.UnionWith(part.Operators);
            others.AddRange(part.Others);
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
    ///     uses an operator. A name can also have a value that an implicit
    ///     conversion gets.
    /// </summary>
    /// <param name="node">A node of the body of a lambda.</param>
    public bool CanRunCode(SyntaxNode node) =>
        node switch
        {
            IdentifierNameSyntax name => this.convertsImplicitly || this.getters.Contains(name.Identifier.ValueText),
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

    // Reads the declarations of the types of one syntax tree.
    private static TreePart PartOf(SyntaxTree tree)
    {
        ImmutableArray<string>.Builder getters = ImmutableArray.CreateBuilder<string>();
        ImmutableArray<string>.Builder madeWithCode = ImmutableArray.CreateBuilder<string>();
        ImmutableArray<string>.Builder operators = ImmutableArray.CreateBuilder<string>();

        ImmutableArray<(string Name, ImmutableArray<string> Bases)>.Builder others =
            ImmutableArray.CreateBuilder<(string Name, ImmutableArray<string> Bases)>();

        foreach (TypeDeclarationSyntax type in tree.GetRoot()
                     .DescendantNodes(static node =>
                         node is CompilationUnitSyntax or BaseNamespaceDeclarationSyntax or TypeDeclarationSyntax)
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
                    case ConversionOperatorDeclarationSyntax conversion:
                        operators.Add("()");

                        if (conversion.ImplicitOrExplicitKeyword.IsKind(SyntaxKind.ImplicitKeyword))
                        {
                            operators.Add(ImplicitConversion);
                        }

                        break;
                }
            }

            if (runsCode)
            {
                madeWithCode.Add(type.Identifier.ValueText);
            }
            else if (type.BaseList is { } bases)
            {
                ImmutableArray<string> names = bases.Types.Select(static each => NameOf(each.Type)).ToImmutableArray();
                others.Add((type.Identifier.ValueText, names));
            }
        }

        return new TreePart(
            getters: getters.ToImmutable(),
            madeWithCode: madeWithCode.ToImmutable(),
            operators: operators.ToImmutable(),
            others: others.ToImmutable());
    }

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

    // The declarations of the types of one syntax tree. Each type in Others has no code of its own,
    // and the search of the compilation finds if one of its base types runs code.
    private sealed class TreePart(
        in ImmutableArray<string> getters,
        in ImmutableArray<string> madeWithCode,
        in ImmutableArray<string> operators,
        in ImmutableArray<(string Name, ImmutableArray<string> Bases)> others)
    {
        public ImmutableArray<string> Getters { get; } = getters;

        public ImmutableArray<string> MadeWithCode { get; } = madeWithCode;

        public ImmutableArray<string> Operators { get; } = operators;

        public ImmutableArray<(string Name, ImmutableArray<string> Bases)> Others { get; } = others;
    }
}
