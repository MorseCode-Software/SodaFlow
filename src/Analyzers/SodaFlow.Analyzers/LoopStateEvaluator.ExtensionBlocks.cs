using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SodaFlow.Analyzers;

// The part of the evaluator for a member of an extension block. It reads a static call of the
// member, a property pattern that names the member, and a write through the receiver.
internal sealed partial class LoopStateEvaluator
{
    /// <summary>
    ///     A static call of a member of an extension block, as in
    ///     <c>E.M(x)</c>. The compiler binds the call to a static method that it
    ///     writes. That method has the declaration of the member. The code of the
    ///     member names the parameter of the block and the parameters of the
    ///     member. Thus, the first argument goes to the parameter of the block.
    /// </summary>
    private Followed? FollowTheStaticFormOfABlockMember(InvocationExpressionSyntax invocation, IMethodSymbol method)
    {
        if (method is not { IsStatic: true, IsImplicitlyDeclared: true, ReducedFrom: null }
            || this.BlockMemberOf(method) is not ({ } extended, var parameters)
            || BodyOf(method.OriginalDefinition) is not { } body)
        {
            return null;
        }

        IMethodSymbol implementation = method.OriginalDefinition;

        if (implementation.Parameters.Length != parameters.Length + 1)
        {
            return null;
        }

        IReadOnlyList<Binding> bindings =
        [
            .. this.Bind(
                    definition: implementation,
                    arguments: invocation.ArgumentList.Arguments,
                    extensionReceiver: null)
                .Select(binding =>
                    new Binding(
                        parameter: OfTheBlock(binding.Parameter),
                        argument: binding.Argument,
                        state: binding.State))
        ];

        Followed? followed =
            this.Enter(
                call: invocation,
                name: method.Name,
                body: body,
                bindings: bindings,
                definer: null,
                members: new Dictionary<ISymbol, LoopState>(SymbolEqualityComparer.Default),
                receiver: null,
                target: null,
                returnsAFunction: implementation.ReturnType.TypeKind == TypeKind.Delegate,
                returnsAnObject: this.DoesNotSayItsClass(method.ReturnType));

        if (followed == null)
        {
            return null;
        }

        // Each argument with ref or out gets what the body gave to the parameter at its position.
        ImmutableDictionary<ISymbol, LoopState>.Builder outs =
            ImmutableDictionary.CreateBuilder<ISymbol, LoopState>(SymbolEqualityComparer.Default);

        foreach (KeyValuePair<ISymbol, LoopState> pair in followed.Outs)
        {
            outs[OfTheImplementation(pair.Key)] = pair.Value;
        }

        return new Followed(
            state: followed.State,
            findings: followed.Findings,
            outs: outs.ToImmutable(),
            sets: followed.Sets,
            through: followed.Through
                .Select(one => ((IParameterSymbol)OfTheImplementation(one.Parameter), one.Member, one.State))
                .ToImmutableArray(),
            alwaysGives: followed.AlwaysGives
                .Select(OfTheImplementation)
                .ToImmutableHashSet(SymbolEqualityComparer.Default),
            thrown: followed.Thrown);

        IParameterSymbol OfTheBlock(IParameterSymbol parameter) =>
            parameter.Ordinal == 0 ? extended : parameters[parameter.Ordinal - 1];

        ISymbol OfTheImplementation(ISymbol parameter) =>
            SymbolEqualityComparer.Default.Equals(x: parameter, y: extended)
                ? implementation.Parameters[0]
                : parameter is IParameterSymbol own
                  && parameters.IndexOf(item: own, equalityComparer: SymbolEqualityComparer.Default) is >= 0 and var at
                    ? implementation.Parameters[at + 1]
                    : parameter;
    }

    // The compiler writes a static method for a member of an extension block. For that method, this
    // gives the parameter of the block and the parameters of the member. Null for each other method.
    private (IParameterSymbol Extended, ImmutableArray<IParameterSymbol> Parameters)? BlockMemberOf(
        IMethodSymbol implementation)
    {
        SyntaxNode? declaration =
            implementation.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() switch
            {
                ArrowExpressionClauseSyntax { Parent: PropertyDeclarationSyntax property } => property,
                var other => other
            };

        ISymbol? member = declaration == null ? null : this.region.ModelFor(declaration).GetDeclaredSymbol(declaration);

        return member != null && this.ExtensionParameterOf(member) is { } extended
            ? (extended, member is IMethodSymbol method ? method.Parameters : ImmutableArray<IParameterSymbol>.Empty)
            : null;
    }

    // The getter of a property of an extension block that a property pattern names. The value of
    // the pattern goes to the parameter of the block.
    private Followed? FollowTheGetterOfABlockMember(
        ExpressionSyntax name,
        IPropertySymbol written,
        IParameterSymbol extended,
        LoopState owner) =>
        GetterOf(written.OriginalDefinition) is { } body
            ? this.Enter(
                call: name,
                name: written.Name,
                body: body,
                bindings: [new Binding(parameter: extended, argument: name, state: owner)],
                definer: null,
                members: new Dictionary<ISymbol, LoopState>(SymbolEqualityComparer.Default),
                receiver: null,
                target: null)
            : null;

    /// <summary>
    ///     Gives the receiver of a call of a member of an extension block what
    ///     the member gave to the parameter of the block.
    /// </summary>
    /// <param name="definition">The member.</param>
    /// <param name="followed">What the walk found in the body of the member.</param>
    /// <param name="receiver">The receiver of the call.</param>
    public void GivesTheReceiverOfABlockMember(IMethodSymbol definition, Followed followed, ExpressionSyntax receiver)
    {
        if (this.ExtensionParameterOf(definition) is { } extended
            && followed.Outs.TryGetValue(key: extended, value: out LoopState? given))
        {
            this.Merge(target: receiver, state: given);
            this.GivesAnArgument(argument: receiver, parameter: extended, followed: followed);
        }
    }
}
