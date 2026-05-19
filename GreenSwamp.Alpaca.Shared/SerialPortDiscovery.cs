using System.IO.Ports;

namespace GreenSwamp.Alpaca.Shared;

/// <summary>
/// Cross-platform helper for enumerating available serial ports.
/// On Windows this delegates to <see cref="SerialPort.GetPortNames"/>.
/// On Linux/Raspberry Pi the same call works but also returns ttyS* and ttyUSB* devices,
/// so duplicate results are deduplicated and sorted for display.
/// </summary>
public static class SerialPortDiscovery
{
    /// <summary>
    /// Returns a sorted, deduplicated list of available serial port names on the current OS.
    /// Returns an empty array if enumeration fails (e.g., no serial subsystem present).
    /// </summary>
    public static string[] GetPortNames()
    {
        try
        {
            return SerialPort.GetPortNames()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch
        {
            return [];
        }
    }
}
