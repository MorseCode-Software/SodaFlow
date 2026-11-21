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
    ///     assignment.
    /// </summary>
    /// <remarks>
    ///     The walk reads the value in the code of a different method. There,
    ///     the locals of the build have no state. A map or a <c>Lazy</c> keeps
    ///     the delegate that it gets. Thus, a local has the value of its last
    ///     write before the assignment. When a different path can give it a
    ///     different value, it has the value of each write.
    /// </remarks>
    /// <param name="value">The value of the assignment.</param>
    private LoopState EvalABuild(ExpressionSyntax value)
    {
        this.GiveTheNamesOf(value: value, given: new HashSet<ISymbol>(SymbolEqualityComparer.Default));

        return this.Eval(value);
    }

    // Gives a state to each local that a value names.
    private void GiveTheNamesOf(SyntaxNode value, HashSet<ISymbol> given)
    {
        foreach (IdentifierNameSyntax name in value.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>())
        {
            this.GiveTheLocal(name: name, value: value, given: given);
        }
    }

    // Gives a state to a local that a name in a value names, at the value. The state stays after the
    // build, because a function of the build can read the local after the build. A Lazy runs its
    // function at the first read of its value. Each build after it gives the local its state again.
    private void GiveTheLocal(SyntaxNode name, SyntaxNode value, HashSet<ISymbol> given)
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
            this.GiveTheNamesOf(value: each, given: given);
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
            if (!gives)
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

        return !inAFunction && LastWriteBefore(writes: writes, at: at) is { } last
            ? new[] { last }
            : writes.Select(static write => write.Value).OfType<ExpressionSyntax>().ToArray();
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
    // writes again after the node.
    private static ExpressionSyntax? LastWriteBefore(
        IReadOnlyCollection<(int Position, ExpressionSyntax? Value, StatementSyntax? Statement)> writes,
        SyntaxNode at)
    {
        (int Position, ExpressionSyntax? Value, StatementSyntax? Statement) last =
            writes.Where(write => write.Position < at.SpanStart)
                .OrderBy(static write => write.Position)
                .LastOrDefault();

        return last is { Value: { } only, Statement.Parent: BlockSyntax block }
               && at.Ancestors().Contains(block)
               && !at.Ancestors()
                   .TakeWhile(around => around != block)
                   .Any(around => around is ForStatementSyntax
                                      or CommonForEachStatementSyntax
                                      or WhileStatementSyntax
                                      or DoStatementSyntax
                                  && writes.Any(write =>
                                      write.Position > at.SpanStart && around.Span.Contains(write.Position)))
            ? only
            : null;
    }
}
