using System.Globalization;

namespace GreenSwamp.Alpaca.Server.Localization
{
    /// <summary>
    /// The culture of one browser connection (Blazor circuit), captured when the circuit starts.
    /// Mount and device state is shared by every connection, so an event raised on a server thread
    /// can render another circuit's components on that thread's culture. Components re-apply this
    /// culture around renders and <c>InvokeAsync</c> work (see <see cref="LocalizedComponentBase"/>).
    /// </summary>
    public sealed class CircuitCulture
    {
        /// <summary>Regional format (numbers, dates) for this circuit.</summary>
        public CultureInfo Culture { get; } = CultureInfo.CurrentCulture;

        /// <summary>Display language (resource lookup) for this circuit.</summary>
        public CultureInfo UICulture { get; } = CultureInfo.CurrentUICulture;

        /// <summary>Makes this circuit's cultures current for the rest of the calling flow.</summary>
        public void Apply()
        {
            if (!Equals(CultureInfo.CurrentCulture, Culture)) CultureInfo.CurrentCulture = Culture;
            if (!Equals(CultureInfo.CurrentUICulture, UICulture)) CultureInfo.CurrentUICulture = UICulture;
        }

        /// <summary>
        /// Applies this circuit's cultures and returns a scope that restores the caller's cultures on dispose,
        /// so server threads that raise events are not left running on a browser's culture.
        /// </summary>
        public Scope Enter()
        {
            var scope = new Scope(CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture);
            Apply();
            return scope;
        }

        public readonly struct Scope : IDisposable
        {
            private readonly CultureInfo _previousCulture;
            private readonly CultureInfo _previousUICulture;

            internal Scope(CultureInfo previousCulture, CultureInfo previousUICulture)
            {
                _previousCulture = previousCulture;
                _previousUICulture = previousUICulture;
            }

            public void Dispose()
            {
                CultureInfo.CurrentCulture = _previousCulture;
                CultureInfo.CurrentUICulture = _previousUICulture;
            }
        }
    }
}
