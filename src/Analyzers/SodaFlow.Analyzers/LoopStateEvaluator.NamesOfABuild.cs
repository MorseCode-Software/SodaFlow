using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SodaFlow.Analyzers;

// The part of the evaluator for a local or a delegate that the value of a build names. WithTheBuildOfAMember reads
// that value in the code of a different method, and the walk did not read the code of the build.
internal sealed partial class LoopStateEvaluator
{
    /// <summary>
    ///     The state of the value of an assignment that can build a member. Each
    ///     local of the code of the assignment has the value that it has at the
    ///     assignment. Each delegate member of this that the value gives has
    ///     that value also.
    /// </summary>
    /// <remarks>
    ///     The walk reads the value in the code of a different method. There,
    ///     the locals of the build have no state. A member of this has no
    ///     state, or the state of this time. A map or a <c>Lazy</c> keeps the
    ///     delegate that it gets. Thus, a local or a delegate member has the
    ///     value of its last write before the assignment. When a different
    ///     path can give it a different value, it has the value of each write.
    /// </remarks>
    /// <param name="value">The value of the assignment.</param>
    private LoopState EvalABuild(ExpressionSyntax value)
    {
        List<(ISymbol Key, LoopState? Old)> members = [];

        this.GiveTheNamesOf(
            value: value,
            given: new HashSet<ISymbol>(SymbolEqualityComparer.Default),
            members: members);

        LoopState state = this.Eval(value);

        // A function that the value keeps reads a member of this when it runs. Thus, each member
        // gets the state of this time again.
        foreach ((ISymbol key, LoopState? old) in Enumerable.Reverse(members))
        {
            if (old == null)
            {
                this.thisMembers.Remove(key);
            }
            else
            {
                this.thisMembers[key] = old;
            }
        }

        return state;
    }

    // Gives a state to each local that a value names. Each delegate member of this that the value
    // names gets its state at the value also.
    private void GiveTheNamesOf(
        SyntaxNode value,
        HashSet<ISymbol> given,
        List<(ISymbol Key, LoopState? Old)> members)
    {
        SyntaxNode? code = FunctionOf(value);

        foreach (ExpressionSyntax name in value.DescendantNodesAndSelf().OfType<ExpressionSyntax>())
        {
            if (name is IdentifierNameSyntax local)
            {
                this.GiveTheLocal(name: local, value: value, given: given, members: members);
            }

            if (name is MemberAccessExpressionSyntax
                {
                    Expression: not (ThisExpressionSyntax or BaseExpressionSyntax)
                } ofADifferentObject
                && code != null)
            {
                this.GiveTheMemberOfADifferentObject(
                    access: ofADifferentObject,
                    code: code,
                    given: given,
                    members: members);
            }

            // The name of a member access is a part of the access, which the loop reads.
            if (name is not (IdentifierNameSyntax
                    or MemberAccessExpressionSyntax { Expression: ThisExpressionSyntax or BaseExpressionSyntax })
                || (name.Parent is MemberAccessExpressionSyntax { Name: var right } && right == name)
                || code == null
                || this.region.SymbolOf(name)
                    is not ({ IsStatic: false } member
                    and (IFieldSymbol { Type.TypeKind: TypeKind.Delegate }
                    or IPropertySymbol { Type.TypeKind: TypeKind.Delegate })))
            {
                continue;
            }

            ISymbol key = member.OriginalDefinition;

            if (members.Any(each => SymbolEqualityComparer.Default.Equals(x: each.Key, y: key)))
            {
                continue;
            }

            LoopState? old = this.thisMembers.TryGetValue(key: key, value: out LoopState? known) ? known : null;
            members.Add((key, old));

            // A value can name the member again. That read gets no state.
            this.thisMembers[key] = LoopState.None;

            // A lambda of the value reads the member when it runs, which can be after a different write.
            bool later = FunctionOf(name) != code;

            IReadOnlyList<ExpressionSyntax> values =
                this.ThroughTheSetters(this.ValuesOfAMemberAt(member: member, code: code, at: later ? name : value));

            foreach (ExpressionSyntax each in values)
            {
                this.GiveTheNamesOf(value: each, given: given, members: members);
            }

            // A method can run after a different method changed the member. Thus, the member of a
            // build in a method also has the state of this time. A constructor runs before each
            // method.
            this.thisMembers[key] = values.Aggregate(
                seed: code is ConstructorDeclarationSyntax || old == null ? LoopState.None : old,
                func: (all, each) => all.Join(this.Eval(each)));

            // The walk knows no state of the member at the read. Thus, the lambda reads each value
            // that the member can have.
            if (later && old == null)
            {
                members[members.Count - 1] = (key, this.thisMembers[key]);
            }
        }
    }

