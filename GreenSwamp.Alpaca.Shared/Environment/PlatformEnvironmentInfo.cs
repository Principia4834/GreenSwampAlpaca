/* Copyright(C) 2019-2026 Rob Morgan (robert.morgan.e@gmail.com)

    This program is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published
    by the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with this program.  If not, see <https://www.gnu.org/licenses/>.
 */

using System;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;
using System.Threading;

namespace GreenSwamp.Alpaca.Shared.EnvironmentLog
{
    /// <summary>
    /// Hardware and privilege queries that differ between Windows and Linux.
    /// Each method uses <see cref="OperatingSystem"/> guards at runtime so
    /// the same binary runs on both platforms without conditional compilation.
    /// macOS is not supported.
    /// </summary>
    internal static class PlatformEnvironmentInfo
    {
        // ── CPU ──────────────────────────────────────────────────────────────

        /// <summary>Log CPU name and clock speed from registry (Windows) or /proc/cpuinfo (Linux).</summary>
        internal static void LogCpuInfo(StreamWriter writer)
        {
            writer.WriteLine("--- CPU ---");
            try
            {
                if (OperatingSystem.IsWindows())
                    LogCpuWindows(writer);
                else if (OperatingSystem.IsLinux())
                    LogCpuLinux(writer);
                else
                    writer.WriteLine("CPU detail: not available on this platform");
            }
            catch (Exception ex)
            {
                writer.WriteLine($"Error: {ex.Message}");
            }
            writer.WriteLine();
        }

        [SupportedOSPlatform("windows")]
        private static void LogCpuWindows(StreamWriter writer)
        {
            QueryWmi(writer, "Processor",
                "SELECT Name, NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed, CurrentClockSpeed FROM Win32_Processor",
                obj =>
                {
                    writer.WriteLine($"CPU:                {obj["Name"]}");
                    writer.WriteLine($"Physical Cores:     {obj["NumberOfCores"]}");
                    writer.WriteLine($"Logical Processors: {obj["NumberOfLogicalProcessors"]}");
                    writer.WriteLine($"Max Clock Speed:    {obj["MaxClockSpeed"]} MHz");
                    if (obj["CurrentClockSpeed"] != null)
                        writer.WriteLine($"Current Clock Speed:{obj["CurrentClockSpeed"]} MHz");
                });
        }

        private static void LogCpuLinux(StreamWriter writer)
        {
            if (!File.Exists("/proc/cpuinfo")) return;

            var lines = File.ReadAllLines("/proc/cpuinfo");

            var model = lines
                .FirstOrDefault(l => l.StartsWith("model name", StringComparison.Ordinal))
                ?.Split(':').ElementAtOrDefault(1)?.Trim();

            var mhz = lines
                .FirstOrDefault(l => l.StartsWith("cpu MHz", StringComparison.Ordinal))
                ?.Split(':').ElementAtOrDefault(1)?.Trim();

            var physicalIds = lines
                .Where(l => l.StartsWith("physical id", StringComparison.Ordinal))
                .Select(l => l.Split(':').ElementAtOrDefault(1)?.Trim())
                .Where(v => v is not null)
                .Distinct()
                .Count();

            var coreIds = lines
                .Where(l => l.StartsWith("core id", StringComparison.Ordinal))
                .Select(l => l.Split(':').ElementAtOrDefault(1)?.Trim())
                .Where(v => v is not null)
                .Distinct()
                .Count();

            if (model is not null) writer.WriteLine($"Name:           {model}");
            if (mhz is not null)   writer.WriteLine($"Clock Speed:    {mhz} MHz");
            if (physicalIds > 0)   writer.WriteLine($"Physical CPUs:  {physicalIds}");
            if (coreIds > 0)       writer.WriteLine($"Physical Cores: {coreIds}");
        }

        // ── Memory ───────────────────────────────────────────────────────────

        /// <summary>Log total and available physical memory.</summary>
        internal static void LogMemoryInfo(StreamWriter writer)
        {
            writer.WriteLine("--- Memory ---");
            try
            {
                // GC reported available memory — available on all platforms (.NET 5+)
                var gcInfo = GC.GetGCMemoryInfo();
                writer.WriteLine($"GC Total Available: {gcInfo.TotalAvailableMemoryBytes / (1024.0 * 1024 * 1024):N2} GB");

                if (OperatingSystem.IsWindows())
                    LogMemoryWindows(writer);
                else if (OperatingSystem.IsLinux())
                    LogMemoryLinux(writer);
            }
            catch (Exception ex)
            {
                writer.WriteLine($"Error: {ex.Message}");
            }
            writer.WriteLine();
        }

