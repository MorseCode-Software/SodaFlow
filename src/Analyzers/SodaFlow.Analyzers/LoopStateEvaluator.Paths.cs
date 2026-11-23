using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SodaFlow.Analyzers;

// The part of the evaluator that tells the walker where code runs on each path.
internal sealed partial class LoopStateEvaluator
{
    /// <summary>
    ///     True for a collection whose positions the store knows, with one
    ///     position or more. A loop over it always has a first turn.
    /// </summary>
    /// <param name="collection">The expression of the collection.</param>
    public bool HasAnElement(ExpressionSyntax collection) =>
        this.StorePositionsOf(collection) is (_, { Length: > 0 });

    /// <summary>
    ///     The value that the call of this code gives a parameter, where that
    ///     value is a constant. The default value of the parameter, where the
    ///     call gives no argument for it. Nothing for each other parameter.
    /// </summary>
    /// <remarks>
    ///     The value of a parameter that the code writes can change. A lambda
    ///     gets no such value for a parameter of the method around it.
    /// </remarks>
    /// <param name="parameter">The parameter.</param>
    public Optional<object?> ArgumentConstantOf(IParameterSymbol parameter)
    {
        if (this.call is not { Site: InvocationExpressionSyntax { ArgumentList.Arguments: var arguments } site }
            || this.region.SymbolOf(site) is not IMethodSymbol method
            || !SymbolEqualityComparer.Default.Equals(
                x: (method.ReducedFrom ?? method).OriginalDefinition,
                y: parameter.ContainingSymbol.OriginalDefinition)
            || IsWrittenIn(parameter: parameter, region: this.region))
        {
            return default;
        }

        for (int i = 0; i < arguments.Count; i++)
        {
            if (ParameterFor(method: method, argument: arguments[i], position: i)?.Name == parameter.Name)
            {
                return this.region.ModelFor(arguments[i].Expression).GetConstantValue(arguments[i].Expression);
            }
        }

        return parameter.HasExplicitDefaultValue ? new Optional<object?>(parameter.ExplicitDefaultValue) : default;
    }

    /// <summary>
    ///     True for a value that has an object of the store. A run counts where
    ///     a test for null of such a value permits it.
    /// </summary>
    /// <param name="value">The expression of the value.</param>
    public bool HasAnObject(ExpressionSyntax value) => !this.region.Objects.Now(this.Eval(value).Objects).IsEmpty;

    /// <summary>
    ///     True when each call between the code that has a value and this code
    ///     runs on all paths of its function. A run of the timers or a Dispose
    ///     that always runs in this code then runs for that code also.
    /// </summary>
    /// <remarks>
    ///     The call in the code that has the value is not part of the test.
    ///     That code reads its own value on each path, as the block does.
    /// </remarks>
    /// <param name="owns">
    ///     True for the frame of the code that has the value, and for the frame
    ///     of each code that called that code.
    /// </param>
    /// <param name="forARun">
    ///     True for a run of the timers or a Dispose. For a cancel, a call in
    ///     the block of a try does not count. The finally block of that try can
    ///     run the timers without the cancel.
    /// </param>
    public bool CallsRunFor(Func<LoopFrame, bool> owns, bool forARun)
    {
        for (CallOfThisCode? each = this.call; each != null && !owns(each.Caller.Frame); each = each.Caller.call)
        {
            if (each.Caller.walker?.SurelyRuns(call: each.Site, forARun: forARun, after: out TryStatementSyntax? after)
                != true
                || (!forARun && after != null))
            {
                return false;
            }
        }

        return true;
    }

    // True when each run of the code runs the node one time. The node has only blocks and its own
    // statement around it, up to the code. A finally block runs what is in it also, and so does the
    // branch of an if that a constant selects. The block of a try with no catch clause runs the
    // node where no code runs after an exception before it. Thus, the finally block of that try
    // must not name a local of the given names.
    private bool RunsStraightIn(SyntaxNode node, SyntaxNode code, IReadOnlyCollection<string> names)
    {
        SyntaxNode child = node;

        foreach (SyntaxNode around in node.Ancestors().TakeWhile(around => around != code))
        {
            bool straight =
                around switch
                {
                    BlockSyntax
                        or ExpressionStatementSyntax
                        or LocalDeclarationStatementSyntax
                        or VariableDeclarationSyntax
                        or VariableDeclaratorSyntax
                        or EqualsValueClauseSyntax
                        or FinallyClauseSyntax
                        or ElseClauseSyntax => true,
                    TryStatementSyntax attempt =>
                        child is FinallyClauseSyntax
                        || (attempt.Catches.Count == 0
                            && attempt.Finally?.Block.DescendantTokens()
                                .Any(token =>
                                    token.IsKind(SyntaxKind.IdentifierToken) && names.Contains(token.ValueText))
                            != true),
                    IfStatementSyntax branch =>
                        SinkWalker.ConstantOf(region: this.region, condition: branch.Condition)
                        == (child == branch.Statement),
                    _ => false
                };

            if (!straight)
            {
                return false;
            }

            child = around;
        }

        return true;
    }

    // The name of the local whose delegate or event a remove changes.
    private static IEnumerable<string> NameOfTheOwner(ExpressionSyntax location) =>
        Unwrapped(location) switch
        {
            IdentifierNameSyntax name => [name.Identifier.ValueText],
            MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax owner } => [owner.Identifier.ValueText],
            _ => []
        };
}
