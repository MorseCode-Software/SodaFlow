using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SodaFlow.Analyzers;

// The part of the evaluator for the static constructor of a type, which runs at the first use of a static member.
internal sealed partial class LoopStateEvaluator
{
    /// <summary>
    ///     The result of the static constructor of a type, at the first use of a
    ///     static member of that type in a block. Null at each other member
    ///     access.
    /// </summary>
    /// <remarks>
    ///     C# runs an explicit static constructor before the first use of a
    ///     static member, and its static initializers first. A type with
    ///     initializers only has no such fixed time: the runtime can run them at
    ///     any time before the first read of a field. The walk reads them at
    ///     that read, which is the last time that they can run. A method of such
    ///     a type does not run them. A constant is no use. The code of the type
    ///     can be the first use, when it runs on an object.
    /// </remarks>
    /// <param name="access">The member access, as in <c>T.Value</c>.</param>
    public Followed? FollowTheStaticConstructor(MemberAccessExpressionSyntax access) =>
        // The walk asks for the symbol of each call, and of each other member access. Thus, the
        // symbol here is no new question. A question about each type of the compilation is one,
        // because the driver can make the symbols of the analyzer again for each file.
        this.quiet || this.StaticMemberOf(access) is not { ContainingType: { } owner } member
            ? null
            : this.FollowTheStaticConstructorOf(
                use: access,
                owner: owner,
                atAFieldRead: this.IsAMemberLocation(member));

    /// <summary>
    ///     The same for a simple name of a static member of a different type,
    ///     such as a name that <c>using static</c> or a base type gives. The code
    ///     of the type itself runs after its static constructor. A read of a
    ///     static field in the code of a type with initializers only runs them.
    /// </summary>
    /// <param name="name">The simple name.</param>
    public Followed? FollowTheStaticConstructor(SimpleNameSyntax name)
    {
        // Most names are locals and types. The syntax and a test of the declarations come before
        // the question to the compiler. A type with initializers only gives the compilation no name
        // of a static constructor. Thus, the test for it is the declaration of the type of the code.
        // With no static constructor and no initializer in a part of that type, the walk has no code
        // to read for the name. Thus, that test also only saves time.
        // A name in a MemberBindingExpressionSyntax, a QualifiedNameSyntax, a NameColonSyntax, or a
        // NameEqualsSyntax is no static member that StaticMemberOf gives. Thus, that test only saves time.
        if (this.quiet
            || (name.Parent is MemberAccessExpressionSyntax access && access.Name == name)
            || name.Parent
                is MemberBindingExpressionSyntax or QualifiedNameSyntax or NameColonSyntax or NameEqualsSyntax
            || SyntaxFacts.IsInTypeOnlyContext(name)
            || (!this.region.Symbols.DeclaresStaticConstructors && !this.IsInCodeWithStaticInitializers(name))
            || this.StaticMemberOf(name) is not { ContainingType: { } type } member)
        {
            return null;
        }

        if (!IsInTheCodeOf(node: name, type: type))
        {
            return this.FollowTheStaticConstructorOf(use: name, owner: type, atAFieldRead: false);
        }

        // An explicit static constructor ran before each code of its type. Code of the type around the
        // block can have run the initializers before the block, and the walk does not know when.
        return type.OriginalDefinition.StaticConstructors.All(static constructor => constructor.IsImplicitlyDeclared)
               && this.IsAMemberLocation(member)
               && !IsInTheCodeOf(node: this.region.Root, type: type)
            ? this.FollowTheStaticConstructorOf(use: name, owner: type, atAFieldRead: true)
            : null;
    }

    private bool IsInCodeWithStaticInitializers(SyntaxNode name) =>
        name.FirstAncestorOrSelf<TypeDeclarationSyntax>() is { } type && this.region.HasStaticInitializers(type);

    /// <summary>
    ///     The results of the static constructors that a creation runs: the one
    ///     of its class, and then the one of each base class.
    /// </summary>
    /// <param name="creation">The creation, as in <c>new T()</c>.</param>
    /// <param name="constructor">The constructor that the creation calls.</param>
    public Followed? FollowTheStaticConstructors(SyntaxNode creation, IMethodSymbol constructor)
    {
        Followed? all = null;

        if (this.quiet || !this.region.Symbols.DeclaresStaticConstructors)
        {
            return all;
        }

        // A struct runs its static constructor only at a call of a constructor that its code declares.
        // The default value of a struct calls no constructor.
        for (INamedTypeSymbol? type = constructor.ContainingType;
             type is { TypeKind: TypeKind.Class or TypeKind.Struct, DeclaringSyntaxReferences.IsEmpty: false }
             && (type.TypeKind == TypeKind.Class || !constructor.IsImplicitlyDeclared);
             type = type.BaseType)
        {
            all = Both(
                first: all,
                second: this.FollowTheStaticConstructorOf(use: creation, owner: type, atAFieldRead: false));
        }

        return this.OfACreation(creation: creation, followed: all);
    }