        [SupportedOSPlatform("windows")]
        private static void LogMemoryWindows(StreamWriter writer)
        {
            var status = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
            if (GlobalMemoryStatusEx(ref status))
            {
                writer.WriteLine($"Total Physical: {status.ullTotalPhys / (1024.0 * 1024 * 1024):N2} GB");
                writer.WriteLine($"Avail Physical: {status.ullAvailPhys / (1024.0 * 1024 * 1024):N2} GB");
                writer.WriteLine($"Memory Load:    {status.dwMemoryLoad}%");
            }
        }

        private static void LogMemoryLinux(StreamWriter writer)
        {
            if (!File.Exists("/proc/meminfo")) return;

            var lines = File.ReadAllLines("/proc/meminfo");

            static long ParseKb(string[] lines, string key)
            {
                var line = lines.FirstOrDefault(l => l.StartsWith(key, StringComparison.Ordinal));
                if (line is null) return -1;
                var parts = line.Split(':', StringSplitOptions.TrimEntries);
                return parts.Length > 1 && long.TryParse(parts[1].Split(' ')[0], out var kb) ? kb : -1;
            }

            var totalKb = ParseKb(lines, "MemTotal:");
            var availKb = ParseKb(lines, "MemAvailable:");

            if (totalKb > 0) writer.WriteLine($"Total Physical: {totalKb / (1024.0 * 1024):N2} GB");
            if (availKb > 0) writer.WriteLine($"Avail Physical: {availKb / (1024.0 * 1024):N2} GB");
        }

        // ── Admin / privilege ─────────────────────────────────────────────────

        /// <summary>Log whether the process is running with elevated privileges.</summary>
        internal static void LogAdminInfo(StreamWriter writer)
        {
            writer.WriteLine("--- Privileges ---");
            try
            {
                bool isElevated;

                if (OperatingSystem.IsWindows())
                {
                    using var identity = WindowsIdentity.GetCurrent();
                    var principal = new WindowsPrincipal(identity);
                    isElevated = principal.IsInRole(WindowsBuiltInRole.Administrator);
                }
                else
                {
                    // On Linux/Unix root has uid 0; Environment.UserName == "root" is the
                    // simplest cross-platform check without requiring a P/Invoke to geteuid.
                    isElevated = System.Environment.UserName == "root";
                }

                writer.WriteLine($"Running Elevated: {isElevated}");
            }
            catch (Exception ex)
            {
                writer.WriteLine($"Error: {ex.Message}");
            }
            writer.WriteLine();
        }

        // ── Win32 P/Invoke ───────────────────────────────────────────────────

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct MEMORYSTATUSEX
        {
            internal uint dwLength;
            internal uint dwMemoryLoad;
            internal ulong ullTotalPhys;
            internal ulong ullAvailPhys;
            internal ulong ullTotalPageFile;
            internal ulong ullAvailPageFile;
            internal ulong ullTotalVirtual;
            internal ulong ullAvailVirtual;
            internal ulong ullAvailExtendedVirtual;
        }

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        [SupportedOSPlatform("windows")]
        private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

        // ── System info (manufacturer / model) ──────────────────────────────

        /// <summary>Log system manufacturer and model from BIOS registry (Windows) or DMI sysfs (Linux).</summary>
        internal static void LogSystemInfo(StreamWriter writer)
        {
            writer.WriteLine("--- Computer System ---");
            try
            {
                if (OperatingSystem.IsWindows())
                    LogSystemWindows(writer);
                else if (OperatingSystem.IsLinux())
                    LogSystemLinux(writer);
                else
                    writer.WriteLine("System info: not available on this platform");
            }
            catch (Exception ex)
            {
                writer.WriteLine($"Error: {ex.Message}");
            }
            writer.WriteLine();
        }

        [SupportedOSPlatform("windows")]
        private static void LogSystemWindows(StreamWriter writer)
        {
            QueryWmi(writer, "Computer System",
                "SELECT TotalPhysicalMemory, Manufacturer, Model FROM Win32_ComputerSystem",
                obj =>
                {
                    if (obj["TotalPhysicalMemory"] != null)
                    {
                        var totalGB = Convert.ToInt64(obj["TotalPhysicalMemory"]) / (1024.0 * 1024 * 1024);
                        writer.WriteLine($"Total Physical Memory: {totalGB:N2} GB");
                    }
                    writer.WriteLine($"Manufacturer: {obj["Manufacturer"]}");
                    writer.WriteLine($"Model:        {obj["Model"]}");
                });

            QueryWmi(writer, "Operating System",
                "SELECT Caption, Version, BuildNumber, OSArchitecture FROM Win32_OperatingSystem",
                obj =>
                {
                    writer.WriteLine($"OS Caption:      {obj["Caption"]}");
                    writer.WriteLine($"OS Version:      {obj["Version"]}");
                    writer.WriteLine($"OS Build:        {obj["BuildNumber"]}");
                    writer.WriteLine($"OS Architecture: {obj["OSArchitecture"]}");
                });
        }

