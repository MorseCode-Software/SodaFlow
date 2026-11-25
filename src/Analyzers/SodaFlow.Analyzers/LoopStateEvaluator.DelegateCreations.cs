using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SodaFlow.Analyzers;

// The part of the evaluator for a creation of a delegate from one function, as in new Func<int, int>(this.Add).
internal sealed partial class LoopStateEvaluator
{
    /// <summary>
    ///     The state of an object that a <c>new</c> expression makes. A
    ///     delegate that the code makes from one function has the state of that
    ///     function. Each other object has the state of its creation.
    /// </summary>
    /// <remarks>
    ///     A delegate of one function runs that function and nothing else.
    ///     Thus, a <c>Map</c> or a <c>Lazy</c> of the delegate runs the same
    ///     code as a <c>Map</c> or a <c>Lazy</c> of the function.
    /// </remarks>
    /// <param name="creation">The <c>new</c> expression.</param>
    private LoopState EvalNew(ExpressionSyntax creation) =>
        creation switch
        {
            ObjectCreationExpressionSyntax { ArgumentList.Arguments: { Count: 1 } arguments } => arguments[0],
            ImplicitObjectCreationExpressionSyntax { ArgumentList.Arguments: { Count: 1 } arguments } => arguments[0],
            _ => null
        } is { } only
        && this.region.TypeOf(creation) is { TypeKind: TypeKind.Delegate }
            ? this.Eval(only.Expression)
            : this.EvalCreation(creation);
}
