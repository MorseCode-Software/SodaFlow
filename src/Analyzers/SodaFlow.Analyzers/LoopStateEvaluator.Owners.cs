using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SodaFlow.Analyzers;

// The part of the evaluator for the owner of a write to a member that the region keeps for the field. A read through a
// different owner does not get the write: see LoopRegion.SetFieldOfAnOwner.
internal sealed partial class LoopStateEvaluator
{
    // The owner of this for the code of a method group, which the call of the group gives: see OwnerOfAGroup.
    private object? givenOwnerOfThis;

    // The owner of this in the method of a method group. For a group of this, it is the owner of this in the code that
    // names the group. For a group of a different owner, as in this.slot.Add, it is that owner.
    private static object? OwnerOfAGroup(FunctionValue function) =>
        function.OfThis?.OwnerOfThis(function.Declaration)
        ?? (function.Owner is (var namer, var written)
            ? namer.KnownOwnerOf(owner: written, has: namer.Eval(written))
            : null);

    /// <summary>
    ///     The one owner of a member write or a member read, when the walk knows
    ///     that object. Null when the owner can be an object that the walk does
    ///     not know.
    /// </summary>
    /// <remarks>
    ///     An owner is a static location whose object no code changes. An
    ///     object of the store that a creation of this code made one time is an
    ///     owner also. Such an object is not older than the block. Thus, it is
    ///     never the object of a static location. The object in a field of a
    ///     known owner is an owner too, where no code can change it: see
    ///     OwnerInAField.
    /// </remarks>
    /// <param name="owner">The expression of the owner, or null.</param>
    /// <param name="has">The state of the owner.</param>
    private object? KnownOwnerOf(ExpressionSyntax? owner, LoopState has)
    {
        if (owner == null)
        {
            return null;
        }

        ExpressionSyntax written = Unwrapped(owner);

        // A local with one value holds the object of that value, as in var slot = this.slot.
        while (has.Objects.IsEmpty
               && written is IdentifierNameSyntax
               && this.region.SymbolOf(written) is ILocalSymbol { RefKind: RefKind.None } local
               && this.OnlyValueOf(local) is { } value
               && Unwrapped(value) is (IdentifierNameSyntax or MemberAccessExpressionSyntax) and var named)
        {
            written = named;
            has = this.Eval(named);
        }

        if (written is ThisExpressionSyntax)
        {
            return this.OwnerOfThis(written);
        }

        if (this.StaticOwnerOf(written) is { } location)
        {
            return location;
        }

        // The objects of the store come first. A field that got an object after its initializer has that object. The
        // object that the store makes for a field at a read is the object of its initializer. A name can also hold a
        // value with no object of the store, and its state does not show that value. Such a name has no owner.
        return has.Objects.Length switch
        {
            > 1 => null,
            1 when this.region.Objects.MemberOf(has.Objects[0]) != null =>
                this.KnownOwnerOf(has.Objects[0]) ?? this.OwnerInAField(written),
            1 => this.HoldsOneValue(written) ? this.KnownOwnerOf(has.Objects[0]) : null,
            _ => this.OwnerInAField(written)
        };
    }

    // The owner of a read of a member: this, for a member that the code names alone, or the expression before the name.
    private object? OwnerOfARead(SyntaxNode read) =>
        read switch
        {
            IdentifierNameSyntax => this.OwnerOfThis(read),
            MemberAccessExpressionSyntax { Expression: var owner } =>
                this.KnownOwnerOf(owner: owner, has: this.Eval(owner)),
            _ => null
        };

    // The owner of a write or a read through this at a node. In a constructor, it is the object that the constructor
    // builds. In a lambda or a local function, it is the owner of this in the code that contains the function. In a
    // method, it is the receiver of the call, where the node is in the code of the class of the method. The walk reads
    // the build of a member there with the object of the call as this: see WithTheBuildOfAMember.
    private object? OwnerOfThis(SyntaxNode at)
    {
        if (this.givenOwnerOfThis != null)
        {
            return this.givenOwnerOfThis;
        }

        if (this.thisInTheStore is { } built)
        {
            return this.KnownOwnerOf(built);
        }

        if (this.function != null)
        {
            return this.definer?.OwnerOfThis(at);
        }

        if (this.call is not { Site: InvocationExpressionSyntax site }
            || this.call.Caller.region.SymbolOf(site) is not IMethodSymbol { IsStatic: false } method
            || !IsInTheCodeOf(node: at, type: method.ContainingType))
        {
            return null;
        }

        LoopStateEvaluator caller = this.call.Caller;

        return site.Expression switch
        {
            MemberAccessExpressionSyntax { Expression: var receiver } =>
                caller.KnownOwnerOf(owner: receiver, has: caller.Eval(receiver)),
            IdentifierNameSyntax => caller.OwnerOfThis(site),
            _ => null
        };
    }

    // A static field, or an element of the array of a static field at a constant index, whose object no code changes:
    // see UnchangedStatics.
    private StaticOwner? StaticOwnerOf(SyntaxNode written) =>
        written switch
        {
            ElementAccessExpressionSyntax { ArgumentList.Arguments: { Count: 1 } arguments } element
                when Unwrapped(element.Expression)
                         is (IdentifierNameSyntax or MemberAccessExpressionSyntax) and var named
                     && this.TheArrayOfAStaticField(named) is { } field
                     && this.region.Symbols.Unchanged.CreationsIn(field) is { IsEmpty: false } creations
                     && this.ConstantIndexOf(arguments[0]) is { } index
                     && index < creations.Length =>
                new StaticOwner(field: field, index: index),
            IdentifierNameSyntax or MemberAccessExpressionSyntax
                when this.region.SymbolOf(written) is IFieldSymbol { IsStatic: true } field
                     && this.region.Symbols.Unchanged.CreationOf(field) != null =>
                new StaticOwner(field: field, index: -1),
            _ => null
        };

