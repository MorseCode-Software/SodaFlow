using System.Linq;
using Microsoft.CodeAnalysis;

namespace SodaFlow.Analyzers;

/// <summary>
///     One object that the code of a loop region makes. A creation, a
///     collection expression, an array, or a copy makes one object in one frame
///     of the walk. A member of such an object can be one object also.
/// </summary>
/// <remarks>
///     The object is an identity. Each name of it has it in its state, and
///     <see cref="ObjectStore" /> has the one state of the object. Thus, a
///     change through one name is a change for each name.
/// </remarks>
/// <param name="type">The type of the object. Null for <see cref="Unknown" />.</param>
internal sealed class MadeObject(in ITypeSymbol? type)
{
    private readonly ITypeSymbol? type = type;

    /// <summary>
    ///     An object that the store does not know. A value that is one of two
    ///     objects has it when the walk saw only one of the two made.
    /// </summary>
    public static readonly MadeObject Unknown = new(type: null);

    /// <summary>
    ///     True when a value of the given type can be this object. The element of
    ///     a dictionary is a pair, and is not the object that the pair holds.
    /// </summary>
    public bool CanBe(ITypeSymbol? written)
    {
        if (this.type == null || Construction.SaysNothing(written))
        {
            return true;
        }

        if (written!.IsValueType)
        {
            return false;
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
