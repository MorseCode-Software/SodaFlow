using System;
using System.Globalization;
using NUnit.Framework;
using SodaFlow.Functional;

namespace SodaFlow.Tests
{
    [TestFixture]
    public class StringExtensionMethodsTests
    {
        private enum Color
        {
            Red = 0,
            Green = 1,
            Blue = 2
        }

        [Test]
        public void TestTryParseInt32()
        {
            Assert.AreEqual(Maybe.Some(42), "42".TryParseInt32());
            Assert.AreEqual(Maybe.Some(-42), "-42".TryParseInt32());
            Assert.AreEqual(Maybe<int>.None, "x".TryParseInt32());
            Assert.AreEqual(Maybe<int>.None, string.Empty.TryParseInt32());
            Assert.AreEqual(Maybe<int>.None, ((string)null).TryParseInt32());
            Assert.AreEqual(Maybe<int>.None, "2147483648".TryParseInt32());
        }

        [Test]
        public void TestTryParseInt32WithStyles()
        {
            Assert.AreEqual(
                Maybe.Some(1234),
                "1,234".TryParseInt32(NumberStyles.Integer | NumberStyles.AllowThousands, CultureInfo.InvariantCulture));
            Assert.AreEqual(
                Maybe<int>.None,
                "1,234".TryParseInt32(NumberStyles.Integer, CultureInfo.InvariantCulture));
        }

        [Test]
        public void TestTryParseIntegralTypes()
        {
            Assert.AreEqual(Maybe.Some((byte)7), "7".TryParseByte());
            Assert.AreEqual(Maybe<byte>.None, "256".TryParseByte());
            Assert.AreEqual(Maybe.Some((sbyte)-7), "-7".TryParseSByte());
            Assert.AreEqual(Maybe.Some((short)-7), "-7".TryParseInt16());
            Assert.AreEqual(Maybe.Some((ushort)7), "7".TryParseUInt16());
            Assert.AreEqual(Maybe.Some(7u), "7".TryParseUInt32());
            Assert.AreEqual(Maybe.Some(-7L), "-7".TryParseInt64());
            Assert.AreEqual(Maybe.Some(7ul), "7".TryParseUInt64());
            Assert.AreEqual(Maybe<uint>.None, "-7".TryParseUInt32());
        }

        [Test]
        public void TestTryParseRealTypes()
        {
            Assert.AreEqual(Maybe.Some(1.5f), "1.5".TryParseSingle(NumberStyles.Float, CultureInfo.InvariantCulture));
            Assert.AreEqual(Maybe.Some(1.5d), "1.5".TryParseDouble(NumberStyles.Float, CultureInfo.InvariantCulture));
            Assert.AreEqual(Maybe.Some(1.5m), "1.5".TryParseDecimal(NumberStyles.Number, CultureInfo.InvariantCulture));
            Assert.AreEqual(Maybe<double>.None, "x".TryParseDouble(NumberStyles.Float, CultureInfo.InvariantCulture));
        }

        [Test]
        public void TestTryParseBoolean()
        {
            Assert.AreEqual(Maybe.Some(true), "true".TryParseBoolean());
            Assert.AreEqual(Maybe.Some(true), " TRUE ".TryParseBoolean());
            Assert.AreEqual(Maybe.Some(false), "False".TryParseBoolean());
            Assert.AreEqual(Maybe<bool>.None, "1".TryParseBoolean());
            Assert.AreEqual(Maybe<bool>.None, ((string)null).TryParseBoolean());
        }

        [Test]
        public void TestTryParseChar()
        {
            Assert.AreEqual(Maybe.Some('a'), "a".TryParseChar());
            Assert.AreEqual(Maybe<char>.None, "ab".TryParseChar());
            Assert.AreEqual(Maybe<char>.None, string.Empty.TryParseChar());
        }

        [Test]
        public void TestTryParseGuid()
        {
            Guid g = Guid.NewGuid();

            Assert.AreEqual(Maybe.Some(g), g.ToString("D").TryParseGuid());
            Assert.AreEqual(Maybe.Some(g), g.ToString("N").TryParseGuid());
            Assert.AreEqual(Maybe<Guid>.None, "not-a-guid".TryParseGuid());
        }

