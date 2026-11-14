namespace GreenSwamp.Alpaca.Server.Helpers
{
    /// <summary>
    /// Shared coordinate display formatting helpers used by UI components.
    /// </summary>
    public static class CoordinateFormatter
    {
        /// <summary>Formats a decimal hour value as ±HHh MMm SS.ssS.</summary>
        public static string FormatHMS(double hours)
        {
            if (double.IsNaN(hours) || double.IsInfinity(hours)) return "N/A";
            var sign = hours < 0 ? "-" : "+";
            hours = Math.Abs(hours);
            Split(hours, long.MaxValue, out var h, out var m, out var s);
            return FormattableString.Invariant($"{sign}{h:00}h {m:00}m {s:00.00}s");
        }

        /// <summary>Formats a decimal degree value as ±DD° MM′ SS.ss″.</summary>
        public static string FormatDMS(double degrees)
        {
            if (double.IsNaN(degrees) || double.IsInfinity(degrees)) return "N/A";
            var sign = degrees < 0 ? "-" : "+";
            degrees = Math.Abs(degrees);
            Split(degrees, long.MaxValue, out var d, out var m, out var s);
            return FormattableString.Invariant($"{sign}{d:00}\u00b0 {m:00}\u2032 {s:00.00}\u2033");
        }

        /// <summary>
        /// Splits a non-negative value into whole units, minutes and seconds, rounding to 0.01 s first
        /// so that seconds never display as 60.
        /// </summary>
        internal static void Split(double value, long maxCentiseconds, out int whole, out int minutes, out double seconds)
        {
            var cs = (long)Math.Min(Math.Round(value * 360000.0, MidpointRounding.AwayFromZero), maxCentiseconds);
            whole = (int)(cs / 360000);
            minutes = (int)(cs % 360000 / 6000);
            seconds = cs % 6000 / 100.0;
        }
    }
}