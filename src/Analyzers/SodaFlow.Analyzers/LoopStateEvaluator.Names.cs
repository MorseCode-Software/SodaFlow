using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace SodaFlow.Analyzers;

// The part of the evaluator for the names that hold one object: see NamesOfOneObject.
internal sealed partial class LoopStateEvaluator
{
    private static readonly IReadOnlyDictionary<ISymbol, int> NoWrites =
        new Dictionary<ISymbol, int>(SymbolEqualityComparer.Default);

    // For each member: the count of member writes of the region at the last write through this. The
    // second record has the count at the last write through a different owner.
    private Dictionary<ISymbol, int>? writesThroughThis;
    private Dictionary<ISymbol, int>? writesThroughOthers;

    // Each member that this code wrote through a second owner after its last write through this. The
    // value is the count at that write through this. See GivesItsOwner.
    private IReadOnlyDictionary<ISymbol, int> WrittenThroughASecondOwnerAfterThis()
    {
        if (this.writesThroughThis == null || this.writesThroughOthers == null)
        {
            return NoWrites;
        }

        Dictionary<ISymbol, int>? later = null;

        foreach (KeyValuePair<ISymbol, int> one in this.writesThroughThis)
        {
            if (this.writesThroughOthers.TryGetValue(key: one.Key, value: out int other) && other > one.Value)
            {
                (later ??= new Dictionary<ISymbol, int>(SymbolEqualityComparer.Default))[one.Key] = one.Value;
            }
        }

        return later ?? NoWrites;
    }

    /// <summary>
    ///     Records that the target of an assignment holds the object of the
    ///     value, when the two are names of an object.
    /// </summary>
    /// <param name="target">
    ///     The target of the assignment, or the location of the local with ref
    ///     that the assignment gives the value.
    /// </param>
    /// <param name="value">The expression of the value.</param>
    /// <param name="replaces">True when the assignment always runs in its function.</param>
    public void NotesNamesOfOneObject(SyntaxNode target, ExpressionSyntax value, bool replaces)
    {
        if (this.quiet
            || this.function != null
            || target is not ExpressionSyntax written
            || this.NameOfAnObjectIn(written) is not { } name
            || value.Parent is not AssignmentExpressionSyntax
            {
                RawKind: (int)SyntaxKind.SimpleAssignmentExpression
            } at)
        {
            return;
        }

        this.NotesNamesOfOneObject(target: name, at: at, value: value, replaces: replaces);
    }

    /// <summary>
    ///     Records that a local holds the object of the value that its
    ///     declaration gives it, when the value is a name of an object.
    /// </summary>
    /// <param name="local">The local.</param>
    /// <param name="declarator">The declaration of the local.</param>
    public void NotesNamesOfOneObject(ISymbol local, VariableDeclaratorSyntax declarator)
    {
        if (this.quiet
            || this.function != null
            || declarator.Initializer is not { Value: { } value }
            || !KeepsItsObject(local))
        {
            return;
        }

        this.NotesNamesOfOneObject(target: (local, null, null), at: declarator, value: value, replaces: true);
    }

    // The two names hold one object from the end of the assignment. The target leaves each other
    // object that it was a name of. Where a record has the value as a name that holds its object at
    // the assignment, the target joins that record. A conversion of the code can make a new object,
    // and it converts only between two types.
    private void NotesNamesOfOneObject(
        (ISymbol Of, ISymbol? Owner, object? Index) target,
        SyntaxNode at,
        ExpressionSyntax value,
        bool replaces)
    {
        List<NamesOfOneObject> all = this.Frame.NamesOfOneObject;
        this.LeavesItsRecords(target);

        if (replaces
            && value is BaseObjectCreationExpressionSyntax
            {
                Initializer: { RawKind: (int)SyntaxKind.ObjectInitializerExpression } initializer
            }
            && FunctionOf(at) is { } made)
        {
            NamesOfOneObject one =
                this.TheMembersOfANewObject(
                    target: target,
                    at: at,
                    value: value,
                    initializer: initializer,
                    code: made);

            all.Add(one);
            this.GivesTheCallersANewObject(name: target, at: at, members: one.Members);

            return;
        }

        if (!replaces
            || this.NameOfAnObjectIn(value) is not { } from
            || (!SymbolEqualityComparer.Default.Equals(x: TypeOfTheName(from), y: TypeOfTheName(target))
                && this.region.ConversionOf(value) != null)
            || FunctionOf(at) is not { } code)
        {
            return;
        }

        this.JoinsTheRecordOf(target: target, from: from, use: value, code: code, at: at);
    }

    // The target leaves each record that has it.
    private void LeavesItsRecords((ISymbol Of, ISymbol? Owner, object? Index) target)
    {
        List<NamesOfOneObject> all = this.Frame.NamesOfOneObject;

        foreach (NamesOfOneObject one in all)
        {
            one.Names.RemoveAll(name => name.Is(of: target.Of, owner: target.Owner, index: target.Index));
        }

        all.RemoveAll(static one => one.Names.Count == 0);
    }

    // The target joins the record that has a name that holds its object at a use, or a new record with that name.
    private void JoinsTheRecordOf(
        (ISymbol Of, ISymbol? Owner, object? Index) target,
        (ISymbol Of, ISymbol? Owner, object? Index) from,
        SyntaxNode use,
        SyntaxNode code,
        SyntaxNode at)
    {
        List<NamesOfOneObject> all = this.Frame.NamesOfOneObject;

        NamesOfOneObject? held =
            all.FirstOrDefault(one => one.Names.Any(name => this.IsTheName(name: name, read: from, use: use)));

        if (held == null)
        {
            held = new NamesOfOneObject();

            held.Names.Add(
                new NameOfAnObject(of: from.Of, owner: from.Owner, index: from.Index, code: code, from: at.Span.End));

            all.Add(held);
        }

        held.Names.Add(
            new NameOfAnObject(of: target.Of, owner: target.Owner, index: target.Index, code: code, from: at.Span.End));
    }