    // The value of a setter, as in set => this.factory = value, is each value that the class gives
    // its property.
    private IReadOnlyList<ExpressionSyntax> ThroughTheSetters(IReadOnlyList<ExpressionSyntax> values) =>
        values.Any(static each => each is IdentifierNameSyntax { Identifier.ValueText: "value" })
            ?
            [
                .. values.SelectMany(each =>
                    this.region.SymbolOf(each)
                        is IParameterSymbol
                        {
                            ContainingSymbol: IMethodSymbol
                            {
                                MethodKind: MethodKind.PropertySet, AssociatedSymbol: IPropertySymbol property
                            }
                        }
                        ? MemberBuilds.Of(member: property, type: property.ContainingType)
                        : (IEnumerable<ExpressionSyntax>)[each])
            ]
            : values;

    // Gives a state to a delegate member of a different object that a value names, at the value. The
    // function of the build writes that member through the same name, as in this.box.F = this.Add.
    // The last write before the value gives the only value, by the rule for a member of this.
    // Otherwise, each write in the function gives a value.
    private void GiveTheMemberOfADifferentObject(
        MemberAccessExpressionSyntax access,
        SyntaxNode code,
        HashSet<ISymbol> given,
        List<(ISymbol Key, LoopState? Old)> members)
    {
        if (this.memo.ContainsKey(access)
            || this.region.SymbolOf(access)
                is not ({ IsStatic: false } member
                and (IFieldSymbol { Type.TypeKind: TypeKind.Delegate }
                or IPropertySymbol { Type.TypeKind: TypeKind.Delegate }))
            || this.region.SymbolOf(Unwrapped(access.Expression)) is not { } owner)
        {
            return;
        }

        List<(int Position, ExpressionSyntax? Value, StatementSyntax? Statement)> writes = [];

        foreach (AssignmentExpressionSyntax assignment in code.DescendantNodes().OfType<AssignmentExpressionSyntax>())
        {
            // The name comes first, because it needs no binding.
            if (Unwrapped(assignment.Left) is not MemberAccessExpressionSyntax { Expression: var itsOwner } written
                || written.Name.Identifier.ValueText != member.Name
                || !SymbolEqualityComparer.Default.Equals(
                    x: this.region.SymbolOf(written)?.OriginalDefinition,
                    y: member.OriginalDefinition)
                || !SymbolEqualityComparer.Default.Equals(x: this.region.SymbolOf(Unwrapped(itsOwner)), y: owner))
            {
                continue;
            }

            writes.Add(WriteOf(assignment));
        }

        if (writes.Count == 0)
        {
            return;
        }

        // A write in a lambda or a local function is in no block around the value. Thus, it is never
        // the last write. It runs only through a call or a creation, and such a call between the last
        // write and the value stops that write.
        ExpressionSyntax? only = this.LastWriteBefore(writes: writes, at: access, noCallBetween: true);
        LoopState state = LoopState.None;

        // A map or a Lazy keeps the delegate that the member has at the build.
        foreach (ExpressionSyntax each in only != null
                     ? [only]
                     : writes.Select(static write => write.Value).OfType<ExpressionSyntax>())
        {
            this.GiveTheNamesOf(value: each, given: given, members: members);
            state = state.Join(this.Eval(each));
        }

        this.memo[access] = state;
    }

    // Gives a state to a local that a name in a value names, at the value. The state stays after the
    // build, because a function of the build can read the local after the build. A Lazy runs its
    // function at the first read of its value. Each build after it gives the local its state again.
    private void GiveTheLocal(
        SyntaxNode name,
        SyntaxNode value,
        HashSet<ISymbol> given,
        List<(ISymbol Key, LoopState? Old)> members)
    {
        if (this.region.SymbolOf(name) is not ILocalSymbol { RefKind: RefKind.None } local
            || given.Contains(local)
            || local.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax()
                is not ({ } declaration and (VariableDeclaratorSyntax or SingleVariableDesignationSyntax))
            || FunctionOf(declaration) is not { } itsCode)
        {
            return;
        }

        // A value can name the local again. That read gets no state.
        this.locals[local] = LoopState.None;
        given.Add(local);

        IReadOnlyList<ExpressionSyntax> values =
            this.ValuesOfALocalAt(local: local, declaration: declaration, code: itsCode, at: value);

        foreach (ExpressionSyntax each in values)
        {
            this.GiveTheNamesOf(value: each, given: given, members: members);
        }

        this.locals[local] = values.Aggregate(seed: LoopState.None, func: (all, each) => all.Join(this.Eval(each)));
    }

