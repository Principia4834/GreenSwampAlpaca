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

    private string _aboutText = string.Empty;
    private string _licenseText = string.Empty;
    private string _creditsText = string.Empty;

    protected override async Task OnInitializedAsync()
    {
        _aboutText = await ReadEmbeddedResourceAsync("About.txt");
        _licenseText = await ReadEmbeddedResourceAsync("License.txt");
        _creditsText = await ReadEmbeddedResourceAsync("Credits.txt");
    }

    private void Close() => MudDialog.Close();

    private async Task<string> ReadEmbeddedResourceAsync(string resourceFileName)
    {
        var assembly = Assembly.GetExecutingAssembly();

        var resourceName = assembly
            .GetManifestResourceNames()
            .FirstOrDefault(name => name.EndsWith(resourceFileName, StringComparison.OrdinalIgnoreCase));

        if (resourceName is null)
        {
            return Loc["About_ContentUnavailable"];
        }

        await using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            return Loc["About_ContentUnavailable"];
        }

        using var reader = new StreamReader(stream, leaveOpen: false);
        return await reader.ReadToEndAsync();
    }
}
