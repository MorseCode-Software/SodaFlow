using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SodaFlow.Analyzers;

// The part of the walker that reads what a serializer writes, also through a converter.
internal sealed partial class SinkWalker
{
    /// <summary>
    ///     Reads what a serializer runs when it writes a value. It enumerates
    ///     each query that the value is or contains. For an object of this
    ///     compilation, it calls the getter of each property that it writes, and
    ///     reads each field that it writes. It writes the value of that member in
    ///     the same way. A converter can write a value for the serializer.
    /// </summary>
    /// <remarks>
    ///     An object of this compilation can keep a query that no property gives.
    ///     The serializer does not get that query. Thus, the walk reads the members
    ///     of such an object only through its getters.
    /// </remarks>
    /// <param name="written">The argument. A report about the value is at this node.</param>
    /// <param name="state">The state of the value.</param>
    /// <param name="serializer">The method of the serializer.</param>
    /// <param name="options">The options of the call.</param>
    /// <param name="member">The property or the field that has the value. Null for an argument.</param>
    /// <param name="declared">
    ///     The type that the call or the property gives the value, for a
    ///     serializer that writes it. Null for each other serializer.
    /// </param>
    /// <param name="depth">The number of objects that the value is in.</param>
    /// <param name="seen">
    ///     Each object of this compilation whose members the walk read for the
    ///     argument, with the type that the serializer wrote it as.
    /// </param>
    private void Serialize(
        ExpressionSyntax written,
        LoopState state,
        IMethodSymbol serializer,
        SerializerOptions options,
        ISymbol? member,
        ITypeSymbol? declared,
        int depth,
        ISet<(Construction, INamedTypeSymbol?)> seen)
    {
        // The elements of a tuple are fields, and not each serializer writes a field.
        if (state.Members is { IsTuple: true } && !options.WritesFields)
        {
            return;
        }

        this.Run(state: state, enumerated: null);

        // System.Text.Json stops with an error at this depth by default. Json.NET has no such
        // limit, and the walk stops there also.
        if (state.Members is not { } members || depth > MaxSerializedDepth)
        {
            return;
        }

        // An anonymous object or a tuple: the serializer writes each value in it.
        if (!members.HasType)
        {
            foreach (KeyValuePair<ISymbol, LoopState> part in members.Members)
            {
                this.Serialize(
                    written: written,
                    state: part.Value,
                    serializer: serializer,
                    options: options,
                    member: null,
                    declared: null,
                    depth: depth + 1,
                    seen: seen);
            }

            return;
        }

        INamedTypeSymbol? own = WrittenTypeOf(declared: declared, members: members);

        if (this.WritesThroughAConverter(
                written: written,
                state: state,
                serializer: serializer,
                options: options,
                member: member,
                own: own))
        {
            return;
        }

        // A second read of the same object as the same type finds the same reads, at the same
        // node. A getter of the object can give the object again, thus the walk stops there.
        if (!seen.Add((members, own)))
        {
            return;
        }

        foreach (IPropertySymbol property in own == null ? members.Properties : Construction.PropertiesOf(own))
        {
            if (property.GetMethod is not { } read
                || !SodaFlowSymbols.IsSerialized(property: property, serializer: serializer, options: options))
            {
                continue;
            }

            this.ReadTheTestsOf(written: written, state: state, serializer: serializer, member: property);

            if (this.evaluator.FollowImpliedOfTheObject(call: written, member: read, owner: state, arguments: [])
                is { } getter)
            {
                this.Report(getter);

                this.Serialize(
                    written: written,
                    state: getter.State,
                    serializer: serializer,
                    options: options,
                    member: property,
                    declared: declared == null ? null : property.Type,
                    depth: depth + 1,
                    seen: seen);
            }
        }

        IEnumerable<IFieldSymbol> fields =
            (own == null ? members.Fields : Construction.FieldsOf(own))
            .Where(field => SodaFlowSymbols.IsSerialized(field: field, serializer: serializer, options: options));

        foreach (IFieldSymbol field in fields)
        {
            this.ReadTheTestsOf(written: written, state: state, serializer: serializer, member: field);

            this.Serialize(
                written: written,
                state: LoopStateEvaluator.StateOfAMember(
                    owner: state,
                    member: field,
                    implementations: this.region.Symbols.Implementations),
                serializer: serializer,
                options: options,
                member: field,
                declared: declared == null ? null : field.Type,
                depth: depth + 1,
                seen: seen);
        }
    }

