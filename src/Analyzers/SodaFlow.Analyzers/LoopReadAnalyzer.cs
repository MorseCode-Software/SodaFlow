using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace SodaFlow.Analyzers;

/// <summary>
///     Finds each read of a looped cell or behavior before its loop closes,
///     and each loop whose definition is linked to its own placeholder.
/// </summary>
/// <remarks>
///     A loop block hands its lambda a placeholder, and the lambda is the code
///     that runs while the loop is open. This analyzer checks that code. The
///     analyzer does not check an explicit loop. Only the block methods of the
///     SodaFlow assemblies make one, and each passes the placeholder directly
///     to the block of the caller. See <see cref="LoopStateEvaluator" /> for
///     the model of the operators.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
// ReSharper disable once InheritdocConsiderUsage - This class has its own summary, which says more than the base class does.
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
        context.RegisterSyntaxNodeAction(action: AnalyzeLoopBlock, SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeLoopBlock(SyntaxNodeAnalysisContext context)
    {
        InvocationExpressionSyntax invocation = (InvocationExpressionSyntax)context.Node;

        if (context.SemanticModel.GetSymbolInfo(invocation).Symbol is not IMethodSymbol method
            || !SodaFlowSymbols.IsLoopBlock(method))
        {
            return;
        }

        // The block is the argument of the one delegate parameter. A public block takes only that
        // argument. A Core block also takes the transaction and the state of the block.
        IMethodSymbol definition = method.OriginalDefinition;

        if (invocation.ArgumentList.Arguments
                .Where((argument, position) =>
                    SodaFlowSymbols.ParameterOf(callee: definition, argument: argument, position: position)?.Type
                        .TypeKind
                    == TypeKind.Delegate)
                .FirstOrDefault()
                ?.Expression is not AnonymousFunctionExpressionSyntax lambda)
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
        bool withCaptures = method.Name is "WithCaptures" or "WithCapturesImpl";

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

        region.Analyze(root: lambda.Body, closings: closings, checkLinks: checkLinks);
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
            tuple.Arguments.FirstOrDefault(static a =>
                a.NameColon?.Name.Identifier.ValueText is "Cell" or "Behavior" or "Stream");

        return (named ?? tuple.Arguments[0]).Expression;
    }
}
