using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SodaFlow.Analyzers;

// The part of the evaluator for a write to a member of an object, and for what a write through a name replaces.
internal sealed partial class LoopStateEvaluator
{
    /// <summary>
    ///     Records the state of a member of the object that the code is in, at
    ///     its initializer or assignment.
    /// </summary>
    public void SetMember(ISymbol member, LoopState state)
    {
        ISymbol key = member.OriginalDefinition;
        this.region.AddAMemberWrite();

        // The object can be older than a turn of a loop of this code. The receiver of a method can
        // be older than a turn of a loop of the caller also. A constructor makes a new object.
        state = this.InALocation(
            state: state,
            location: this,
            isOfItsTurn: static (_, made, code) => code.Frame.IsOfAConstructor && code.RunsInTheTurnOf(made));

        this.thisMembers[key] =
            this.thisMembers.TryGetValue(key: key, value: out LoopState? existing) ? existing.Join(state) : state;
    }

    /// <summary>
    ///     Records a value that the code gives to a member of an object, as in
    ///     <c>row.Share = value</c>. An object in a local keeps the state with its
    ///     members. For each other owner, and for a static member, the region keeps
    ///     the state for the field.
    /// </summary>
    /// <param name="owner">The expression of the object. Null for a static member.</param>
    /// <param name="member">The member.</param>
    /// <param name="state">The state of the value.</param>
    /// <param name="replaces">True when the assignment always runs in its function.</param>
    /// <param name="turn">
    ///     The body of the loop whose each turn always runs the assignment. Null
    ///     when no loop does.
    /// </param>
    public void SetMemberOf(ExpressionSyntax? owner, ISymbol member, LoopState state, bool replaces, SyntaxNode? turn)
    {
        LoopState has = this.Eval(owner);
        state = this.InAnObject(owner: has, state: state);
        this.region.AddAMemberWrite();
        this.NoteAWriteThroughEach(owner: owner, member: member, state: state, turn: turn);

        if (!replaces
            || this.quiet
            || this.function != null
            || owner is not IdentifierNameSyntax
            || this.region.SymbolOf(owner) is not { } name
            || !KeepsItsObject(name)
            || !IsALocation(member)
            || FunctionOf(owner) is not { } code)
        {
            this.Write(owner: owner, member: member, state: state, replaces: replaces, turn: turn, has: has);
            this.Frame.LastWrite = null;

            return;
        }

        this.TakeBackTheWriteBefore(name: name, member: member, code: code, position: owner.SpanStart);

        List<(MadeObject Object, LoopState Before)> before = [];

        foreach (MadeObject made in this.region.Objects.Now(has.Objects))
        {
            if (this.region.Objects.StateOf(made) is { } old)
            {
                before.Add((made, old));
            }
        }

        this.Write(owner: owner, member: member, state: state, replaces: replaces, turn: turn, has: has);

        this.Frame.LastWrite =
            new WriteThroughAName(
                name: name,
                member: member.OriginalDefinition,
                code: code,
                position: owner.SpanStart,
                state: state,
                local: this.locals[name],
                writes: this.region.MemberWrites,
                objects:
                [
                    .. before.Select(one =>
                        (one.Object, one.Before, After: this.region.Objects.StateOf(one.Object) ?? one.Before))
                ]);
    }

    /// <summary>
    ///     Records the start of the walk of a foreach statement, after the walk
    ///     of its collection. A write through an element takes the positions of
    ///     the collection away, thus the elements are from this time.
    /// </summary>
    public void StartsEach(ForEachStatementSyntax loop)
    {
        if (!this.quiet && this.function == null && this.ElementsOf(loop.Expression) is { } elements)
        {
            this.Frame.WritesThroughEach[loop] =
                new WritesThroughEach(
                    elements: elements,
                    start: this.region.MemberWrites,
                    member: null,
                    state: LoopState.None,
                    same: true,
                    visits: 0,
                    last: 0);
        }
    }

    // A write to a member through the variable of a foreach statement, which each turn of the
    // statement always runs. EndsEach uses it.
    private void NoteAWriteThroughEach(SyntaxNode? owner, ISymbol member, LoopState state, SyntaxNode? turn)
    {
        if (!this.quiet
            && this.function == null
            && turn != null
            && owner is IdentifierNameSyntax
            && this.region.SymbolOf(owner) is ILocalSymbol each
            && each.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() is ForEachStatementSyntax loop
            && loop.Statement == turn
            && this.Frame.WritesThroughEach.TryGetValue(key: loop, value: out WritesThroughEach? noted))
        {
            this.Frame.WritesThroughEach[loop] =
                noted.With(
                    written: member.OriginalDefinition,
                    value: state,
                    isALocation: IsALocation(member),
                    writes: this.region.MemberWrites);
        }
    }

