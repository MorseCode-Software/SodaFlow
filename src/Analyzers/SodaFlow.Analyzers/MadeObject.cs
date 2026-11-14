using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;

namespace SodaFlow.Analyzers;

/// <summary>
///     One object that the code of a loop region makes. A creation, a
///     collection expression, an array, a copy, or a call makes one object in
///     one frame of the walk. A member of such an object can be one object
///     also.
/// </summary>
/// <remarks>
///     <para>
///         The object is an identity. Each name of it has it in its state, and
///         <see cref="ObjectStore" /> has the one state of the object. Thus, a
///         change through one name is a change for each name.
///     </para>
///     <para>
///         The body of a loop makes a new object in each turn. The walk reads
///         the body one time, and that read is one turn. The object of that
///         read is the object of this turn. A second identity is each object
///         that the same code made in the turns before.
///     </para>
/// </remarks>
/// <param name="type">The type of the object. Null for <see cref="Unknown" />.</param>
/// <param name="exact">
///     True when the object has the type, as the object of a creation has. False
///     for the result of a call and for a member. The code names a type there,
///     and the object can have a type that derives from it.
/// </param>
/// <param name="frame">The frame whose code made the object. Null for <see cref="Unknown" />.</param>
/// <param name="turn">The value of <see cref="Turn" />.</param>
/// <param name="isOfTurnsBefore">The value of <see cref="IsOfTurnsBefore" />.</param>
/// <param name="shapeOf">
///     The identity that gives <see cref="Shape" /> its first part. Null for an
///     identity that has a number of its own.
/// </param>
/// <param name="shapePart">The second part of <see cref="Shape" />, when <paramref name="shapeOf" /> is there.</param>
internal sealed class MadeObject(
    in ITypeSymbol? type,
    in bool exact,
    in LoopFrame? frame,
    in SyntaxNode? turn,
    in bool isOfTurnsBefore,
    in MadeObject? shapeOf,
    in int shapePart)
{
    private readonly LoopFrame? frame = frame;
    private readonly ITypeSymbol? type = type;

    /// <summary>
    ///     A number for the identity, as <see cref="LoopState.Shape" /> is for a
    ///     state. Each read of a loop makes a new identity for the object of its
    ///     turn. Those identities have one number, from the identity of the turns
    ///     before, which each read has. Thus, a read that only makes the object of
    ///     its turn again changes no number.
    /// </summary>
    public int Shape
    {
        // Zero is for an identity that has a number of its own.
        get => field != 0 ? field : RuntimeHelpers.GetHashCode(this);
    } = shapeOf == null ? 0 : unchecked(shapeOf.Shape * 397 + shapePart);

    /// <summary>
    ///     The body of the loop whose turn makes the object. Null for an object
    ///     that the code makes one time. For the objects of the turns before of a
    ///     loop, it is the body of the loop around that loop. Those turns are in
    ///     one turn of the loop around. Null when no loop is around it.
    /// </summary>
    public SyntaxNode? Turn { get; } = turn;

    /// <summary>
    ///     True for the object of a creation. It is a new object, and it has the
    ///     type of the creation. The result of a call can be an object that is
    ///     older than the call, and a member can have such an object. Each of the
    ///     two can have a type that derives from the type that the code names.
    /// </summary>
    public bool IsNew { get; } = exact;

    /// <summary>
    ///     True for the objects that a loop made in the turns before this one.
    ///     The identity is then two or more objects, and a write to it only adds.
    /// </summary>
    public bool IsOfTurnsBefore { get; } = isOfTurnsBefore;

    /// <summary>
    ///     An object that the store does not know. A value that is one of two
    ///     objects has it when the walk saw only one of the two made.
    /// </summary>
    public static readonly MadeObject Unknown =
        new(type: null, exact: false, frame: null, turn: null, isOfTurnsBefore: false, shapeOf: null, shapePart: 0);

    /// <summary>
    ///     True when the code of the given frame, or of a callee of that code,
    ///     made the object. When that code runs, it makes the object.
    /// </summary>
    public bool IsFrom(LoopFrame code) => this.frame?.IsWithin(code) == true;

    /// <summary>
    ///     The object in a member of this object, where the member has the given
    ///     type. It is of the turn of this object, or of the turns before as this
    ///     object is.
    /// </summary>
    public MadeObject InAMember(ITypeSymbol declared, ISymbol member) =>
        new(
            type: declared,
            exact: false,
            frame: this.frame,
            turn: this.Turn,
            isOfTurnsBefore: this.IsOfTurnsBefore,
            shapeOf: this,
            shapePart: member.Name.GetHashCode());

    /// <summary>
    ///     An identity for the objects that the same code makes in the turns
    ///     before of its loop, in each turn of each loop around it.
    /// </summary>
    public static MadeObject OfTurnsBefore(ITypeSymbol type, bool exact, LoopFrame frame) =>
        new(type: type, exact: exact, frame: frame, turn: null, isOfTurnsBefore: true, shapeOf: null, shapePart: 0);

    /// <summary>The object that the same code makes in this turn of its loop.</summary>
    /// <param name="turn">The body of the loop.</param>
    /// <param name="frame">The frame whose code makes the object. A second call of a callee has a new frame.</param>
    public MadeObject OfThisTurn(SyntaxNode turn, LoopFrame frame) =>
        new(
            type: this.type,
            exact: this.IsNew,
            frame: frame,
            turn: turn,
            isOfTurnsBefore: false,
            shapeOf: this,
            shapePart: 1);

    /// <summary>
    ///     An identity for the objects that the same code made in the turns
    ///     before of its loop. The turns are those in this turn of a loop around
    ///     that loop, and the identity is new in each turn of the loop around.
    /// </summary>
    /// <param name="around">The body of the loop around.</param>
    /// <param name="frame">The frame whose code made the objects.</param>
    public MadeObject OfTurnsBeforeIn(SyntaxNode around, LoopFrame frame) =>
        new(
            type: this.type,
            exact: this.IsNew,
            frame: frame,
            turn: around,
            isOfTurnsBefore: true,
            shapeOf: this,
            shapePart: 2);

    /// <summary>
    ///     True when a value of the given type can be this object. The element of
    ///     a dictionary is a pair, and is not the object that the pair holds.
    /// </summary>
    public bool CanBe(ITypeSymbol? written)
    {
        if (this.type == null || written == null || Construction.SaysNothing(written))
        {
            return true;
        }

        if (written.IsValueType)
        {
            return false;
        }

        // The result of a call can have a type that derives from the type that the code names. It
        // cannot be a value of a class with no relation to that type. A query is not one of its
        // elements, and a copy of a list is not one of the objects in it.
        if (!this.IsNew)
        {
            return CanHaveBoth(named: this.type, written: written);
        }

        for (ITypeSymbol? own = this.type; own != null; own = own.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(x: own, y: written))
            {
                return true;
            }
        }

        return this.type.AllInterfaces.Any(contract => SymbolEqualityComparer.Default.Equals(x: contract, y: written));
    }

    // True when one object can have the type that the code names for it and the type of the
    // location that it is read from. The check is false only where no type can derive from the two.
    private static bool CanHaveBoth(ITypeSymbol named, ITypeSymbol written)
    {
        if (written.TypeKind != TypeKind.Class
            || written.SpecialType is SpecialType.System_Object or SpecialType.System_Array)
        {
            return true;
        }

        return named.TypeKind switch
        {
            // An array is no value of a class.
            TypeKind.Array => false,
            // Two classes have one object only when one of them derives from the other.
            TypeKind.Class => DerivesFrom(type: named, other: written) || DerivesFrom(type: written, other: named),
            // A class that no type derives from has the interface, or its object does not.
            TypeKind.Interface =>
                !written.IsSealed
                || written.AllInterfaces.Any(contract =>
                    SymbolEqualityComparer.Default.Equals(x: contract.OriginalDefinition, y: named.OriginalDefinition)),
            _ => true
        };
    }

    private static bool DerivesFrom(ITypeSymbol type, ITypeSymbol other)
    {
        for (ITypeSymbol? own = type; own != null; own = own.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(x: own.OriginalDefinition, y: other.OriginalDefinition))
            {
                return true;
            }
        }

        return false;
    }
}
