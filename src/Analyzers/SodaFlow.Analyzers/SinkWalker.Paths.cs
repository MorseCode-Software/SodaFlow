using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SodaFlow.Analyzers;

// The part of the walker that finds code that runs on each path of its function. A callee runs the
// timers or disposes the objects of its caller only there.
internal sealed partial class SinkWalker
{
    // The key is a function with a statement that can end its call before its last statement. The
    // value has each such statement, with the position of the label that a goto goes to.
    private readonly Dictionary<SyntaxNode, List<(SyntaxNode Exit, int Label)>> exitsOf = [];

    // The key is a switch statement that the walk asked about. The value is the section that each
    // value of the switch selects, or null when the walk does not know one.
    private readonly Dictionary<SwitchStatementSyntax, SwitchSectionSyntax?> sectionsOfEachValue = [];

    /// <summary>
    ///     True when each run of the function of a call runs the call one time
    ///     or more.
    /// </summary>
    /// <remarks>
    ///     A loop that always has a first turn runs the call in that turn. A
    ///     <c>ForEach</c> over a collection with an element runs its lambda one
    ///     time or more also.
    /// </remarks>
    /// <param name="call">The call.</param>
    /// <param name="forARun">
    ///     True for a run of the timers or a Dispose. A null-conditional access
    ///     to a value with an object of the store then runs the call: see
    ///     ValueOf.
    /// </param>
    /// <param name="after">
    ///     The try with no catch clause whose block has the call. The code after
    ///     that try runs after the call, but its finally block can run without
    ///     the call. Null when no such try is around the call.
    /// </param>
    public bool SurelyRuns(SyntaxNode call, bool forARun, out TryStatementSyntax? after)
    {
        after = null;
        SyntaxNode start = call;

        while (forARun
               && start.Parent is ConditionalAccessExpressionSyntax access
               && access.WhenNotNull == start
               && this.evaluator.HasAnObject(access.Expression))
        {
            start = access;
        }

        return WhereItAlwaysRuns(expression: start, constants: null) is { } place
               && this.SurelyRunsAt(place: place, forARun: forARun, after: out after);
    }

    // The same for a statement, or for the expression that is the body of a function.
    private bool SurelyRunsAt(SyntaxNode place, bool forARun, out TryStatementSyntax? after)
    {
        after = null;

        for (SyntaxNode? at = place; at != null;)
        {
            bool always =
                this.AlwaysRunsAt(
                    place: at,
                    forAFirstRead: false,
                    forACall: true,
                    forARun: forARun,
                    turn: out SyntaxNode? turn,
                    after: out TryStatementSyntax? around);

            // Each node that the walk asks about contains the node before it. Thus, the try that
            // the walk found last contains each other try.
            after = around ?? after;

            if (always)
            {
                return true;
            }

            at = turn?.Parent switch
            {
                { } loop when LoopStateEvaluator.LoopBodyOf(loop) == turn && this.HasATurn(loop) => loop,
                AnonymousFunctionExpressionSyntax lambda when this.ForEachOfAnElement(lambda) is { } each =>
                    WhereItAlwaysRuns(expression: each, constants: null),
                _ => null
            };
        }

        return false;
    }

    // True when each run of the function of a remove of a handler runs it. AlwaysRuns finds it with
    // no turn, or the remove runs as a call runs. AfterTheLastRemove asks that each add and the remove
    // run one time: see RunsStraightIn.
    private bool RemovesOnEachPath(SyntaxNode remove) =>
        (this.AlwaysRuns(assignment: remove, turn: out SyntaxNode? turn) && turn == null)
        || (remove.Parent is ExpressionStatementSyntax statement
            && this.SurelyRunsAt(place: statement, forARun: false, after: out _));

    // A foreach over a collection whose positions the store knows has a first turn when the
    // collection has an element.
    private bool HasATurn(SyntaxNode loop) =>
        this.HasAFirstTurn(loop)
        || (loop is ForEachStatementSyntax each && this.evaluator.HasAnElement(each.Expression));

    // The call of ForEach whose one argument is a lambda, where the receiver has an element. The walk
    // read the lambda where it is, thus the callee runs it for each element. Null for each other call.
    private InvocationExpressionSyntax? ForEachOfAnElement(SyntaxNode lambda) =>
        lambda.Parent is ArgumentSyntax
        {
            Parent: ArgumentListSyntax
            {
                Arguments.Count: 1,
                Parent: InvocationExpressionSyntax
                {
                    Expression: MemberAccessExpressionSyntax
                    {
                        Name.Identifier.ValueText: "ForEach",
                        Expression: var receiver
                    }
                } call
            }
        }
        && (HasAWrittenElement(receiver) || this.evaluator.HasAnElement(receiver))
            ? call
            : null;

