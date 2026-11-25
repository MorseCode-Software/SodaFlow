using Microsoft.CodeAnalysis;

namespace SodaFlow.Analyzers;

// The part of the walker for a property with no getter to follow, which a foreach reads with no syntax of a read.
internal sealed partial class SinkWalker
{
    // The value of an auto-property of the object. The walk follows no getter for it. The construction of the object
    // gave the property its state, also from a parameter of a primary constructor. A method that sets the property
    // changes that state in the store.
    private LoopState ValueOfAnAutoProperty(LoopState owner, IPropertySymbol property) =>
        owner.Members is { } members
            ? members.StateThrough(member: property, implementations: this.region.Symbols.Implementations)
            : LoopState.None;
}