    /// <summary>
    ///     The same at the call of a delegate of a static method of a different
    ///     type. A delegate made from the method runs nothing until such a call.
    /// </summary>
    /// <param name="call">The call of the delegate.</param>
    /// <param name="method">The method that the delegate calls.</param>
    private Followed? FollowTheStaticConstructorAtACallOf(SyntaxNode call, IMethodSymbol method) =>
        method is { IsStatic: true, ContainingType: { DeclaringSyntaxReferences.IsEmpty: false } owner }
        && !this.quiet
        && !IsInTheCodeOf(node: call, type: owner)
            ? this.FollowTheStaticConstructorOf(use: call, owner: owner, atAFieldRead: false)
            : null;

    // The static member that a use names, when the use can run the static constructor of its type. A
    // nested type runs only its own. A method runs it at a call: a delegate of the method runs it at the
    // call of the delegate.
    private ISymbol? StaticMemberOf(SyntaxNode used) =>
        this.MemberOf(used) is { IsStatic: true, ContainingType.DeclaringSyntaxReferences.IsEmpty: false } member
        and not (INamedTypeSymbol or IFieldSymbol { IsConst: true })
        && (member is not IMethodSymbol
            || (used.Parent is InvocationExpressionSyntax { Expression: var called } && called == used))
            ? member
            : null;

    // True when each run of the block runs a use in the first turn of a loop of this code. The walker
    // of a different frame knows the loops of its code. Thus, a loop of a caller does not count.
    private bool RunsInEachFirstTurn(SyntaxNode use) =>
        this.walker?.WhereACallRuns(use) is (_, { } turn)
        && this.walker.AlwaysRunsTheFirstTurnOf(turn)
        && (this.call?.AlwaysRuns ?? true);

    // True when a node is in the code of a type itself, and not in a type in it.
    private static bool IsInTheCodeOf(SyntaxNode node, INamedTypeSymbol type) =>
        node.FirstAncestorOrSelf<TypeDeclarationSyntax>() is { } code
        && type.OriginalDefinition.DeclaringSyntaxReferences.Any(declared => declared.GetSyntax() == code);

    // The static constructor of a type at a use of the type. Each constructed generic type has its
    // own run. A type with initializers only runs them at a read of a field through the name of the
    // type. A property with no code reads its field.
    private Followed? FollowTheStaticConstructorOf(SyntaxNode use, INamedTypeSymbol owner, bool atAFieldRead)
    {
        ConstructorDeclarationSyntax? constructor =
            owner.OriginalDefinition.StaticConstructors.FirstOrDefault()?.DeclaringSyntaxReferences.FirstOrDefault()
                ?.GetSyntax() as ConstructorDeclarationSyntax;

        // A use that the code can skip, as in a branch, runs the constructor only when it runs. Thus,
        // the walk reads the constructor there, and the next use reads it again. A use in each turn of
        // a loop counts as sure only when the first turn always runs. A loop can run no turn.
        if ((constructor?.Parent
                ?? (atAFieldRead
                    ? owner.OriginalDefinition.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax()
                    : null)) is not TypeDeclarationSyntax
            || !this.region.StartsTheType(
                type: owner,
                sure: this.WhereItRuns(use) is (true, _) || this.RunsInEachFirstTurn(use)))
        {
            return null;
        }

        try
        {
            // The static initializers run in the order of the code, and then the body. Each part of a
            // partial type has initializers. The compiler runs the parts in the order of their declarations.
            IEnumerable<SyntaxNode> parts =
                owner.OriginalDefinition.DeclaringSyntaxReferences
                    .Select(static reference => reference.GetSyntax())
                    .OfType<TypeDeclarationSyntax>()
                    .SelectMany(static declaration => declaration.Members)
                    .Where(static part => part.Modifiers.Any(SyntaxKind.StaticKeyword))
                    .SelectMany(InitializersOf)
                    .OfType<SyntaxNode>();

            if ((constructor?.Body ?? (SyntaxNode?)constructor?.ExpressionBody?.Expression) is { } body)
            {
                parts = parts.Append(body);
            }

            // A write in an initializer replaces the state of a static field for the code after it. The runtime can
            // run the initializers of a type with no static constructor before the block writes the field. Thus,
            // after them, the field also keeps the state of the time before them.
            (Dictionary<ISymbol, LoopState> Fields, Dictionary<ISymbol, LoopState> Closed)? earlier =
                constructor == null ? this.region.TheFieldsNow() : null;

            Followed? followed =
                parts.Aggregate(
                    seed: (Followed?)null,
                    func: (all, part) =>
                        Both(
                            first: all,
                            second: this.Enter(
                                call: use,
                                name: owner.Name,
                                body: part,
                                bindings: [],
                                definer: null,
                                members: new Dictionary<ISymbol, LoopState>(SymbolEqualityComparer.Default),
                                receiver: null,
                                target: null)));

            if (earlier is { } before)
            {
                this.region.JoinsTheFieldsOf(before);
            }

            // The runtime replaces each object that the constructor throws with a TypeInitializationException.
            return this.Wrapped(followed: followed, wrapper: "System.TypeInitializationException");
        }
        finally
        {
            this.region.EndsTheStaticConstructorOf(owner);
        }

        static IEnumerable<ExpressionSyntax?> InitializersOf(MemberDeclarationSyntax part) =>
            part switch
            {
                FieldDeclarationSyntax field => field.Declaration.Variables
                    .Select(static variable => variable.Initializer?.Value),
                PropertyDeclarationSyntax property => [property.Initializer?.Value],
                _ => []
            };
    }
}