        private static void LogSystemLinux(StreamWriter writer)
        {
            static string ReadDmi(string file)
            {
                var path = $"/sys/class/dmi/id/{file}";
                return File.Exists(path) ? File.ReadAllText(path).Trim() : string.Empty;
            }

            var vendor = ReadDmi("sys_vendor");
            var product = ReadDmi("product_name");
            var biosVendor = ReadDmi("bios_vendor");
            var biosVersion = ReadDmi("bios_version");
            var biosDate = ReadDmi("bios_date");

            if (!string.IsNullOrEmpty(vendor))   writer.WriteLine($"Manufacturer: {vendor}");
            if (!string.IsNullOrEmpty(product))  writer.WriteLine($"Model:        {product}");
            if (!string.IsNullOrEmpty(biosVendor))   writer.WriteLine($"BIOS Vendor:  {biosVendor}");
            if (!string.IsNullOrEmpty(biosVersion))  writer.WriteLine($"BIOS Version: {biosVersion}");
            if (!string.IsNullOrEmpty(biosDate))     writer.WriteLine($"BIOS Date:    {biosDate}");
        }

        // ── Video adapter ────────────────────────────────────────────────────

        /// <summary>Log display adapter name, driver, and primary screen resolution.</summary>
        internal static void LogVideoInfo(StreamWriter writer)
        {
            writer.WriteLine("--- Video ---");
            try
            {
                if (OperatingSystem.IsWindows())
                    LogVideoWindows(writer);
                else if (OperatingSystem.IsLinux())
                    LogVideoLinux(writer);
                else
                    writer.WriteLine("Video info: not available on this platform");
            }
            catch (Exception ex)
            {
                writer.WriteLine($"Error: {ex.Message}");
            }
            writer.WriteLine();
        }

        [SupportedOSPlatform("windows")]
        private static void LogVideoWindows(StreamWriter writer)
        {
            QueryWmi(writer, "Video Controller",
                "SELECT Name, AdapterRAM, DriverVersion, VideoModeDescription FROM Win32_VideoController",
                obj =>
                {
                    writer.WriteLine($"Video Card:     {obj["Name"]}");
                    if (obj["AdapterRAM"] != null)
                    {
                        var ramMB = Convert.ToInt64(obj["AdapterRAM"]) / (1024 * 1024);
                        writer.WriteLine($"Video RAM:      {ramMB:N0} MB");
                    }
                    if (obj["DriverVersion"] != null)
                        writer.WriteLine($"Driver Version: {obj["DriverVersion"]}");
                    if (obj["VideoModeDescription"] != null)
                        writer.WriteLine($"Video Mode:     {obj["VideoModeDescription"]}");
                });
        }

        private static void LogVideoLinux(StreamWriter writer)
        {
            const string drmPath = "/sys/class/drm";
            if (!Directory.Exists(drmPath)) return;

            var cards = Directory.GetDirectories(drmPath, "card*")
                .Where(d => !Path.GetFileName(d).Contains('-'))
                .OrderBy(d => d)
                .Take(4);

            foreach (var card in cards)
            {
                var vendorFile = Path.Combine(card, "device", "vendor");
                var deviceFile = Path.Combine(card, "device", "device");
                var vendor = File.Exists(vendorFile) ? File.ReadAllText(vendorFile).Trim() : string.Empty;
                var device = File.Exists(deviceFile) ? File.ReadAllText(deviceFile).Trim() : string.Empty;
                writer.WriteLine($"{Path.GetFileName(card)}: vendor={vendor} device={device}");
            }
        }

        // ── WMI helper ───────────────────────────────────────────────────────

        private const int WmiTimeoutSeconds = 2;

        [SupportedOSPlatform("windows")]
        private static void QueryWmi(
            StreamWriter writer,
            string description,
            string query,
            Action<ManagementObject> processResult,
            CancellationToken cancellationToken = default)
        {
            if (cancellationToken.IsCancellationRequested) return;

            try
            {
                using var searcher = new ManagementObjectSearcher(query);
                searcher.Options.Timeout = TimeSpan.FromSeconds(WmiTimeoutSeconds);

                var found = false;
                foreach (ManagementObject obj in searcher.Get())
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        writer.WriteLine($"{description}: Cancelled");
                        return;
                    }
                    processResult(obj);
                    found = true;
                }

                if (!found)
                    writer.WriteLine($"{description}: No data returned");
            }
            catch (UnauthorizedAccessException ex)
            {
                writer.WriteLine($"{description}: Access Denied - {ex.Message}");
            }
            catch (ManagementException ex)
            {
                writer.WriteLine($"{description}: WMI Error - {ex.Message}");
            }
            catch (TimeoutException ex)
            {
                writer.WriteLine($"{description}: Timeout - {ex.Message}");
            }
            catch (Exception ex)
            {
                writer.WriteLine($"{description}: Failed - {ex.GetType().Name}: {ex.Message}");
            }
        }
    }
}
