using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace SodaFlow.Analyzers;

/// <summary>
///     Finds each read of a looped cell or behavior before its loop closes, and each
///     loop whose definition is linked to its own placeholder.
/// </summary>
/// <remarks>
///     A loop block hands its lambda a placeholder. An explicit loop makes one with
///     <c>new CellLoop&lt;T&gt;()</c> and closes it with <c>Loop</c>. Both give a
///     region of code that runs while the loop is open, and this analyzer checks
///     that region. See <see cref="LoopStateEvaluator" /> for the model of the
///     operators.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class LoopReadAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(
            Diagnostics.ReadBeforeClose,
            Diagnostics.LinkedClose,
            Diagnostics.Escape,
            Diagnostics.ReadInCallee,
            Diagnostics.MemberReadInCallee,
            Diagnostics.PendingClose);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeLoopBlock, SyntaxKind.InvocationExpression);
        context.RegisterSyntaxNodeAction(AnalyzeExplicitLoop, SyntaxKind.LocalDeclarationStatement);
    }

    private static void AnalyzeLoopBlock(SyntaxNodeAnalysisContext context)
    {
        InvocationExpressionSyntax invocation = (InvocationExpressionSyntax)context.Node;

        if (context.SemanticModel.GetSymbolInfo(invocation).Symbol is not IMethodSymbol method
            || method.Name is not ("WithCaptures" or "WithoutCaptures")
            || !SodaFlowSymbols.IsLooper(method.ContainingType))
        {
            return;
        }

        if (invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression is not AnonymousFunctionExpressionSyntax lambda)
        {
            return;
        }

        ParameterSyntax? parameterSyntax =
            lambda switch
            {
                SimpleLambdaExpressionSyntax s => s.Parameter,
                ParenthesizedLambdaExpressionSyntax p => p.ParameterList.Parameters.FirstOrDefault(),
                _ => null
            };

        if (parameterSyntax == null || context.SemanticModel.GetDeclaredSymbol(parameterSyntax) is not { } placeholder)
        {
            return;
        }

        string looper = method.ContainingType.OriginalDefinition.Name;

        LoopState placeholderState =
            looper == "StreamLooper" ? LoopState.StreamPlaceholder : LoopState.PendingPlaceholder;

        // A forward reference closes its loop with a constant cell, which has no links.
        bool checkLinks = looper != "ForwardReference";
        bool withCaptures = method.Name == "WithCaptures";

        IEnumerable<ExpressionSyntax> closings =
            (lambda.ExpressionBody != null
                ? [lambda.ExpressionBody]
                : LoopStateEvaluator.ReturnExpressions(lambda.Body))
            .Select(e => withCaptures ? LoopedElement(e) : e);

        LoopRegion region =
            new(
                compilation: context.Compilation,
                placeholder: placeholder,
                placeholderState: placeholderState,
                report: context.ReportDiagnostic);

        region.Analyze(roots: [lambda.Body], closings: closings, checkLinks: checkLinks);
    }

    /// <summary>
    ///     The element of the returned tuple that closes the loop: the one named
    ///     Cell, Behavior, or Stream, or else the first.
    /// </summary>
    private static ExpressionSyntax LoopedElement(ExpressionSyntax returned)
    {
        if (returned is not TupleExpressionSyntax tuple || tuple.Arguments.Count == 0)
        {
            return returned;
        }

        ArgumentSyntax? named =
            tuple.Arguments.FirstOrDefault(
                static a => a.NameColon?.Name.Identifier.ValueText is "Cell" or "Behavior" or "Stream");

        return (named ?? tuple.Arguments[0]).Expression;
    }

    private static void AnalyzeExplicitLoop(SyntaxNodeAnalysisContext context)
    {
        LocalDeclarationStatementSyntax declaration = (LocalDeclarationStatementSyntax)context.Node;

        if (declaration.Parent is not BlockSyntax block)
        {
            return;
        }

        foreach (VariableDeclaratorSyntax declarator in declaration.Declaration.Variables)
        {
            if (declarator.Initializer == null
                || context.SemanticModel.GetDeclaredSymbol(declarator) is not ILocalSymbol local)
            {
                continue;
            }

            // A local of a placeholder type is one explicit loop. A local of a collection of them
            // is a region too, such as a list of loops closed in a for loop. It has no one closing
            // expression to check.
            LoopState placeholderState = SodaFlowSymbols.StateOfType(local.Type);

            if (!placeholderState.Pending && !placeholderState.Linked)
            {
                continue;
            }

            bool single =
                local.Type is INamedTypeSymbol type
                && SodaFlowSymbols.IsSodaFlow(type)
                && type.OriginalDefinition.Name
                    is "CellLoop" or "BehaviorLoop" or "LoopedCell" or "LoopedBehavior" or "StreamLoop" or "LoopedStream";

            AnalyzeExplicitLoop(
                context: context,
                block: block,
                declaration: declaration,
                local: local,
                placeholderState: placeholderState,
                single: single);
        }
    }

    /// <summary>
    ///     The region of an explicit loop is each statement after its declaration up
    ///     to the one that calls <c>Loop</c> on it. The argument of that call closes
    ///     the loop.
    /// </summary>
    private static void AnalyzeExplicitLoop(
        SyntaxNodeAnalysisContext context,
        BlockSyntax block,
        LocalDeclarationStatementSyntax declaration,
        ILocalSymbol local,
        LoopState placeholderState,
        bool single)
    {
        int start = block.Statements.IndexOf(declaration) + 1;
        List<SyntaxNode> roots = [];
        ExpressionSyntax? closing = null;

        for (int i = start; i < block.Statements.Count; i++)
        {
            StatementSyntax statement = block.Statements[i];
            roots.Add(statement);

            // For one loop, the Loop call on the local. For a collection of loops, the first Loop
            // call on any placeholder, which is the loop statement that closes them.
            InvocationExpressionSyntax? close =
                statement.DescendantNodes(descendIntoChildren: static n => n is not AnonymousFunctionExpressionSyntax)
                    .OfType<InvocationExpressionSyntax>()
                    .FirstOrDefault(
                        inv =>
                            inv.Expression is MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Loop" } access
                            && (single
                                ? SymbolEqualityComparer.Default.Equals(
                                    context.SemanticModel.GetSymbolInfo(access.Expression).Symbol,
                                    local)
                                : SodaFlowSymbols.StateOfType(context.SemanticModel.GetTypeInfo(access.Expression).Type)
                                    .Linked));

            if (close != null)
            {
                closing = single ? close.ArgumentList.Arguments.LastOrDefault()?.Expression : null;

                break;
            }
        }

        LoopRegion region =
            new(
                compilation: context.Compilation,
                placeholder: local,
                placeholderState: placeholderState,
                report: context.ReportDiagnostic);

        region.Analyze(roots: roots, closings: closing == null ? [] : [closing], checkLinks: closing != null);
    }
}
