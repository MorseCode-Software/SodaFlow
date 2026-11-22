using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SodaFlow.Analyzers;

// The part of the evaluator for a cell or a Lazy that a method of an object builds into a member. A different method
// of the object can read the value after a third method changes a member that the function reads.
internal sealed partial class LoopStateEvaluator
{
    /// <summary>
    ///     The state of a member that holds a cell or a <c>Lazy</c>, at a read of
    ///     its value. The walk can know nothing of the member. Then, the state
    ///     comes from each assignment in the code of its type that can give it a
    ///     function. Each other state stays as it is.
    /// </summary>
    /// <remarks>
    ///     The walk does not read a method or a constructor that gets no state
    ///     from the loop. Such a call can build the value of the member, as a
    ///     constructor of a view model does. Thus, the walk reads the value of
    ///     each such assignment here, with the members that the object has now.
    ///     It does not know if the code of the assignment ran. Thus, the result
    ///     can have a function that does not run.
    /// </remarks>
    /// <param name="heldIn">The expression of the member.</param>
    /// <param name="state">The state of the member that the walk knows.</param>
    public LoopState WithTheBuildOfAMember(ExpressionSyntax heldIn, LoopState state)
    {
        if (!state.IsNone)
        {
            return state;
        }

        // A member of this, or a member of a different object.
        ExpressionSyntax? owner;

        switch (Unwrapped(heldIn))
        {
            case MemberAccessExpressionSyntax { Expression: ThisExpressionSyntax or BaseExpressionSyntax }:
            case IdentifierNameSyntax:
                owner = null;

                break;
            case MemberAccessExpressionSyntax access:
                owner = access.Expression;

                break;
            default:
                return state;
        }

        if (this.region.SymbolOf(Unwrapped(heldIn))
            is not ({ } member and (IFieldSymbol { IsStatic: false } or IPropertySymbol { IsStatic: false })))
        {
            return state;
        }

        LoopState ofTheOwner = owner == null ? LoopState.None : this.Eval(owner);

        // A read through an interface reads the member of the class of the object.
        ISymbol read =
            owner != null
            && member.ContainingType.TypeKind == TypeKind.Interface
            && ofTheOwner.Members?.Resolve(member).FirstOrDefault() is { } implementation
                ? implementation
                : member;

        if ((owner == null ? this.thisMembers.ContainsKey(read.OriginalDefinition) : ofTheOwner.Members?.Has(read))
            != false)
        {
            return state;
        }

        ImmutableArray<ExpressionSyntax> values =
            MemberBuilds.Of(
                member: read,
                type: owner == null
                    ? heldIn.FirstAncestorOrSelf<TypeDeclarationSyntax>() is { } code
                        ? this.region.ModelFor(code).GetDeclaredSymbol(code) as ITypeSymbol
                        : null
                    : ReferenceEquals(objA: read, objB: member)
                        ? this.region.TypeOf(owner)
                        : null);

        if (values.IsEmpty)
        {
            return state;
        }

        // The code of the assignment reads the members of this, which is the object of the member.
        LoopStateEvaluator reader =
            owner == null
                ? this
                : this.Inner(
                    call: heldIn,
                    bindings: [],
                    definer: null,
                    members: TaggedMembers(ofTheOwner),
                    function: null,
                    eachRun: null,
                    self: null,
                    itsObject: ofTheOwner.Members);

        // The evaluator keeps the value of each expression. Thus, a second read of a member of this
        // gets the same cell, and does not run its functions again.
        return values.Aggregate(seed: state, func: (all, value) => all.Join(reader.EvalABuild(value)));
    }

    /// <summary>
    ///     The walk of a function at the first read of its value. The function
    ///     gets the members that the object of the read has now.
    /// </summary>
    /// <remarks>
    ///     The function reads the members of this through the record of the
    ///     code that built it. A method that returned keeps the members of that
    ///     time. Thus, the walk gives the record the members of the object that
    ///     holds the value. Then, it gives the record its own members again.
    /// </remarks>
    /// <param name="heldIn">The expression of the value at the read.</param>
    /// <param name="function">The function.</param>
    /// <param name="walk">The walk of the function.</param>
    private Followed? WithTheMembersAtTheRead(ExpressionSyntax heldIn, FunctionValue function, Func<Followed?> walk)
    {
        if ((function.Definer ?? function.OfThis) is not { } definer
            || ReferenceEquals(objA: definer.thisMembers, objB: this.thisMembers)
            || this.MembersAtTheRead(heldIn) is not { } atTheRead)
        {
            return walk();
        }

        Dictionary<ISymbol, LoopState> record = definer.thisMembers;

        List<(ISymbol Key, LoopState? Old)> changed =
        [
            .. atTheRead.Select(pair =>
                (pair.Key, record.TryGetValue(key: pair.Key, value: out LoopState? old) ? old : null))
        ];

        foreach (KeyValuePair<ISymbol, LoopState> pair in atTheRead)
        {
            record[pair.Key] = pair.Value;
        }

        Followed? followed = walk();

        foreach ((ISymbol key, LoopState? old) in changed)
        {
            if (old == null)
            {
                record.Remove(key);
            }
            else
            {
                record[key] = old;
            }
        }

        return followed;
    }

    // The members of the object that holds a value at a read of the value. The value is a member of
    // this, or a member of a different object. The state of each member of a different object names
    // that member, as in a callee on that object.
    private IReadOnlyDictionary<ISymbol, LoopState>? MembersAtTheRead(ExpressionSyntax heldIn) =>
        Unwrapped(heldIn) switch
        {
            MemberAccessExpressionSyntax { Expression: ThisExpressionSyntax or BaseExpressionSyntax } =>
                this.thisMembers,
            IdentifierNameSyntax name
                when this.region.SymbolOf(name)
                    is IFieldSymbol { IsStatic: false } or IPropertySymbol { IsStatic: false } => this.thisMembers,
            MemberAccessExpressionSyntax { Expression: var owner } => TaggedMembers(this.Eval(owner)),
            _ => null
        };
}
