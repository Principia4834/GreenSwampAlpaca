using System.Collections.Concurrent;
using System.Globalization;
using MudBlazor;
using MudBlazor.Utilities.Exceptions;

namespace GreenSwamp.Alpaca.Server.Helpers
{
    /// <summary>
    /// Converter for <c>MudNumericField&lt;double&gt;</c> that accepts either "," or "." as the decimal
    /// separator and never treats either as a thousands separator.
    /// MudBlazor's default converter parses with <see cref="NumberStyles.Any"/>, which would read "1,5" as 15.
    /// Instances are stateless and cached, so they are safe to share between components.
    /// </summary>
    public sealed class NumericFieldConverter : IReversibleConverter<double, string?>
    {
        private static readonly ConcurrentDictionary<(bool Invariant, string? Format), NumericFieldConverter> Cache = new();

        private readonly bool _invariantDisplay;
        private readonly string? _format;

        private NumericFieldConverter(bool invariantDisplay, string? format)
        {
            _invariantDisplay = invariantDisplay;
            _format = format;
        }

        /// <summary>Displays using the regional decimal separator (configuration values).</summary>
        public static NumericFieldConverter Localized(string? format = null) =>
            Cache.GetOrAdd((false, format), key => new NumericFieldConverter(key.Invariant, key.Format));

        /// <summary>Always displays "." (latitude, longitude, UTC offset, axis angles).</summary>
        public static NumericFieldConverter Invariant(string? format = null) =>
            Cache.GetOrAdd((true, format), key => new NumericFieldConverter(key.Invariant, key.Format));

        public string? Convert(double input)
        {
            var culture = _invariantDisplay ? CultureInfo.InvariantCulture : CultureInfo.CurrentCulture;
            return input.ToString(_format, culture);
        }

        public double ConvertBack(string? input)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                return 0d;
            }

            if (CoordinateParser.TryParseDecimal(input.Trim(), out var value))
            {
                return value;
            }

            throw new ConversionException("Converter_InvalidNumber");
        }
    }
}
