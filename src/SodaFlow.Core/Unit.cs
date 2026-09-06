namespace SodaFlow;

internal sealed class UnitInternal
{
    internal static readonly UnitInternal Value = new();

    private UnitInternal()
    {
    }

    public override bool Equals(object? obj)
    {
        if (ReferenceEquals(objA: null, objB: obj))
        {
            return false;
        }

        if (ReferenceEquals(objA: this, objB: obj))
        {
            return true;
        }

        return obj.GetType() == this.GetType();
    }

    public override int GetHashCode() => 1;

    public static bool operator ==(UnitInternal x, UnitInternal y) =>
        ReferenceEquals(objA: x, objB: null) == ReferenceEquals(objA: y, objB: null);

    public static bool operator !=(UnitInternal x, UnitInternal y) =>
        ReferenceEquals(objA: x, objB: null) != ReferenceEquals(objA: y, objB: null);
}
