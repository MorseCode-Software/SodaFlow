using System.Collections.Generic;
using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Simplification;

namespace SodaFlow.Analyzers.CodeFixes;

/// <summary>
///     The fixes for SF2001, a listener that nothing holds. Each fix gives the
///     listener a holder, and the user chooses the one whose lifetime is right.
/// </summary>
/// <remarks>
///     <para>
///         The field fix puts the listener in a new private field, so that it lives
///         as long as its owner. The provider offers it in an instance member of a
///         class, where the code can write a field of the instance. In a static
///         member, a static field holds the listener for the life of the process.
///         Thus, the provider does not offer the fix there.
///     </para>
///     <para>
///         The <c>using</c> fix makes a <c>using</c> declaration of the listener,
///         so that it lives to the end of its block and then stops. The provider
///         offers it for a statement in a block, where C# allows that declaration.
///     </para>
///     <para>
///         There is no fix for all the reports at one time. Each fix gives a name,
///         and two fixes in one scope that are made apart can give the same name.
///     </para>
/// </remarks>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(DroppedListenerCodeFixProvider))]
[Shared]
// ReSharper disable once InheritdocConsiderUsage - This class has its own summary, which says more than the base class does.
public sealed class DroppedListenerCodeFixProvider : CodeFixProvider
{
    /// <summary>The id of the rule that this provider fixes.</summary>
    public const string DiagnosticId = "SF2001";

    /// <summary>The key of the field fix.</summary>
    public const string FieldKey = "SF2001.Field";

    /// <summary>The key of the <c>using</c> fix.</summary>
    public const string UsingKey = "SF2001.Using";

    /// <inheritdoc />
    public override ImmutableArray<string> FixableDiagnosticIds { get; } = ImmutableArray.Create(DiagnosticId);

    /// <inheritdoc />
    public override FixAllProvider? GetFixAllProvider() => null;

    /// <inheritdoc />
    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        Document document = context.Document;
        CancellationToken cancellation = context.CancellationToken;

        if (await document.GetSyntaxRootAsync(cancellation).ConfigureAwait(false) is not { } root
            || await document.GetSemanticModelAsync(cancellation).ConfigureAwait(false) is not { } model)
        {
            return;
        }

