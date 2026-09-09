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
/// <typeparam name="TId">The type of the immutable portion.</typeparam>
/// <typeparam name="TState">The type of the mutable portion.</typeparam>
[PublicAPI]
public sealed class Entry<TId, TState>
    where TId : notnull
{
    /// <summary>Creates an entry from its two halves.</summary>
    /// <param name="identity">The immutable portion, which the key is derived from.</param>
    /// <param name="state">The mutable portion.</param>
    public Entry(TId identity, TState state)
    {
        this.Identity = identity;
        this.State = state;
    }

    /// <summary>The immutable portion, which the key is derived from.</summary>
    public TId Identity { get; }

    /// <summary>The mutable portion.</summary>
    public TState State { get; }
}

/// <summary>
///     Optional convenience for identities that carry their own key. Use the
///     <c>Func&lt;TId, TKey&gt;</c> selector overloads instead when the identity type cannot or
///     should not implement this.
/// </summary>
/// <typeparam name="TKey">The type of the key this identity carries.</typeparam>
[PublicAPI]
public interface IIdentity<out TKey>
    where TKey : notnull
{
    /// <summary>The key derived from this identity.</summary>
    TKey Key { get; }
}
