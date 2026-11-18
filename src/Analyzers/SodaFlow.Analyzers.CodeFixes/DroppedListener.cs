using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SodaFlow.Analyzers.CodeFixes;

/// <summary>The forms of code in which nothing holds a listener, as SF2001 reports them.</summary>
internal enum DroppedForm
{
    /// <summary>A statement of the call alone.</summary>
    Statement,

    /// <summary>The body of a lambda or of a member that is the call alone.</summary>
    ExpressionBody,

    /// <summary>A discard.</summary>
    Discard,

    /// <summary>An assignment to a local that is declared before it.</summary>
    LocalAssignment,

    /// <summary>The declaration of a local.</summary>
    Declaration
}

/// <summary>
///     A call to <c>Listen</c> or <c>ListenOnce</c> that SF2001 reports, and what
///     each fix needs to know about the code around it.
/// </summary>
internal sealed class DroppedListener
{
    private DroppedListener(
        InvocationExpressionSyntax call,
        ExpressionSyntax result,
        DroppedForm form,
        SemanticModel model,
        ITypeSymbol listenerType)
    {
        this.Call = call;
        this.Result = result;
        this.Form = form;
        this.Model = model;
        this.ListenerType = listenerType;
    }

    /// <summary>The call.</summary>
    public InvocationExpressionSyntax Call { get; }

    /// <summary>The expression whose value is the listener: the call, or the conditional access that holds it.</summary>
    public ExpressionSyntax Result { get; }

    /// <summary>The form of the code that drops the listener.</summary>
    public DroppedForm Form { get; }

    /// <summary>The semantic model of the document.</summary>
    public SemanticModel Model { get; }

    /// <summary>The type of the result of the call.</summary>
    public ITypeSymbol ListenerType { get; }

    /// <summary>The statement that has the call, for each form but <see cref="DroppedForm.ExpressionBody" />.</summary>
    public StatementSyntax? Statement =>
        this.Form switch
        {
            DroppedForm.Statement => (StatementSyntax?)this.Result.Parent,
            DroppedForm.Discard or DroppedForm.LocalAssignment => (StatementSyntax?)this.Result.Parent?.Parent,
            DroppedForm.Declaration => this.Result.FirstAncestorOrSelf<LocalDeclarationStatementSyntax>(),
            _ => null
        };

    /// <summary>The local of an assignment or a declaration.</summary>
    public ILocalSymbol? Local =>
        this.Form switch
        {
            DroppedForm.LocalAssignment when this.Result.Parent is AssignmentExpressionSyntax assignment =>
                this.Model.GetSymbolInfo(assignment.Left).Symbol as ILocalSymbol,
            DroppedForm.Declaration when this.Result.FirstAncestorOrSelf<VariableDeclaratorSyntax>() is { } declarator =>
                this.Model.GetDeclaredSymbol(declarator) as ILocalSymbol,
            _ => null
        };

    /// <summary>
    ///     Finds the form of the code around a call. Gives null for code that is
    ///     not one of the forms that SF2001 reports.
    /// </summary>
    public static DroppedListener? Find(InvocationExpressionSyntax call, SemanticModel model, CancellationToken cancellation)
    {
        if (model.GetTypeInfo(expression: call, cancellationToken: cancellation).Type is not { } listenerType)
        {
            return null;
        }

        ExpressionSyntax result =
            call.Parent is ConditionalAccessExpressionSyntax access && access.WhenNotNull == call ? access : call;

        DroppedForm? form = result.Parent switch
        {
            ExpressionStatementSyntax => DroppedForm.Statement,
            ArrowExpressionClauseSyntax or LambdaExpressionSyntax => DroppedForm.ExpressionBody,
            AssignmentExpressionSyntax { RawKind: (int)SyntaxKind.SimpleAssignmentExpression, Parent: ExpressionStatementSyntax } assignment
                when assignment.Right == result =>
                model.GetSymbolInfo(expression: assignment.Left, cancellationToken: cancellation).Symbol switch
                {
                    IDiscardSymbol => DroppedForm.Discard,
                    ILocalSymbol => DroppedForm.LocalAssignment,
                    _ => null
                },
            EqualsValueClauseSyntax { Parent: VariableDeclaratorSyntax { Parent: VariableDeclarationSyntax { Parent: LocalDeclarationStatementSyntax } } } =>
                DroppedForm.Declaration,
            _ => null
        };

        return form is { } found
            ? new DroppedListener(call: call, result: result, form: found, model: model, listenerType: listenerType)
            : null;
    }

    /// <summary>
    ///     The name for a new field or local. It is the name of the local, or a
    ///     name from the receiver of the call, as <c>clicksListener</c> for
    ///     <c>clicks.Listen</c>. A number makes it unique among the names given.
    /// </summary>
    public string NameFor(ISet<string> taken)
    {
        string name = this.Local?.Name is { Length: > 0 } local && local != "_" ? local : FromReceiver(this.Receiver());

        if (!taken.Contains(name))
        {
            return name;
        }

        int number = 2;

        while (taken.Contains(name + number))
        {
            number++;
        }

        return name + number;
    }

    /// <summary>
    ///     The names that a new name must not take. Each identifier in the node is
    ///     one, and so are the locals that later code declares. Each name in scope
    ///     at the call is one, and so are the members of the type and of its bases.
    /// </summary>
    public ISet<string> NamesNear(SyntaxNode scope)
    {
        HashSet<string> names = new(StringComparer.Ordinal);

        foreach (SyntaxToken token in scope.DescendantTokens().Where(static token => token.IsKind(SyntaxKind.IdentifierToken)))
        {
            names.Add(token.ValueText);
        }

        foreach (ISymbol symbol in this.Model.LookupSymbols(this.Call.SpanStart))
        {
            names.Add(symbol.Name);
        }

        return names;
    }

    // The receiver of the call: the expression before the name, or the first argument of the static
    // form of an extension method.
    private ExpressionSyntax? Receiver()
    {
        if (this.Model.GetSymbolInfo(this.Call).Symbol is IMethodSymbol { IsExtensionMethod: true, ReducedFrom: null, MethodKind: MethodKind.Ordinary })
        {
            return this.Call.ArgumentList.Arguments.FirstOrDefault()?.Expression;
        }

        return this.Call.Expression switch
        {
            MemberAccessExpressionSyntax access => access.Expression,
            MemberBindingExpressionSyntax => (this.Result as ConditionalAccessExpressionSyntax)?.Expression,
            _ => null
        };
    }

    private static string FromReceiver(ExpressionSyntax? receiver)
    {
        string text = receiver switch
        {
            IdentifierNameSyntax name => name.Identifier.ValueText,
            MemberAccessExpressionSyntax { Name: var name } => name.Identifier.ValueText,
            _ => string.Empty
        };

        if (text.StartsWith(value: "m_", comparisonType: StringComparison.Ordinal))
        {
            text = text.Substring(startIndex: 2);
        }

        text = text.TrimStart('_');

        return text.Length == 0
            ? "listener"
            : char.ToLowerInvariant(text[0]) + text.Substring(1) + "Listener";
    }
}
