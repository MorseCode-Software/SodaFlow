using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace SodaFlow.Analyzers;

// The part of the evaluator that finds if a function can run between the record of a second name and its use.
internal sealed partial class LoopStateEvaluator
{
    // True when code in the windows can give one of the names a value. A function of the code that
    // gives a name a value counts only where it can run in the window: see CanRunAFunctionBetween.
    // A window of more than one part has a call, and a call can run each function. The first test
    // is the one of before, thus the other two run only where it finds a value.
    private bool GetsAValueIn(
        ImmutableArray<(SyntaxNode Code, int After, int Before)> windows,
        ImmutableArray<SyntaxNode> ran,
        ISymbol[] names,
        SyntaxNode use)
    {
        return GetsAValueInAny(aFunctionCanRun: true)
               && (windows.Length > 1
                   || GetsAValueInAny(aFunctionCanRun: false)
                   || this.CanRunAFunctionBetween(code: windows[0].Code, after: windows[0].After, use: use));

        bool GetsAValueInAny(bool aFunctionCanRun) =>
            windows.Any(window => names.Any(one =>
                this.GetsAValue(
                    name: one,
                    code: window.Code,
                    after: window.After,
                    before: window.Before,
                    orAFunctionUsesIt: false,
                    ran: ran,
                    aFunctionCanRun: aFunctionCanRun)));
    }

    /// <summary>
    ///     True when a lambda or a local function of the code can run between a
    ///     position and a use.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A function runs only when code calls it. Code calls it by its name
    ///         or through a delegate. A callee that got the function can call it,
    ///         also in a different call. Code that C# calls with no call syntax can
    ///         call it too, such as a getter, an operator, or a conversion. Thus,
    ///         each such node between the two can run the function. A node that
    ///         RunsNoCode allows runs no code.
    ///     </para>
    ///     <para>
    ///         A loop, a label, or a switch section around the use can run the
    ///         use again after code that comes after it. So can a use that comes
    ///         before the position in the code. The walk takes code as
    ///         one thread. A change by a different thread comes before the use
    ///         only through a call or a read of a volatile field between them.
    ///         RunsNoCode allows no call and no such read.
    ///     </para>
    /// </remarks>
    private bool CanRunAFunctionBetween(SyntaxNode code, int after, SyntaxNode use) =>
        after > use.SpanStart
        || use.Ancestors()
            .TakeWhile(around => around != code)
            .Any(static around => around is ForStatementSyntax
                or CommonForEachStatementSyntax
                or WhileStatementSyntax
                or DoStatementSyntax
                or LabeledStatementSyntax
                or SwitchSectionSyntax)
        || code.DescendantNodes(
                span: TextSpan.FromBounds(start: after, end: use.SpanStart),
                descendIntoChildren: node => node == code || !IsAFunction(node))
            .Any(node => node.SpanStart >= after && node.Span.End <= use.SpanStart && !this.RunsNoCode(node));

    // A function that code makes and does not run where it makes it.
    private static bool IsAFunction(SyntaxNode node) =>
        node is AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax;

    // True for a node that runs no code. It is a name of a local, a parameter, or a type, or an
    // element of an array. It is an assignment with no conversion of code also: its target is such
    // a node or a member, which the scan reads apart. Statements, declarations, literals, and
    // functions that it is part of are such nodes too. A member is not, because its read can run a
    // getter or a type initializer, or read a volatile field.
    private bool RunsNoCode(SyntaxNode node) =>
        node switch
        {
            IdentifierNameSyntax or GenericNameSyntax =>
                this.region.SymbolOf(node) is ILocalSymbol or IParameterSymbol or ITypeSymbol or INamespaceSymbol,
            AssignmentExpressionSyntax { RawKind: (int)SyntaxKind.SimpleAssignmentExpression } assignment =>
                this.ConvertsWithNoCode(assignment.Right),
            VariableDeclaratorSyntax declarator =>
                declarator.Initializer == null || this.ConvertsWithNoCode(declarator.Initializer.Value),
            ElementAccessExpressionSyntax element => this.region.TypeOf(element.Expression) is IArrayTypeSymbol,
            LocalDeclarationStatementSyntax declaration =>
                declaration.UsingKeyword.IsKind(SyntaxKind.None) && declaration.AwaitKeyword.IsKind(SyntaxKind.None),
            ArgumentSyntax argument => argument.RefKindKeyword.IsKind(SyntaxKind.None),
            PostfixUnaryExpressionSyntax postfix => postfix.IsKind(SyntaxKind.SuppressNullableWarningExpression),
            ExpressionStatementSyntax
                or BlockSyntax
                or EmptyStatementSyntax
                or VariableDeclarationSyntax
                or EqualsValueClauseSyntax
                or BracketedArgumentListSyntax
                or LiteralExpressionSyntax
                or ParenthesizedExpressionSyntax
                or PredefinedTypeSyntax
                or TypeArgumentListSyntax
                or QualifiedNameSyntax
                or ArrayTypeSyntax
                or ArrayRankSpecifierSyntax
                or OmittedArraySizeExpressionSyntax
                or NullableTypeSyntax
                or AnonymousFunctionExpressionSyntax
                or LocalFunctionStatementSyntax => true,
            _ => false
        };

    // True for a value whose conversion to its target runs no code. A conversion that a type
    // declares runs code.
    private bool ConvertsWithNoCode(ExpressionSyntax value) =>
        Unwrapped(value) is AnonymousFunctionExpressionSyntax
        || !this.region.ModelFor(value).GetConversion(value).IsUserDefined;
}
