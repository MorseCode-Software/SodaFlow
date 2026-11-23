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

            IReadOnlyList<ExpressionSyntax> values = this.ValuesOfAMemberAt(member: member, code: code, at: value);

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
        }
    }

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
        bool inAFunction = false;

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

            inAFunction |= FunctionOf(assignment) != code;

            writes.Add(WriteOf(assignment));
        }

        if (writes.Count == 0)
        {
            return;
        }

        ExpressionSyntax? only = inAFunction ? null : LastWriteBefore(writes: writes, at: access, noCallBetween: true);
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
                is not VariableDeclaratorSyntax declarator
            || FunctionOf(declarator) is not { } itsCode)
        {
            return;
        }

        // A value can name the local again. That read gets no state.
        this.locals[local] = LoopState.None;
        given.Add(local);

        IReadOnlyList<ExpressionSyntax> values =
            this.ValuesOfALocalAt(local: local, declarator: declarator, code: itsCode, at: value);

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
    ///     or a node in a function, has no last write.
    /// </remarks>
    private IReadOnlyList<ExpressionSyntax> ValuesOfALocalAt(
        ISymbol local,
        VariableDeclaratorSyntax declarator,
        SyntaxNode code,
        SyntaxNode at)
    {
        List<(int Position, ExpressionSyntax? Value, StatementSyntax? Statement)> writes = [];

        if (declarator.Initializer is { Value: var initial })
        {
            writes.Add((declarator.SpanStart, initial, declarator.Parent?.Parent as LocalDeclarationStatementSyntax));
        }

        bool inAFunction = FunctionOf(at) != code;

        foreach ((int position, bool gives, bool function) in this.UsesOf(name: local, code: code))
        {
            if (!gives || this.NeverRuns(node: code.FindToken(position).Parent, code: code))
            {
                continue;
            }

            inAFunction |= function;

            writes.Add(
                code.FindToken(position).Parent
                    is IdentifierNameSyntax { Parent: AssignmentExpressionSyntax assignment } written
                && assignment.Left == written
                    ? WriteOf(assignment)
                    : (position, null, null));
        }

        return !inAFunction && LastWriteBefore(writes: writes, at: at, noCallBetween: false) is { } last
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
        bool inAFunction = false;

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

        if (!inAFunction && LastWriteBefore(writes: writes, at: at, noCallBetween: true) is { } last)
        {
            return [last];
        }

        return MemberBuilds.Of(
            member: member,
            type: at.FirstAncestorOrSelf<TypeDeclarationSyntax>() is { } type
                ? this.region.ModelFor(type).GetDeclaredSymbol(type) as ITypeSymbol
                : null);
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
    // writes again after the node. With noCallBetween, the statements from the write to the node
    // contain no call and no creation.
    private static ExpressionSyntax? LastWriteBefore(
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
                                   write.Position > at.SpanStart && around.Span.Contains(write.Position))))
        {
            return null;
        }

        return noCallBetween
               && block.Statements
                   .Where(each => each.SpanStart > statement.SpanStart && each.Span.End <= at.SpanStart)
                   .Any(static each => each.DescendantNodesAndSelf()
                       .Any(static node => node is InvocationExpressionSyntax or BaseObjectCreationExpressionSyntax))
            ? null
            : only;
    }
}
