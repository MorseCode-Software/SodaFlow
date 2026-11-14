using System.Linq;
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
internal sealed class MadeObject(
    in ITypeSymbol? type,
    in bool exact,
    in LoopFrame? frame,
    in SyntaxNode? turn,
    in bool isOfTurnsBefore)
{
    private readonly LoopFrame? frame = frame;
    private readonly ITypeSymbol? type = type;

    /// <summary>
    ///     The body of the loop whose turn makes the object. Null for an object
    ///     that the code makes one time, and for the objects of the turns before.
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
        new(type: null, exact: false, frame: null, turn: null, isOfTurnsBefore: false);

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
    public MadeObject InAMember(ITypeSymbol declared) =>
        new(
            type: declared,
            exact: false,
            frame: this.frame,
            turn: this.Turn,
            isOfTurnsBefore: this.IsOfTurnsBefore);

    /// <summary>A new identity for the objects that the same code made in the turns before.</summary>
    public MadeObject OfTurnsBefore() =>
        new(type: this.type, exact: this.IsNew, frame: this.frame, turn: null, isOfTurnsBefore: true);

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

        if (!this.IsNew)
        {
            return true;
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
}
