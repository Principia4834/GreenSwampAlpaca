using System.Globalization;

namespace GreenSwamp.Alpaca.Server.Tests
{
    /// <summary>Runs a test on a chosen regional format and restores the previous cultures afterwards.</summary>
    public sealed class CultureScope : IDisposable
    {
        private readonly CultureInfo _previous = CultureInfo.CurrentCulture;
        private readonly CultureInfo _previousUi = CultureInfo.CurrentUICulture;

        public CultureScope(string name)
        {
            var culture = CultureInfo.GetCultureInfo(name);
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
        }

        public void Dispose()
        {
            CultureInfo.CurrentCulture = _previous;
            CultureInfo.CurrentUICulture = _previousUi;
        }
    }

    /// <summary>Culture settings are process wide, so these tests must not run in parallel.</summary>
    [CollectionDefinition(Name, DisableParallelization = true)]
    public sealed class CultureCollection
    {
        public const string Name = "Culture";
    }
}
