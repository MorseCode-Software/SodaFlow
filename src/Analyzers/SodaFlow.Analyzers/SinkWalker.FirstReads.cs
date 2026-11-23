using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SodaFlow.Analyzers;

// The part of the walker that records the first read of a cell. That record has its own rule for
// where a read runs. The record of writes does not count a try block. A call in the block can throw
// before the write, and a catch clause then runs. With no catch clause, the code after the try runs
// only when the block runs to its end.
internal sealed partial class SinkWalker
{
    // Each if on a constant that the walk read. The walk reads only the branch that the constant
    // selects.
    private readonly HashSet<IfStatementSyntax> alwaysTaken = [];

    // The marks of the first reads in a try block, which the walk applies at the end of that try.
    private List<(TryStatementSyntax After, Action Marks)>? afterTheTry;

    // The marks of the first reads in each turn of a loop, with the body of that loop.
    private List<(SyntaxNode Turn, ComputedValue Cell)>? turnMarks;

    // Each build of two or more cells with a record of a read through a name.
    private List<ComputedValue>? runsAtNames;

    private bool AlwaysRunsAt(SyntaxNode place, out SyntaxNode? turn) =>
        this.AlwaysRunsAt(
            place: place,
            forAFirstRead: false,
            forACall: false,
            forARun: false,
            turn: out turn,
            after: out _);

    /// <summary>
    ///     What records the run of each cell that a first read runs. Null when
    ///     the walk cannot know that the read runs.
    /// </summary>
    /// <remarks>
    ///     A read in a try block with no catch clause runs before the code
    ///     after that try. A read in each turn of a loop runs before the next
    ///     turn. It also runs before the code after a loop that always has a
    ///     first turn.
    /// </remarks>
    private Action<ComputedValue, ReadKey?>? MarksOfAFirstRead(SyntaxNode read)
    {
        if (WhereItAlwaysRuns(expression: read, constants: this.region) is not { } place)
        {
            return null;
        }

        bool always =
            this.AlwaysRunsAt(
                place: place,
                forAFirstRead: true,
                forACall: false,
                forARun: false,
                turn: out SyntaxNode? turn,
                after: out TryStatementSyntax? after);

        // A callee that runs a lambda for each element can run it no time. The walk does not record
        // the end of such a turn.
        // A build of two or more cells records only a read through a name that always runs.
        Action<ComputedValue, ReadKey?>? marks =
            always
                ? this.Runs
                : turn is { Parent: { } loop } && LoopStateEvaluator.LoopBodyOf(loop) == turn
                    ? (cell, _) => this.RunsInATurn(cell: cell, turn: turn)
                    : null;

        return marks == null || after == null
            ? marks
            : (cell, key) => (this.afterTheTry ??= []).Add((after, () => marks(cell, key)));
    }

    private void Runs(ComputedValue cell, ReadKey? key)
    {
        if (!cell.ManyCells)
        {
            cell.Runs();
        }
        else if (key != null)
        {
            cell.RunsAt(key);
            (this.runsAtNames ??= []).Add(cell);
        }
    }

    // Removes the records of the reads through a name that the code gives a value. The name then holds a
    // different cell.
    private void ForgetsTheRunsThrough(ExpressionSyntax written)
    {
        if (this.runsAtNames is not { Count: > 0 })
        {
            return;
        }

        switch (LoopStateEvaluator.Unwrapped(written))
        {
            case TupleExpressionSyntax parts:
                foreach (ArgumentSyntax part in parts.Arguments)
                {
                    this.ForgetsTheRunsThrough(part.Expression);
                }

                break;
            case ElementAccessExpressionSyntax element:
                this.ForgetsTheRunsThrough(element.Expression);

                break;
            case var name when this.region.SymbolOf(name) is { } symbol:
                this.ForgetsTheRunsOf(symbol);

                break;
        }
    }

    private void ForgetsTheRunsOf(ISymbol name)
    {
        foreach (ComputedValue cell in this.runsAtNames ?? [])
        {
            cell.Forgets(name);
        }
    }

    // True when a statement or a clause always runs the code in it that holds a node. The walk reads
    // an else clause only when the constant of its if selects it. A switch with one section always
    // runs it when it matches each value and no break of the switch comes before the node. A goto
    // before the node is an exit of the function, which the walk finds at the function. A goto to
    // the label after the node can only run the labeled statement again.
    private bool AlwaysRunsItsCode(SyntaxNode construct, SyntaxNode place) =>
        construct switch
        {
            // The walk reads only the branch that the constant of an if selects.
            IfStatementSyntax branch => this.alwaysTaken.Contains(branch),
            ElseClauseSyntax { Parent: { } choice } => this.AlwaysRunsItsCode(construct: choice, place: place),
            SwitchSectionSyntax { Parent: SwitchStatementSyntax { Sections.Count: 1 } choice } section =>
                section.Labels.Any(this.MatchesEachValue)
                && !JumpsOutBefore(construct: choice, code: section, at: place.SpanStart),
            // The walk gets to the switch only from a section that always runs.
            SwitchStatementSyntax => true,
            LabeledStatementSyntax => true,
            _ => false
        };

