using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SodaFlow.Analyzers;

// The part of the walker for the initializer of a static member. The type initializer runs each such initializer one
// time, in the order of the code, and no statement can end that run before an initializer.
internal sealed partial class SinkWalker
{
    // True for the value clause of the initializer of a static field or of a static property.
    private static bool IsTheInitializerOfAStaticMember(SyntaxNode clause) =>
        clause switch
        {
            EqualsValueClauseSyntax { Parent: VariableDeclaratorSyntax { Parent.Parent: FieldDeclarationSyntax field } } =>
                field.Modifiers.Any(SyntaxKind.StaticKeyword),
            EqualsValueClauseSyntax { Parent: PropertyDeclarationSyntax property } =>
                property.Modifiers.Any(SyntaxKind.StaticKeyword),
            _ => false
        };
}