    // A record for a name that gets a new object, with the state that the initializer gives each
    // member. The initializer runs after the constructor, and no code runs between it and the
    // assignment. Thus, the object has only those states then.
    private NamesOfOneObject TheMembersOfANewObject(
        (ISymbol Of, ISymbol? Owner, object? Index) target,
        SyntaxNode at,
        ExpressionSyntax value,
        InitializerExpressionSyntax initializer,
        SyntaxNode code)
    {
        NamesOfOneObject one = new();

        one.Names.Add(
            new NameOfAnObject(of: target.Of, owner: target.Owner, index: target.Index, code: code, from: at.Span.End));

        Construction? members =
            this.TheOneObjectIn(this.Eval(value)) is { } made && this.region.Objects.StateOf(made) is { } state
                ? state.Members
                : this.Eval(value).Members;

        foreach (ExpressionSyntax each in initializer.Expressions)
        {
            if (each is AssignmentExpressionSyntax
                {
                    RawKind: (int)SyntaxKind.SimpleAssignmentExpression,
                    Left: IdentifierNameSyntax name
                }
                && this.region.SymbolOf(name) is { } member
                && this.IsAMemberLocation(member)
                && members != null)
            {
                one.Members.Add(
                    (member.OriginalDefinition, at.Span.End, members.StateOf(member), true, this.region.MemberWrites));
            }
        }

        return one;
    }

    // Gives each caller a record for a new object that this code put into a name. The call always
    // runs, the caller has a name for it, and the name keeps the object to the end of the code.
    private void GivesTheCallersANewObject(
        (ISymbol Of, ISymbol? Owner, object? Index) name,
        SyntaxNode at,
        IReadOnlyCollection<(ISymbol Member, int End, LoopState State, bool Accurate, int At)> members)
    {
        (ISymbol Of, ISymbol? Owner, object? Index)? named = name;
        SyntaxNode use = at;

        for (LoopStateEvaluator code = this; code.call is { } call; code = call.Caller)
        {
            named =
                named is { } inThisCode
                && call.AlwaysRuns
                && code.TheNameToTheEnd(name: inThisCode, after: use) is { } kept
                    ? call.Caller.TheNameAtTheCall(name: kept, site: call.Site)
                    : null;

            if (named is not { } atTheCall || FunctionOf(call.Site) is not { } caller)
            {
                return;
            }

            use = call.Site;

            if (!ReferenceEquals(objA: call.Caller.Frame, objB: code.Frame))
            {
                NamesOfOneObject one = new();

                one.Names.Add(
                    new NameOfAnObject(
                        of: atTheCall.Of,
                        owner: atTheCall.Owner,
                        index: atTheCall.Index,
                        code: caller,
                        from: use.Span.End));

                one.Members.AddRange(members.Select(each => each with { End = use.Span.End }));
                call.Caller.Frame.NamesOfOneObject.Add(one);
            }
        }
    }

    // A name of this code that keeps its object from a node to the end of the code, as the caller
    // names it. A local that is a second name of a static field, or of its array, is that field at
    // the node. Null for each other name.
    private (ISymbol Of, ISymbol? Owner, object? Index)? TheNameToTheEnd(
        (ISymbol Of, ISymbol? Owner, object? Index) name,
        SyntaxNode after)
    {
        if (FunctionOf(after) is not BaseMethodDeclarationSyntax code
            || (code.Body ?? (SyntaxNode?)code.ExpressionBody) is not { } body
            || name.Index is ISymbol)
        {
            return null;
        }

        (ISymbol Of, ISymbol? Owner, object? Index) held = this.AsTheField(name: name, use: after);

        NameOfAnObject kept =
            new(of: held.Of, owner: held.Owner, index: held.Index, code: code, from: after.Span.End);

        return held.Of is not ILocalSymbol
               && held.Owner is not ILocalSymbol
               && new[] { held.Of, held.Owner, held.Index as ISymbol }.All(part =>
                   part == null
                   || !this.UsesOf(name: part, code: code)
                       .Any(one => one.InAFunction || (one.Gives && one.Position > kept.From)))
               && this.NothingBetweenChanges(name: kept, after: kept.From, before: body.Span.End, ofTheName: true)
            ? held
            : null;
    }

    // A name at a use, as a caller can name it. An element of a local that is a second name of the
    // array of a static field is the element of that field. Each other name stays as it is.
    private (ISymbol Of, ISymbol? Owner, object? Index) AsTheField(
        (ISymbol Of, ISymbol? Owner, object? Index) name,
        SyntaxNode use) =>
        name is { Of: ILocalSymbol local, Owner: null, Index: not null }
        && this.Frame.NamesOfOneObject
                .Where(one => one.Names.Any(each =>
                    each.Is(of: local, owner: null, index: null) && this.HoldsItsObject(name: each, use: use)))
                .SelectMany(static one => one.Names)
                .FirstOrDefault(each =>
                    each is { Owner: null, Index: null }
                    && IsAStaticLocation(each.Of)
                    && this.HoldsItsObject(name: each, use: use))
            is { } field
            ? (field.Of, null, name.Index)
            : name;

