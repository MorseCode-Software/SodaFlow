using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
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
///     A loop block hands its function a placeholder, and the function is the
///     code that runs while the loop is open. This analyzer checks that code.
///     The function can be a lambda, an anonymous method, or a method of this
///     compilation that the block names. The analyzer does not check an explicit
///     loop. Only the block methods of the SodaFlow assemblies make one, and
///     each passes the placeholder directly to the block of the caller. See
///     <see cref="LoopStateEvaluator" /> for the model of the operators.
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

        context.RegisterCompilationStartAction(static start =>
        {
            SodaFlowSymbols symbols = new(start.Compilation);

            // Code that does not reference the library has no loop block.
            if (!symbols.HasLibrary)
            {
                return;
            }

            start.RegisterSyntaxNodeAction(
                action: node => AnalyzeLoopBlock(
                    invocation: (InvocationExpressionSyntax)node.Node,
                    model: node.SemanticModel,
                    symbols: symbols,
                    report: node.ReportDiagnostic,
                    cancellation: node.CancellationToken),
                SyntaxKind.InvocationExpression);
        });
    }

    /// <summary>
    ///     Reads one invocation, and reports what the walk finds when it is a loop
    ///     block.
    /// </summary>
    /// <param name="invocation">The invocation.</param>
    /// <param name="model">The semantic model of the file of the invocation.</param>
    /// <param name="symbols">The symbols of the library in the compilation.</param>
    /// <param name="report">The action that gets each diagnostic, at the time that the walk finds it.</param>
    /// <param name="cancellation">The token of the compiler. The walk throws when the token is canceled.</param>
    internal static void AnalyzeLoopBlock(
        InvocationExpressionSyntax invocation,
        SemanticModel model,
        SodaFlowSymbols symbols,
        Action<Diagnostic> report,
        CancellationToken cancellation)
    {
        // The name of the method is in the syntax. This action runs for each invocation in the
        // compilation, thus it asks for the symbol only where the name is the name of a block.
        SimpleNameSyntax? name =
            invocation.Expression switch
            {
                MemberAccessExpressionSyntax access => access.Name,
                MemberBindingExpressionSyntax binding => binding.Name,
                SimpleNameSyntax simple => simple,
                _ => null
            };

        if (name == null
            || !SodaFlowSymbols.IsLoopBlockName(name.Identifier.ValueText)
            || model.GetSymbolInfo(invocation).Symbol is not IMethodSymbol method
            || !symbols.IsLoopBlock(method))
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
                ?.Expression is not { } written
            || BlockOf(written: written, model: model) is not (var placeholder, var body))
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
            (body is ExpressionSyntax expression ? [expression] : LoopStateEvaluator.ReturnExpressions(body))
            .Select(e => withCaptures ? LoopedElement(e) : e);

        LoopRegion region =
            new(
                compilation: model.Compilation,
                symbols: symbols,
                placeholder: placeholder,
                placeholderState: placeholderState,
                root: body,
                report: report,
                cancellation: cancellation);

        region.Analyze(closings: closings, checkLinks: checkLinks);
    }

    /// <summary>
    ///     The placeholder and the body of a block. The block can be a lambda, an
    ///     anonymous method, or the name of a method or a local function of this
    ///     compilation. A delegate from a different source has no body to read.
    /// </summary>
    private static (ISymbol Placeholder, SyntaxNode Body)? BlockOf(ExpressionSyntax written, SemanticModel model)
    {
        if (written is AnonymousFunctionExpressionSyntax function)
        {
            ParameterSyntax? parameter =
                function switch
                {
                    SimpleLambdaExpressionSyntax simple => simple.Parameter,
                    ParenthesizedLambdaExpressionSyntax lambda => lambda.ParameterList.Parameters.FirstOrDefault(),
                    AnonymousMethodExpressionSyntax anonymous => anonymous.ParameterList?.Parameters.FirstOrDefault(),
                    _ => null
                };

            return parameter != null && model.GetDeclaredSymbol(parameter) is { } declared
                ? (declared, function.Body)
                : null;
        }

        if (model.GetSymbolInfo(written).Symbol is not IMethodSymbol named)
        {
            return null;
        }

        // The definition, because its body binds to its own parameters. For an extension method on
        // a value, that value is the first parameter and the placeholder is the second.
        IMethodSymbol method = (named.ReducedFrom ?? named).OriginalDefinition;
        int position = named.ReducedFrom != null ? 1 : 0;

        SyntaxNode? body =
            method.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() switch
            {
                MethodDeclarationSyntax declaration =>
                    declaration.Body ?? (SyntaxNode?)declaration.ExpressionBody?.Expression,
                LocalFunctionStatementSyntax local => local.Body ?? (SyntaxNode?)local.ExpressionBody?.Expression,
                _ => null
            };

        return body != null && position < method.Parameters.Length ? (method.Parameters[position], body) : null;
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
