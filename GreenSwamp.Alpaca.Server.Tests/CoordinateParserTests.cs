using GreenSwamp.Alpaca.Server.Helpers;

namespace GreenSwamp.Alpaca.Server.Tests
{
    [Collection(CultureCollection.Name)]
    public class CoordinateParserTests
    {
        private const string Cultures = "en-US,de-DE,fr-FR,zh-CN,en-GB";

        public static TheoryData<string> CultureNames => new(Cultures.Split(','));

        [Theory]
        [MemberData(nameof(CultureNames))]
        public void Parse_AcceptsDotAndCommaDecimals_InAnyRegionalFormat(string culture)
        {
            using var scope = new CultureScope(culture);

            Assert.True(CoordinateParser.TryParse("23.75", true, out var dot, out _));
            Assert.True(CoordinateParser.TryParse("23,75", true, out var comma, out _));
            Assert.Equal(23.75, dot);
            Assert.Equal(23.75, comma);
        }

        [Theory]
        [MemberData(nameof(CultureNames))]
        public void Parse_SingleDigitDecimal_IsNotTreatedAsThousands(string culture)
        {
            using var scope = new CultureScope(culture);

            Assert.True(CoordinateParser.TryParse("1.5", true, out var dot, out _));
            Assert.True(CoordinateParser.TryParse("1,5", true, out var comma, out _));
            Assert.Equal(1.5, dot);
            Assert.Equal(1.5, comma);
        }

        [Theory]
        [InlineData("-12,5", false, -12.5)]
        [InlineData("+45.5", false, 45.5)]
        [InlineData("-90", false, -90.0)]
        [InlineData("12 30", true, 12.5)]
        [InlineData("-12 30", false, -12.5)]
        [InlineData("12h 30m 36s", true, 12.51)]
        [InlineData("12° 30′ 36″", false, 12.51)]
        [InlineData("12 30 36,0", true, 12.51)]
        public void Parse_ValidInput_ReturnsDecimalValue(string input, bool isRa, double expected)
        {
            using var scope = new CultureScope("de-DE");

            Assert.True(CoordinateParser.TryParse(input, isRa, out var value, out var error));
            Assert.Equal(CoordinateParseError.None, error);
            Assert.Equal(expected, value, 9);
        }

        [Theory]
        [InlineData("", true, CoordinateParseError.Required)]
        [InlineData("   ", false, CoordinateParseError.Required)]
        [InlineData("24", true, CoordinateParseError.OutOfRange)]
        [InlineData("24.0", true, CoordinateParseError.OutOfRange)]
        [InlineData("91", false, CoordinateParseError.OutOfRange)]
        [InlineData("-90.5", false, CoordinateParseError.OutOfRange)]
        [InlineData("10 61", false, CoordinateParseError.MinutesRange)]
        [InlineData("10 30 60", false, CoordinateParseError.SecondsRange)]
        [InlineData("1,234.5", true, CoordinateParseError.Invalid)]
        [InlineData("1,2,3", true, CoordinateParseError.Invalid)]
        [InlineData("abc", true, CoordinateParseError.Invalid)]
        public void Parse_InvalidInput_ReturnsExpectedError(string input, bool isRa, CoordinateParseError expected)
        {
            using var scope = new CultureScope("fr-FR");

            Assert.False(CoordinateParser.TryParse(input, isRa, out _, out var error));
            Assert.Equal(expected, error);
        }

        [Theory]
        [InlineData("12,5", false, "+12.5")]
        [InlineData("1,50", true, "1.5")]
        [InlineData("5", true, "05h")]
        [InlineData("12 30", true, "12h 30m")]
        [InlineData("12 30 5,5", true, "12h 30m 05.5s")]
        public void Canonicalize_AlwaysUsesDotDecimals(string input, bool isRa, string expected)
        {
            foreach (var culture in Cultures.Split(','))
            {
                using var scope = new CultureScope(culture);
                Assert.Equal(expected, CoordinateParser.Canonicalize(input, isRa, true));
            }
        }

        [Theory]
        [InlineData("", true, CoordinateParseError.None)]
        [InlineData("-", false, CoordinateParseError.None)]
        [InlineData("25", true, CoordinateParseError.OutOfRange)]
        [InlineData("abc", true, CoordinateParseError.NotInteger)]
        [InlineData("12 75", true, CoordinateParseError.MinutesNotInteger)]
        [InlineData("12 30 61", true, CoordinateParseError.SecondsRange)]
        [InlineData("12,5", true, CoordinateParseError.None)]
        public void ValidatePartial_ReportsFirstProblem(string input, bool isRa, CoordinateParseError expected)
        {
            using var scope = new CultureScope("de-DE");

            Assert.Equal(expected, CoordinateParser.ValidatePartial(input, isRa));
        }

        [Theory]
        [InlineData("1,5", "1.5")]
        [InlineData("1.5", "1.5")]
        [InlineData("1,234.5", "1,234.5")]
        [InlineData("1,2,3", "1,2,3")]
        public void NormalizeDecimalSeparator_OnlyRewritesASingleComma(string input, string expected)
        {
            Assert.Equal(expected, CoordinateParser.NormalizeDecimalSeparator(input));
        }
    }
}