    /// <summary>
    ///     Records a write to a member for
    ///     <see cref="WrittenThroughANameOfItsObject" />, in the frame of this
    ///     code and in the frame of each caller.
    /// </summary>
    /// <remarks>
    ///     Only an assignment of the code is sure. A callee can give back a write
    ///     through an argument. That write has each write of the callee in one
    ///     state. A second name of the object in the callee can have a write
    ///     after it. Thus, that write is not sure. Each write of the callee comes
    ///     to the caller one at a time, here. Such a write is sure for the caller
    ///     when the call always runs one time, and the caller has a name for it.
    ///     That name is a static field, an element of an array in one, or the
    ///     name that an argument gives a parameter.
    /// </remarks>
    /// <param name="written">The owner as the code has it at the write.</param>
    /// <param name="member">The member.</param>
    /// <param name="state">The state of the value.</param>
    /// <param name="replaces">True when the write always runs in its function.</param>
    /// <param name="seen">
    ///     True for the writes of a callee through a parameter, which a call
    ///     gives back to its argument. Each frame got those writes one at a
    ///     time, in the order of the callee.
    /// </param>
    /// <param name="givenBackBy">
    ///     The call on the owner whose callee wrote the member through this.
    ///     The state is the state of the member after the call. Thus, the write
    ///     is sure when the call always runs, as an assignment is.
    /// </param>
    /// <param name="laterThan">
    ///     For a write that a call gives back, where the callee wrote the
    ///     member through a second owner after it wrote the member through
    ///     this. The count of member writes of the region at that write
    ///     through this.
    /// </param>
    private void NotesAWriteThroughANameOfOneObject(
        ExpressionSyntax? written,
        ISymbol member,
        LoopState state,
        bool replaces,
        bool seen,
        SyntaxNode? givenBackBy = null,
        int? laterThan = null)
    {
        if (this.quiet)
        {
            return;
        }

        // The write changes no record. Each frame got the writes of the callee one at a time. Thus, this
        // write adds nothing, and it can only make an accurate record less accurate.
        if (seen)
        {
            for (LoopStateEvaluator? code = this; code != null; code = code.call?.Caller)
            {
                if (code.Frame.NamesWritesSeen == this.region.MemberWrites - 1)
                {
                    code.Frame.NamesWritesSeen = this.region.MemberWrites;
                }
            }

            return;
        }

        (ISymbol Of, ISymbol? Owner, object? Index)? name =
            (replaces || laterThan != null)
            && this.function == null
            && written != null
            && (givenBackBy != null
                || (written.Parent is MemberAccessExpressionSyntax
                    {
                        Parent: AssignmentExpressionSyntax
                        {
                            RawKind: (int)SyntaxKind.SimpleAssignmentExpression
                        } assignment
                    } access
                    && assignment.Left == access))
                ? this.NameOfAnObjectIn(written)
                : null;

        // C# reads the receiver before the arguments. Thus, the callee writes to the object that the
        // name had at the start of the call. An argument that gives the name a new object stops the
        // record at the next read.
        this.NotesAWriteInThisFrame(
            name: name,
            use: givenBackBy ?? written,
            from: (givenBackBy ?? written)?.Span.End ?? 0,
            end: givenBackBy?.Span.End ?? written?.Parent?.Parent?.Span.End ?? 0,
            member: member,
            state: state,
            madeAt: givenBackBy == null && written != null ? this.WhereTheCodeMadeTheObjectOf(written) : null,
            field: givenBackBy == null && name is { Of: IFieldSymbol field, Owner: null, Index: null } ? field : null,
            laterThan: laterThan);

        SyntaxNode? use = givenBackBy ?? written;

        for (LoopStateEvaluator code = this; code.call is { } call; code = call.Caller)
        {
            name =
                name is { } inThisCode
                && use != null
                && call.AlwaysRuns
                && code.TheNameFromTheStart(name: inThisCode, use: use) is { } fromTheStart
                    ? call.Caller.TheNameAtTheCall(name: fromTheStart, site: call.Site)
                    : null;

            use = call.Site;

            if (!ReferenceEquals(objA: call.Caller.Frame, objB: code.Frame))
            {
                call.Caller.NotesAWriteInThisFrame(
                    name: name,
                    use: use,
                    from: use.Span.End,
                    end: use.Span.End,
                    member: member,
                    state: state,
                    before: use switch
                    {
                        InvocationExpressionSyntax invocation => invocation.ArgumentList.Span.End,
                        BaseObjectCreationExpressionSyntax { ArgumentList: { } arguments } => arguments.Span.End,
                        _ => use.Span.End
                    },
                    laterThan: laterThan);
            }
        }
    }

    // The same, for the frame of this code. A sure write through a name replaces the state of the
    // member in each record that has the name. Where no record has it, the write makes one. Each
    // other write adds its state to each record of a member with the same name. A write that this
    // frame did not see takes each state away. The name is null for a write that is not sure here.
    // The arguments of a call run before its callee, and they can give a name a new object. Thus,
    // for a write of a callee, the name must hold its object up to the end of the arguments. A
    // write to a new object does not go to a record whose object is older: see IsOlderThan. A write
    // through a static field that holds its object alone goes to no other record. A callee can write
    // a member through this and then through a second name. The copy of this that comes back has
    // the first write only. Thus, the copy does not replace the second write in a record of the
    // name. It adds to it, or it changes nothing where a sure write gave the member its state after
    // the write through this.
    private void NotesAWriteInThisFrame(
        (ISymbol Of, ISymbol? Owner, object? Index)? name,
        SyntaxNode? use,
        int from,
        int end,
        ISymbol member,
        LoopState state,
        int? before = null,
        int? madeAt = null,
        IFieldSymbol? field = null,
        int? laterThan = null)
    {
        List<NamesOfOneObject> all = this.Frame.NamesOfOneObject;
        bool? alone = null;

        if (this.Frame.NamesWritesSeen != this.region.MemberWrites - 1)
        {
            foreach (NamesOfOneObject one in all)
            {
                one.Members.Clear();
            }
        }

        this.Frame.NamesWritesSeen = this.region.MemberWrites;
        List<NamesOfOneObject> held = [];

        foreach (NamesOfOneObject one in all)
        {
            bool isOfIt =
                name is { } through
                && use != null
                && one.Names.Any(each => this.IsTheName(name: each, read: through, use: use, before: before));

            if (isOfIt && laterThan == null)
            {
                held.Add(one);
                one.Members.RemoveAll(each => each.Member.Name == member.Name);
                one.ThroughASecondField.RemoveAll(each => each.Member.Name == member.Name);
            }
            else if (isOfIt
                         ? !one.Members.Any(each =>
                             each.Member.Name == member.Name && each.Accurate && each.At > laterThan)
                         : one.Members.Any(each => each.Member.Name == member.Name)
                           && (madeAt is not { } made || !IsOlderThan(names: one, made: made))
                           && !(alone ??= field != null && this.HoldsItsObjectAlone(field)))
            {
                for (int i = 0; i < one.Members.Count; i++)
                {
                    if (one.Members[i].Member.Name == member.Name)
                    {
                        one.Members[i] =
                            one.Members[i] with
                            {
                                State = one.Members[i].State.Join(state),
                                Accurate = false,
                                At = this.region.MemberWrites
                            };
                    }
                }

                if (name is { } second
                    && use != null
                    && this.CanHoldTheObjectOf(names: one, second: second, use: use))
                {
                    one.ThroughASecondField.Add((member.OriginalDefinition, state));
                }
            }
        }

        if (name is not { } named || laterThan != null || use == null || FunctionOf(use) is not { } code)
        {
            return;
        }

        if (held.Count == 0)
        {
            NamesOfOneObject one = new();

            one.Names.Add(
                new NameOfAnObject(of: named.Of, owner: named.Owner, index: named.Index, code: code, from: from));

            all.Add(one);
            held.Add(one);
        }

        foreach (NamesOfOneObject one in held)
        {
            one.Members.Add((member.OriginalDefinition, end, state, true, this.region.MemberWrites));
        }
    }

