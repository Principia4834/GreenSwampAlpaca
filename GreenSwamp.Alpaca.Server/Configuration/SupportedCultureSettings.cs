using System.Globalization;
using GreenSwamp.Alpaca.Settings.Models;

namespace GreenSwamp.Alpaca.Server.Configuration;

public sealed class SupportedCultureSettings
{
    public const string SectionName = "CultureSettings";

    private static readonly IReadOnlyDictionary<string, string> DisplayLanguageCultures =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["en"] = "en-US",
            ["fr"] = "fr-FR",
            ["it"] = "it-IT",
            ["de"] = "de-DE",
            ["es"] = "es-ES",
            ["zh"] = "zh-CN"
        };

    public List<string> SupportedLanguages { get; set; } = [];

    public List<string> SupportedRegionalFormats { get; set; } = [];

    public void Validate()
    {
        NormalizeAndValidateLanguages();
        NormalizeAndValidate(SupportedRegionalFormats, nameof(SupportedRegionalFormats));
    }

    public void ValidateServerDefaults(ServerConfig config)
    {
        var normalizedDefaultLanguage = GetDisplayLanguageCode(config.DefaultDisplayLanguage);
        if (normalizedDefaultLanguage is null)
        {
            throw new InvalidOperationException(
                $"ServerConfig.DefaultDisplayLanguage '{config.DefaultDisplayLanguage}' is not in CultureSettings:{nameof(SupportedLanguages)}.");
        }
        config.DefaultDisplayLanguage = normalizedDefaultLanguage;

        if (!IsSupportedRegionalFormat(config.DefaultRegionalFormat))
        {
            throw new InvalidOperationException(
                $"ServerConfig.DefaultRegionalFormat '{config.DefaultRegionalFormat}' is not in CultureSettings:{nameof(SupportedRegionalFormats)}.");
        }
    }

    public bool IsSupportedDisplayLanguage(string? cultureName) =>
        !string.IsNullOrWhiteSpace(cultureName) &&
        SupportedLanguages.Contains(cultureName, StringComparer.OrdinalIgnoreCase);

    public string? GetDisplayCulture(string? languageCode) =>
        IsSupportedDisplayLanguage(languageCode) &&
        DisplayLanguageCultures.TryGetValue(languageCode!, out var cultureName)
            ? cultureName
            : null;

    public string? GetDisplayLanguageCode(string? cultureOrLanguage)
    {
        if (string.IsNullOrWhiteSpace(cultureOrLanguage))
        {
            return null;
        }

        if (IsSupportedDisplayLanguage(cultureOrLanguage))
        {
            return SupportedLanguages.First(language =>
                language.Equals(cultureOrLanguage, StringComparison.OrdinalIgnoreCase));
        }

        foreach (var language in SupportedLanguages)
        {
            if (DisplayLanguageCultures.TryGetValue(language, out var cultureName) &&
                cultureName.Equals(cultureOrLanguage, StringComparison.OrdinalIgnoreCase))
            {
                return language;
            }
        }

        var primaryLanguage = cultureOrLanguage.Split('-', 2)[0];
        return IsSupportedDisplayLanguage(primaryLanguage)
            ? SupportedLanguages.First(language =>
                language.Equals(primaryLanguage, StringComparison.OrdinalIgnoreCase))
            : null;
    }

    public List<CultureInfo> GetSupportedDisplayCultures() =>
        SupportedLanguages
            .Select(language => CultureInfo.GetCultureInfo(GetDisplayCulture(language)!))
            .ToList();

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

    private void NormalizeAndValidateLanguages()
    {
        if (SupportedLanguages.Count == 0)
        {
            throw new InvalidOperationException($"CultureSettings:{nameof(SupportedLanguages)} must contain at least one language.");
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < SupportedLanguages.Count; index++)
        {
            var language = SupportedLanguages[index];
            if (string.IsNullOrWhiteSpace(language) || !DisplayLanguageCultures.ContainsKey(language))
            {
                throw new InvalidOperationException(
                    $"CultureSettings:{nameof(SupportedLanguages)} contains unsupported language '{language}'.");
            }

            var normalizedLanguage = language.ToLowerInvariant();
            if (!seen.Add(normalizedLanguage))
            {
                throw new InvalidOperationException(
                    $"CultureSettings:{nameof(SupportedLanguages)} contains duplicate language '{normalizedLanguage}'.");
            }

            SupportedLanguages[index] = normalizedLanguage;
        }
    }
}