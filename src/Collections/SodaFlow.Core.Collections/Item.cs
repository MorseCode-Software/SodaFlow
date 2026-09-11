using JetBrains.Annotations;

namespace SodaFlow.Collections;

/// <summary>
///     An item in the collection, split into its immutable portion (<see cref="Identity" />) and
///     its mutable portion (<see cref="State" />).
/// </summary>
/// <remarks>
///     The key is derived from <see cref="Identity" /> alone. Nothing in the update path can reach
///     <see cref="Identity" /> — updates carry only <c>Func&lt;TState, TState&gt;</c> — so key
///     stability is structural rather than validated at runtime. Changing an identity is therefore
///     expressed as a remove followed by an add, which is a structural edit.
/// </remarks>
/// <typeparam name="TIdentity">The type of the immutable portion.</typeparam>
/// <typeparam name="TState">The type of the mutable portion.</typeparam>
[PublicAPI]
public sealed class Item<TIdentity, TState>
    where TIdentity : notnull
{
    /// <summary>Creates an item from its two halves.</summary>
    /// <param name="identity">The immutable portion, which the key is derived from.</param>
    /// <param name="state">The mutable portion.</param>
    public Item(TIdentity identity, TState state)
    {
        this.Identity = identity;
        this.State = state;
    }

    /// <summary>The immutable portion, which the key is derived from.</summary>
    public TIdentity Identity { get; }

    /// <summary>The mutable portion.</summary>
    public TState State { get; }
}

/// <summary>
///     Optional convenience for identities that carry their own key: implement this and
///     <see cref="ReactiveCollection" />'s <c>Create</c> overloads will take the key from the
///     identity rather than asking for a selector.
/// </summary>
/// <remarks>
///     Entirely optional, and the selector overloads on
///     <see cref="ReactiveCollection{TKey,TIdentity,TState}" /> remain the way to do this when the
///     identity type cannot or should not implement an interface - a record from another assembly,
///     or one whose key is a projection rather than a property.
/// </remarks>
/// <typeparam name="TKey">The type of the key this identity carries.</typeparam>
[PublicAPI]
public interface IIdentity<out TKey>
    where TKey : notnull
{
    /// <summary>The key derived from this identity.</summary>
    TKey Key { get; }
}