    /// <summary>
    ///     The values that a local can have at a node of its function.
    /// </summary>
    /// <remarks>
    ///     The last write before the node gives the only value when it is at
    ///     the first level of a block around the node. No loop around the node
    ///     can write the local after the node. Otherwise, each write gives a
    ///     value. A function can run at each time. Thus, a write in a function,
    ///     or a node in a function, has no last write. A lambda of the node
    ///     reads the local when it runs, also after the node. Thus, a read in
    ///     such a lambda also has no last write.
    /// </remarks>
    private IReadOnlyList<ExpressionSyntax> ValuesOfALocalAt(
        ISymbol local,
        SyntaxNode declaration,
        SyntaxNode code,
        SyntaxNode at)
    {
        List<(int Position, ExpressionSyntax? Value, StatementSyntax? Statement)> writes = [];

        switch (declaration)
        {
            case VariableDeclaratorSyntax { Initializer.Value: var initial }:
                writes.Add(
                    (declaration.SpanStart, initial, declaration.Parent?.Parent as LocalDeclarationStatementSyntax));

                break;
            // A pattern gives the local the value that it tests.
            case SingleVariableDesignationSyntax
            {
                Parent: DeclarationPatternSyntax { Parent: IsPatternExpressionSyntax test }
            }:
                writes.Add((declaration.SpanStart, test.Expression, null));

                break;
            case SingleVariableDesignationSyntax { Parent: DeclarationExpressionSyntax declared }:
                writes.AddRange(this.WritesOf(declared));

                break;
            case SingleVariableDesignationSyntax:
                writes.Add((declaration.SpanStart, null, null));

                break;
        }

        bool inAFunction = FunctionOf(at) != code;

        foreach ((int position, bool gives, bool function) in this.UsesOf(name: local, code: code))
        {
            if (!gives || this.NeverRuns(node: code.FindToken(position).Parent, code: code))
            {
                inAFunction |= function && at.Span.Contains(position);

                continue;
            }

            inAFunction |= function;

            writes.AddRange(
                code.FindToken(position).Parent is IdentifierNameSyntax written
                    ? this.WritesOf(written)
                    : [(position, null, null)]);
        }

        return !inAFunction && this.LastWriteBefore(writes: writes, at: at, noCallBetween: false) is { } last
            ? new[] { last }
            : writes.Select(static write => write.Value).OfType<ExpressionSyntax>().ToArray();
    }

    // True when a node is in a branch of an if statement that never runs. That is the statement of a
    // constant false condition, or the else of a constant true condition. A write there gives no value.
    private bool NeverRuns(SyntaxNode? node, SyntaxNode code)
    {
        for (SyntaxNode? current = node; current != null && current != code; current = current.Parent)
        {
            if (current.Parent is IfStatementSyntax branch
                && (current == branch.Statement || current == branch.Else)
                && SinkWalker.ConstantOf(region: this.region, condition: branch.Condition) == (current == branch.Else))
            {
                return true;
            }
        }

        return false;
    }

    // The writes that the code makes where it writes a name. An assignment, a part of a
    // deconstruction of a tuple literal, and an out argument of a method of this compilation give
    // their values. Each other write gives no value that the walk can read. A write with more than
    // one value gives each of them. It then gives a write with no value, because no value of it is
    // the only one.
    private IEnumerable<(int Position, ExpressionSyntax? Value, StatementSyntax? Statement)> WritesOf(
        SyntaxNode written) =>
        written.Parent switch
        {
            AssignmentExpressionSyntax assignment when assignment.Left == written => [WriteOf(assignment)],
            ArgumentSyntax
                {
                    Parent: TupleExpressionSyntax
                    {
                        Parent: AssignmentExpressionSyntax
                        {
                            RawKind: (int)SyntaxKind.SimpleAssignmentExpression,
                            Right: TupleExpressionSyntax values
                        } deconstruction
                    } parts
                } part
                when deconstruction.Left == parts && values.Arguments.Count == parts.Arguments.Count =>
            [
                (written.SpanStart, values.Arguments[parts.Arguments.IndexOf(part)].Expression,
                    deconstruction.Parent as ExpressionStatementSyntax)
            ],
            ArgumentSyntax
                {
                    RefKindKeyword.RawKind: (int)SyntaxKind.OutKeyword,
                    Parent: ArgumentListSyntax { Parent: InvocationExpressionSyntax call }
                } argument
                when this.ValuesOfAnOut(argument: argument, call: call) is { Count: > 0 } given =>
                given.Count == 1
                    ? [(written.SpanStart, given[0], call.Parent as ExpressionStatementSyntax)]
                    : [.. given.Select(each => (written.SpanStart, (ExpressionSyntax?)each, (StatementSyntax?)null)),
                        (written.SpanStart, null, null)],
            _ => [(written.SpanStart, null, null)]
        };

