using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SodaFlow.Analyzers;

// The part of the evaluator that follows the getter of a property and the function of a Lazy.
internal sealed partial class LoopStateEvaluator
{
    /// <summary>
    ///     The read of a property whose getter has a body. The getter is a member
    ///     of the object that owns the property, and reads the members of that
    ///     object.
    /// </summary>
    private Followed? FollowGetter(SyntaxNode access)
    {
        if (this.region.SymbolOf(access) is not IPropertySymbol written || this.region.Symbols.IsSodaFlow(written))
        {
            return null;
        }

        ExpressionSyntax? owner =
            access switch
            {
                MemberAccessExpressionSyntax member => member.Expression,
                MemberBindingExpressionSyntax binding => ConditionalReceiver(binding),
                _ => null
            };

        if (SodaFlowSymbols.IsLazyValue(written))
        {
            return owner == null
                ? null
                : this.FollowLazy(
                    access: access,
                    written: owner,
                    lazy: this.WithTheBuildOfAMember(heldIn: owner, state: this.Eval(owner)),
                    value: written.Type);
        }

        // A property that the code reads through an interface is the property of the object. A
        // value that is one of two objects has the property of each.
        IPropertySymbol[] onThis =
            owner is null or ThisExpressionSyntax
                ? [.. this.ImplementationsOnThis(written).OfType<IPropertySymbol>()]
                : [];

        return (onThis.Length > 0
                ? onThis
                : PropertiesOf(
                    owner: this.builtType != null && owner is null or ThisExpressionSyntax
                        ? Construction.Of(this.builtType)
                        : this.Self(owner).Members,
                    written: written))
            .Aggregate(
                seed: (Followed?)null,
                func: (all, property) =>
                {
                    if (GetterOf(property.OriginalDefinition) is not { } body)
                    {
                        return all;
                    }

                    IParameterSymbol? extended = owner == null ? null : this.ExtensionParameterOf(property);

                    (Dictionary<ISymbol, LoopState> members, LoopState? receiver) =
                        this.MembersFor(isStatic: property.IsStatic || extended != null, owner: owner);

                    return Both(
                        first: all,
                        second: this.Enter(
                            call: access,
                            name: property.Name,
                            body: body,
                            bindings: extended != null && owner != null
                                ? [new Binding(parameter: extended, argument: owner, state: this.Eval(owner))]
                                : [],
                            definer: null,
                            members: members,
                            receiver: receiver,
                            target: null,
                            returnsAFunction: this.CanReturnALaterRead(property.Type),
                            returnsAnObject: this.DoesNotSayItsClass(property.Type)));
                });
    }

    // The parameter of the extension block of C# 14 that declares a member. The member reads its
    // receiver through that parameter. Null for each other member, and for a static member.
    private IParameterSymbol? ExtensionParameterOf(ISymbol definition) =>
        definition is { IsStatic: false, ContainingType: { } block }
        && block.TypeKind == SodaFlowSymbols.ExtensionKind
        && block.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax()
            is TypeDeclarationSyntax { ParameterList.Parameters.Count: 1 } declaration
            ? this.region.ModelFor(declaration).GetDeclaredSymbol(declaration.ParameterList.Parameters[0])
            : null;

    private static IEnumerable<IPropertySymbol> PropertiesOf(Construction? owner, IPropertySymbol written)
    {
        IPropertySymbol[] actual = [.. owner?.Resolve(written).OfType<IPropertySymbol>() ?? []];

        return actual.Length == 0 ? [written] : actual;
    }

    private Followed? FollowGetterOn(ExpressionSyntax access, IPropertySymbol written, LoopState owner)
    {
        if (SodaFlowSymbols.IsLazyValue(written))
        {
            return this.FollowLazy(access: access, written: access, lazy: owner, value: written.Type);
        }

        // A property that the pattern reads through an interface is the property of the object.
        return PropertiesOf(owner: owner.Members, written: written)
            .Aggregate(
                seed: (Followed?)null,
                func: (all, property) =>
                    property.IsStatic || GetterOf(property.OriginalDefinition) is not { } body
                        ? all
                        : Both(
                            first: all,
                            second: this.Enter(
                                call: access,
                                name: property.Name,
                                body: body,
                                bindings: [],
                                definer: null,
                                members: TaggedMembers(owner),
                                receiver: owner,
                                target: null)));
    }

    /// <summary>
    ///     The read of <c>Value</c> of a <c>Lazy</c>, which runs the functions
    ///     that the <c>Lazy</c> has. A call of <c>RunSynchronously</c> runs the
    ///     functions of a task, as this read does. The result is null when it has
    ///     no function that the evaluator can read.
    /// </summary>
    /// <remarks>
    ///     A <c>Lazy</c> from a <c>Map</c> has two functions, and the second one
    ///     gets the value of the first one as its parameter. Thus, a second walk
    ///     gives each parameter the result of the first walk also.
    /// </remarks>
    /// <param name="access">The read of the property.</param>
    /// <param name="written">The expression that names the <c>Lazy</c>, for a report.</param>
    /// <param name="lazy">The state of the <c>Lazy</c>.</param>
    /// <param name="value">The type of the value.</param>
    private Followed? FollowLazy(SyntaxNode access, ExpressionSyntax written, LoopState lazy, ITypeSymbol value)
    {
        // A Lazy of a delegate can hold a function as its value. The read gives that function,
        // and does not run it. A function that makes such a value returns a delegate.
        ImmutableArray<FunctionValue> makers =
            value.TypeKind == TypeKind.Delegate
                ? lazy.Functions.Where(this.ReturnsDelegate).ToImmutableArray()
                : lazy.Functions;

        if (makers.IsEmpty)
        {
            return null;
        }

        Followed? first = Run(lazy);

        Followed? followed =
            first == null || makers.Length == 1 || first.State.IsNone ? first : Run(lazy.Join(first.State));

        if (followed == null || makers.Length == lazy.Functions.Length)
        {
            return followed;
        }

        return new Followed(
            state: lazy.Functions
                .Where(function => !makers.Contains(function))
                .Aggregate(
                    seed: followed.State,
                    func: static (state, function) => state.Join(LoopState.OfFunction(function))),
            findings: followed.Findings,
            outs: followed.Outs,
            sets: followed.Sets,
            through: followed.Through,
            alwaysGives: followed.AlwaysGives,
            thrown: followed.Thrown);

        Followed? Run(LoopState given) =>
            makers.Aggregate(
                seed: (Followed?)null,
                func: (all, function) =>
                    Both(
                        first: all,
                        second: this.FollowFunction(
                            call: access,
                            name: NameOf(written),
                            function: function,
                            target: lazy,
                            arguments: default,
                            each: (written, given, ManyTimes: true),
                            heldIn: written)));
    }
}