        [Test]
        public void TestTryParseGuidExact()
        {
            Guid g = Guid.NewGuid();

            Assert.AreEqual(Maybe.Some(g), g.ToString("N").TryParseGuidExact("N"));
            Assert.AreEqual(Maybe<Guid>.None, g.ToString("D").TryParseGuidExact("N"));
        }

        [Test]
        public void TestTryParseDateTime()
        {
            Assert.AreEqual(
                Maybe.Some(new DateTime(2026, 3, 4)),
                "2026-03-04".TryParseDateTime(CultureInfo.InvariantCulture, DateTimeStyles.None));
            Assert.AreEqual(
                Maybe<DateTime>.None,
                "not a date".TryParseDateTime(CultureInfo.InvariantCulture, DateTimeStyles.None));
        }

        [Test]
        public void TestTryParseDateTimeExact()
        {
            Assert.AreEqual(
                Maybe.Some(new DateTime(2026, 3, 4)),
                "04/03/2026".TryParseDateTimeExact("dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None));
            Assert.AreEqual(
                Maybe<DateTime>.None,
                "2026-03-04".TryParseDateTimeExact("dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None));
        }

        [Test]
        public void TestTryParseDateTimeOffset()
        {
            Assert.AreEqual(
                Maybe.Some(new DateTimeOffset(2026, 3, 4, 0, 0, 0, TimeSpan.Zero)),
                "2026-03-04T00:00:00+00:00".TryParseDateTimeOffset(
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None));
            Assert.AreEqual(
                Maybe<DateTimeOffset>.None,
                "x".TryParseDateTimeOffset(CultureInfo.InvariantCulture, DateTimeStyles.None));
        }

        [Test]
        public void TestTryParseTimeSpan()
        {
            Assert.AreEqual(
                Maybe.Some(TimeSpan.FromMinutes(90)),
                "01:30:00".TryParseTimeSpan(CultureInfo.InvariantCulture));
            Assert.AreEqual(Maybe<TimeSpan>.None, "x".TryParseTimeSpan(CultureInfo.InvariantCulture));
        }

        [Test]
        public void TestTryParseUri()
        {
            Assert.AreEqual(
                Maybe.Some(new Uri("https://example.com/a")),
                "https://example.com/a".TryParseUri());
            Assert.AreEqual(Maybe<Uri>.None, "/a/b".TryParseUri());
            Assert.AreEqual(Maybe.Some(new Uri("/a/b", UriKind.Relative)), "/a/b".TryParseUri(UriKind.Relative));
        }

        [Test]
        public void TestTryParseEnum()
        {
            Assert.AreEqual(Maybe.Some(Color.Green), "Green".TryParseEnum<Color>());
            Assert.AreEqual(Maybe<Color>.None, "green".TryParseEnum<Color>());
            Assert.AreEqual(Maybe.Some(Color.Green), "green".TryParseEnum<Color>(true));
            Assert.AreEqual(Maybe<Color>.None, "Mauve".TryParseEnum<Color>());
            Assert.AreEqual(Maybe<Color>.None, ((string)null).TryParseEnum<Color>());
        }

        [Test]
        public void TestTryParseEnumAcceptsUndeclaredNumbers()
        {
            Assert.AreEqual(Maybe.Some((Color)37), "37".TryParseEnum<Color>());
        }

        [Test]
        public void TestTryParseDefinedEnum()
        {
            Assert.AreEqual(Maybe.Some(Color.Green), "Green".TryParseDefinedEnum<Color>());
            Assert.AreEqual(Maybe.Some(Color.Green), "1".TryParseDefinedEnum<Color>());
            Assert.AreEqual(Maybe<Color>.None, "37".TryParseDefinedEnum<Color>());
            Assert.AreEqual(Maybe<Color>.None, "green".TryParseDefinedEnum<Color>());
            Assert.AreEqual(Maybe.Some(Color.Green), "green".TryParseDefinedEnum<Color>(true));
        }
    }
}