    // The values that a call gives an out argument. The callee is a method of this compilation, and each
    // assignment to the parameter in its code gives a value. A value that is a parameter of the callee is the
    // argument for that parameter. Each other value names the object of the callee. Thus, it counts only for a
    // static callee or a callee on this. Null when a value is not known.
    private IReadOnlyList<ExpressionSyntax>? ValuesOfAnOut(ArgumentSyntax argument, InvocationExpressionSyntax call)
    {
        SeparatedSyntaxList<ArgumentSyntax> arguments = call.ArgumentList.Arguments;

        if (this.region.SymbolOf(call) is not IMethodSymbol { ReducedFrom: null } method
            || BodyOf(method.OriginalDefinition) is not { } body
            || method.OriginalDefinition.Parameters.FirstOrDefault(each => ArgumentOfTheCallFor(each) == argument)
                is not { } parameter)
        {
            return null;
        }

        bool ofThisObject =
            method.IsStatic
            || call.Expression
                is IdentifierNameSyntax
                or MemberAccessExpressionSyntax { Expression: ThisExpressionSyntax };

        List<ExpressionSyntax> values = [];

        foreach (AssignmentExpressionSyntax assignment in
                 body.DescendantNodesAndSelf().OfType<AssignmentExpressionSyntax>())
        {
            if (!assignment.IsKind(SyntaxKind.SimpleAssignmentExpression)
                || Unwrapped(assignment.Left) is not IdentifierNameSyntax { Identifier.ValueText: var name }
                || name != parameter.Name
                || !SymbolEqualityComparer.Default.Equals(x: this.region.SymbolOf(assignment.Left), y: parameter))
            {
                continue;
            }

            if (this.region.SymbolOf(Unwrapped(assignment.Right)) is IParameterSymbol source
                && SymbolEqualityComparer.Default.Equals(x: source.ContainingSymbol, y: method.OriginalDefinition))
            {
                if (ArgumentOfTheCallFor(source) is not { } fromTheCall)
                {
                    return null;
                }

                values.Add(fromTheCall.Expression);
            }
            else if (ofThisObject)
            {
                values.Add(assignment.Right);
            }
            else
            {
                return null;
            }
        }

        return values;

        // An argument with a name goes to the parameter with that name. Each other argument goes to
        // the parameter at its position.
        ArgumentSyntax? ArgumentOfTheCallFor(IParameterSymbol of) =>
            arguments.FirstOrDefault(each => each.NameColon?.Name.Identifier.ValueText == of.Name)
            ?? (of.Ordinal < arguments.Count && arguments[of.Ordinal].NameColon == null
                ? arguments[of.Ordinal]
                : null);
    }

