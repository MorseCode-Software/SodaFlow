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
    // The most names that can give the expression of a member that holds a build. See
    // WithTheBuildOfAMember.
    private const int MaxNamesOfABuild = 4;

    private static readonly IReadOnlyDictionary<ISymbol, LoopState> NoMembers =
        new Dictionary<ISymbol, LoopState>(SymbolEqualityComparer.Default);

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
        ISymbol? symbol;

        // A local with one value and a getter that is only an expression each give the member that
        // they read. Two getters can read each other, thus the number of such names has a limit.
        for (int names = 0;; names++)
        {
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

            symbol = this.region.SymbolOf(Unwrapped(heldIn));

            if (names == MaxNamesOfABuild || this.MemberThatANameReads(symbol: symbol, owner: owner) is not { } named)
            {
                break;
            }

            heldIn = named;
        }

        if (symbol is not ({ } member and (IFieldSymbol { IsStatic: false } or IPropertySymbol { IsStatic: false })))
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
                    ? this.TypeOfThis(heldIn)
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
                    itsObject: ofTheOwner.Members,
                    objectsOfThis: ofTheOwner.Objects);

        // The evaluator keeps the value of each expression. Thus, a second read of a member of this
        // gets the same cell, and does not run its functions again.
        LoopState built = values.Aggregate(seed: state, func: (all, value) => all.Join(reader.EvalABuild(value)));

        // The code of the build ran before the read. A lambda of the build that runs at the read
        // is then a function of a frame that returned. Thus, the read gets its findings, as for a
        // call: see Leave.
        if (!ReferenceEquals(objA: reader, objB: this))
        {
            reader.Frame.Return();
        }

        return built;
    }

    // The expression that a local or a getter of this gives, when it can name a member. The local
    // has one value. The getter is only an expression, and no override can change it. Null for each
    // other name.
    private ExpressionSyntax? MemberThatANameReads(ISymbol? symbol, ExpressionSyntax? owner) =>
        symbol switch
        {
            // The search for the writes of a local binds each use. Thus, the value comes first.
            ILocalSymbol local
                when local.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax()
                    is VariableDeclaratorSyntax { Initializer.Value: var initial }
                && Unwrapped(initial) is IdentifierNameSyntax or MemberAccessExpressionSyntax =>
                this.OnlyValueOf(local),
            // The getter of a different object reads the members of that object, not those of this.
            IPropertySymbol { IsStatic: false, IsVirtual: false, IsAbstract: false, IsOverride: false } property
                when owner == null => GetterOf(property.OriginalDefinition) as ExpressionSyntax,
            _ => null
        };

    // The type of the object of this code, for the code of a node. A method of a base class can run
    // on an object of a derived class. The code of that class can give the members their values.
    // Thus, the one type that the walk knows for the object of the call comes first, when it is that
    // class or a class derived from it.
    private ITypeSymbol? TypeOfThis(SyntaxNode at)
    {
        if (at.FirstAncestorOrSelf<TypeDeclarationSyntax>() is not { } code
            || this.region.ModelFor(code).GetDeclaredSymbol(code) is not INamedTypeSymbol declared)
        {
            return null;
        }

        INamedTypeSymbol[] known = [.. (this.thisObject?.Types ?? []).Take(2)];
        INamedTypeSymbol? actual = known.Length == 1 ? known[0] : null;

        for (INamedTypeSymbol? each = actual?.OriginalDefinition;
             each != null;
             each = each.BaseType?.OriginalDefinition)
        {
            if (SymbolEqualityComparer.Default.Equals(x: each, y: declared.OriginalDefinition))
            {
                return actual;
            }
        }

        return declared;
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
    /// <param name="heldIn">
    ///     The expression of the value at the read, or null where the read has
    ///     none. For a value that no member holds, the function gets only what
    ///     the store has now for the object of its code: see
    ///     RunsAfterTheCodeOfItsObject.
    /// </param>
    /// <param name="function">The function.</param>
    /// <param name="walk">The walk of the function.</param>
    private Followed? WithTheMembersAtTheRead(ExpressionSyntax? heldIn, FunctionValue function, Func<Followed?> walk)
    {
        if ((function.Definer ?? function.OfThis) is not { } definer
            || ReferenceEquals(objA: definer.thisMembers, objB: this.thisMembers)
            || ((heldIn == null ? null : this.MembersAtTheRead(heldIn))
                ?? (RunsAfterTheCodeOfItsObject(function) ? NoMembers : null)) is not { } atTheRead)
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

        // The function can be of the code of a different object. The record then keeps what it had, and gets what
        // the store has now: see MembersOfItsObjectNow. Each such state names its member, as a member of a callee
        // does. Without that name, the caller does not report what the function reads: see Leave.
        foreach (KeyValuePair<ISymbol, LoopState> pair in definer.MembersOfItsObjectNow(but: atTheRead))
        {
            LoopState? old = record.TryGetValue(key: pair.Key, value: out LoopState? had) ? had : null;
            changed.Add((pair.Key, old));
            LoopState now = pair.Value.FromIfUnknown(pair.Key);
            record[pair.Key] = old?.Join(now) ?? now;
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

    // True for a function of a method on an object of the store, where that method returned. The
    // function reads the members of this through the record of that method. A write to the object
    // after the return goes to the store and not to the record.
    private static bool RunsAfterTheCodeOfItsObject(FunctionValue function) =>
        (function.Definer ?? function.OfThis) is { objectsOfThis.IsEmpty: false, Frame.HasReturned: true };

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
