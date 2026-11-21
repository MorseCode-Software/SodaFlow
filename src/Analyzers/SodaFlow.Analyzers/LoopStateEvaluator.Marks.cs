using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SodaFlow.Analyzers;

// The part of the evaluator that finds the value that a mark in a local covers.
internal sealed partial class LoopStateEvaluator
{
    /// <summary>
    ///     The only value of a local in its function. It is the initializer
    ///     when no code gives the local a different value. For a local with no
    ///     initializer, it is the value of the one simple assignment to it. Null
    ///     for each other local.
    /// </summary>
    /// <remarks>
    ///     C# lets no code read a local before it gets a value. Thus, each read
    ///     of a local with one assignment gets the value of that assignment.
    /// </remarks>
    private ExpressionSyntax? OnlyValueOf(ILocalSymbol local)
    {
        if (local.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() is not VariableDeclaratorSyntax declarator
            || FunctionOf(declarator) is not { } code)
        {
            return null;
        }

        (int Position, bool Gives, bool InAFunction)[] gives =
        [
            .. this.UsesOf(name: local, code: code).Where(static use => use.Gives)
        ];

        if (declarator.Initializer is { Value: var initial })
        {
            return gives.Length == 0 ? initial : null;
        }

        return gives.Length == 1
               && code.FindToken(gives[0].Position).Parent
                   is IdentifierNameSyntax
                   {
                       Parent: AssignmentExpressionSyntax
                       {
                           RawKind: (int)SyntaxKind.SimpleAssignmentExpression, Right: var value
                       } assignment
                   } name
               && assignment.Left == name
            ? value
            : null;
    }
}