    /// <summary>
    ///     The values that a delegate member of this can have at a node.
    /// </summary>
    /// <remarks>
    ///     The last write in the function of the node gives the only value, as
    ///     for a local. A call between the write and the node can change the
    ///     member, thus no call can come between. Otherwise, each assignment in
    ///     the code of the type gives a value: see <see cref="MemberBuilds" />.
    /// </remarks>
    private IReadOnlyList<ExpressionSyntax> ValuesOfAMemberAt(ISymbol member, SyntaxNode code, SyntaxNode at)
    {
        List<(int Position, ExpressionSyntax? Value, StatementSyntax? Statement)> writes = [];
        bool inAFunction = FunctionOf(at) != code;

        foreach (AssignmentExpressionSyntax assignment in code.DescendantNodes().OfType<AssignmentExpressionSyntax>())
        {
            // The name comes first, because it needs no binding.
            if (Unwrapped(assignment.Left) switch
                {
                    IdentifierNameSyntax name => name,
                    MemberAccessExpressionSyntax
                    {
                        Expression: ThisExpressionSyntax or BaseExpressionSyntax, Name: IdentifierNameSyntax name
                    } => name,
                    _ => null
                } is not { } written
                || written.Identifier.ValueText != member.Name
                || !SymbolEqualityComparer.Default.Equals(
                    x: this.region.SymbolOf(assignment.Left)?.OriginalDefinition,
                    y: member.OriginalDefinition))
            {
                continue;
            }

            inAFunction |= FunctionOf(assignment) != code;

            writes.Add(WriteOf(assignment));
        }

        foreach (ArgumentSyntax argument in code.DescendantNodes().OfType<ArgumentSyntax>())
        {
            // The name comes first, because it needs no binding.
            if (!GivesAValue(argument.Expression)
                || Unwrapped(argument.Expression) switch
                {
                    IdentifierNameSyntax name => name,
                    MemberAccessExpressionSyntax
                    {
                        Expression: ThisExpressionSyntax or BaseExpressionSyntax, Name: IdentifierNameSyntax name
                    } => name,
                    _ => null
                } is not { } written
                || written.Identifier.ValueText != member.Name
                || !SymbolEqualityComparer.Default.Equals(
                    x: this.region.SymbolOf(argument.Expression)?.OriginalDefinition,
                    y: member.OriginalDefinition))
            {
                continue;
            }

            inAFunction |= FunctionOf(argument) != code;

            writes.AddRange(this.WritesOf(argument.Expression));
        }

        if (!inAFunction && this.LastWriteBefore(writes: writes, at: at, noCallBetween: true) is { } last)
        {
            return [last];
        }

        // A value of an out argument comes from the code of the callee, which MemberBuilds does not read.
        return
        [
            .. MemberBuilds.Of(
                    member: member,
                    type: at.FirstAncestorOrSelf<TypeDeclarationSyntax>() is { } type
                        ? this.region.ModelFor(type).GetDeclaredSymbol(type) as ITypeSymbol
                        : null)
                .Concat(writes.Select(static write => write.Value).OfType<ExpressionSyntax>())
                .Distinct()
        ];
    }

    // A write of an assignment, with the value that it gives and its statement. A `+=` adds a
    // function, and a `??=` can give its value. Thus, each of the two gives a value with no
    // statement, and that value joins the value of each other write. Each other assignment gives no
    // value that the walk can read.
    private static (int Position, ExpressionSyntax? Value, StatementSyntax? Statement) WriteOf(
        AssignmentExpressionSyntax assignment) =>
        assignment.Kind() switch
        {
            SyntaxKind.SimpleAssignmentExpression =>
                (assignment.SpanStart, assignment.Right, assignment.Parent as ExpressionStatementSyntax),
            SyntaxKind.AddAssignmentExpression or SyntaxKind.CoalesceAssignmentExpression =>
                (assignment.SpanStart, assignment.Right, null),
            _ => (assignment.SpanStart, null, null)
        };

    // The value of the last write before a node, when it is the only value that the node can see.
    // The write is at the first level of a block around the node, and no loop around the node
    // writes again after the node. A goto in the block can go back over the node, as a loop can.
    // With noCallBetween, the statements from the write to the node contain no call, no creation,
    // and no assignment through a setter with code.
    private ExpressionSyntax? LastWriteBefore(
        IReadOnlyCollection<(int Position, ExpressionSyntax? Value, StatementSyntax? Statement)> writes,
        SyntaxNode at,
        bool noCallBetween)
    {
        (int Position, ExpressionSyntax? Value, StatementSyntax? Statement) last =
            writes.Where(write => write.Position < at.SpanStart)
                .OrderBy(static write => write.Position)
                .LastOrDefault();

        if (last is not { Value: { } only, Statement: { Parent: BlockSyntax block } statement }
            || !at.Ancestors().Contains(block)
            || at.Ancestors()
                .TakeWhile(around => around != block)
                .Any(around => around is ForStatementSyntax
                                   or CommonForEachStatementSyntax
                                   or WhileStatementSyntax
                                   or DoStatementSyntax
                               && writes.Any(write =>
                                   write.Position > at.SpanStart && around.Span.Contains(write.Position)))
            || (writes.Any(write => write.Position > at.SpanStart && block.Span.Contains(write.Position))
                && block.DescendantNodes().Any(static node => node.IsKind(SyntaxKind.GotoStatement))))
        {
            return null;
        }

        return noCallBetween
               && block.Statements
                   .Where(each => each.SpanStart > statement.SpanStart && each.Span.End <= at.SpanStart)
                   .Any(each => each.DescendantNodesAndSelf()
                       .Any(node => node is InvocationExpressionSyntax or BaseObjectCreationExpressionSyntax
                                    || (node is AssignmentExpressionSyntax { Left: var target }
                                        && this.region.SymbolOf(target) is IPropertySymbol { SetMethod: { } setter }
                                        && setter.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax()
                                            is AccessorDeclarationSyntax { Body: not null }
                                            or AccessorDeclarationSyntax { ExpressionBody: not null })))
            ? null
            : only;
    }
}