    /// <summary>
    ///     Records the end of the walk of a foreach statement. When the statement
    ///     gave each element of its collection a value for a member, each of those
    ///     objects then has only that value.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A write in the body of a loop adds, because the walk does not know
    ///         which turns ran. After a loop that has a turn for each element, each
    ///         element has the value. The collection must keep its positions, thus
    ///         the walk knows each of its elements. Each element must be one object
    ///         that the code of this frame made.
    ///     </para>
    ///     <para>
    ///         A second write to a member in the body can give an element that a
    ///         turn before changed a different value. Thus, each write to a member
    ///         in each read of the body is the write through the variable.
    ///     </para>
    /// </remarks>
    /// <param name="loop">The statement.</param>
    /// <param name="hasATurnForEachElement">
    ///     True when the statement always runs in its function, and no statement
    ///     ends a turn or the loop before its end.
    /// </param>
    public void EndsEach(ForEachStatementSyntax loop, bool hasATurnForEachElement)
    {
        if (!this.Frame.WritesThroughEach.TryGetValue(key: loop, value: out WritesThroughEach? noted))
        {
            return;
        }

        this.Frame.WritesThroughEach.Remove(loop);

        if (!hasATurnForEachElement
            || noted.Member is not { } member
            || !noted.IsTheOnlyWrite(this.region.MemberWrites))
        {
            return;
        }

        List<(MadeObject Object, LoopState State)> objects = [];

        foreach (MadeObject? made in noted.Elements.Select(this.TheOneObjectOf))
        {
            if (made is not { Turn: null, IsOfTurnsBefore: false }
                || !made.IsFrom(this.Frame)
                || this.region.Objects.StateOf(made) is not { } old)
            {
                return;
            }

            objects.Add((made, old));
        }

        foreach ((MadeObject made, LoopState old) in objects)
        {
            this.region.Objects.Set(
                found: made,
                state: old.WithMembers((old.Members ?? Construction.Empty).With(member: member, state: noted.State)));
        }
    }

    // The object of the store that the value of an expression is now, when it is one object and no
    // other value.
    private MadeObject? TheOneObjectOf(ExpressionSyntax expression)
    {
        ImmutableArray<MadeObject> now = this.region.Objects.Now(this.Eval(expression).Objects);

        return now.Length == 1 ? now[0] : null;
    }

    // True for a local or a parameter that is one object until code gives it a value. A name with
    // ref is a second name of a location, and a write to that location gives the name a value.
    private static bool KeepsItsObject(ISymbol name) =>
        name is ILocalSymbol { RefKind: RefKind.None } or IParameterSymbol { RefKind: RefKind.None };

    // True for a member that is a location and no code: a field, or a property whose accessors
    // have no body. A read of it gives the value of the last write to it.
    private static bool IsALocation(ISymbol member) =>
        member is IFieldSymbol
        || (member is IPropertySymbol property
            && property.DeclaringSyntaxReferences.All(static declared =>
                declared.GetSyntax() is ParameterSyntax
                    or PropertyDeclarationSyntax { ExpressionBody: null, AccessorList: null }
                || (declared.GetSyntax() is PropertyDeclarationSyntax { ExpressionBody: null, AccessorList: { } list }
                    && list.Accessors.All(static accessor => accessor.Body == null && accessor.ExpressionBody == null))));

    /// <summary>
    ///     The state of a read of a member through a name, when the last write
    ///     through that name gave the member its value. Null for each other read.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The name can be one of two or more objects, and the store has the
    ///         write and the older state together for each of them. The name is
    ///         one object, and that object has only what the write gave.
    ///     </para>
    ///     <para>
    ///         That is true while no code changes the member or the name. Thus, no
    ///         write to a member came after that write, no code gave the name a
    ///         value, and no function uses the name. The value has no members and
    ///         is no object, thus no change after the write is a change to it.
    ///     </para>
    /// </remarks>
    private LoopState? WrittenThroughItsName(MemberAccessExpressionSyntax read) =>
        !this.quiet
        && this.function == null
        && this.Frame.LastWrite is { State: { Members: null, Objects.IsEmpty: true } } last
        && last.Writes == this.region.MemberWrites
        && Unwrapped(read.Expression) is IdentifierNameSyntax owner
        && owner.SpanStart > last.Position
        && FunctionOf(owner) == last.Code
        && SymbolEqualityComparer.Default.Equals(x: this.region.SymbolOf(owner), y: last.Name)
        && SymbolEqualityComparer.Default.Equals(x: this.region.SymbolOf(read)?.OriginalDefinition, y: last.Member)
        && this.locals.TryGetValue(key: last.Name, value: out LoopState? own)
        && ReferenceEquals(objA: own, objB: last.Local)
        && last.Objects.All(one => ReferenceEquals(objA: this.region.Objects.StateOf(one.Object), objB: one.After))
        && !this.GetsAValue(
            name: last.Name,
            code: last.Code,
            after: last.Position,
            before: owner.SpanStart,
            orAFunctionUsesIt: true)
            ? last.State.Join(this.TypeState(read))
            : null;

    // The function that has a node in its code.
    private static SyntaxNode? FunctionOf(SyntaxNode node) =>
        node.Ancestors()
            .FirstOrDefault(static around =>
                around is AnonymousFunctionExpressionSyntax
                    or LocalFunctionStatementSyntax
                    or BaseMethodDeclarationSyntax
                    or AccessorDeclarationSyntax);

