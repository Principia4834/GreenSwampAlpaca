using System.Globalization;

namespace GreenSwamp.Alpaca.Server.Helpers
{
    /// <summary>Result codes for <see cref="CoordinateParser"/>; the UI maps them to localized messages.</summary>
    public enum CoordinateParseError
    {
        None,
        Required,
        Invalid,
        NotInteger,
        OutOfRange,
        MinutesNotInteger,
        MinutesRange,
        SecondsRange
    }

    /// <summary>
    /// Culture-invariant parsing and canonical formatting of user-typed RA/Dec/Alt/Az values.
    /// "." is always the decimal separator; a single "," is tolerated as a decimal separator on input
    /// and is normalised to "." so that results never depend on the circuit or browser culture.
    /// </summary>
    public static class CoordinateParser
    {
        private const NumberStyles DecimalStyle = NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign;
        private const NumberStyles IntegerStyle = NumberStyles.AllowLeadingSign;
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        /// <summary>Replaces a single "," with "." when the text contains no "."; otherwise returns the text unchanged.</summary>
        public static string NormalizeDecimalSeparator(string input)
        {
            if (input.Contains('.')) return input;
            var first = input.IndexOf(',');
            if (first < 0 || first != input.LastIndexOf(',')) return input;
            return input.Replace(',', '.');
        }

        public static bool TryParseInteger(string token, out int value)
        {
            return int.TryParse(token, IntegerStyle, Inv, out value);
        }

        public static bool TryParseDecimal(string token, out double value)
        {
            if (double.TryParse(NormalizeDecimalSeparator(token), DecimalStyle, Inv, out value) && double.IsFinite(value))
            {
                return true;
            }

            value = 0;
            return false;
        }

        public static string[] Tokenize(string input)
        {
            var normalized = input.Trim()
                .Replace("º", "°")
                .Replace('’', '′')
                .Replace('\'', '′')
                .Replace('"', '″')
                .Replace(':', ' ')
                .Replace("°", " ")
                .Replace("h", " ", StringComparison.OrdinalIgnoreCase)
                .Replace("m", " ", StringComparison.OrdinalIgnoreCase)
                .Replace("s", " ", StringComparison.OrdinalIgnoreCase)
                .Replace("′", " ")
                .Replace("″", " ");

            return normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        }

        /// <summary>
        /// Handles plain decimal input such as "23.75" or "-12,5". Returns true when the input is in decimal mode.
        /// <paramref name="canonical"/> is empty when the value is out of range.
        /// </summary>
        public static bool TryParseDecimalMode(string input, bool isRa, out double value, out string canonical)
        {
            value = 0;
            canonical = string.Empty;

            var text = NormalizeDecimalSeparator(input).Trim();
            if (!text.Contains('.'))
            {
                return false;
            }

            var negative = false;
            if (!isRa && (text.StartsWith('+') || text.StartsWith('-')))
            {
                negative = text[0] == '-';
                text = text[1..].TrimStart();
                if (text.StartsWith('+') || text.StartsWith('-'))
                {
                    return false;
                }
            }

            if (!double.TryParse(text, DecimalStyle, Inv, out value) || !double.IsFinite(value))
            {
                value = 0;
                return false;
            }

            if (isRa)
            {
                if (value < 0 || value >= 24)
                {
                    return true;
                }

                canonical = value.ToString("0.##", Inv);
                return true;
            }

            if (negative) value = -value;
            if (value < -90 || value > 90)
            {
                return true;
            }

            canonical = value.ToString("+0.##;-0.##;0.##", Inv);
            return true;
        }

        /// <summary>Validates input while it is being typed; empty and incomplete input is not an error.</summary>
        public static CoordinateParseError ValidatePartial(string input, bool isRa)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                return CoordinateParseError.None;
            }

            input = NormalizeDecimalSeparator(input);
            var trimmed = input.Trim();
            if (!isRa && (trimmed == "+" || trimmed == "-"))
            {
                return CoordinateParseError.None;
            }

            if (TryParseDecimalMode(trimmed, isRa, out _, out var decimalCanonical))
            {
                return string.IsNullOrEmpty(decimalCanonical) ? CoordinateParseError.OutOfRange : CoordinateParseError.None;
            }

            var tokens = Tokenize(input);
            if (tokens.Length == 0)
            {
                return CoordinateParseError.None;
            }

            if (!TryParseInteger(tokens[0], out var major))
            {
                return CoordinateParseError.NotInteger;
            }

            var sign = !isRa && trimmed.StartsWith('-') ? -1 : 1;
            var signedMajor = isRa ? major : Math.Abs(major) * sign;
            if (isRa)
            {
                if (signedMajor < 0 || signedMajor >= 24) return CoordinateParseError.OutOfRange;
            }
            else
            {
                if (signedMajor < -90 || signedMajor > 90) return CoordinateParseError.OutOfRange;
            }

            if (tokens.Length > 1 && (!TryParseInteger(tokens[1], out var minutes) || minutes < 0 || minutes >= 60))
            {
                return CoordinateParseError.MinutesNotInteger;
            }

            if (tokens.Length > 2 && (!TryParseDecimal(tokens[2], out var seconds) || seconds < 0 || seconds >= 60))
            {
                return CoordinateParseError.SecondsRange;
            }