    // The object in a field of a known owner, as in this.slot, slot, or holder.Slot. The initializer of the field
    // makes the object, and no code can give the field a different one. A readonly field that no constructor writes is
    // such a field. A field of a holder local is one also: see KeepsTheObjectsOfItsFields.
    private MemberOwner? OwnerInAField(SyntaxNode written)
    {
        if (written is not (IdentifierNameSyntax or MemberAccessExpressionSyntax)
            || this.region.SymbolOf(written) is not IFieldSymbol { IsStatic: false } field
            || field.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax()
                is not VariableDeclaratorSyntax { Initializer.Value: BaseObjectCreationExpressionSyntax })
        {
            return null;
        }

        ExpressionSyntax? holder = (written as MemberAccessExpressionSyntax)?.Expression;

        bool keepsIt =
            this.region.Symbols.Unchanged.KeepsItsObject(field)
            || (holder is IdentifierNameSyntax
                && this.region.SymbolOf(holder) is ILocalSymbol local
                && this.KeepsTheObjectsOfItsFields(local));

        object? owner =
            !keepsIt
                ? null
                : holder is null or ThisExpressionSyntax
                    ? this.OwnerOfThis(written)
                    : this.KnownOwnerOf(owner: holder, has: this.Eval(holder));

        return owner == null ? null : new MemberOwner(owner: owner, field: field);
    }

    // True for a name that holds the value of one expression. That is a local with one assignment, a parameter that its
    // code gives no value, a call, or a creation. An element of such a name is one also, where the collection holds
    // only what the walk saw go in.
    private bool HoldsOneValue(SyntaxNode written) =>
        written switch
        {
            IdentifierNameSyntax when this.region.SymbolOf(written) is { } name =>
                name switch
                {
                    ILocalSymbol { RefKind: RefKind.None } local => this.OnlyValueOf(local) != null,
                    IParameterSymbol { RefKind: RefKind.None } parameter =>
                        FunctionOf(written) is { } code
                        && !this.UsesOf(name: parameter, code: code).Any(static use => use.Gives),
                    _ => false
                },
            ElementAccessExpressionSyntax element =>
                !this.CanBeUnseen(element) && this.HoldsOneValue(Unwrapped(element.Expression)),
            InvocationExpressionSyntax or BaseObjectCreationExpressionSyntax => true,
            _ => false
        };

    // An object of the store as an owner. A creation of this code made it one time, and not in a loop: see
    // MadeObject.IsNew. The object of a static member is no such owner, because a static field names it.
    private object? KnownOwnerOf(MadeObject one)
    {
        if (ReferenceEquals(objA: one, objB: MadeObject.Unknown) || one.Turn != null || one.IsOfTurnsBefore)
        {
            return null;
        }

        return one.IsNew && !this.region.IsAnOlderObject(one) && !this.region.Objects.IsInAStaticMember(one)
            ? one
            : null;
    }

    // True for a local that gets a new object of a class with no constructor of its own. Each use of the local must
    // read a field of it, as in holder.Slot.Share. No code can then get the object, or give a field a different object.
    private bool KeepsTheObjectsOfItsFields(ISymbol local)
    {
        if (this.region.HoldersOfFields.TryGetValue(key: local, value: out bool keeps))
        {
            return keeps;
        }

        keeps =
            local.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax()
                is VariableDeclaratorSyntax
                {
                    Initializer.Value: BaseObjectCreationExpressionSyntax creation
                } declarator
            && this.region.SymbolOf(creation) is IMethodSymbol
            {
                IsImplicitlyDeclared: true,
                ContainingType: { TypeKind: TypeKind.Class, BaseType.SpecialType: SpecialType.System_Object }
            }
            && FunctionOf(declarator) is { } code
            && code.DescendantNodes()
                .OfType<IdentifierNameSyntax>()
                .Where(name => name.Identifier.ValueText == local.Name)
                .All(name =>
                    name.Parent is MemberAccessExpressionSyntax
                    {
                        Parent: not (ArgumentSyntax or RefExpressionSyntax)
                    } access
                    && access.Expression == name
                    && !UnchangedStatics.IsWritten(access)
                    && this.region.SymbolOf(access) is IFieldSymbol);

        this.region.HoldersOfFields[local] = keeps;

        return keeps;
    }

    // The static field that a name of an array names. A local whose declaration gets the field, and whose each use only
    // reads an element, names the field also: see UnchangedStatics.LocalOfOnlyReads.
    private IFieldSymbol? TheArrayOfAStaticField(SyntaxNode named) =>
        this.region.SymbolOf(named) switch
        {
            IFieldSymbol { IsStatic: true, Type: IArrayTypeSymbol { Rank: 1 } } field => field,
            ILocalSymbol { RefKind: RefKind.None, Type: IArrayTypeSymbol { Rank: 1 } } local
                when local.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax()
                         is VariableDeclaratorSyntax { Initializer.Value: { } value }
                     && Unwrapped(value) is (IdentifierNameSyntax or MemberAccessExpressionSyntax) and var initial
                     && this.region.SymbolOf(initial)
                         is IFieldSymbol { IsStatic: true, Type: IArrayTypeSymbol { Rank: 1 } } field => field,
            _ => null
        };
}
