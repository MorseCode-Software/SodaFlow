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

        foreach (NamesOfOneObject one in all)
        {
            one.Names.RemoveAll(name => name.Is(of: target.Of, owner: target.Owner, index: target.Index));
        }

        all.RemoveAll(static one => one.Names.Count == 0);

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

        NamesOfOneObject? held =
            all.FirstOrDefault(one => one.Names.Any(name => this.IsTheName(name: name, read: from, use: value)));

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
                && IsAMemberLocation(member)
                && members != null)
            {
                one.Members.Add((member.OriginalDefinition, at.Span.End, members.StateOf(member)));
            }
        }

        return one;
    }

    // Gives each caller a record for a new object that this code put into a name. The call always
    // runs, the caller has a name for it, and the name keeps the object to the end of the code.
    private void GivesTheCallersANewObject(
        (ISymbol Of, ISymbol? Owner, object? Index) name,
        SyntaxNode at,
        IReadOnlyCollection<(ISymbol Member, int End, LoopState State)> members)
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
    private void NotesAWriteThroughANameOfOneObject(
        ExpressionSyntax? written,
        ISymbol member,
        LoopState state,
        bool replaces)
    {
        if (this.quiet)
        {
            return;
        }

        (ISymbol Of, ISymbol? Owner, object? Index)? name =
            replaces
            && this.function == null
            && written?.Parent is MemberAccessExpressionSyntax
            {
                Parent: AssignmentExpressionSyntax { RawKind: (int)SyntaxKind.SimpleAssignmentExpression } assignment
            } access
            && assignment.Left == access
                ? this.NameOfAnObjectIn(written)
                : null;

        this.NotesAWriteInThisFrame(
            name: name,
            use: written,
            from: written?.Span.End ?? 0,
            end: written?.Parent?.Parent?.Span.End ?? 0,
            member: member,
            state: state);

        SyntaxNode? use = written;

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
                    });
            }
        }
    }

    // The same, for the frame of this code. A sure write through a name replaces the state of the
    // member in each record that has the name. Where no record has it, the write makes one. Each
    // other write adds its state to each record of a member with the same name. A write that this
    // frame did not see takes each state away. The name is null for a write that is not sure here.
    // The arguments of a call run before its callee, and they can give a name a new object. Thus,
    // for a write of a callee, the name must hold its object up to the end of the arguments.
    private void NotesAWriteInThisFrame(
        (ISymbol Of, ISymbol? Owner, object? Index)? name,
        SyntaxNode? use,
        int from,
        int end,
        ISymbol member,
        LoopState state,
        int? before = null)
    {
        List<NamesOfOneObject> all = this.Frame.NamesOfOneObject;

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

            if (isOfIt)
            {
                held.Add(one);
                one.Members.RemoveAll(each => each.Member.Name == member.Name);
            }
            else
            {
                for (int i = 0; i < one.Members.Count; i++)
                {
                    if (one.Members[i].Member.Name == member.Name)
                    {
                        one.Members[i] = (one.Members[i].Member, one.Members[i].End, one.Members[i].State.Join(state));
                    }
                }
            }
        }

        if (name is not { } named || use == null || FunctionOf(use) is not { } code)
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
            one.Members.Add((member.OriginalDefinition, end, state));
        }
    }

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
    private LoopState? WrittenThroughANameOfItsObject(MemberAccessExpressionSyntax read)
    {
        if (this.Frame.NamesOfOneObject.Count == 0
            || this.quiet
            || this.Frame.NamesWritesSeen != this.region.MemberWrites
            || this.region.SymbolOf(read) is not { } member
            || !IsAMemberLocation(member))
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
            foreach ((ISymbol written, int end, LoopState state) in one.Members)
            {
                if (state is { Members: null, Objects.IsEmpty: true, Pending: false, Linked: false }
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
            IdentifierNameSyntax when this.TheDeclaredLocationOf(written) is { } location =>
                this.NameOfAnObjectIn(location),
            MemberAccessExpressionSyntax access
                when this.region.SymbolOf(access) is { } read
                     && (IsAMemberLocation(read) ? read : this.TheFieldBehind(read)) is { } member
                     && this.HoldsAnObject(member) =>
                IsAStaticLocation(member)
                    ? (member, null, null)
                    : access.Expression is IdentifierNameSyntax holder
                      && this.region.SymbolOf(holder) is { } owner
                      && KeepsItsObject(owner)
                        ? (member, owner, null)
                        : null,
            ElementAccessExpressionSyntax { ArgumentList.Arguments: { Count: 1 } indexes } element
                when this.region.TypeOf(element) is { } type
                     && this.CanBeAnObject(type)
                     && element.Expression is IdentifierNameSyntax array
                     && this.region.SymbolOf(array) is { } named
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
        || (symbol is IPropertySymbol { IsStatic: true } && IsAMemberLocation(symbol));

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
    // its collection, its index, or its owner a value, and no function gives them a value. For an
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
                       .Any(one => one.Gives && (one.InAFunction || (one.Position > name.From && one.Position < end))))
               && (name is { Index: null, Of: ILocalSymbol or IParameterSymbol or IFieldSymbol { IsReadOnly: true } }
                   || this.NothingBetweenChanges(name: name, after: name.From, before: end, ofTheName: true));
    }

    // True when no node between two positions can change the object of a name, or a member of an
    // object. Each such node is one that CannotChangeTheElement allows for the type of the
    // collection, of the owner, or of the name. A method of this compilation can come between where
    // CanStandBetween permits its code. This is so also when the method gets the collection or the
    // owner. That code puts nothing into an element, and the walk reads its writes to a member. When
    // the last argument is true, an assignment to the field of the name is no such node also. That
    // is so in the code between and in the code of such a method.
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
                                        this.CanStandBetween(
                                            method: method.ReducedFrom ?? method,
                                            collection: type,
                                            callees: MaxCalleesBetween,
                                            call: call,
                                            callee: null)
                                        && (!ofTheName
                                            || SetsNoName(
                                                method: method.ReducedFrom ?? method,
                                                callees: MaxCalleesBetween)),
                                    _ => this.CannotChangeTheElement(node: node, collection: type, callees: 0)
                                });

        // A member write of a callee is a write that the walk reads. An assignment to the field of
        // the name in the callee is not.
        bool SetsNoName(IMethodSymbol method, int callees) =>
            callees > 0
            && method.OriginalDefinition.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax()
                is MethodDeclarationSyntax declaration
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