    // True when a write through a name can be a write to the object of a record of names. The record
    // has a static field, and the name is a second static field or a name of a record with one. The
    // walk does not read each code that gives a static field a value. Thus, the two fields can hold
    // one object. Two fields that each keep the object of their creation hold two objects, and so do
    // two fields where one holds its object alone.
    private bool CanHoldTheObjectOf(
        NamesOfOneObject names,
        (ISymbol Of, ISymbol? Owner, object? Index) second,
        SyntaxNode use)
    {
        IFieldSymbol[] fields =
        [
            .. names.Names
                .Select(static name => StaticFieldOf(of: name.Of, owner: name.Owner, index: name.Index))
                .OfType<IFieldSymbol>()
        ];

        return fields.Length > 0
               && this.Frame.NamesOfOneObject
                   .Where(one => one.Names.Any(name => this.IsTheName(name: name, read: second, use: use)))
                   .SelectMany(static one => one.Names)
                   .Select(static name => StaticFieldOf(of: name.Of, owner: name.Owner, index: name.Index))
                   .Append(StaticFieldOf(of: second.Of, owner: second.Owner, index: second.Index))
                   .OfType<IFieldSymbol>()
                   .Any(field =>
                       !fields.Contains(value: field, comparer: SymbolEqualityComparer.Default)
                       && fields.Any(other => this.CanHoldOneObject(one: other, second: field)));

        static IFieldSymbol? StaticFieldOf(ISymbol of, ISymbol? owner, object? index) =>
            owner == null && index == null && of is IFieldSymbol { IsStatic: true } field ? field : null;
    }

    // True when two static fields can hold one object: see CanHoldTheObjectOf.
    private bool CanHoldOneObject(IFieldSymbol one, IFieldSymbol second) =>
        (this.region.Symbols.Unchanged.CreationOf(one) == null
         || this.region.Symbols.Unchanged.CreationOf(second) == null)
        && !this.HoldsItsObjectAlone(one);

    // The state of a read of a member through a name of a record, from the writes through a second
    // static field. See CanHoldTheObjectOf. None where no record is for the read.
    private LoopState WrittenThroughASecondField(MemberAccessExpressionSyntax read)
    {
        if (this.quiet
            || !this.Frame.NamesOfOneObject.Any(static one => one.ThroughASecondField.Count > 0)
            || this.region.SymbolOf(read) is not { } member
            || this.NameOfAnObjectIn(read.Expression) is not { } name)
        {
            return LoopState.None;
        }

        ISymbol definition = member.OriginalDefinition;
        LoopState found = LoopState.None;

        foreach (NamesOfOneObject one in this.Frame.NamesOfOneObject)
        {
            if (one.ThroughASecondField.Count > 0
                && one.Names.Any(each => this.IsTheName(name: each, read: name, use: read.Expression)))
            {
                found = one.ThroughASecondField
                    .Where(each => SymbolEqualityComparer.Default.Equals(x: each.Member, y: definition))
                    .Aggregate(seed: found, func: static (all, each) => all.Join(each.State));
            }
        }

        return found;
    }

    // The position of the creation that made the object of an expression. The expression is the
    // creation, or a local that got the creation in its declaration and gets no other value. Null
    // for each other expression.
    private int? WhereTheCodeMadeTheObjectOf(ExpressionSyntax written)
    {
        ExpressionSyntax owner = Unwrapped(written);

        if (owner is BaseObjectCreationExpressionSyntax creation)
        {
            return creation.SpanStart;
        }

        return owner is IdentifierNameSyntax
               && this.region.SymbolOf(owner) is ILocalSymbol { RefKind: RefKind.None } local
               && local.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax()
                   is VariableDeclaratorSyntax { Initializer.Value: { } value } declarator
               && Unwrapped(value) is BaseObjectCreationExpressionSyntax
               && FunctionOf(declarator) is { } code
               && code == FunctionOf(owner)
               && !this.UsesOf(name: local, code: code).Any(static one => one.Gives)
            ? declarator.SpanStart
            : null;
    }

    // True for a private static readonly field that no second name can hold the object of. Its
    // initializer gives it a new object whose constructor does not give this away. Each use of the
    // field in its type reads or writes a member of the object that is a location.
    private bool HoldsItsObjectAlone(IFieldSymbol field) =>
        field is { IsStatic: true, IsReadOnly: true, DeclaredAccessibility: Accessibility.Private }
        && this.region.Symbols.HoldsItsObjectAlone(field: field, read: this.ReadsTheUsesOf);

    // The same, read from the code of the type. The search for the name in the text costs less
    // than a walk of each node, and most types do not use the name.
    private bool ReadsTheUsesOf(IFieldSymbol field)
    {
        if (field.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax()
                is not VariableDeclaratorSyntax { Initializer.Value: { } value } declarator
            || Unwrapped(value) is not BaseObjectCreationExpressionSyntax creation
            || this.region.SymbolOf(creation) is not IMethodSymbol constructor
            || (constructor.IsImplicitlyDeclared
                ? !HasNoInitializers(constructor.ContainingType)
                : this.region.Symbols.GivesThisAway(constructor.OriginalDefinition)))
        {
            return false;
        }

        foreach (SyntaxReference declared in field.ContainingType.DeclaringSyntaxReferences)
        {
            SyntaxNode type = declared.GetSyntax();
            string text = type.SyntaxTree.GetText().ToString(type.Span);

            for (int at = text.IndexOf(value: field.Name, comparisonType: StringComparison.Ordinal);
                 at >= 0;
                 at = text.IndexOf(value: field.Name, startIndex: at + 1, comparisonType: StringComparison.Ordinal))
            {
                SyntaxToken token = type.FindToken(type.SpanStart + at);

                if (token.SpanStart == type.SpanStart + at
                    && token.IsKind(SyntaxKind.IdentifierToken)
                    && token.ValueText == field.Name
                    && token != declarator.Identifier
                    && !this.ReadsOnlyAMemberOf(token: token, field: field))
                {
                    return false;
                }
            }
        }

        return true;
    }

