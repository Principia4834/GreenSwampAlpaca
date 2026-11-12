using System.Globalization;
using GreenSwamp.Alpaca.Settings.Models;

namespace GreenSwamp.Alpaca.Server.Configuration;

public sealed class SupportedCultureSettings
{
    public const string SectionName = "CultureSettings";

    public List<string> SupportedDisplayLanguages { get; set; } = [];

    public List<string> SupportedRegionalFormats { get; set; } = [];

    public void Validate()
    {
        NormalizeAndValidate(SupportedDisplayLanguages, nameof(SupportedDisplayLanguages));
        NormalizeAndValidate(SupportedRegionalFormats, nameof(SupportedRegionalFormats));
    }

    public void ValidateServerDefaults(ServerConfig config)
    {
        if (!IsSupportedDisplayLanguage(config.DefaultDisplayLanguage))
        {
            throw new InvalidOperationException(
                $"ServerConfig.DefaultDisplayLanguage '{config.DefaultDisplayLanguage}' is not in CultureSettings:{nameof(SupportedDisplayLanguages)}.");
        }

        if (!IsSupportedRegionalFormat(config.DefaultRegionalFormat))
        {
            throw new InvalidOperationException(
                $"ServerConfig.DefaultRegionalFormat '{config.DefaultRegionalFormat}' is not in CultureSettings:{nameof(SupportedRegionalFormats)}.");
        }
    }

    public bool IsSupportedDisplayLanguage(string? cultureName) =>
        SupportedDisplayLanguages.Contains(cultureName, StringComparer.OrdinalIgnoreCase);

    public bool IsSupportedRegionalFormat(string? cultureName) =>
        SupportedRegionalFormats.Contains(cultureName, StringComparer.OrdinalIgnoreCase);

    private static void NormalizeAndValidate(List<string> cultureNames, string settingName)
    {
        if (cultureNames.Count == 0)
        {
            throw new InvalidOperationException($"CultureSettings:{settingName} must contain at least one culture.");
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < cultureNames.Count; index++)
        {
            var cultureName = cultureNames[index];
            if (string.IsNullOrWhiteSpace(cultureName))
            {
                throw new InvalidOperationException($"CultureSettings:{settingName} cannot contain an empty culture.");
            }

            var normalizedName = CultureInfo.GetCultureInfo(cultureName).Name;
            if (!seen.Add(normalizedName))
            {
                throw new InvalidOperationException($"CultureSettings:{settingName} contains duplicate culture '{normalizedName}'.");
            }

            cultureNames[index] = normalizedName;
        }
    }
}