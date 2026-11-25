using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace SodaFlow.Analyzers;

// The part of the evaluator for a local that gets a field of this, as s in var s = this.F. The local is then a second
// name of the field: see TheFirstNameOf.
internal sealed partial class LoopStateEvaluator
{
    // The field of this that a value reads, as this.F or F. A local that gets the value can be a second name of the
    // field. The field holds a reference to an object with no object of the store. Null for each other value.
    private ExpressionSyntax? AFieldOfThisIn(ExpressionSyntax value)
    {
        ExpressionSyntax read = Unwrapped(value);

        return read is IdentifierNameSyntax or MemberAccessExpressionSyntax { Expression: ThisExpressionSyntax }
               && this.region.SymbolOf(read) is IFieldSymbol { IsStatic: false, RefKind: RefKind.None } field
               && this.CanBeAnObject(field.Type)
               && this.Eval(read).Objects.IsEmpty
            ? read
            : null;
    }

    // Records a local that gets a field of this. After an assignment that is not sure to run, the local can be a second
    // name of the field, as for two locals.
    private void NotesALocalOfAFieldOfThis(ISymbol local, ExpressionSyntax field, bool sure)
    {
        if (sure)
        {
            this.Frame.FirstNameOf[local] = field;
            this.Frame.RunThatNamed[field] = this;
        }
        else
        {
            this.Frame.WasOneOfTwoNames.Add(local);
        }
    }

    // True when code in the windows can give a field of this a different value. Only a constructor can give a
    // readonly field of a class a value, and only with an assignment in its own code. A call can give each other
    // field a value, also through an assignment to this in a struct. Thus, for such a field, a call between ends the
    // record. Each window after the first starts at a call that the window before it has.
    private bool CanChangeTheFieldIn(
        IFieldSymbol field,
        ImmutableArray<(SyntaxNode Code, int After, int Before)> windows)
    {
        bool onlyAConstructor = field is { IsReadOnly: true, ContainingType.IsReferenceType: true };

        return windows.Any(window => window.Code
            .DescendantNodes(TextSpan.FromBounds(start: window.After, end: window.Before))
            .Any(node => node.SpanStart >= window.After
                         && node.Span.End <= window.Before
                         && ((node is ThisExpressionSyntax && GivesAValue(node))
                             || this.CanChangeTheLocation(
                                 node: node,
                                 key: (field, null, -1),
                                 name: field,
                                 aCallCounts: !onlyAConstructor))));
    }
}
