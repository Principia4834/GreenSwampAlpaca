namespace GreenSwamp.Alpaca.Server.Components.Home
{
    /// <summary>Display data for one telescope tile on the home page.</summary>
    public sealed record HomeDeviceInfo(
        int DeviceNumber,
        string Name,
        string Description,
        string MountType,
        string AlignmentMode,
        bool Connected,
        bool Enabled);
}
