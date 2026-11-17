using System.Text.RegularExpressions;
using ASCOM.Common.DeviceInterfaces;
using GreenSwamp.Alpaca.Settings.Models;
using CameraState = GreenSwamp.Alpaca.Settings.Models.CameraState;

namespace GreenSwamp.Alpaca.Server.Models;

public sealed class TelescopeViewerSettings
{
    public const string DefaultModelColor = "#52b788";
    public const string DefaultBackground = "NorthAmerica";

    public Model3DType ModelType { get; set; } = Model3DType.Default;
    public CameraState Camera { get; set; } = CreateDefaultCamera();
    public string ModelColor { get; set; } = DefaultModelColor;
    public string Background { get; set; } = DefaultBackground;
    public bool ShowGrid { get; set; } = true;

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

        if (string.IsNullOrWhiteSpace(Background))
            Background = DefaultBackground;

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