    /// <summary>
    ///     Takes the last write through a name back, before a second write
    ///     through that name to the same member.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The name is one object for the two writes, because no code gives it
    ///         a value between them. Thus, that object has only the new state, and
    ///         each other object that the name can be did not get the first write.
    ///         Each object of the store gets the state from before the first write
    ///         back, and the second write then adds its state.
    ///     </para>
    ///     <para>
    ///         The two writes are at the first level of one function, in the order
    ///         of the code. Thus, one call of the function runs them. No object
    ///         changed between them: no write to a member came between, and each
    ///         object has the state that the first write gave.
    ///     </para>
    /// </remarks>
    private void TakeBackTheWriteBefore(ISymbol name, ISymbol member, SyntaxNode code, int position)
    {
        if (this.Frame.LastWrite is not { } last
            || last.Code != code
            || last.Position >= position
            || !SymbolEqualityComparer.Default.Equals(x: last.Name, y: name)
            || !SymbolEqualityComparer.Default.Equals(x: last.Member, y: member.OriginalDefinition)
            || last.Writes != this.region.MemberWrites - 1
            || last.Objects.Any(one =>
                !ReferenceEquals(objA: this.region.Objects.StateOf(one.Object), objB: one.After))
            || this.GetsAValue(
                name: name,
                code: code,
                after: last.Position,
                before: position,
                orAFunctionUsesIt: false))
        {
            return;
        }

        foreach ((MadeObject found, LoopState before, _) in last.Objects)
        {
            this.region.Objects.Set(found: found, state: before);
        }
    }

    // True when code of a function can give a name a value between two positions. That code is
    // between the two, or it is in a function in that function, which can run between them. A
    // function that uses the name can change the object of the name also, and the last argument
    // says if that use is such code.
    private bool GetsAValue(ISymbol name, SyntaxNode code, int after, int before, bool orAFunctionUsesIt) =>
        code.DescendantNodes()
            .OfType<IdentifierNameSyntax>()
            .Any(use =>
                use.Identifier.ValueText == name.Name
                && (FunctionOf(use) != code
                    ? orAFunctionUsesIt || GivesAValue(use)
                    : use.SpanStart > after && use.SpanStart < before && GivesAValue(use))
                && SymbolEqualityComparer.Default.Equals(x: this.region.SymbolOf(use), y: name));

    // True for a use of a name that can give the name a value. The use is the target of an
    // assignment or of a deconstruction, or an argument or an expression with ref, out, or in. The
    // operand of an operator that changes it is such a use also.
    private static bool GivesAValue(SyntaxNode use)
    {
        SyntaxNode target = use;

        while (target.Parent is ArgumentSyntax { Parent: TupleExpressionSyntax tuple })
        {
            target = tuple;
        }

        return target.Parent switch
        {
            AssignmentExpressionSyntax assignment => assignment.Left == target,
            ForEachVariableStatementSyntax each => each.Variable == target,
            ArgumentSyntax argument => !argument.RefKindKeyword.IsKind(SyntaxKind.None),
            RefExpressionSyntax => true,
            PrefixUnaryExpressionSyntax or PostfixUnaryExpressionSyntax =>
                target.Parent.Kind() is SyntaxKind.PreIncrementExpression
                    or SyntaxKind.PreDecrementExpression
                    or SyntaxKind.PostIncrementExpression
                    or SyntaxKind.PostDecrementExpression,
            _ => false
        };
    }

    // The write itself: what SetMemberOf records for each owner.
    private void Write(
        ExpressionSyntax? owner,
        ISymbol member,
        LoopState state,
        bool replaces,
        SyntaxNode? turn,
        LoopState has)
    {
        LoopState changed = LoopState.None.WithMembers(Construction.Empty.With(member: member, state: state));

        // The object of the store gets the state for the member, for each name of the object.
        if (owner != null)
        {
            this.Change(
                owner: has,
                change: old => old.WithMembers((old.Members ?? Construction.Empty).With(member: member, state: state)),
                replaces: replaces,
                turn: turn);
        }

        // The owner is an element of a collection. The collection then contains an object whose
        // member has the state.
        if (owner != null && this.CollectionOf(owner) != null)
        {
            this.Merge(target: owner, state: changed);
        }

        if (owner is IdentifierNameSyntax
            && this.region.SymbolOf(owner) is { } holder
                and (ILocalSymbol or IParameterSymbol))
        {
            // A collection that got the object of this local contains that object also.
            this.MergeIntoHolders(
                local: holder,
                state: AsACopyOfAChangeTo(changed: has, state: changed),
                level: 1);

            // A parameter starts with the state of its argument, which has the type of the object.
            // The callee gives this state back, and the object of the caller keeps its type.
            LoopState known =
                this.locals.TryGetValue(key: holder, value: out LoopState? existing)
                    ? existing
                    : this.bound.TryGetValue(key: holder, value: out LoopState? given)
                        ? given
                        : LoopState.None;

            this.locals[holder] =
                known.WithMembers((known.Members ?? Construction.Empty).With(member: member, state: state));

            return;
        }

        this.SetField(field: member, state: state);
    }
}
