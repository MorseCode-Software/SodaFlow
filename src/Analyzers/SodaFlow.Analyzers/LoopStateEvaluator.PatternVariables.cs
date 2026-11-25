using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SodaFlow.Analyzers;

// The part of the evaluator for a variable of a pattern that the evaluator reads before the walk reads its pattern.
internal sealed partial class LoopStateEvaluator
{
    /// <summary>
    ///     The state of a variable of a pattern, from the value that the pattern
    ///     tests. Null for each other local.
    /// </summary>
    /// <remarks>
    ///     The walk gives the variable its state where it reads the pattern. But
    ///     the evaluator can read an expression before that, as for an argument
    ///     of a call. It keeps the state of each expression that it read.
    ///     The language lets code read the variable only after the pattern
    ///     matched. Thus, the variable then has the value that the pattern
    ///     tests.
    /// </remarks>
    /// <param name="local">The local that the code reads.</param>
    private LoopState? BeforeItsPatternIsRead(ISymbol local)
    {
        if (local.DeclaringSyntaxReferences.Length != 1
            || local.DeclaringSyntaxReferences[0].GetSyntax() is not SingleVariableDesignationSyntax
            {
                Parent: { } declares
            })
        {
            return null;
        }

        return declares switch
        {
            DeclarationPatternSyntax declaration => this.TestedBy(pattern: declaration, type: declaration.Type),
            RecursivePatternSyntax recursive => this.TestedBy(pattern: recursive, type: recursive.Type),
            VarPatternSyntax variable => this.TestedBy(pattern: variable, type: null),
            _ => null
        };
    }

    // The state of the value that a pattern tests. A pattern of a type that the value can never have gives no state.
    // Null where the evaluator does not know the value, as for a position of a list or a positional pattern. The walk
    // gives each variable of a case label its state before it reads a section. Thus, a case label is not here.
    private LoopState? TestedBy(SyntaxNode pattern, ExpressionSyntax? type)
    {
        SyntaxNode? around = pattern.Parent;

        while (around is ParenthesizedPatternSyntax or BinaryPatternSyntax { RawKind: (int)SyntaxKind.AndPattern })
        {
            around = around.Parent;
        }

        ExpressionSyntax? value =
            around switch
            {
                IsPatternExpressionSyntax test => test.Expression,
                SwitchExpressionArmSyntax { Parent: SwitchExpressionSyntax choice } => choice.GoverningExpression,
                _ => null
            };

        if (value != null)
        {
            return type != null && this.region.TypeOf(type) is { } tested && this.NeverHas(value: value, type: tested)
                ? LoopState.None
                : this.Eval(value);
        }

        // A pattern of a property gets the state of that member of the value that its owner tests.
        return around is SubpatternSyntax
               {
                   NameColon.Name: { } name, Parent.Parent: RecursivePatternSyntax owner
               }
               && this.region.SymbolOf(name) is { } member
               && this.TestedBy(pattern: owner, type: owner.Type) is { Members: { } members }
            ? members.StateThrough(member: member, implementations: this.region.Symbols.Implementations)
            : null;
    }
}
