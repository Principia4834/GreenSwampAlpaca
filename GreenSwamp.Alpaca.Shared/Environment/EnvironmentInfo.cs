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
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Runtime.InteropServices;

namespace GreenSwamp.Alpaca.Shared.EnvironmentLog
{
    /// <summary>
    /// Cross-platform environment information — no WMI or WPF dependencies.
    /// Hardware-specific queries that differ between Windows and Linux are in
    /// <see cref="PlatformEnvironmentInfo"/>.
    /// </summary>
    internal static class EnvironmentInfo
    {
        // -- Privacy helpers --------------------------------------------------

        /// <summary>
        /// Masks all characters except the first and last with asterisks.
        /// Returns the original value when it is null, empty, or fewer than 3 characters.
        /// </summary>
        internal static string ObscureText(string? text)
        {
            if (string.IsNullOrEmpty(text) || text.Length < 3)
                return text ?? string.Empty;

            return $"{text[0]}{new string('*', text.Length - 2)}{text[^1]}";
        }

        /// <summary>
        /// Masks the username segment in a file path.
        /// Handles both Windows (<c>\Users\name\</c>) and Linux (<c>/home/name/</c>) patterns.
        /// </summary>
        internal static string ObscurePath(string? path)
        {
            if (string.IsNullOrEmpty(path))
                return path ?? string.Empty;

            return ObscurePathSegment(ObscurePathSegment(path, "\\Users\\"), "/home/");
        }

        private static string ObscurePathSegment(string path, string marker)
        {
            var idx = path.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return path;

            var nameStart = idx + marker.Length;
            var sep = path.IndexOfAny(['\\', '/'], nameStart);
            var username = sep < 0 ? path[nameStart..] : path[nameStart..sep];
            var masked = ObscureText(username);

            return sep < 0
                ? string.Concat(path.AsSpan(0, nameStart), masked)
                : string.Concat(path.AsSpan(0, nameStart), masked, path.AsSpan(sep));
        }

        // -- MAC address helper -----------------------------------------------

        /// <summary>
        /// Masks all but the last two octets of a MAC address.
        /// Example: A4:C3:F0:85:AC:D1 → **:**:**:**:AC:D1
        /// </summary>
        private static string ObscureMac(PhysicalAddress address)
        {
            var bytes = address.GetAddressBytes();
            if (bytes.Length == 0) return string.Empty;

            var parts = bytes.Select((b, i) => i < bytes.Length - 2
                ? "**"
                : b.ToString("X2"));

            return string.Join(":", parts);
        }

        // -- Sections ---------------------------------------------------------

        /// <summary>Log application assembly details.</summary>
        internal static void LogApplicationInfo(StreamWriter writer)
        {
            writer.WriteLine("--- Application ---");
            try
            {
                var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
                var fvi = FileVersionInfo.GetVersionInfo(Environment.ProcessPath);

                writer.WriteLine($"Name:            {assembly.GetName().Name}");
                writer.WriteLine($"Version:         {assembly.GetName().Version}");
                writer.WriteLine($"File Version:    {fvi.FileVersion}");
                writer.WriteLine($"Product Version: {fvi.ProductVersion}");
                writer.WriteLine($"Copyright:       {fvi.LegalCopyright}");
                writer.WriteLine($"Build Date:      {File.GetLastWriteTime(Environment.ProcessPath):yyyy-MM-dd HH:mm:ss}");
                writer.WriteLine($"Location:        {ObscurePath(Environment.ProcessPath)}");
            }
            catch (Exception ex)
            {
                writer.WriteLine($"Error: {ex.Message}");
            }
            writer.WriteLine();
        }

        /// <summary>Log operating system details.</summary>
        internal static void LogOperatingSystemInfo(StreamWriter writer)
        {
            writer.WriteLine("--- Operating System ---");
            try
            {
                writer.WriteLine($"Description:    {RuntimeInformation.OSDescription}");
                writer.WriteLine($"Architecture:   {RuntimeInformation.OSArchitecture}");
                writer.WriteLine($"Version:        {Environment.OSVersion.Version}");
                writer.WriteLine($"Platform:       {Environment.OSVersion.Platform}");
                writer.WriteLine($"64-bit OS:      {Environment.Is64BitOperatingSystem}");
                writer.WriteLine($"Machine Name:   {ObscureText(Environment.MachineName)}");
                writer.WriteLine($"User Name:      {ObscureText(Environment.UserName)}");

                if (OperatingSystem.IsWindows())
                    writer.WriteLine($"User Domain:    {ObscureText(Environment.UserDomainName)}");

                writer.WriteLine($"System Dir:     {Environment.SystemDirectory}");
                writer.WriteLine($"Uptime:         {TimeSpan.FromMilliseconds(Environment.TickCount64):dd\\:hh\\:mm\\:ss}");

                // Richer OS description from platform files
                if (OperatingSystem.IsLinux())
                    LogLinuxOsRelease(writer);
            }
            catch (Exception ex)
            {
                writer.WriteLine($"Error: {ex.Message}");
            }
            writer.WriteLine();
        }

