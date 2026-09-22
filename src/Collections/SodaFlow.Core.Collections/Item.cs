using JetBrains.Annotations;

namespace SodaFlow.Collections;

/// <summary>
///     An item in the collection, in two parts: the immutable part
///     (<see cref="Identity" />) and the mutable part (<see cref="State" />).
/// </summary>
/// <remarks>
///     The key comes from <see cref="Identity" /> only. No code in the update path can read
///     <see cref="Identity" />, because an update holds only a
///     <c>Func&lt;TState, TState&gt;</c>. Thus, the structure keeps each key constant, and no test
///     at run time is necessary. A change to an identity is a removal and then an add, which is a
///     structural edit.
/// </remarks>
/// <typeparam name="TIdentity">The type of the immutable part.</typeparam>
/// <typeparam name="TState">The type of the mutable part.</typeparam>
[PublicAPI]
public sealed class Item<TIdentity, TState>
    where TIdentity : notnull
{
    /// <summary>Creates an item from its two halves.</summary>
    /// <param name="identity">The immutable part, which gives the key.</param>
    /// <param name="state">The mutable part.</param>
    public Item(TIdentity identity, TState state)
    {
        this.Identity = identity;
        this.State = state;
    }

    /// <summary>The immutable part, which gives the key.</summary>
    public TIdentity Identity { get; }

    /// <summary>The mutable part.</summary>
    public TState State { get; }
}

/// <summary>
///     An optional short path for an identity that holds its own key. Use this interface, and the
///     <c>Create</c> overloads of <see cref="ReactiveCollection" /> read the key from the identity,
///     and a selector is not necessary.
/// </summary>
/// <remarks>
///     This interface is optional. The overloads with a selector on
///     <see cref="ReactiveCollection{TKey,TIdentity,TState}" /> are the path for an identity type
///     that cannot use an interface, or that must not use one. Examples are a record from a
///     different assembly, and a record whose code calculates the key and does not hold it in a
///     property.
/// </remarks>
/// <typeparam name="TKey">The type of the key this identity carries.</typeparam>
[PublicAPI]
public interface IIdentity<out TKey>
    where TKey : notnull
{
    /// <summary>The key derived from this identity.</summary>
    TKey Key { get; }
}