        foreach (Diagnostic diagnostic in context.Diagnostics)
        {
            if (root.FindNode(span: diagnostic.Location.SourceSpan, getInnermostNodeForTie: true)
                    .FirstAncestorOrSelf<InvocationExpressionSyntax>() is not { } call
                || DroppedListener.Find(call: call, model: model, cancellation: cancellation) is not { } dropped)
            {
                continue;
            }

            if (FieldOwner(dropped) is { } owner && CanWriteAField(dropped))
            {
                context.RegisterCodeFix(
                    action: CodeAction.Create(
                        title: "Keep the listener in a field",
                        createChangedDocument: token => KeepInAField(document: document, dropped: dropped, owner: owner, cancellation: token),
                        equivalenceKey: FieldKey),
                    diagnostic: diagnostic);
            }

            if (CanHoldWithUsing(dropped))
            {
                context.RegisterCodeFix(
                    action: CodeAction.Create(
                        title: "Hold the listener to the end of the block with 'using'",
                        createChangedDocument: token => HoldWithUsing(document: document, dropped: dropped, cancellation: token),
                        equivalenceKey: UsingKey),
                    diagnostic: diagnostic);
            }
        }
    }

    // The class that gets the field: the type that has the call. The call must be in an instance
    // member of the type, with no static function between them. A struct does not qualify, because
    // a lambda in a struct cannot write a field of the instance.
    private static TypeDeclarationSyntax? FieldOwner(DroppedListener dropped)
    {
        foreach (SyntaxNode node in dropped.Call.Ancestors())
        {
            switch (node)
            {
                case AnonymousFunctionExpressionSyntax function when function.Modifiers.Any(SyntaxKind.StaticKeyword):
                case LocalFunctionStatementSyntax local when local.Modifiers.Any(SyntaxKind.StaticKeyword):
                    return null;
                case TypeDeclarationSyntax type:
                    return type is ClassDeclarationSyntax
                           || (type is RecordDeclarationSyntax record && !record.ClassOrStructKeyword.IsKind(SyntaxKind.StructKeyword))
                        ? type
                        : null;
            }
        }

        return null;
    }

    // True when the member that has the call is a method of the instance. A static member and the
    // initializer of a field cannot use this. A declaration keeps its local when other code uses
    // the local, or when it has more than one local.
    private static bool CanWriteAField(DroppedListener dropped)
    {
        ISymbol? member = dropped.Model.GetEnclosingSymbol(dropped.Call.SpanStart);

        while (member is IMethodSymbol { MethodKind: MethodKind.AnonymousFunction or MethodKind.LocalFunction })
        {
            member = member.ContainingSymbol;
        }

        if (member is not IMethodSymbol { IsStatic: false })
        {
            return false;
        }

        return dropped.Form != DroppedForm.Declaration
               || (dropped.Statement is LocalDeclarationStatementSyntax { Declaration.Variables.Count: 1 }
                   && ReferencesOf(dropped: dropped, local: dropped.Local).Count == 0);
    }

    // True when C# allows a using declaration in the place of the statement: a statement of a block,
    // in C# 8 or later. Each listener is an IDisposable, but a declaration can give its local another
    // type. The code cannot assign a using local, thus a declaration whose local the code uses again
    // keeps its form. The rule does not report a using or a const declaration.
    private static bool CanHoldWithUsing(DroppedListener dropped)
    {
        if (dropped.Statement is not { Parent: BlockSyntax } statement
            || statement.SyntaxTree.Options is not CSharpParseOptions { LanguageVersion: >= LanguageVersion.CSharp8 }
            || dropped.Model.Compilation.GetSpecialType(SpecialType.System_IDisposable) is not { } disposable)
        {
            return false;
        }

        return dropped.Form switch
        {
            DroppedForm.Statement or DroppedForm.Discard => true,
            DroppedForm.Declaration =>
                statement is LocalDeclarationStatementSyntax { Declaration.Variables.Count: 1 }
                && dropped.Local is { Type: var type }
                && ReferencesOf(dropped: dropped, local: dropped.Local).Count == 0
                && dropped.Model.Compilation.ClassifyCommonConversion(source: type, destination: disposable).IsImplicit,
            _ => false
        };
    }

    private static async Task<Document> KeepInAField(
        Document document,
        DroppedListener dropped,
        TypeDeclarationSyntax owner,
        CancellationToken cancellation)
    {
        DocumentEditor editor = await DocumentEditor.CreateAsync(document: document, cancellationToken: cancellation).ConfigureAwait(false);
        SemanticModel model = dropped.Model;
        ISet<string> taken = dropped.NamesNear(owner);

        // The fix removes a local that it takes the place of, and the field can have its name.
        if (dropped.Local is { } replaced && (dropped.Form == DroppedForm.Declaration || UnusedDeclaration(dropped) != null))
        {
            taken.Remove(replaced.Name);
        }

        string name = dropped.NameFor(taken);

        ITypeSymbol fieldType =
            dropped is { Form: DroppedForm.Declaration, Statement: LocalDeclarationStatementSyntax { Declaration.Type.IsVar: false }, Local: { } local }
                ? local.Type
                : dropped.ListenerType;

        TypeSyntax typeSyntax = SyntaxFactory.ParseTypeName(fieldType.ToMinimalDisplayString(semanticModel: model, position: owner.SpanStart));

        if (model.GetNullableContext(owner.OpenBraceToken.SpanStart).AnnotationsEnabled() && fieldType.IsReferenceType)
        {
            typeSyntax = SyntaxFactory.NullableType(typeSyntax.WithoutTrivia());
        }

        FieldDeclarationSyntax field =
            SyntaxFactory.FieldDeclaration(
                    SyntaxFactory.VariableDeclaration(
                        type: typeSyntax,
                        variables: SyntaxFactory.SingletonSeparatedList(SyntaxFactory.VariableDeclarator(name))))
                .WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PrivateKeyword)))
                .WithAdditionalAnnotations(Formatter.Annotation);

        ExpressionSyntax access =
            SyntaxFactory.MemberAccessExpression(
                    kind: SyntaxKind.SimpleMemberAccessExpression,
                    expression: SyntaxFactory.ThisExpression(),
                    name: SyntaxFactory.IdentifierName(name))
                .WithAdditionalAnnotations(Simplifier.Annotation);

        if (dropped.Form is DroppedForm.Statement or DroppedForm.ExpressionBody)
        {
            editor.ReplaceNode(
                node: dropped.Result,
                newNode: SyntaxFactory.AssignmentExpression(
                        kind: SyntaxKind.SimpleAssignmentExpression,
                        left: access,
                        right: dropped.Result.WithoutTrivia())
                    .WithTriviaFrom(dropped.Result)
                    .WithAdditionalAnnotations(Formatter.Annotation));
        }
        else if (dropped.Result.Parent is AssignmentExpressionSyntax assignment)
        {
            // A discard, or an assignment to a local.
            editor.ReplaceNode(node: assignment.Left, newNode: access.WithTriviaFrom(assignment.Left));
            RemoveUnusedDeclaration(editor: editor, dropped: dropped);
        }
        else if (dropped.Statement is LocalDeclarationStatementSyntax declaration)
        {
            editor.ReplaceNode(
                node: declaration,
                newNode: SyntaxFactory.ExpressionStatement(
                        SyntaxFactory.AssignmentExpression(
                            kind: SyntaxKind.SimpleAssignmentExpression,
                            left: access,
                            right: dropped.Result.WithoutTrivia()))
                    .WithTriviaFrom(declaration)
                    .WithAdditionalAnnotations(Formatter.Annotation));
        }

        if (owner.Members.OfType<FieldDeclarationSyntax>().LastOrDefault() is { } last)
        {
            editor.InsertAfter(node: last, newNode: field);
        }
        else
        {
            editor.InsertMembers(declaration: owner, index: 0, members: [field]);
        }

        return editor.GetChangedDocument();
    }

    // An assignment to a local leaves the declaration of the local with no use, which is a warning.
    // The fix removes the declarator of that local when it has no value and nothing else uses the
    // local. When the local is the only one of its statement, the fix removes the statement.
    private static void RemoveUnusedDeclaration(SyntaxEditor editor, DroppedListener dropped)
    {
        if (UnusedDeclaration(dropped) is { } declarator)
        {
            editor.RemoveNode(
                node: declarator.Parent is VariableDeclarationSyntax { Variables.Count: 1, Parent: LocalDeclarationStatementSyntax statement }
                    ? statement
                    : declarator,
                options: SyntaxRemoveOptions.KeepNoTrivia);
        }
    }

    private static VariableDeclaratorSyntax? UnusedDeclaration(DroppedListener dropped) =>
        dropped is { Form: DroppedForm.LocalAssignment, Local: { } local }
        && local.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() is VariableDeclaratorSyntax
        {
            Initializer: null,
            Parent: VariableDeclarationSyntax { Parent: LocalDeclarationStatementSyntax }
        } declarator
        && ReferencesOf(dropped: dropped, local: local).Count == 1
            ? declarator
            : null;

    private static async Task<Document> HoldWithUsing(Document document, DroppedListener dropped, CancellationToken cancellation)
    {
        SyntaxNode root =
            await document.GetSyntaxRootAsync(cancellation).ConfigureAwait(false)
            ?? await dropped.Call.SyntaxTree.GetRootAsync(cancellation).ConfigureAwait(false);

        if (dropped.Statement is not { } statement)
        {
            return document;
        }

        StatementSyntax replacement;

        if (statement is LocalDeclarationStatementSyntax declaration)
        {
            replacement =
                declaration.WithoutLeadingTrivia()
                    .WithUsingKeyword(
                        SyntaxFactory.Token(
                            leading: declaration.GetLeadingTrivia(),
                            kind: SyntaxKind.UsingKeyword,
                            trailing: SyntaxFactory.TriviaList(SyntaxFactory.Space)));
        }
        else
        {
            SyntaxNode scope = statement.FirstAncestorOrSelf<MemberDeclarationSyntax>() ?? root;
            string name = dropped.NameFor(dropped.NamesNear(scope));

            replacement =
                SyntaxFactory.LocalDeclarationStatement(
                        SyntaxFactory.VariableDeclaration(
                            type: SyntaxFactory.IdentifierName("var"),
                            variables: SyntaxFactory.SingletonSeparatedList(
                                SyntaxFactory.VariableDeclarator(name)
                                    .WithInitializer(SyntaxFactory.EqualsValueClause(dropped.Result.WithoutTrivia())))))
                    .WithUsingKeyword(SyntaxFactory.Token(SyntaxKind.UsingKeyword))
                    .WithTriviaFrom(statement)
                    .WithAdditionalAnnotations(Formatter.Annotation);
        }

        return document.WithSyntaxRoot(root.ReplaceNode(oldNode: statement, newNode: replacement));
    }

    // The identifiers in the member of the call that name the local, but for its declaration.
    private static List<IdentifierNameSyntax> ReferencesOf(DroppedListener dropped, ISymbol? local)
    {
        if (local == null)
        {
            return [];
        }

        SyntaxNode scope = dropped.Call.FirstAncestorOrSelf<MemberDeclarationSyntax>() ?? dropped.Call.SyntaxTree.GetRoot();

        return
        [
            .. scope.DescendantNodes()
                .OfType<IdentifierNameSyntax>()
                .Where(name => name.Identifier.ValueText == local.Name
                               && SymbolEqualityComparer.Default.Equals(x: dropped.Model.GetSymbolInfo(name).Symbol, y: local))
        ];
    }
}
