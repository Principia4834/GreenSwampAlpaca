using System.Reflection;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace GreenSwamp.Alpaca.Server.Components.About;

/// <summary>
/// Modal dialog showing project information, license text and credits read from embedded resources.
/// </summary>
public partial class AboutDialog
{
    [CascadingParameter]
    private IMudDialogInstance MudDialog { get; set; } = default!;

    private const string MissingResourceText = "Content is not available.";

    private string _aboutText = MissingResourceText;
    private string _licenseText = MissingResourceText;
    private string _creditsText = MissingResourceText;

    protected override async Task OnInitializedAsync()
    {
        _aboutText = await ReadEmbeddedResourceAsync("About.txt");
        _licenseText = await ReadEmbeddedResourceAsync("License.txt");
        _creditsText = await ReadEmbeddedResourceAsync("Credits.txt");
    }

    private void Close() => MudDialog.Close();

    private static async Task<string> ReadEmbeddedResourceAsync(string resourceFileName)
    {
        var assembly = Assembly.GetExecutingAssembly();

        var resourceName = assembly
            .GetManifestResourceNames()
            .FirstOrDefault(name => name.EndsWith(resourceFileName, StringComparison.OrdinalIgnoreCase));

        if (resourceName is null)
        {
            return MissingResourceText;
        }

        await using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            return MissingResourceText;
        }

        using var reader = new StreamReader(stream, leaveOpen: false);
        return await reader.ReadToEndAsync();
    }
}