    // True when a token is the name of a different symbol, or the owner of a member that is a
    // location. A name with no symbol can be the field.
    private bool ReadsOnlyAMemberOf(SyntaxToken token, ISymbol field)
    {
        if (token.Parent is not IdentifierNameSyntax name)
        {
            return true;
        }

        if (this.region.SymbolOf(name) is not { } named)
        {
            return false;
        }

        if (!SymbolEqualityComparer.Default.Equals(x: named, y: field))
        {
            return true;
        }

        ExpressionSyntax owner = name.Parent is MemberAccessExpressionSyntax qualified && qualified.Name == name
            ? qualified
            : name;

        while (owner.Parent is ParenthesizedExpressionSyntax
               or PostfixUnaryExpressionSyntax { RawKind: (int)SyntaxKind.SuppressNullableWarningExpression })
        {
            owner = (ExpressionSyntax)owner.Parent;
        }

        return owner.Parent is MemberAccessExpressionSyntax access
               && access.Expression == owner
               && this.region.SymbolOf(access) is { } member
               && this.IsAMemberLocation(member);
    }

    // True when a name of a record got its object before a position. Each name that the record got
    // after it got that object from a name that held it. Thus, the object of the record is older than an
    // object that the code made at the position. A read through a name of the record tests that the
    // name holds its object to the read.
    private static bool IsOlderThan(NamesOfOneObject names, int made) =>
        names.Names.Any(name => name.From <= made);

    // A name of this code at a use, when it holds its object from the start of the code to the use.
    // A local that is a second name of a static field, or of its array, is that field. Null for
    // each other name: a local of this code is no name of the caller.
    private (ISymbol Of, ISymbol? Owner, object? Index)? TheNameFromTheStart(
        (ISymbol Of, ISymbol? Owner, object? Index) name,
        SyntaxNode use)
    {
        if (FunctionOf(use) is not BaseMethodDeclarationSyntax code
            || (code.Body ?? (SyntaxNode?)code.ExpressionBody) is not { } body
            || name.Index is ISymbol)
        {
            return null;
        }

        (ISymbol Of, ISymbol? Owner, object? Index) held = this.AsTheField(name: name, use: use);

        return held.Of is not ILocalSymbol
               && held.Owner is not ILocalSymbol
               && this.HoldsItsObject(
                   name: new NameOfAnObject(
                       of: held.Of,
                       owner: held.Owner,
                       index: held.Index,
                       code: code,
                       from: body.SpanStart),
                   use: use)
            ? held
            : null;
    }

    // A name of a callee as the caller names it at the call. A static field is the same name. A
    // parameter is the name of its argument. Null where the caller has no name for it.
    private (ISymbol Of, ISymbol? Owner, object? Index)? TheNameAtTheCall(
        (ISymbol Of, ISymbol? Owner, object? Index) name,
        SyntaxNode site)
    {
        if (name is { Owner: null, Index: null })
        {
            return name.Of is IParameterSymbol whole
                ? this.ArgumentOf(parameter: whole, site: site) is { } argument
                    ? this.NameOfAnObjectIn(argument)
                    : null
                : name;
        }

        if ((name.Owner ?? name.Of) is not IParameterSymbol parameter)
        {
            return name;
        }

        return this.ArgumentOf(parameter: parameter, site: site) is { } given
               && this.NameOfAnObjectIn(given) is { Owner: null, Index: null } named
            ? name.Owner == null
                ? (named.Of, null, name.Index)
                : (name.Of, named.Of, null)
            : null;
    }

    // The argument that a call gives a parameter of its callee, with no ref. Null for a parameter
    // of a different method, and for a call with a named argument.
    private ExpressionSyntax? ArgumentOf(IParameterSymbol parameter, SyntaxNode site)
    {
        (ArgumentListSyntax? arguments, ExpressionSyntax? receiver) =
            site switch
            {
                InvocationExpressionSyntax call =>
                    (call.ArgumentList, (call.Expression as MemberAccessExpressionSyntax)?.Expression),
                BaseObjectCreationExpressionSyntax creation => (creation.ArgumentList, null),
                _ => (null, null)
            };

        if (arguments == null
            || arguments.Arguments.Any(static each => each.NameColon != null)
            || this.region.SymbolOf(site) is not IMethodSymbol callee
            || !SymbolEqualityComparer.Default.Equals(
                x: parameter.ContainingSymbol,
                y: (callee.ReducedFrom ?? callee).OriginalDefinition))
        {
            return null;
        }

        int ordinal = callee.ReducedFrom != null ? parameter.Ordinal - 1 : parameter.Ordinal;

        return ordinal < 0
            ? receiver
            : ordinal < arguments.Arguments.Count && arguments.Arguments[ordinal].RefKindKeyword.IsKind(SyntaxKind.None)
                ? arguments.Arguments[ordinal].Expression
                : null;
    }

    /// <summary>
    ///     The state of a read of a member through a name of an object, from
    ///     <see cref="NotesAWriteThroughANameOfOneObject" />. Null when no record
    ///     is for the read. Null also when code between the write and the read
    ///     can change the object of the name or the member.
    /// </summary>
    /// <remarks>
    ///     A record adds each write through each other owner, also through an
    ///     object that the store knows is a different object. Thus, a record
    ///     that has a pending value says less than the other records of the
    ///     walk. The read then uses those. Each of the two is correct, thus a
    ///     record with no pending value can replace them.
    /// </remarks>
    private LoopState? WrittenThroughANameOfItsObject(MemberAccessExpressionSyntax read) =>
        this.FromARecordOf(read: read, accurate: false);