    // The serializer reads the ShouldSerialize method and the Specified getter of a member before it
    // writes the member.
    private void ReadTheTestsOf(SyntaxNode written, LoopState state, IMethodSymbol serializer, ISymbol member)
    {
        if (SodaFlowSymbols.ShouldSerializeOf(member: member, serializer: serializer) is { } test)
        {
            this.Report(
                this.evaluator.FollowImpliedOfTheObject(call: written, member: test, owner: state, arguments: []));
        }

        if (SodaFlowSymbols.SpecifiedOf(member: member, serializer: serializer) is { } specified)
        {
            this.Report(
                this.evaluator.FollowImpliedOfTheObject(call: written, member: specified, owner: state, arguments: []));
        }
    }

    /// <summary>
    ///     A converter that writes a value for the serializer. The first
    ///     is the one that the property or the field names. Then comes one of the
    ///     options for the type, and then the one that the type names. The walk
    ///     reads its write method with the value. The result is false with no
    ///     such converter.
    /// </summary>
    /// <remarks>
    ///     The serializer makes the converter of an attribute, which thus has no
    ///     state. A converter of the options has the state of its creation.
    /// </remarks>
    private bool WritesThroughAConverter(
        ExpressionSyntax written,
        LoopState state,
        IMethodSymbol serializer,
        SerializerOptions options,
        ISymbol? member,
        ISymbol? own)
    {
        if (state.Members is not { } members)
        {
            return false;
        }

        if ((Named(member)
             ?? Listed()
             ?? (own == null ? members.Types.Select(Named).FirstOrDefault(static found => found != null) : Named(own)))
            is not { } converter)
        {
            return false;
        }

        this.Report(
            this.evaluator.FollowImpliedOfTheObject(
                call: written,
                member: converter.Write,
                owner: converter.State,
                arguments: [(written, LoopState.None), (written, state), (written, LoopState.None)]));

        return true;

        (LoopState State, IMethodSymbol Write)? Named(ISymbol? holder) =>
            holder != null
            && SodaFlowSymbols.ConverterOf(holder) is { } type
            && SodaFlowSymbols.WriteOf(converter: type, serializer: serializer) is { } write
                ? (LoopState.None.WithMembers(Construction.Of(type)), write)
                : null;

        // A converter of System.Text.Json writes the type that it names, and only that type.
        (LoopState State, IMethodSymbol Write)? Listed() =>
            options.Converters
                .Select(made =>
                    this.region.TypeOf(made) is INamedTypeSymbol type
                    && SodaFlowSymbols.HandledBy(type) is { } handled
                    && (own == null ? members.Is(handled) : SymbolEqualityComparer.Default.Equals(x: handled, y: own))
                    && SodaFlowSymbols.WriteOf(converter: type, serializer: serializer) is { } write
                        ? (this.evaluator.Eval(made), write)
                        : ((LoopState State, IMethodSymbol Write)?)null)
                .FirstOrDefault(static found => found != null);
    }

    // The type whose properties the serializer writes for an object. It is the type that the
    // call or the property names, or the element type of a sequence of that type. The type of
    // the object counts where that type says nothing, or where it lets a derived type write its
    // own properties. Null then.
    private static INamedTypeSymbol? WrittenTypeOf(ITypeSymbol? declared, Construction members)
    {
        while (declared != null && !members.Is(declared) && SodaFlowSymbols.ElementTypeOf(declared) is { } element)
        {
            declared = element;
        }

        return declared is INamedTypeSymbol named
               && !Construction.SaysNothing(named)
               && members.Is(named)
               && !SodaFlowSymbols.IsPolymorphic(named)
            ? named
            : null;
    }
}