    /// <summary>
    ///     True when each run of the function runs the first turn of a loop. The
    ///     loop always has a first turn, and each loop around it has one also.
    ///     A try with no catch clause around the loop can end before the loop.
    /// </summary>
    /// <param name="turn">
    ///     The body of the loop, or the body of a lambda that a callee runs for
    ///     each element. Such a lambda has no first turn.
    /// </param>
    public bool AlwaysRunsTheFirstTurnOf(SyntaxNode turn) =>
        turn.Parent is { } loop
        && this.HasAFirstTurn(loop)
        && this.AlwaysRunsAt(
            place: loop,
            forAFirstRead: true,
            forACall: false,
            forARun: false,
            turn: out _,
            after: out TryStatementSyntax? after)
        && after == null;

    private void RunsInATurn(ComputedValue cell, SyntaxNode turn)
    {
        cell.RunsInATurn();
        (this.turnMarks ??= []).Add((turn, cell));
    }

    // Removes the marks of the turns of a loop at its end. The loop can have no turn.
    private void EndsTheTurnsOf(SyntaxNode body)
    {
        if (this.turnMarks is not { Count: > 0 } marks)
        {
            return;
        }

        foreach ((SyntaxNode _, ComputedValue cell) in marks.Where(mark => mark.Turn == body))
        {
            cell.LeavesATurn();
        }

        marks.RemoveAll(mark => mark.Turn == body);
    }

    public override void VisitTryStatement(TryStatementSyntax node)
    {
        base.VisitTryStatement(node);

        if (this.afterTheTry is not { Count: > 0 } waiting)
        {
            return;
        }

        foreach ((TryStatementSyntax _, Action marks) in waiting.Where(mark => mark.After == node).ToList())
        {
            marks();
        }

        waiting.RemoveAll(mark => mark.After == node);
    }

    /// <summary>
    ///     True when a loop always runs its first turn. A do loop, a while loop
    ///     on the constant true, and a for loop with no condition have one. A
    ///     for loop from a constant up to a constant that it reaches has one
    ///     also. A foreach over an array with an element has one too. That
    ///     array is an array literal, a collection expression of an array type,
    ///     or an array creation with constant sizes larger than zero. It can
    ///     also be the value of a local that no code writes.
    /// </summary>
    private bool HasAFirstTurn(SyntaxNode loop) =>
        loop switch
        {
            DoStatementSyntax => true,
            WhileStatementSyntax repeat => this.ConstantOf(repeat.Condition) == true,
            ForStatementSyntax counted => counted.Condition == null || this.CountsUpFromBelow(counted),
            ForEachStatementSyntax each => this.HasAnElement(LoopStateEvaluator.Unwrapped(each.Expression)),
            _ => false
        };

    // The compiler needs an element to give an implicit array its type. An element that starts with
    // .. can give no item.
    // The value of a local that no code writes is the value of its declaration. For each other name,
    // the evaluator gives the name itself.
    private bool HasAnElement(ExpressionSyntax array) =>
        array switch
        {
            ArrayCreationExpressionSyntax { Initializer: { } items } => items.Expressions.Count > 0,
            ArrayCreationExpressionSyntax { Type.RankSpecifiers: { Count: > 0 } ranks } => ranks[0].Sizes.All(size =>
                this.region.ModelFor(size).GetConstantValue(size) is { Value: > 0 }),
            ImplicitArrayCreationExpressionSyntax => true,
            // The compiler gives a collection expression in a cast no type. The cast has the type.
            CollectionExpressionSyntax collection =>
                collection.Elements.Any(static element => element is ExpressionElementSyntax)
                && (collection.Parent is CastExpressionSyntax cast
                    ? this.region.ModelFor(cast).GetTypeInfo(cast).Type
                    : this.region.ModelFor(collection).GetTypeInfo(collection).ConvertedType) is IArrayTypeSymbol,
            IdentifierNameSyntax name =>
                this.evaluator.ValueOfALocalThatNoCodeWrites(name) is not IdentifierNameSyntax and var value
                && this.HasAnElement(value),
            _ => false
        };

    // True for a for loop whose condition compares a variable of the loop to a constant with < or
    // <=. The variable starts at a constant that meets the condition.
    private bool CountsUpFromBelow(ForStatementSyntax loop) =>
        loop is
        {
            Declaration.Variables: var variables,
            Condition: BinaryExpressionSyntax
            {
                RawKind: (int)SyntaxKind.LessThanExpression or (int)SyntaxKind.LessThanOrEqualExpression,
                Left: IdentifierNameSyntax name,
                Right: var end
            } condition
        }
        && variables.FirstOrDefault(variable => variable.Identifier.ValueText == name.Identifier.ValueText)
            is { Initializer.Value: var start }
        && this.region.ModelFor(loop).GetConstantValue(start) is { Value: int from }
        && this.region.ModelFor(loop).GetConstantValue(end) is { Value: int to }
        && (condition.IsKind(SyntaxKind.LessThanExpression) ? from < to : from <= to);
}