    // A creation with an element in its initializer.
    private static bool HasAWrittenElement(ExpressionSyntax collection) =>
        LoopStateEvaluator.Unwrapped(collection) is BaseObjectCreationExpressionSyntax
        {
            Initializer.Expressions.Count: > 0
        };

    // True when a statement before a position can end a call of a function. For a call, a goto to a
    // label at or before the position is no such statement. The code from the label goes on to the
    // position. A goto can skip a statement that gives a location the value of a write. Thus, a
    // write does not have this rule.
    private bool ExitsBefore(SyntaxNode function, int place, bool forACall, bool forARun)
    {
        if (this.FirstExitOf(function) >= place)
        {
            return false;
        }

        if (!forACall)
        {
            return true;
        }

        if (!this.exitsOf.TryGetValue(key: function, value: out List<(SyntaxNode Exit, int Label)>? exits))
        {
            exits =
            [
                .. CodeOf(function)
                    .Where(static node =>
                        node is ReturnStatementSyntax or GotoStatementSyntax
                        || node.IsKind(SyntaxKind.YieldBreakStatement))
                    .Select(node => (node, LabelOf(exit: node, function: function)))
            ];

            this.exitsOf[function] = exits;
        }

        return exits.Any(exit =>
            exit.Exit.SpanStart < place
            && exit.Label > place
            && !this.RulesOut(node: exit.Exit, function: function, forARun: forARun));
    }

    // True when the walk knows that an if around a node in a function does not run the part that has
    // the node. For a call, the walk knows the value of more conditions: see ValueOf.
    private bool RulesOut(SyntaxNode node, SyntaxNode function, bool forARun)
    {
        SyntaxNode child = node;

        foreach (SyntaxNode around in node.Ancestors().TakeWhile(around => around != function))
        {
            if (around is IfStatementSyntax branch
                && (child == branch.Statement || child == branch.Else)
                && this.ValueOf(condition: branch.Condition, forARun: forARun) == (child == branch.Else))
            {
                return true;
            }

            child = around;
        }

        return false;
    }

    // True when an if runs a part of it, the statement or the else clause. The walk reads only the
    // statement of an if on the constant true, and only the else clause of an if on the constant
    // false. For a call, the walk knows the value of more conditions: see ValueOf.
    private bool RunsThePartOf(IfStatementSyntax branch, SyntaxNode part, bool forACall, bool forARun) =>
        part == branch.Else
            ? this.alwaysTaken.Contains(branch)
              || (forACall && this.ValueOf(condition: branch.Condition, forARun: forARun) == false)
            : this.alwaysTaken.Contains(branch)
              || (forACall
                  && part == branch.Statement
                  && this.ValueOf(condition: branch.Condition, forARun: forARun) == true);

    /// <summary>
    ///     The value of a condition, where the walk knows it for a call. Null
    ///     for each other condition.
    /// </summary>
    /// <remarks>
    ///     A constant gives the value, and so does a parameter whose argument
    ///     is a constant: see LoopStateEvaluator.ArgumentConstantOf. For a run,
    ///     a value with an object of the store is not null. The value can be
    ///     null at run time. Thus, a run counts where it maybe does not run,
    ///     but it does not count less. A cancel does not use this rule.
    /// </remarks>
    /// <param name="condition">The condition.</param>
    /// <param name="forARun">True for a run of the timers or a Dispose.</param>
    private bool? ValueOf(SyntaxNode condition, bool forARun) =>
        this.KnownValueOf(expression: condition, forARun: forARun) is { HasValue: true, Value: bool value }
            ? value
            : null;

    private Optional<object?> KnownValueOf(SyntaxNode expression, bool forARun) =>
        expression switch
        {
            PrefixUnaryExpressionSyntax { RawKind: (int)SyntaxKind.LogicalNotExpression } not =>
                this.KnownValueOf(expression: not.Operand, forARun: forARun) is { Value: bool operand }
                    ? !operand
                    : default(Optional<object?>),
            BinaryExpressionSyntax comparison => this.ValueOfAComparison(comparison: comparison, forARun: forARun),
            IdentifierNameSyntax name when this.region.SymbolOf(name) is IParameterSymbol parameter =>
                this.evaluator.ArgumentConstantOf(parameter),
            LiteralExpressionSyntax => this.region.ModelFor(expression).GetConstantValue(expression),
            _ => default
        };

