namespace SodaFlow.Functional
{
    /// <summary>
    ///     A class representing the unit type (similar to <code>void</code>).
    /// </summary>
    public sealed class Unit
    {
        /// <summary>
        ///     The singleton value of type <see cref="Unit" />.
        /// </summary>
        public static readonly Unit Value = new Unit();

        private Unit()
        {
        }

        /// <summary>
        ///     Determines whether the given object is also a <see cref="Unit" />.
        /// </summary>
        /// <param name="obj">The object to compare against.</param>
        /// <returns>
        ///     <see langword="true" /> if <paramref name="obj" /> is a <see cref="Unit" />, and
        ///     <see langword="false" /> otherwise.
        /// </returns>
        /// <remarks>
        ///     There is only one value of this type, so any two instances are equal.
        /// </remarks>
        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj))
            {
                return false;
            }

            if (ReferenceEquals(this, obj))
            {
                return true;
            }

            if (obj.GetType() != this.GetType())
            {
                return false;
            }

            return true;
        }

        /// <summary>
        ///     Returns a hash code for this instance.
        /// </summary>
        /// <returns>The same constant for every instance, since all of them are equal.</returns>
        public override int GetHashCode() => 1;

        /// <summary>
        ///     Determines whether two references are equal.
        /// </summary>
        /// <param name="x">The first reference.</param>
        /// <param name="y">The second reference.</param>
        /// <returns>
        ///     <see langword="true" /> if both are <see langword="null" /> or neither is, since any two
        ///     non-null instances are equal.
        /// </returns>
        public static bool operator ==(Unit x, Unit y) => ReferenceEquals(x, null) == ReferenceEquals(y, null);

        /// <summary>
        ///     Determines whether two references differ, by negating <see cref="op_Equality" />.
        /// </summary>
        /// <param name="x">The first reference.</param>
        /// <param name="y">The second reference.</param>
        /// <returns>
        ///     <see langword="true" /> if exactly one of the two is <see langword="null" />.
        /// </returns>
        public static bool operator !=(Unit x, Unit y) => ReferenceEquals(x, null) != ReferenceEquals(y, null);
    }
}