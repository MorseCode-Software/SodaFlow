using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace SodaFlow.Analyzers;

// The part of the walker that does not read code that a constant rules out. The compiler gives the
// value of the constant. Thus, the walk skips only code that cannot run.
internal sealed partial class SinkWalker
{
    // Each switch section that the constant value of its switch rules out.
    private HashSet<SwitchSectionSyntax>? ruledOut;

    // The right side of && does not run after the constant false, and the right side of || does not
    // run after the constant true.
    public override void VisitBinaryExpression(BinaryExpressionSyntax node)
    {
        bool? stopsAt =
            node.RawKind switch
            {
                (int)SyntaxKind.LogicalAndExpression => false,
                (int)SyntaxKind.LogicalOrExpression => true,
                _ => null
            };

        if (stopsAt != null && this.ConstantOf(node.Left) == stopsAt)
        {
            this.Visit(node.Left);

            return;
        }

        base.VisitBinaryExpression(node);
    }

    public override void VisitSwitchSection(SwitchSectionSyntax node)
    {
        if (this.ruledOut?.Contains(node) == true)
        {
            return;
        }

        base.VisitSwitchSection(node);
    }

    // The compiler finds no path to the start of a section that the constant value of a switch
    // cannot select. The walk asks only about a switch on a constant.
    private void RulesOutSectionsOf(SwitchStatementSyntax node)
    {
        if (!CanBeAConstant(node.Expression)
            || !this.region.ModelFor(node).GetConstantValue(node.Expression).HasValue)
        {
            return;
        }

        foreach (SwitchSectionSyntax section in node.Sections)
        {
            if (section.Statements.Count > 0
                && this.region.ModelFor(section).AnalyzeControlFlow(section.Statements[0])
                    is { StartPointIsReachable: false })
            {
                (this.ruledOut ??= []).Add(section);
            }
        }
    }

    // A switch expression on a constant does not run an arm whose constant pattern is a different
    // value. It also does not run an arm after an arm whose constant pattern is the same value.
    private void VisitTheArmsThatCanRun(SwitchExpressionSyntax node)
    {
        if (!CanBeAConstant(node.GoverningExpression)
            || this.region.ModelFor(node).GetConstantValue(node.GoverningExpression) is not { HasValue: true } input)
        {
            base.VisitSwitchExpression(node);

            return;
        }

        this.Visit(node.GoverningExpression);

        foreach (SwitchExpressionArmSyntax arm in node.Arms)
        {
            bool? matches = this.MatchesTheConstant(arm: arm, input: input.Value);

            if (matches == false)
            {
                continue;
            }

            this.Visit(arm);

            if (matches == true)
            {
                return;
            }
        }
    }

    // True when an arm with a constant pattern always matches a constant value, and false when it
    // never matches. Null for each other arm. The compiler converts the constant of a pattern to the
    // type of the value. Thus, Equals gives the result of the pattern for each value, also NaN.
    private bool? MatchesTheConstant(SwitchExpressionArmSyntax arm, object? input) =>
        this.region.ModelFor(arm).GetOperation(arm)
            is ISwitchExpressionArmOperation
            {
                Guard: null,
                Pattern: IConstantPatternOperation { Value.ConstantValue: { HasValue: true, Value: var value } }
            }
            ? Equals(objA: value, objB: input)
            : null;
}