    // The value when the walk compares two known values. For a run, the walk also knows the value
    // when it compares null with a value that has an object of the store.
    private Optional<object?> ValueOfAComparison(BinaryExpressionSyntax comparison, bool forARun)
    {
        bool isEqual = comparison.IsKind(SyntaxKind.EqualsExpression);

        if (forARun
            && (isEqual || comparison.IsKind(SyntaxKind.NotEqualsExpression))
            && (IsNull(comparison.Right) ? comparison.Left : IsNull(comparison.Left) ? comparison.Right : null)
                is { } compared
            && this.evaluator.HasAnObject(compared))
        {
            return !isEqual;
        }

        if (this.KnownValueOf(expression: comparison.Left, forARun: forARun) is not { HasValue: true } left
            || this.KnownValueOf(expression: comparison.Right, forARun: forARun) is not { HasValue: true } right)
        {
            return default;
        }

        if (IsAnInteger(left.Value) && IsAnInteger(right.Value))
        {
            int order =
                Convert.ToDecimal(value: left.Value, provider: CultureInfo.InvariantCulture)
                    .CompareTo(Convert.ToDecimal(value: right.Value, provider: CultureInfo.InvariantCulture));

            return (SyntaxKind)comparison.RawKind switch
            {
                SyntaxKind.EqualsExpression => order == 0,
                SyntaxKind.NotEqualsExpression => order != 0,
                SyntaxKind.LessThanExpression => order < 0,
                SyntaxKind.LessThanOrEqualExpression => order <= 0,
                SyntaxKind.GreaterThanExpression => order > 0,
                SyntaxKind.GreaterThanOrEqualExpression => order >= 0,
                _ => default(Optional<object?>)
            };
        }

        return (SyntaxKind)comparison.RawKind switch
        {
            SyntaxKind.EqualsExpression => Equals(objA: left.Value, objB: right.Value),
            SyntaxKind.NotEqualsExpression => !Equals(objA: left.Value, objB: right.Value),
            _ => default(Optional<object?>)
        };
    }

    private static bool IsNull(ExpressionSyntax expression) =>
        expression is LiteralExpressionSyntax { RawKind: (int)SyntaxKind.NullLiteralExpression };

    // The nodes of the code of a function, and not those of a lambda or a local function in it.
    private static IEnumerable<SyntaxNode> CodeOf(SyntaxNode function) =>
        function.DescendantNodes(node =>
            node == function || node is not (AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax));

    // The position of the label that a goto statement goes to. A goto case, a goto default, and each
    // other statement that ends the call have no label, and get the largest position.
    private static int LabelOf(SyntaxNode exit, SyntaxNode function) =>
        exit is GotoStatementSyntax { RawKind: (int)SyntaxKind.GotoStatement, Expression: IdentifierNameSyntax label }
        && CodeOf(function)
                .OfType<LabeledStatementSyntax>()
                .FirstOrDefault(statement => statement.Identifier.ValueText == label.Identifier.ValueText)
            is { } target
            ? target.SpanStart
            : int.MaxValue;

    // True for the section that each value of its switch statement selects, where no break of the
    // switch comes before the position. That is the one section of a default label, or the section
    // that a constant value selects.
    private bool IsTheSectionOfEachValue(SyntaxNode section, int at) =>
        section.Parent is SwitchStatementSyntax switching
        && this.SectionOfEachValue(switching) == section
        && !JumpsOutBefore(construct: switching, code: section, at: at);

    private SwitchSectionSyntax? SectionOfEachValue(SwitchStatementSyntax switching)
    {
        if (!this.sectionsOfEachValue.TryGetValue(key: switching, value: out SwitchSectionSyntax? section))
        {
            section = switching.Sections.Count == 1 && switching.Sections[0].Labels.Any(this.MatchesEachValue)
                ? switching.Sections[0]
                : this.SectionOfAConstant(switching);

            this.sectionsOfEachValue[switching] = section;
        }

        return section;
    }

    // The section of the case label that a constant value of a switch selects. Each label is a
    // constant or default. Thus, no pattern or when clause can come before the section.
    private SwitchSectionSyntax? SectionOfAConstant(SwitchStatementSyntax switching)
    {
        SemanticModel model = this.region.ModelFor(switching);

        if (!switching.Sections.SelectMany(static section => section.Labels)
                .All(static label => label is CaseSwitchLabelSyntax or DefaultSwitchLabelSyntax)
            || model.GetConstantValue(switching.Expression) is not { HasValue: true } value)
        {
            return null;
        }

        return switching.Sections.FirstOrDefault(section =>
            section.Labels.OfType<CaseSwitchLabelSyntax>()
                .Any(label =>
                    model.GetConstantValue(label.Value) is { HasValue: true } constant
                    && AreEqual(first: constant.Value, second: value.Value)));
    }

    // Two constants of a switch. The compiler converts each integer constant to the type of the
    // switch, thus the walk compares the values of integers.
    private static bool AreEqual(object? first, object? second) =>
        IsAnInteger(first) && IsAnInteger(second)
            ? Convert.ToDecimal(value: first, provider: CultureInfo.InvariantCulture)
              == Convert.ToDecimal(value: second, provider: CultureInfo.InvariantCulture)
            : Equals(objA: first, objB: second);

    private static bool IsAnInteger(object? value) =>
        value is sbyte or byte or short or ushort or int or uint or long or ulong;
}
