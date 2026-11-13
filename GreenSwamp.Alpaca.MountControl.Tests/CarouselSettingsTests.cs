using System.Text.Json;
using GreenSwamp.Alpaca.Settings.Models;
using GreenSwamp.Alpaca.Settings.Services;
using Microsoft.Extensions.Configuration;

namespace GreenSwamp.Alpaca.MountControl.Tests;

public class CarouselSettingsTests
{
    [Theory]
    [InlineData(0, 2)]
    [InlineData(-5, 2)]
    [InlineData(5, 5)]
    [InlineData(999, 60)]
    public void Sanitize_ClampsDwell(int input, int expected)
    {
        var s = CarouselSettings.CreateDefault();
        s.DwellSeconds = input;
        Assert.Equal(expected, s.Sanitize().DwellSeconds);
    }

    [Theory]
    [InlineData("a.jpg", true)]
    [InlineData("my image.jpg", true)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("..", false)]
    [InlineData("../a.jpg", false)]
    [InlineData("sub\\a.jpg", false)]
    [InlineData("sub/a.jpg", false)]
    [InlineData("C:a.jpg", false)]
    public void IsPlainName_RejectsPathsAndTraversal(string? name, bool expected)
        => Assert.Equal(expected, CarouselSettings.IsPlainName(name));

    [Fact]
    public void Sanitize_RemovesInvalidSlidesAndShows_AndFixesActiveShow()
    {
        var s = new CarouselSettings
        {
            ActiveShow = "missing",
            Shows =
            [
                new CarouselShow { Id = "bad", Folder = "..\\x" },
                new CarouselShow
                {
                    Id = "good", Folder = "good",
                    Slides = [new CarouselSlide { File = "ok.jpg", Caption = "ok" },
                              new CarouselSlide { File = "..\\evil.jpg", Caption = "no" }]
                }
            ]
        };
        var messages = new List<string>();

        s.Sanitize(messages.Add);

        Assert.Single(s.Shows);
        Assert.Equal("good", s.ActiveShow);
        Assert.Single(s.Shows[0].Slides);
        Assert.Equal(2, messages.Count);
    }

    [Fact]
    public void Default_HasTwoShowsAndFiveSecondDwell()
    {
        var s = CarouselSettings.CreateDefault();
        Assert.Equal(5, s.DwellSeconds);
        Assert.Equal(["reference", "astronomy"], s.Shows.Select(x => x.Id).ToArray());
    }
}

// Serialised: the service resolves its folder from a process-wide environment variable.
[Collection("SettingsPathEnv")]
public class CarouselSettingsServiceTests : IDisposable
{
    private const string EnvVar = "GREENSWAMP_SETTINGS_PATH";
    private readonly string? _oldEnv = Environment.GetEnvironmentVariable(EnvVar);
    private readonly string _root = Path.Combine(Path.GetTempPath(), "gs-carousel-" + Guid.NewGuid().ToString("N"));

    public CarouselSettingsServiceTests()
    {
        Directory.CreateDirectory(_root);
        Environment.SetEnvironmentVariable(EnvVar, _root);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(EnvVar, _oldEnv);
        try { Directory.Delete(_root, true); } catch { /* best effort */ }
    }

    private static VersionedSettingsService Create()
        => new(new ConfigurationBuilder().Build());

    [Fact]
    public void FirstRun_SeedsDefaultFile()
    {
        var svc = Create();
        Assert.True(File.Exists(svc.CarouselSettingsPath));
        Assert.Equal(2, svc.GetCarouselSettings().Shows.Count);
    }

    [Fact]
    public async Task Save_Then_Get_RoundTrips_AndRaisesEvent()
    {
        var svc = Create();
        CarouselSettings? raised = null;
        svc.CarouselSettingsChanged += (_, s) => raised = s;

        var s = svc.GetCarouselSettings();
        s.DwellSeconds = 9;
        s.ActiveShow = "astronomy";
        s.Shows[1].Slides.Add(new CarouselSlide { File = "m31.jpg", Caption = "Andromeda" });
        await svc.SaveCarouselSettingsAsync(s);

        var back = svc.GetCarouselSettings();
        Assert.NotNull(raised);
        Assert.Equal(9, back.DwellSeconds);
        Assert.Equal("astronomy", back.ActiveShow);
        Assert.Equal("Andromeda", back.Shows[1].Slides[0].Caption);
    }

    [Fact]
    public void CorruptFile_FallsBackToDefaults()
    {
        var svc = Create();
        File.WriteAllText(svc.CarouselSettingsPath, "{ not json");
        Assert.Equal(2, svc.GetCarouselSettings().Shows.Count);
    }

    [Fact]
    public void EditedFile_IsSanitisedOnRead()
    {
        var svc = Create();
        File.WriteAllText(svc.CarouselSettingsPath, JsonSerializer.Serialize(new
        {
            dwellSeconds = 1000,
            activeShow = "reference",
            shows = new[] { new { id = "reference", title = "R", folder = "reference",
                slides = new[] { new { file = "../x.jpg", caption = "x" }, new { file = "a.jpg", caption = "a" } } } }
        }));

        var s = svc.GetCarouselSettings();

        Assert.Equal(60, s.DwellSeconds);
        Assert.Single(s.Shows[0].Slides);
    }
}