    /// <summary>
    ///     The state of a read of a member through a name, with a pending value
    ///     from a record of <see cref="NotesAWriteThroughANameOfOneObject" />
    ///     that is accurate.
    ///     The given state when it has a pending value itself, or when no such
    ///     record is for the read. The given state comes from the last write
    ///     through the name, or from a write at its element.
    /// </summary>
    /// <remarks>
    ///     A record that got no write through a different owner is accurate.
    ///     Thus, it is correct also with a pending value. The other records of the
    ///     walk can say where a pending value came from, thus a read that finds one
    ///     there keeps it.
    /// </remarks>
    /// <param name="read">The read.</param>
    /// <param name="state">The state that the other records give the read.</param>
    private LoopState WithAnAccurateRecord(MemberAccessExpressionSyntax read, LoopState state) =>
        state is { Pending: false, Linked: false } && this.FromARecordOf(read: read, accurate: true) is { } recorded
            ? recorded
            : state;

    // The same for the general read of a name. A write between can end the record of the last
    // write through a name, and an accurate record can keep the state. Most frames have no
    // accurate record with a pending value, thus that test comes first.
    private LoopState WithARecordOfNames(ExpressionSyntax read, LoopState state) =>
        read is MemberAccessExpressionSyntax member
        && state is { Pending: false, Linked: false }
        && this.Frame.NamesOfOneObject.Any(static one =>
            one.Members.Any(static each => each is { Accurate: true, State: { Pending: true } or { Linked: true } }))
            ? this.WithAnAccurateRecord(read: member, state: state)
            : state;

    // The state of a read of a member from a record. With accurate false, only a record with no
    // pending value counts. With true, only an accurate record with a pending value counts.
    private LoopState? FromARecordOf(MemberAccessExpressionSyntax read, bool accurate)
    {
        if (this.Frame.NamesOfOneObject.Count == 0
            || this.quiet
            || this.Frame.NamesWritesSeen != this.region.MemberWrites
            || this.region.SymbolOf(read) is not { } member
            || !this.IsAMemberLocation(member))
        {
            return null;
        }

        ISymbol definition = member.OriginalDefinition;

        if (!this.Frame.NamesOfOneObject.Any(one =>
                one.Members.Any(each => SymbolEqualityComparer.Default.Equals(x: each.Member, y: definition)))
            || this.NameOfAnObjectIn(read.Expression) is not { } name)
        {
            return null;
        }

        foreach (NamesOfOneObject one in this.Frame.NamesOfOneObject)
        {
            foreach ((ISymbol written, int end, LoopState state, bool isAccurate, int _) in one.Members)
            {
                if (state is { Members: null, Objects.IsEmpty: true }
                    && (accurate
                        ? isAccurate && state is { Pending: true } or { Linked: true }
                        : state is { Pending: false, Linked: false })
                    && SymbolEqualityComparer.Default.Equals(x: written, y: definition)
                    && one.Names.FirstOrDefault(each => this.IsTheName(name: each, read: name, use: read.Expression))
                        is { } held
                    && this.NothingBetweenChanges(
                        name: held,
                        after: end,
                        before: read.Expression.SpanStart,
                        ofTheName: false))
                {
                    return state.Join(this.TypeState(read));
                }
            }
        }

        return null;
    }

    // Gives a callee on a second object each member whose state a record of this frame knows for
    // the receiver. WrittenThroughANameOfItsObject reads the same records for a read. The callee
    // then reads the member as after a sure write of its own: see OfThisObject. Thus, a read
    // through this there does not add the state that the region keeps for the field. The records
    // are good only when this frame saw each member write after them.
    private void GivesTheCalleeTheWritesThroughAName(
        ExpressionSyntax owner,
        IDictionary<ISymbol, LoopState> members,
        LoopFrame callee)
    {
        if (this.Frame.NamesOfOneObject.Count == 0
            || this.quiet
            || this.Frame.NamesWritesSeen != this.region.MemberWrites
            || this.NameOfAnObjectIn(owner) is not { } name)
        {
            return;
        }

        foreach (NamesOfOneObject one in this.Frame.NamesOfOneObject)
        {
            foreach ((ISymbol written, int _, LoopState state, bool _, int _) in one.Members)
            {
                if (state is { Members: null, Objects.IsEmpty: true }
                    && one.Names.Any(each => this.IsTheName(name: each, read: name, use: owner)))
                {
                    members[written] = state.From(written);
                    callee.OwnWriteOf[written] = this.region.MemberWrites;
                }
            }
        }
    }

