using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SodaFlow.Analyzers;

/// <summary>
///     The code that runs while one loop is open: a loop block, or the statements
///     between an explicit loop's creation and its <c>Loop</c> call.
/// </summary>
internal sealed class LoopRegion(
    in Compilation compilation,
    in ISymbol placeholder,
    in LoopState placeholderState,
    in Action<Diagnostic> report)
{
    private readonly Compilation compilation = compilation;
    private readonly Dictionary<SyntaxTree, SemanticModel> models = [];
    private readonly Action<Diagnostic> report = report;

    public ISymbol Placeholder { get; } = placeholder;

    public LoopState PlaceholderState { get; } = placeholderState;

    public string PlaceholderName => this.Placeholder.Name;

    public SemanticModel ModelFor(SyntaxTree tree)
    {
        if (!this.models.TryGetValue(tree, out SemanticModel? model))
        {
            model = this.compilation.GetSemanticModel(tree);
            this.models[tree] = model;
        }

        return model;
    }

    public SemanticModel ModelFor(SyntaxNode node) => this.ModelFor(node.SyntaxTree);

    public void Report(Diagnostic diagnostic) => this.report(diagnostic);

    /// <summary>
    ///     Walks the region for reads that run before the close, then checks each
    ///     expression that closes the loop for a link back to the placeholder.
    /// </summary>
    public void Analyze(IEnumerable<SyntaxNode> roots, IEnumerable<ExpressionSyntax> closings, bool checkLinks)
    {
        LoopStateEvaluator evaluator = new(this);
        SinkWalker walker = new(region: this, evaluator: evaluator);

        foreach (SyntaxNode root in roots)
        {
            walker.Visit(root);
        }

        if (!checkLinks)
        {
            return;
        }

        foreach (ExpressionSyntax closing in closings)
        {
            if (evaluator.Eval(closing).Linked)
            {
                this.Report(
                    Diagnostic.Create(
                        descriptor: Diagnostics.LinkedClose,
                        location: closing.GetLocation(),
                        this.PlaceholderName));
            }
        }
    }
}