        private static void LogLinuxOsRelease(StreamWriter writer)
        {
            try
            {
                if (!File.Exists("/etc/os-release")) return;
                var lines = File.ReadAllLines("/etc/os-release");
                var pretty = lines.FirstOrDefault(l => l.StartsWith("PRETTY_NAME=", StringComparison.Ordinal));
                if (pretty is not null)
                    writer.WriteLine($"Distro:         {pretty["PRETTY_NAME=".Length..].Trim('"')}");
            }
            catch { /* non-critical */ }
        }

        /// <summary>Log .NET runtime details.</summary>
        internal static void LogRuntimeInfo(StreamWriter writer)
        {
            writer.WriteLine("--- Runtime ---");
            try
            {
                writer.WriteLine($"CLR Version:      {System.Environment.Version}");
                writer.WriteLine($"Framework:        {RuntimeInformation.FrameworkDescription}");
                writer.WriteLine($"Process Arch:     {RuntimeInformation.ProcessArchitecture}");
                writer.WriteLine($"64-bit Process:   {System.Environment.Is64BitProcess}");
                writer.WriteLine($"Processor Count:  {System.Environment.ProcessorCount}");
                writer.WriteLine($"System Page Size: {System.Environment.SystemPageSize:N0} bytes");
            }
            catch (Exception ex)
            {
                writer.WriteLine($"Error: {ex.Message}");
            }
            writer.WriteLine();
        }

        /// <summary>Log current process memory and thread details.</summary>
        internal static void LogProcessInfo(StreamWriter writer)
        {
            writer.WriteLine("--- Process ---");
            try
            {
                var proc = Process.GetCurrentProcess();
                writer.WriteLine($"PID:             {proc.Id}");
                writer.WriteLine($"Name:            {proc.ProcessName}");
                writer.WriteLine($"Start Time:      {proc.StartTime:yyyy-MM-dd HH:mm:ss}");
                writer.WriteLine($"Threads:         {proc.Threads.Count}");
                writer.WriteLine($"Handles:         {proc.HandleCount}");
                writer.WriteLine($"Working Set:     {proc.WorkingSet64 / (1024.0 * 1024):N2} MB");
                writer.WriteLine($"Private Memory:  {proc.PrivateMemorySize64 / (1024.0 * 1024):N2} MB");
                writer.WriteLine($"Virtual Memory:  {proc.VirtualMemorySize64 / (1024.0 * 1024):N2} MB");
                writer.WriteLine($"Peak Working Set:{proc.PeakWorkingSet64 / (1024.0 * 1024):N2} MB");
                writer.WriteLine($"GC Memory:       {GC.GetTotalMemory(false) / (1024.0 * 1024):N2} MB");
                writer.WriteLine($"GC Max Gen:      {GC.MaxGeneration}");

                var gcInfo = GC.GetGCMemoryInfo();
                writer.WriteLine($"GC Total Avail:  {gcInfo.TotalAvailableMemoryBytes / (1024.0 * 1024 * 1024):N2} GB");

                var args = System.Environment.GetCommandLineArgs();
                writer.WriteLine($"Args Count:      {args.Length}");
                for (var i = 0; i < Math.Min(args.Length, 10); i++)
                    writer.WriteLine($"  [{i}]: {args[i]}");
            }
            catch (Exception ex)
            {
                writer.WriteLine($"Error: {ex.Message}");
            }
            writer.WriteLine();
        }

        /// <summary>Log culture and locale settings.</summary>
        internal static void LogCultureInfo(StreamWriter writer)
        {
            writer.WriteLine("--- Culture & Locale ---");
            try
            {
                var culture = CultureInfo.CurrentCulture;
                var uiCulture = CultureInfo.CurrentUICulture;
                writer.WriteLine($"Culture:         {culture.Name} ({culture.DisplayName})");
                writer.WriteLine($"UI Culture:      {uiCulture.Name} ({uiCulture.DisplayName})");
                writer.WriteLine($"Installed UI:    {CultureInfo.InstalledUICulture.Name}");
                writer.WriteLine($"Time Zone:       {TimeZoneInfo.Local.DisplayName}");
                writer.WriteLine($"UTC Offset:      {TimeZoneInfo.Local.BaseUtcOffset}");
                writer.WriteLine($"DST Active:      {TimeZoneInfo.Local.IsDaylightSavingTime(DateTime.Now)}");
            }
            catch (Exception ex)
            {
                writer.WriteLine($"Error: {ex.Message}");
            }
            writer.WriteLine();
        }

        /// <summary>Log key file system paths (with username segments masked).</summary>
        internal static void LogPathInfo(StreamWriter writer)
        {
            writer.WriteLine("--- Paths ---");
            try
            {
                writer.WriteLine($"Current Dir:     {ObscurePath(System.Environment.CurrentDirectory)}");
                writer.WriteLine($"Base Dir:        {ObscurePath(AppDomain.CurrentDomain.BaseDirectory)}");
                writer.WriteLine($"Temp:            {ObscurePath(Path.GetTempPath())}");
                writer.WriteLine($"AppData Roaming: {ObscurePath(System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData))}");
                writer.WriteLine($"AppData Local:   {ObscurePath(System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData))}");
                writer.WriteLine($"Home:            {ObscurePath(System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile))}");

                if (OperatingSystem.IsWindows())
                {
                    writer.WriteLine($"Documents:       {ObscurePath(System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments))}");
                    writer.WriteLine($"Program Files:   {ObscurePath(System.Environment.GetFolderPath(System.Environment.SpecialFolder.ProgramFiles))}");
                    if (System.Environment.Is64BitOperatingSystem)
                        writer.WriteLine($"Program Files x86:{ObscurePath(System.Environment.GetFolderPath(System.Environment.SpecialFolder.ProgramFilesX86))}");
                }
            }
            catch (Exception ex)
            {
                writer.WriteLine($"Error: {ex.Message}");
            }
            writer.WriteLine();
        }

        /// <summary>Log ready drive/mount information.</summary>
        internal static void LogDriveInfo(StreamWriter writer)
        {
            writer.WriteLine("--- Drives ---");
            try
            {
                foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady))
                {
                    try
                    {
                        var freeGb = drive.AvailableFreeSpace / (1024.0 * 1024 * 1024);
                        var totalGb = drive.TotalSize / (1024.0 * 1024 * 1024);
                        var pct = totalGb > 0 ? freeGb / totalGb * 100 : 0;
                        writer.WriteLine($"{drive.Name} [{drive.DriveType}] {drive.DriveFormat}: {freeGb:N2} GB free / {totalGb:N2} GB total ({pct:N1}% free)");
                    }
                    catch (Exception ex)
                    {
                        writer.WriteLine($"{drive.Name}: Error – {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                writer.WriteLine($"Error: {ex.Message}");
            }
            writer.WriteLine();
        }

        /// <summary>Log network interfaces with MAC addresses partially masked.</summary>
        internal static void LogNetworkInfo(StreamWriter writer)
        {
            writer.WriteLine("--- Network ---");
            try
            {
                writer.WriteLine($"Host Name: {ObscureText(Dns.GetHostName())}");
                writer.WriteLine();

                var interfaces = NetworkInterface.GetAllNetworkInterfaces()
                    .Where(n => n.OperationalStatus == OperationalStatus.Up
                                && n.NetworkInterfaceType != NetworkInterfaceType.Loopback);

                foreach (var nic in interfaces)
                {
                    try
                    {
                        var obscuredMac = ObscureMac(nic.GetPhysicalAddress());
                        // Only log interfaces with a valid MAC address (skip virtual adapters without one)
                        if (obscuredMac.Length > 0)
                        {
                            writer.WriteLine($"  {nic.Name} [{nic.NetworkInterfaceType}]");
                            writer.WriteLine($"    MAC:   {obscuredMac}");

                            var props = nic.GetIPProperties();
                            foreach (var addr in props.UnicastAddresses)
                                writer.WriteLine($"    IP:    {addr.Address}");
                        }
                    }
                    catch (Exception ex)
                    {
                        writer.WriteLine($"    Error: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                writer.WriteLine($"Error: {ex.Message}");
            }
            writer.WriteLine();
        }
    }
}