            return CoordinateParseError.None;
        }

        /// <summary>Rewrites user input into the canonical display form (always "." decimals).</summary>
        public static string Canonicalize(string input, bool isRa, bool canonicalizeSign)
        {
            input = NormalizeDecimalSeparator(input);
            var trimmed = input.Trim();
            if (!isRa && (trimmed == "+" || trimmed == "-"))
            {
                return trimmed;
            }

            if (TryParseDecimalMode(trimmed, isRa, out _, out var decimalCanonical) && !string.IsNullOrEmpty(decimalCanonical))
            {
                return decimalCanonical;
            }

            var tokens = Tokenize(input);
            if (tokens.Length == 0)
            {
                return string.Empty;
            }

            if (!TryParseInteger(tokens[0], out var major))
            {
                return trimmed;
            }

            var minutes = 0;
            var seconds = 0.0;
            if (tokens.Length > 1)
            {
                _ = TryParseInteger(tokens[1], out minutes);
            }
            if (tokens.Length > 2)
            {
                _ = TryParseDecimal(tokens[2], out seconds);
            }

            if (isRa)
            {
                return tokens.Length > 2
                    ? FormattableString.Invariant($"{major:00}h {minutes:00}m {FormatSeconds(seconds)}s")
                    : tokens.Length > 1
                        ? FormattableString.Invariant($"{major:00}h {minutes:00}m")
                        : FormattableString.Invariant($"{major:00}h");
            }

            var sign = string.Empty;
            if (canonicalizeSign)
            {
                if (input.TrimStart().StartsWith('-')) sign = "-";
                else if (input.TrimStart().StartsWith('+')) sign = "+";
            }

            var signedMajor = sign == "-" ? -Math.Abs(major) : Math.Abs(major);
            return tokens.Length > 2
                ? FormattableString.Invariant($"{signedMajor:+00;-00;00}° {minutes:00}′ {FormatSeconds(seconds)}″")
                : tokens.Length > 1
                    ? FormattableString.Invariant($"{signedMajor:+00;-00;00}° {minutes:00}′")
                    : FormattableString.Invariant($"{signedMajor:+00;-00;00}°");
        }

        /// <summary>Parses user input to decimal hours (RA) or degrees.</summary>
        public static bool TryParse(string input, bool isRa, out double value, out CoordinateParseError error)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(input))
            {
                error = CoordinateParseError.Required;
                return false;
            }

            input = NormalizeDecimalSeparator(input);
            var trimmed = input.Trim();
            if (TryParseDecimalMode(trimmed, isRa, out var decimalValue, out var decimalCanonical))
            {
                if (string.IsNullOrEmpty(decimalCanonical))
                {
                    error = CoordinateParseError.OutOfRange;
                    return false;
                }

                value = decimalValue;
                error = CoordinateParseError.None;
                return true;
            }

            var parts = Tokenize(input);
            if (parts.Length == 0 || parts.Length > 3)
            {
                error = CoordinateParseError.Invalid;
                return false;
            }

            var negative = !isRa && parts[0].StartsWith('-');
            var b = 0;
            var c = 0.0;
            if (!TryParseInteger(parts[0], out var a)
                || (parts.Length > 1 && !TryParseInteger(parts[1], out b))
                || (parts.Length > 2 && !TryParseDecimal(parts[2], out c)))
            {
                error = CoordinateParseError.Invalid;
                return false;
            }

            if (isRa && a < 0)
            {
                error = CoordinateParseError.OutOfRange;
                return false;
            }

            if (parts.Length > 1 && (b < 0 || b >= 60))
            {
                error = CoordinateParseError.MinutesRange;
                return false;
            }

            if (parts.Length > 2 && (c < 0 || c >= 60))
            {
                error = CoordinateParseError.SecondsRange;
                return false;
            }

            value = Math.Abs(a) + b / 60.0 + c / 3600.0;
            if (negative) value = -value;

            if (isRa ? (value < 0 || value >= 24) : (value < -90 || value > 90))
            {
                value = 0;
                error = CoordinateParseError.OutOfRange;
                return false;
            }

            error = CoordinateParseError.None;
            return true;
        }

        /// <summary>Formats seconds as "00.##" without ever producing "60".</summary>
        public static string FormatSeconds(double seconds)
        {
            var rounded = Math.Round(seconds, 2, MidpointRounding.AwayFromZero);
            if (seconds < 60 && rounded >= 60) rounded = 59.99;
            return rounded.ToString("00.##", Inv);
        }

        /// <summary>Formats decimal hours as "HHh MMm SS.##s".</summary>
        public static string FormatRa(double hours)
        {
            if (!double.IsFinite(hours)) hours = 0;
            CoordinateFormatter.Split(Math.Max(0.0, hours), 24L * 360000 - 1, out var h, out var m, out var s);
            return FormattableString.Invariant($"{h:00}h {m:00}m {FormatSeconds(s)}s");
        }

        /// <summary>Formats decimal degrees as "±DD° MM′ SS.##″".</summary>
        public static string FormatAngle(double degrees)
        {
            if (!double.IsFinite(degrees)) degrees = 0;
            var sign = degrees < 0 ? "-" : "+";
            CoordinateFormatter.Split(Math.Abs(degrees), long.MaxValue, out var d, out var m, out var s);
            return FormattableString.Invariant($"{sign}{d:00}° {m:00}′ {FormatSeconds(s)}″");
        }
    }
}
