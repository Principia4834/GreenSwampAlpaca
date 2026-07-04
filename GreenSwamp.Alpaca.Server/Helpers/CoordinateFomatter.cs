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
            var h = (int)hours;
            var m = (int)((hours - h) * 60);
            var s = ((hours - h) * 60 - m) * 60;
            return $"{sign}{h:00}h {m:00}m {s:00.00}s";
        }

        /// <summary>Formats a decimal degree value as ±DD° MM′ SS.ss″.</summary>
        public static string FormatDMS(double degrees)
        {
            if (double.IsNaN(degrees) || double.IsInfinity(degrees)) return "N/A";
            var sign = degrees < 0 ? "-" : "+";
            degrees = Math.Abs(degrees);
            var d = (int)degrees;
            var m = (int)((degrees - d) * 60);
            var s = ((degrees - d) * 60 - m) * 60;
            return $"{sign}{d:00}\u00b0 {m:00}\u2032 {s:00.00}\u2033";
        }
    }
}