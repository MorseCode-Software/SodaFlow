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
    /// <param name="after">
    ///     The try with no catch clause whose block has the call. The code after
    ///     that try runs after the call, but its finally block can run without
    ///     the call. Null when no such try is around the call.
    /// </param>
    private bool SurelyRuns(SyntaxNode call, out TryStatementSyntax? after)
    {
        after = null;

        return WhereItAlwaysRuns(expression: call, constants: null) is { } place
               && this.SurelyRunsAt(place: place, after: out after);
    }

    // The same for a statement, or for the expression that is the body of a function.
    private bool SurelyRunsAt(SyntaxNode place, out TryStatementSyntax? after)
    {
        after = null;

        for (SyntaxNode? at = place; at != null;)
        {
            bool always =
                this.AlwaysRunsAt(
                    place: at,
                    forAFirstRead: false,
                    forACall: true,
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

    // A foreach over a collection whose positions the store knows has a first turn when the
    // collection has an element.
    private bool HasATurn(SyntaxNode loop) =>
        this.HasAFirstTurn(loop)
        || (loop is ForEachStatementSyntax each && this.evaluator.HasAnElement(each.Expression));

    // The call of ForEach whose one argument is a lambda, where the receiver has an element. The walk
    // read the lambda where it is, thus the callee runs it for each element. Null for each other call.
    private InvocationExpressionSyntax? ForEachOfAnElement(AnonymousFunctionExpressionSyntax lambda) =>
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
    private bool ExitsBefore(SyntaxNode function, int place, bool forACall)
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

        return exits.Any(exit => exit.Exit.SpanStart < place && exit.Label > place);
    }

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
    private bool IsTheSectionOfEachValue(SwitchSectionSyntax section, int at) =>
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
