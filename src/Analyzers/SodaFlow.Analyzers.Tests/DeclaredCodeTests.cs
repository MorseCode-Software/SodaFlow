using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace SodaFlow.Analyzers.Tests;

/// <summary>
///     Checks that <see cref="DeclaredCode" /> gives each compilation the
///     answer for all of its trees. A different compilation can read one of
///     those trees first.
/// </summary>
internal sealed class DeclaredCodeTests
{
    /// <summary>
    ///     A type with no code of its own runs code when its base type runs
    ///     code. The base type can be in a different tree. An edit gives the base
    ///     type a constructor with a body, as the IDE does. The new compilation
    ///     has the tree of the derived type that the first compilation also has.
    ///     Each compilation must keep its own answer.
    /// </summary>
    [Test]
    public async Task AnEditOfABaseTypeChangesTheAnswerOnlyForTheNewCompilation()
    {
        SyntaxTree derived = CSharpSyntaxTree.ParseText(
            """
            internal class Derived : Base
            {
            }

            internal static class Maker
            {
                public static object Make() => new Derived();
            }
            """);

        SyntaxTree baseWithNoCode = CSharpSyntaxTree.ParseText("internal class Base { }");
        SyntaxTree baseWithCode = CSharpSyntaxTree.ParseText("internal class Base { public Base() { } }");

        CSharpCompilation before =
            CSharpCompilation.Create(assemblyName: "Before", syntaxTrees: [derived, baseWithNoCode]);

        CSharpCompilation after = before.ReplaceSyntaxTree(oldTree: baseWithNoCode, newTree: baseWithCode);

        SyntaxNode root = await derived.GetRootAsync().ConfigureAwait(false);

        ObjectCreationExpressionSyntax creation =
            root.DescendantNodes().OfType<ObjectCreationExpressionSyntax>().Single();

        bool runsBefore = DeclaredCode.Of(before).CanRunCode(creation);
        bool runsAfter = DeclaredCode.Of(after).CanRunCode(creation);
        bool runsBeforeAgain = DeclaredCode.Of(before).CanRunCode(creation);

        await Assert.That(runsBefore).IsFalse();
        await Assert.That(runsAfter).IsTrue();
        await Assert.That(runsBeforeAgain).IsFalse();
    }
}
