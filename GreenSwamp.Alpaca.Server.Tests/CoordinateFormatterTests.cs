using GreenSwamp.Alpaca.Server.Helpers;

namespace GreenSwamp.Alpaca.Server.Tests
{
    [Collection(CultureCollection.Name)]
    public class CoordinateFormatterTests
    {
        public static TheoryData<string> CultureNames => new("en-US", "de-DE", "fr-FR", "zh-CN", "ar-SA", "en-GB");

        [Theory]
        [MemberData(nameof(CultureNames))]
        public void Display_UsesDotDecimalsAndAsciiDigits_InAnyRegionalFormat(string culture)
        {
            using var _ = new CultureScope(culture);

            Assert.Equal("+05h 30m 00.00s", CoordinateFormatter.FormatHMS(5.5));
            Assert.Equal("-12\u00b0 30\u2032 15.50\u2033", CoordinateFormatter.FormatDMS(-(12 + 30 / 60.0 + 15.5 / 3600)));
            Assert.Equal("12h 30m 15.5s", CoordinateParser.FormatRa(12 + 30 / 60.0 + 15.5 / 3600));
            Assert.Equal("-12\u00b0 30\u2032 00\u2033", CoordinateParser.FormatAngle(-12.5));
        }

        [Fact]
        public void Seconds_NeverRoundUpTo60()
        {
            Assert.Equal("+06h 00m 00.00s", CoordinateFormatter.FormatHMS(5.99999999));
            Assert.Equal("+11\u00b0 59\u2032 59.99\u2033", CoordinateFormatter.FormatDMS(11 + 59 / 60.0 + 59.99 / 3600));
            Assert.Equal("59.99", CoordinateParser.FormatSeconds(59.999));
            Assert.Equal("05.26", CoordinateParser.FormatSeconds(5.256));
        }

        [Fact]
        public void FormatRa_NeverReachesTwentyFourHours()
        {
            Assert.Equal("23h 59m 59.99s", CoordinateParser.FormatRa(23.9999999999));
        }

        [Theory]
        [InlineData(double.NaN)]
        [InlineData(double.PositiveInfinity)]
        public void NonFiniteValues_AreHandled(double value)
        {
            Assert.Equal("N/A", CoordinateFormatter.FormatHMS(value));
            Assert.Equal("N/A", CoordinateFormatter.FormatDMS(value));
            Assert.Equal("00h 00m 00s", CoordinateParser.FormatRa(value));
        }
    }
}
