using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SodaFlow.Analyzers;

// The part of the evaluator for an operation on a value of the type dynamic. The binder finds the member at run time,
// from the type of the object. Where the walk saw the object made, the evaluator reads the member of that type that
// the operation names. Otherwise, it reads nothing.
internal sealed partial class LoopStateEvaluator
{
    // The operator that a binary or a unary operation on a dynamic value calls. Each operand of an
    // object that the walk saw made gives the operators of its type. The evaluator reads one only
    // when the types have one operator with that name and number of operands.
    private Followed? FollowDynamicOperator(CSharpSyntaxNode operation, IReadOnlyCollection<ExpressionSyntax> operands)
    {
        if (OperatorName(operation.Kind()) is not { } name)
        {
            return null;
        }

        LoopState[] states = [.. operands.Select(this.Self)];

        IMethodSymbol[] found =
        [
            .. states
                .SelectMany(state => state.Members?.OperatorsOf(name: name, parameters: operands.Count) ?? [])
                .Distinct<IMethodSymbol>(SymbolEqualityComparer.Default)
        ];

        return found.Length == 1
            ? this.FollowImplied(
                call: operation,
                member: found[0],
                owner: null,
                arguments:
                [
                    .. operands.Zip(second: states, resultSelector: static (operand, state) => (operand, state))
                ])
            : null;
    }

    // The implicit conversion that the binder calls where the code converts a dynamic value to a type
    // with no cast. It is an operator of the type of the object that gives the type of the
    // conversion.
    private Followed? FollowDynamicConversion(ExpressionSyntax expression, ISymbol to)
    {
        LoopState state = this.Self(expression);

        IMethodSymbol[] found =
        [
            .. state.Members?.OperatorsOf(name: "op_Implicit", parameters: 1)
                    .Where(conversion => SymbolEqualityComparer.Default.Equals(x: conversion.ReturnType, y: to))
                ?? []
        ];

        return found.Length == 1
            ? this.FollowImplied(call: expression, member: found[0], owner: null, arguments: [(expression, state)])
            : null;
    }

    // The operator true or operator false that the binder calls where a dynamic value is a condition.
    private Followed? FollowDynamicTruth(ExpressionSyntax condition, string name)
    {
        LoopState state = this.Self(condition);

        return state.Members?.OperatorsOf(name: name, parameters: 1).FirstOrDefault() is { } truth
            ? this.FollowImplied(call: condition, member: truth, owner: null, arguments: [(condition, state)])
            : null;
    }

    /// <summary>
    ///     The setter that an assignment to a member or an element of a dynamic
    ///     value calls. It is the setter of the property with that name, or of
    ///     the indexer with that number of indexes.
    /// </summary>
    /// <param name="assignment">The assignment.</param>
    public Followed? FollowDynamicWrite(AssignmentExpressionSyntax assignment)
    {
        (ExpressionSyntax? owner, IPropertySymbol? property, IEnumerable<ArgumentSyntax> indexes) =
            assignment.Left switch
            {
                MemberAccessExpressionSyntax access when this.Self(access.Expression).Members is { } members =>
                    (access.Expression, members.PropertiesNamed(access.Name.Identifier.ValueText).FirstOrDefault(),
                        []),
                ElementAccessExpressionSyntax element => (element.Expression,
                    this.DynamicIndexerOf(owner: element.Expression, indexes: element.ArgumentList.Arguments.Count),
                    element.ArgumentList.Arguments),
                _ => (null, null, [])
            };

        return owner != null && property?.SetMethod is { } setter
            ? this.FollowImplied(
                call: assignment,
                member: setter,
                owner: this.Self(owner),
                arguments:
                [
                    .. indexes.Select(index => (index.Expression, this.Eval(index.Expression))),
                    (assignment.Right, this.Eval(assignment.Right))
                ])
            : null;
    }

    // The indexer with the number of indexes that the type of the object has. Null when the walk did
    // not see the object made.
    private IPropertySymbol? DynamicIndexerOf(ExpressionSyntax owner, int indexes) =>
        this.Self(owner)
            .Members?.PropertiesNamed("this[]")
            .FirstOrDefault(indexer => indexer.Parameters.Length == indexes);

    // The metadata name of the operator that an operation of the language calls.
    private static string? OperatorName(SyntaxKind operation) =>
        operation switch
        {
            SyntaxKind.AddExpression => "op_Addition",
            SyntaxKind.SubtractExpression => "op_Subtraction",
            SyntaxKind.MultiplyExpression => "op_Multiply",
            SyntaxKind.DivideExpression => "op_Division",
            SyntaxKind.ModuloExpression => "op_Modulus",
            SyntaxKind.BitwiseAndExpression => "op_BitwiseAnd",
            SyntaxKind.BitwiseOrExpression => "op_BitwiseOr",
            SyntaxKind.ExclusiveOrExpression => "op_ExclusiveOr",
            SyntaxKind.LeftShiftExpression => "op_LeftShift",
            SyntaxKind.RightShiftExpression => "op_RightShift",
            SyntaxKind.EqualsExpression => "op_Equality",
            SyntaxKind.NotEqualsExpression => "op_Inequality",
            SyntaxKind.LessThanExpression => "op_LessThan",
            SyntaxKind.LessThanOrEqualExpression => "op_LessThanOrEqual",
            SyntaxKind.GreaterThanExpression => "op_GreaterThan",
            SyntaxKind.GreaterThanOrEqualExpression => "op_GreaterThanOrEqual",
            SyntaxKind.UnaryMinusExpression => "op_UnaryNegation",
            SyntaxKind.UnaryPlusExpression => "op_UnaryPlus",
            SyntaxKind.LogicalNotExpression => "op_LogicalNot",
            SyntaxKind.BitwiseNotExpression => "op_OnesComplement",
            _ => null
        };
}
