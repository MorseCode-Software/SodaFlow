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
            Assert.AreEqual(expected: Maybe.Some(42), actual: "42".TryParseInt32());
            Assert.AreEqual(expected: Maybe.Some(-42), actual: "-42".TryParseInt32());
            Assert.AreEqual(expected: Maybe<int>.None, actual: "x".TryParseInt32());
            Assert.AreEqual(expected: Maybe<int>.None, actual: string.Empty.TryParseInt32());
            Assert.AreEqual(expected: Maybe<int>.None, actual: ((string)null).TryParseInt32());
            Assert.AreEqual(expected: Maybe<int>.None, actual: "2147483648".TryParseInt32());
        }

        [Test]
        public void TestTryParseInt32WithStyles()
        {
            Assert.AreEqual(
                expected: Maybe.Some(1234),
                actual: "1,234".TryParseInt32(
                    styles: NumberStyles.Integer | NumberStyles.AllowThousands,
                    provider: CultureInfo.InvariantCulture));

            Assert.AreEqual(
                expected: Maybe<int>.None,
                actual: "1,234".TryParseInt32(styles: NumberStyles.Integer, provider: CultureInfo.InvariantCulture));
        }

        [Test]
        public void TestTryParseIntegralTypes()
        {
            Assert.AreEqual(expected: Maybe.Some((byte)7), actual: "7".TryParseByte());
            Assert.AreEqual(expected: Maybe<byte>.None, actual: "256".TryParseByte());
            Assert.AreEqual(expected: Maybe.Some((sbyte)-7), actual: "-7".TryParseSByte());
            Assert.AreEqual(expected: Maybe.Some((short)-7), actual: "-7".TryParseInt16());
            Assert.AreEqual(expected: Maybe.Some((ushort)7), actual: "7".TryParseUInt16());
            Assert.AreEqual(expected: Maybe.Some(7u), actual: "7".TryParseUInt32());
            Assert.AreEqual(expected: Maybe.Some(-7L), actual: "-7".TryParseInt64());
            Assert.AreEqual(expected: Maybe.Some(7ul), actual: "7".TryParseUInt64());
            Assert.AreEqual(expected: Maybe<uint>.None, actual: "-7".TryParseUInt32());
        }

        [Test]
        public void TestTryParseRealTypes()
        {
            Assert.AreEqual(
                expected: Maybe.Some(1.5f),
                actual: "1.5".TryParseSingle(styles: NumberStyles.Float, provider: CultureInfo.InvariantCulture));

            Assert.AreEqual(
                expected: Maybe.Some(1.5d),
                actual: "1.5".TryParseDouble(styles: NumberStyles.Float, provider: CultureInfo.InvariantCulture));

            Assert.AreEqual(
                expected: Maybe.Some(1.5m),
                actual: "1.5".TryParseDecimal(styles: NumberStyles.Number, provider: CultureInfo.InvariantCulture));

            Assert.AreEqual(
                expected: Maybe<double>.None,
                actual: "x".TryParseDouble(styles: NumberStyles.Float, provider: CultureInfo.InvariantCulture));
        }

        [Test]
        public void TestTryParseBoolean()
        {
            Assert.AreEqual(expected: Maybe.Some(true), actual: "true".TryParseBoolean());
            Assert.AreEqual(expected: Maybe.Some(true), actual: " TRUE ".TryParseBoolean());
            Assert.AreEqual(expected: Maybe.Some(false), actual: "False".TryParseBoolean());
            Assert.AreEqual(expected: Maybe<bool>.None, actual: "1".TryParseBoolean());
            Assert.AreEqual(expected: Maybe<bool>.None, actual: ((string)null).TryParseBoolean());
        }

        [Test]
        public void TestTryParseChar()
        {
            Assert.AreEqual(expected: Maybe.Some('a'), actual: "a".TryParseChar());
            Assert.AreEqual(expected: Maybe<char>.None, actual: "ab".TryParseChar());
            Assert.AreEqual(expected: Maybe<char>.None, actual: string.Empty.TryParseChar());
        }

        [Test]
        public void TestTryParseGuid()
        {
            Guid g = Guid.NewGuid();

            Assert.AreEqual(expected: Maybe.Some(g), actual: g.ToString("D").TryParseGuid());
            Assert.AreEqual(expected: Maybe.Some(g), actual: g.ToString("N").TryParseGuid());
            Assert.AreEqual(expected: Maybe<Guid>.None, actual: "not-a-guid".TryParseGuid());
        }

        [Test]
        public void TestTryParseGuidExact()
        {
            Guid g = Guid.NewGuid();

            Assert.AreEqual(expected: Maybe.Some(g), actual: g.ToString("N").TryParseGuidExact("N"));
            Assert.AreEqual(expected: Maybe<Guid>.None, actual: g.ToString("D").TryParseGuidExact("N"));
        }

        [Test]
        public void TestTryParseDateTime()
        {
            Assert.AreEqual(
                expected: Maybe.Some(new DateTime(year: 2026, month: 3, day: 4)),
                actual: "2026-03-04".TryParseDateTime(
                    provider: CultureInfo.InvariantCulture,
                    styles: DateTimeStyles.None));

            Assert.AreEqual(
                expected: Maybe<DateTime>.None,
                actual: "not a date".TryParseDateTime(
                    provider: CultureInfo.InvariantCulture,
                    styles: DateTimeStyles.None));
        }

        [Test]
        public void TestTryParseDateTimeExact()
        {
            Assert.AreEqual(
                expected: Maybe.Some(new DateTime(year: 2026, month: 3, day: 4)),
                actual: "04/03/2026".TryParseDateTimeExact(
                    format: "dd/MM/yyyy",
                    provider: CultureInfo.InvariantCulture,
                    styles: DateTimeStyles.None));

            Assert.AreEqual(
                expected: Maybe<DateTime>.None,
                actual: "2026-03-04".TryParseDateTimeExact(
                    format: "dd/MM/yyyy",
                    provider: CultureInfo.InvariantCulture,
                    styles: DateTimeStyles.None));
        }

        [Test]
        public void TestTryParseDateTimeOffset()
        {
            Assert.AreEqual(
                expected: Maybe.Some(
                    new DateTimeOffset(
                        year: 2026,
                        month: 3,
                        day: 4,
                        hour: 0,
                        minute: 0,
                        second: 0,
                        offset: TimeSpan.Zero)),
                actual: "2026-03-04T00:00:00+00:00".TryParseDateTimeOffset(
                    provider: CultureInfo.InvariantCulture,
                    styles: DateTimeStyles.None));

            Assert.AreEqual(
                expected: Maybe<DateTimeOffset>.None,
                actual: "x".TryParseDateTimeOffset(
                    provider: CultureInfo.InvariantCulture,
                    styles: DateTimeStyles.None));
        }

        [Test]
        public void TestTryParseTimeSpan()
        {
            Assert.AreEqual(
                expected: Maybe.Some(TimeSpan.FromMinutes(90)),
                actual: "01:30:00".TryParseTimeSpan(CultureInfo.InvariantCulture));

            Assert.AreEqual(expected: Maybe<TimeSpan>.None, actual: "x".TryParseTimeSpan(CultureInfo.InvariantCulture));
        }

        [Test]
        public void TestTryParseUri()
        {
            Assert.AreEqual(
                expected: Maybe.Some(new Uri("https://example.com/a")),
                actual: "https://example.com/a".TryParseUri());

            Assert.AreEqual(expected: Maybe<Uri>.None, actual: "/a/b".TryParseUri());

            Assert.AreEqual(
                expected: Maybe.Some(new Uri(uriString: "/a/b", uriKind: UriKind.Relative)),
                actual: "/a/b".TryParseUri(UriKind.Relative));
        }

        [Test]
        public void TestTryParseEnum()
        {
            Assert.AreEqual(expected: Maybe.Some(Color.Green), actual: "Green".TryParseEnum<Color>());
            Assert.AreEqual(expected: Maybe<Color>.None, actual: "green".TryParseEnum<Color>());
            Assert.AreEqual(expected: Maybe.Some(Color.Green), actual: "green".TryParseEnum<Color>(true));
            Assert.AreEqual(expected: Maybe<Color>.None, actual: "Mauve".TryParseEnum<Color>());
            Assert.AreEqual(expected: Maybe<Color>.None, actual: ((string)null).TryParseEnum<Color>());
        }

        [Test]
        public void TestTryParseEnumAcceptsUndeclaredNumbers() =>
            Assert.AreEqual(expected: Maybe.Some((Color)37), actual: "37".TryParseEnum<Color>());

        [Test]
        public void TestTryParseDefinedEnum()
        {
            Assert.AreEqual(expected: Maybe.Some(Color.Green), actual: "Green".TryParseDefinedEnum<Color>());
            Assert.AreEqual(expected: Maybe.Some(Color.Green), actual: "1".TryParseDefinedEnum<Color>());
            Assert.AreEqual(expected: Maybe<Color>.None, actual: "37".TryParseDefinedEnum<Color>());
            Assert.AreEqual(expected: Maybe<Color>.None, actual: "green".TryParseDefinedEnum<Color>());
            Assert.AreEqual(expected: Maybe.Some(Color.Green), actual: "green".TryParseDefinedEnum<Color>(true));
        }
    }
}