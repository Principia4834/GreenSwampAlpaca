using GreenSwamp.Alpaca.Server.Helpers;
using MudBlazor.Utilities.Exceptions;

namespace GreenSwamp.Alpaca.Server.Tests
{
    [Collection(CultureCollection.Name)]
    public class NumericFieldConverterTests
    {
        [Fact]
        public void Localized_DisplaysWithRegionalSeparator()
        {
            using (new CultureScope("de-DE"))
            {
                Assert.Equal("1234,50", NumericFieldConverter.Localized("F2").Convert(1234.5));
            }

            using (new CultureScope("en-US"))
            {
                Assert.Equal("1234.50", NumericFieldConverter.Localized("F2").Convert(1234.5));
            }
        }

        [Theory]
        [InlineData("de-DE")]
        [InlineData("fr-FR")]
        [InlineData("zh-CN")]
        public void Invariant_AlwaysDisplaysDot(string culture)
        {
            using var _ = new CultureScope(culture);

            Assert.Equal("51.500000", NumericFieldConverter.Invariant("F6").Convert(51.5));
            Assert.Equal("-0.1", NumericFieldConverter.Invariant("F1").Convert(-0.1));
        }

        [Theory]
        [InlineData("de-DE", false)]
        [InlineData("de-DE", true)]
        [InlineData("en-US", false)]
        [InlineData("en-US", true)]
        public void ConvertBack_AcceptsBothSeparators_InAnyRegionalFormat(string culture, bool invariantDisplay)
        {
            using var _ = new CultureScope(culture);
            var converter = invariantDisplay ? NumericFieldConverter.Invariant("F2") : NumericFieldConverter.Localized("F2");

            Assert.Equal(1.5, converter.ConvertBack("1,5"));
            Assert.Equal(1.5, converter.ConvertBack("1.5"));
            Assert.Equal(-12.25, converter.ConvertBack("-12,25"));
            Assert.Equal(2.5, converter.ConvertBack(" 2,5 "));
            Assert.Equal(1500.0, converter.ConvertBack("1500"));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void ConvertBack_EmptyInput_IsZero(string? input)
        {
            Assert.Equal(0.0, NumericFieldConverter.Localized().ConvertBack(input));
        }

        [Theory]
        [InlineData("1,234.5")]
        [InlineData("1.234,5")]
        [InlineData("1.2.3")]
        [InlineData("1,2,3")]
        [InlineData("abc")]
        [InlineData("NaN")]
        [InlineData("Infinity")]
        [InlineData("1e3")]
        public void ConvertBack_AmbiguousOrInvalidInput_IsRejected(string input)
        {
            using var _ = new CultureScope("de-DE");

            var ex = Assert.Throws<ConversionException>(() => NumericFieldConverter.Localized().ConvertBack(input));
            Assert.Equal("Converter_InvalidNumber", ex.ErrorMessageKey);
        }

        [Fact]
        public void Instances_AreCachedPerKindAndFormat()
        {
            Assert.Same(NumericFieldConverter.Localized("F2"), NumericFieldConverter.Localized("F2"));
            Assert.NotSame(NumericFieldConverter.Localized("F2"), NumericFieldConverter.Invariant("F2"));
            Assert.NotSame(NumericFieldConverter.Localized("F2"), NumericFieldConverter.Localized("F3"));
        }
    }
}