    // The parts of a name of an object. Null for an expression that is no such name: see
    // NameOfAnObject. The name holds a reference to an object, and no copy of a value.
    private (ISymbol Of, ISymbol? Owner, object? Index)? NameOfAnObjectIn(ExpressionSyntax expression)
    {
        ExpressionSyntax written = expression;

        while (written is ParenthesizedExpressionSyntax
               or PostfixUnaryExpressionSyntax { RawKind: (int)SyntaxKind.SuppressNullableWarningExpression })
        {
            written = written is ParenthesizedExpressionSyntax around
                ? around.Expression
                : ((PostfixUnaryExpressionSyntax)written).Operand;
        }

        return written switch
        {
            IdentifierNameSyntax when this.region.SymbolOf(written) is { } named
                                      && (KeepsItsObject(named) || IsAStaticLocation(named))
                                      && this.HoldsAnObject(named) =>
                (named, null, null),
            IdentifierNameSyntax or MemberAccessExpressionSyntax
                when this.region.SymbolOf(written) is IPropertySymbol { IsStatic: true } property
                     && this.TheStaticElementBehind(property) is { } element =>
                element,
            IdentifierNameSyntax when this.TheDeclaredLocationOf(written) is { } location =>
                this.NameOfAnObjectIn(location),
            MemberAccessExpressionSyntax access
                when this.region.SymbolOf(access) is { } read
                     && (this.IsAMemberLocation(read) ? read : this.TheFieldBehind(read)) is { } member
                     && this.HoldsAnObject(member) =>
                IsAStaticLocation(member)
                    ? (member, null, null)
                    : access.Expression is IdentifierNameSyntax holder
                      && this.region.SymbolOf(holder) is { } owner
                      && KeepsItsObject(owner)
                        ? (member, owner, null)
                        : null,
            InvocationExpressionSyntax { ArgumentList.Arguments.Count: 0 } call
                when this.region.SymbolOf(call) is IMethodSymbol method
                     && this.FieldReturnedBy(method) is { } returned
                     && this.HoldsAnObject(returned) =>
                returned.IsStatic
                    ? (returned, null, null)
                    : !method.IsStatic
                      && call.Expression is MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax holder }
                      && this.region.SymbolOf(holder) is { } owner
                      && KeepsItsObject(owner)
                        ? (returned, owner, null)
                        : null,
            ElementAccessExpressionSyntax { ArgumentList.Arguments: { Count: 1 } indexes } element
                when this.region.TypeOf(element) is { } type
                     && this.CanBeAnObject(type)
                     && element.Expression is IdentifierNameSyntax or MemberAccessExpressionSyntax
                     {
                         Expression: IdentifierNameSyntax or MemberAccessExpressionSyntax
                     }
                     && this.region.SymbolOf(element.Expression) is { } named
                     && (element.Expression is IdentifierNameSyntax || IsAStaticLocation(named))
                     && this.TheCollectionIn(named) is var collection
                     && (KeepsItsObject(collection)
                         ? this.KeepsItsElements(collection)
                         : collection is IFieldSymbol { IsStatic: true, Type: IArrayTypeSymbol { Rank: 1 } })
                     && (KeyOf(this.region.ModelFor(element).GetConstantValue(indexes[0].Expression))
                         ?? (indexes[0].Expression is IdentifierNameSyntax
                             && this.region.SymbolOf(indexes[0].Expression) is { } local
                             && KeepsItsObject(local)
                                 ? local
                                 : null)) is { } index =>
                (collection, null, index),
            _ => null
        };
    }

    // True for a local, a parameter, a field, or a property whose value is a reference to an object.
    private bool HoldsAnObject(ISymbol named) =>
        named switch
        {
            ILocalSymbol local => this.CanBeAnObject(local.Type),
            IParameterSymbol parameter => this.CanBeAnObject(parameter.Type),
            IFieldSymbol field => this.CanBeAnObject(field.Type),
            IPropertySymbol property => this.CanBeAnObject(property.Type),
            _ => false
        };

    // The type of the value that a name holds. Null for a collection whose element type the code
    // does not read here.
    private static ITypeSymbol? TypeOfTheName((ISymbol Of, ISymbol? Owner, object? Index) name)
    {
        ITypeSymbol? type =
            name.Of switch
            {
                ILocalSymbol local => local.Type,
                IParameterSymbol parameter => parameter.Type,
                IFieldSymbol field => field.Type,
                IPropertySymbol property => property.Type,
                _ => null
            };

        return name.Index == null
            ? type
            : type switch
            {
                IArrayTypeSymbol array => array.ElementType,
                INamedTypeSymbol { TypeArguments: { Length: > 0 } arguments } => arguments[arguments.Length - 1],
                _ => null
            };
    }

    // A static field, or a static property with no code. Each read of it at one time gives one
    // value.
    private static bool IsAStaticLocation(ISymbol symbol) =>
        symbol is IFieldSymbol { IsStatic: true }
        || (symbol is IPropertySymbol { IsStatic: true, DeclaringSyntaxReferences.IsEmpty: false }
            && HasNoCode(symbol));

    // True when a name of an object is the name that an expression reads, at a use, and it holds
    // the object there. An element of a second collection is the same name when the two
    // collections are one object. A local that got the array of a field is such a collection.
    private bool IsTheName(
        NameOfAnObject name,
        (ISymbol Of, ISymbol? Owner, object? Index) read,
        SyntaxNode use,
        int? before = null) =>
        name.HasTheIndexAndTheOwner(owner: read.Owner, index: read.Index)
        && (SymbolEqualityComparer.Default.Equals(x: name.Of, y: read.Of)
            || (read.Index != null && this.AreOneObject(one: name.Of, other: read.Of, use: use, before: before)))
        && this.HoldsItsObject(name: name, use: use, before: before);

    // True when two names hold one object at a use, as the record of the walk says.
    private bool AreOneObject(ISymbol one, ISymbol other, SyntaxNode use, int? before) =>
        this.Frame.NamesOfOneObject.Any(names =>
            names.Names.Any(name =>
                name.Is(of: one, owner: null, index: null)
                && this.HoldsItsObject(name: name, use: use, before: before))
            && names.Names.Any(name =>
                name.Is(of: other, owner: null, index: null)
                && this.HoldsItsObject(name: name, use: use, before: before)));

    // True when a name holds its object from the position where it got it to a use. The two are in
    // one function, and no loop or label is around the use. No code between them gives the name,
    // its collection, its index, or its owner a value, and no function gives them a value. A ref to
    // such a local or parameter, also before the position, can give it a value at each time. For an
    // element, a static field, or a field of an owner, each node between must be one that
    // NothingBetweenChanges allows. Only a constructor gives a readonly field a value, and the
    // object of the field is older than the name.
    private bool HoldsItsObject(NameOfAnObject name, SyntaxNode use, int? before = null)
    {
        int end = before ?? use.SpanStart;

        if (name.From > use.SpanStart
            || FunctionOf(use) != name.Code
            || use.Ancestors()
                .TakeWhile(around => around != name.Code)
                .Any(static around => around is ForStatementSyntax
                    or CommonForEachStatementSyntax
                    or WhileStatementSyntax
                    or DoStatementSyntax
                    or LabeledStatementSyntax))
        {
            return false;
        }

        return new[] { name.Of, name.Owner, name.Index as ISymbol }.All(part =>
                   part == null
                   || !this.UsesOf(name: part, code: name.Code)
                       .Any(one => one.Gives
                                   && (one.InAFunction
                                       || (one.Position > name.From && one.Position < end)
                                       || (part is ILocalSymbol or IParameterSymbol
                                           && IsARef(name.Code.FindToken(one.Position).Parent)))))
               && (name is { Index: null, Of: ILocalSymbol or IParameterSymbol or IFieldSymbol { IsReadOnly: true } }
                   || this.NothingBetweenChanges(name: name, after: name.From, before: end, ofTheName: true));
    }

    // True for a use of a name as a ref: a ref argument or the location of a ref.
    private static bool IsARef(SyntaxNode? use) =>
        use?.Parent is RefExpressionSyntax or ArgumentSyntax { RefKindKeyword.RawKind: (int)SyntaxKind.RefKeyword };

    // True when no node between two positions can change the object of a name, or a member of an
    // object. Each such node is one that CannotChangeTheElement allows for the type of the
    // collection, of the owner, or of the name. A method of this compilation can come between where
    // CanStandBetween permits its code, and a constructor where ConstructorCanStandBetween does.
    // This is so also when the method gets the collection or the owner. That code puts nothing
    // into an element, and the walk reads its writes to a member. When the last argument is true,
    // an assignment to the field of the name is no such node also. That is so in the code between
    // and in the code of such a method.
    private bool NothingBetweenChanges(NameOfAnObject name, int after, int before, bool ofTheName)
    {
        if (after > before)
        {
            return true;
        }

        ITypeSymbol? type =
            (name.Index != null ? name.Of : name.Owner ?? name.Of) switch
            {
                ILocalSymbol local => local.Type,
                IParameterSymbol parameter => parameter.Type,
                IFieldSymbol field => field.Type,
                IPropertySymbol property => property.Type,
                _ => null
            };

        return type != null
               && name.Code.DescendantNodes(TextSpan.FromBounds(start: after, end: before))
                   .All(node => node.SpanStart < after
                                || node.Span.End > before
                                || node switch
                                {
                                    // An element of an array is no location of a field or of a
                                    // property. Thus, a put into it keeps a name that is not an
                                    // element.
                                    AssignmentExpressionSyntax assignment =>
                                        !(ofTheName
                                          && this.region.SymbolOf(assignment.Left) is IFieldSymbol
                                              or IPropertySymbol
                                          && SymbolEqualityComparer.Default.Equals(
                                              x: this.region.SymbolOf(assignment.Left),
                                              y: name.Of))
                                        && (this.CannotChangeTheElement(node: node, collection: type, callees: 0)
                                            || (ofTheName
                                                && name.Index == null
                                                && assignment is
                                                {
                                                    RawKind: (int)SyntaxKind.SimpleAssignmentExpression,
                                                    Left: ElementAccessExpressionSyntax put
                                                }
                                                && this.region.SymbolOf(put) == null)),
                                    InvocationExpressionSyntax call
                                        when this.region.SymbolOf(call) is IMethodSymbol
                                             {
                                                 MethodKind: MethodKind.Ordinary or MethodKind.ReducedExtension,
                                                 IsVirtual: false,
                                                 IsAbstract: false,
                                                 IsOverride: false
                                             } method
                                             && !(method.ReducedFrom ?? method).DeclaringSyntaxReferences.IsEmpty =>
                                        this.GivesOnlyFields(method)
                                        || (this.CanStandBetween(
                                                method: method.ReducedFrom ?? method,
                                                collection: type,
                                                callees: MaxCalleesBetween,
                                                call: call,
                                                callee: null)
                                            && (!ofTheName
                                                || SetsNoName(
                                                    method: method.ReducedFrom ?? method,
                                                    callees: MaxCalleesBetween))),
                                    // The constructor that the compiler writes for a type with no
                                    // initializer and no static constructor runs no code of the
                                    // project.
                                    BaseObjectCreationExpressionSyntax creation
                                        when this.region.SymbolOf(creation) is IMethodSymbol
                                        {
                                            MethodKind: MethodKind.Constructor,
                                            IsImplicitlyDeclared: true
                                        } implicitConstructor =>
                                        implicitConstructor.ContainingType.StaticConstructors.IsEmpty
                                        && HasNoInitializers(implicitConstructor.ContainingType),
                                    // An out argument that declares a local gives no older name a
                                    // value.
                                    ArgumentSyntax
                                    {
                                        RefKindKeyword.RawKind: (int)SyntaxKind.OutKeyword,
                                        Expression: DeclarationExpressionSyntax
                                    } => true,
                                    DeclarationExpressionSyntax or SingleVariableDesignationSyntax => true,
                                    // A new object gets the values of its initializer. The walk
                                    // reads each assignment of the initializer as a node.
                                    InitializerExpressionSyntax
                                    {
                                        RawKind: (int)SyntaxKind.ObjectInitializerExpression,
                                        Parent: BaseObjectCreationExpressionSyntax
                                    } => true,
                                    BaseObjectCreationExpressionSyntax creation
                                        when this.region.SymbolOf(creation) is IMethodSymbol
                                             {
                                                 MethodKind: MethodKind.Constructor,
                                                 DeclaringSyntaxReferences.IsEmpty: false
                                             } constructor =>
                                        this.ConstructorCanStandBetween(
                                            constructor: constructor,
                                            collection: type,
                                            callees: MaxCalleesBetween)
                                        && (!ofTheName || SetsNoName(method: constructor, callees: MaxCalleesBetween)),
                                    _ => this.CannotChangeTheElement(node: node, collection: type, callees: 0)
                                });

        // A member write of a callee is a write that the walk reads. An assignment to the field of
        // the name in the callee is not.
        bool SetsNoName(IMethodSymbol method, int callees) =>
            callees > 0
            && method.OriginalDefinition.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax()
                is BaseMethodDeclarationSyntax declaration
            && declaration.DescendantNodes()
                .All(node => node switch
                {
                    AssignmentExpressionSyntax assignment => !SymbolEqualityComparer.Default.Equals(
                        x: this.region.SymbolOf(assignment.Left),
                        y: name.Of),
                    InvocationExpressionSyntax inner
                        when this.region.SymbolOf(inner) is IMethodSymbol called
                             && !(called.ReducedFrom ?? called).DeclaringSyntaxReferences.IsEmpty =>
                        SetsNoName(method: called.ReducedFrom ?? called, callees: callees - 1),
                    _ => true
                });
    }
}
