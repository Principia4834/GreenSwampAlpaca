using System.Text.RegularExpressions;
using ASCOM.Common.DeviceInterfaces;
using GreenSwamp.Alpaca.Settings.Models;
using CameraState = GreenSwamp.Alpaca.Settings.Models.CameraState;

namespace GreenSwamp.Alpaca.Server.Models;

public sealed class TelescopeViewerSettings
{
    public const string DefaultModelColor = "#52b788";
    public const string DefaultBackground = "NorthAmerica";
    public const int MinAxisAdjustment = -90;
    public const int MaxAxisAdjustment = 90;

    public Model3DType ModelType { get; set; } = Model3DType.Default;
    public CameraState Camera { get; set; } = CreateDefaultCamera();
    public string ModelColor { get; set; } = DefaultModelColor;
    public string Background { get; set; } = DefaultBackground;
    public bool ShowGrid { get; set; } = true;
    public bool ShowCardinals { get; set; }
    public CardinalSize CardinalSize { get; set; } = CardinalSize.Large;
    public bool ShowHeading { get; set; }
    public bool UseAssembly { get; set; }
    public AssemblySupport AssemblySupport { get; set; } = AssemblySupport.MediumTripod;
    public AssemblyMount AssemblyMount { get; set; } = AssemblyMount.Eq6rPro;
    public AssemblyOta AssemblyOta { get; set; } = AssemblyOta.Refractor;
    public int AssemblyOtaSize { get; set; } = 100;
    public int RaAdjustmentDegrees { get; set; }
    public int DecAdjustmentDegrees { get; set; }

    public static IReadOnlyList<(string Name, string Hex)> ColorPresets { get; } =
    [
        ("Green", "#52b788"),
        ("Yellow", "#f2c84b"),
        ("Moon", "#e8e6df"),
        ("Red", "#ef6461"),
        ("Blue", "#5aa9e6"),
        ("Orange", "#f08a4b"),
        ("Slate", "#7d8c92")
    ];

    public static IReadOnlyList<string> BackgroundChoices { get; } =
        ["NorthAmerica", "Heart", "Rosette", "M51", "M33", "Stars", "None"];

    public static IReadOnlyList<int> AssemblyOtaSizes { get; } = [50, 75, 100, 125, 150, 200];

    public static TelescopeViewerSettings CreateDefault(Model3DType modelType = Model3DType.Default) => new()
    {
        ModelType = Enum.IsDefined(typeof(Model3DType), modelType) ? modelType : Model3DType.Default,
        Camera = CreateDefaultCamera()
    };

    public static string GetStorageKey(int deviceNumber, MountType mountType, Model3DType modelType)
        => $"greenswamp.ui.telescope-view.v1.device.{deviceNumber}.mount.{mountType}.model.{modelType}";

    public static string GetModelSelectionStorageKey(int deviceNumber, MountType mountType)
        => $"greenswamp.ui.telescope-view.v1.device.{deviceNumber}.mount.{mountType}.selection";

    public void Normalize()
    {
        if (!Enum.IsDefined(typeof(Model3DType), ModelType))
            ModelType = Model3DType.Default;

        if (!Regex.IsMatch(ModelColor ?? string.Empty, "^#[0-9a-fA-F]{6}$"))
            ModelColor = DefaultModelColor;

        if (!BackgroundChoices.Contains(Background, StringComparer.OrdinalIgnoreCase))
            Background = DefaultBackground;

        if (!Enum.IsDefined(typeof(CardinalSize), CardinalSize))
            CardinalSize = CardinalSize.Large;

        if (!Enum.IsDefined(typeof(AssemblySupport), AssemblySupport))
            AssemblySupport = AssemblySupport.MediumTripod;

        if (!Enum.IsDefined(typeof(AssemblyMount), AssemblyMount))
            AssemblyMount = AssemblyMount.Eq6rPro;

        if (!Enum.IsDefined(typeof(AssemblyOta), AssemblyOta))
            AssemblyOta = AssemblyOta.Refractor;

        if (!AssemblyOtaSizes.Contains(AssemblyOtaSize))
            AssemblyOtaSize = 100;

        RaAdjustmentDegrees = Math.Clamp(RaAdjustmentDegrees, MinAxisAdjustment, MaxAxisAdjustment);
        DecAdjustmentDegrees = Math.Clamp(DecAdjustmentDegrees, MinAxisAdjustment, MaxAxisAdjustment);

        if (!IsValidCamera(Camera))
            Camera = CreateDefaultCamera();
    }

    private static CameraState CreateDefaultCamera() => new()
    {
        Alpha = 150,
        Beta = 60,
        Radius = 40,
        Target = new CameraTarget { X = 0, Y = 6, Z = 0 }
    };

    private static bool IsValidCamera(CameraState camera)
        => camera is not null
           && double.IsFinite(camera.Alpha) && double.IsFinite(camera.Beta)
           && double.IsFinite(camera.Radius)
           && camera.Alpha >= 0 && camera.Alpha <= 360
           && camera.Beta > 0 && camera.Beta < 180
           && camera.Radius >= 5 && camera.Radius <= 200
           && camera.Target is not null
           && double.IsFinite(camera.Target.X)
           && double.IsFinite(camera.Target.Y)
           && double.IsFinite(camera.Target.Z);
}

public sealed class TelescopeViewerModelSelection
{
    public Model3DType ModelType { get; set; } = Model3DType.Default;
}