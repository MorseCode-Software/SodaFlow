using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SodaFlow.Analyzers;

/// <summary>
///     The code that runs while one loop is open, which is the body of a loop
///     block.
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

    private SemanticModel ModelFor(SyntaxTree tree)
    {
        if (!this.models.TryGetValue(key: tree, value: out SemanticModel? model))
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
    public void Analyze(SyntaxNode root, IEnumerable<ExpressionSyntax> closings, bool checkLinks)
    {
        LoopStateEvaluator evaluator = new(this);
        new SinkWalker(region: this, evaluator: evaluator).Visit(root);

        if (!checkLinks)
        {
            return;
        }

        foreach (ExpressionSyntax closing in closings)
        {
            LoopState state = evaluator.Eval(closing);

            if (state.Linked)
            {
                this.Report(
                    Diagnostic.Create(
                        descriptor: Diagnostics.LinkedClose,
                        location: closing.GetLocation(),
                        this.PlaceholderName));
            }
            else if (state.Pending)
            {
                // A pending close with no link is a lazy sample of the loop in its own initial
                // value. That sample forces itself at the end of the transaction.
                this.Report(
                    Diagnostic.Create(
                        descriptor: Diagnostics.PendingClose,
                        location: closing.GetLocation(),
                        this.PlaceholderName));
            }
        }
    }
}
