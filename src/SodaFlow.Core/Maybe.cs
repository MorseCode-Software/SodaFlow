using System;
using System.Collections.Generic;

namespace SodaFlow;

internal static class MaybeInternal
{
    public static MaybeInternal<T> Some<T>(T value) => MaybeInternal<T>.Some(value);
}

internal readonly struct MaybeInternal<T>
    : IEquatable<MaybeInternal<T>>
{
    private readonly bool hasValue;
    private readonly T value;

    private MaybeInternal(T value)
    {
        this.hasValue = true;
        this.value = value;
    }

    #region Type Constructors

    internal static MaybeInternal<T> Some(T value) => new(value);
    internal static readonly MaybeInternal<T> None = new();

    #endregion

    #region Base Functionality

    internal TResult Match<TResult>(Func<T, TResult> onSome, Func<TResult> onNone) =>
        this.hasValue ? onSome(this.value) : onNone();

    // ReSharper disable once UnusedMember.Global - Left for parity with C# Maybe<T> implementation.
    internal void MatchVoid(Action<T> onSome, Action onNone)
    {
        if (this.hasValue)
        {
            onSome(this.value);
        }
        else
        {
            onNone();
        }
    }

    internal void MatchSome(Action<T> onSome)
    {
        if (this.hasValue)
        {
            onSome(this.value);
        }
    }

    // ReSharper disable once UnusedMember.Global - Left for parity with C# Maybe<T> implementation.
    internal void MatchNone(Action onNone)
    {
        if (!this.hasValue)
        {
            onNone();
        }
    }

    #endregion

    #region Helper Methods

    internal bool HasValue() => this.hasValue;

    /// <summary>
    ///     Reads the value without going through a callback, for hot paths where the delegates
    ///     <see cref="Match{TResult}" /> and friends require would be allocated per call.
    /// </summary>
    internal bool TryGetValue(out T v)
    {
        v = this.value;
        return this.hasValue;
    }

    #endregion

    public static bool operator ==(MaybeInternal<T> x, MaybeInternal<T> y) =>
        x.hasValue == y.hasValue && EqualityComparer<T>.Default.Equals(x: x.value, y: y.value);

    public static bool operator !=(MaybeInternal<T> x, MaybeInternal<T> y) => !(x == y);
    public override bool Equals(object? obj) => obj is MaybeInternal<T> m && this == m;

    public override int GetHashCode()
    {
        unchecked
        {
            int hashCode = this.hasValue.GetHashCode();

            if (this.value is not null)
            {
                hashCode = (hashCode * 397) ^ EqualityComparer<T>.Default.GetHashCode(this.value);
            }

            return hashCode;
        }
    }

    public override string ToString() =>
        this.Match(onSome: static v => $"{{Some: {v}}}", onNone: static () => "{None}");

    /// <inheritdoc />
    public bool Equals(MaybeInternal<T> other) => this == other;
}
