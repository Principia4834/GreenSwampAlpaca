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

using System.Reflection;

namespace GreenSwamp.Alpaca.Settings.Services
{
    /// <summary>
    /// Resolves where settings and log files are stored, selecting an appropriate
    /// root directory based on the runtime context (interactive user, Windows SCM
    /// service, or Linux systemd service).
    ///
    /// Priority order for the settings root:
    ///   1. <c>--service-settings-path=&lt;path&gt;</c> CLI argument (set via <see cref="ApplyCommandLineArgs"/>)
    ///   2. <c>GREENSWAMP_SETTINGS_PATH</c> environment variable
    ///   3. Windows SCM service  → <c>CommonDocuments\GreenSwampServer</c>
    ///                             (e.g. <c>C:\Users\Public\Documents\GreenSwampServer</c>)
    ///   4. Linux systemd service → <c>{UserProfile}/GreenSwampServer</c>
    ///                             (e.g. <c>/home/greenswamp/GreenSwampServer</c>)
    ///   5. Interactive user default → <c>%AppData%\GreenSwampAlpaca</c> (Windows)
    ///                                  or <c>~/.config/GreenSwampAlpaca</c> (Linux)
    ///
    /// The versioned settings path appends the assembly version to the root.
    /// Log files go to <c>&lt;root&gt;\Logs</c> in service / overridden mode,
    /// or to the legacy <c>%MyDocuments%\GSServer</c> / <c>~/GSServer</c> in interactive mode.
    /// </summary>
    public static class SettingsPathResolver
    {
        private const string ServiceSettingsArg = "--service-settings-path=";
        private const string EnvVarName = "GREENSWAMP_SETTINGS_PATH";
        private const string ServiceFolderName = "GreenSwampServer";
        private const string UserFolderName = "GreenSwampAlpaca";

        private static string? _cliOverrideRoot;
        private static bool _cliArgsParsed;
        private static bool _isServiceMode;

        /// <summary>
        /// Call this once at the very start of <c>Program.Main()</c>, before any path
        /// is resolved, to record the service-mode flag and extract any CLI path override.
        /// Safe to call multiple times; subsequent calls are no-ops.
        /// </summary>
        /// <param name="args">Command-line arguments passed to <c>Main()</c>.</param>
        /// <param name="isService">
        /// Pass <c>true</c> when the host detects it is running as a managed service
        /// (Windows SCM <em>or</em> Linux systemd). The caller is responsible for the
        /// detection to avoid pulling platform-specific packages into this project.
        /// </param>
        public static void ApplyCommandLineArgs(string[] args, bool isService = false)
        {
            if (_cliArgsParsed) return;
            _cliArgsParsed = true;

            _isServiceMode = isService;

            if (args == null) return;
            foreach (var arg in args)
            {
                if (arg.StartsWith(ServiceSettingsArg, StringComparison.OrdinalIgnoreCase))
                {
                    var value = arg[ServiceSettingsArg.Length..].Trim('"', '\'');
                    if (!string.IsNullOrWhiteSpace(value))
                        _cliOverrideRoot = value;
                    break;
                }
            }
        }

        /// <summary>
        /// Returns <c>true</c> when the process is running as a managed service
        /// (Windows SCM or Linux systemd), as reported by the caller of
        /// <see cref="ApplyCommandLineArgs"/>.
        /// </summary>
        public static bool IsService => _isServiceMode;

        /// <summary>
        /// Returns the settings root directory (without version subfolder).
        /// <list type="bullet">
        ///   <item>CLI override            → the supplied path</item>
        ///   <item>Env var override        → the env-var value</item>
        ///   <item>Windows SCM service     → <c>CommonDocuments\GreenSwampServer</c></item>
        ///   <item>Linux systemd service   → <c>{UserProfile}/GreenSwampServer</c></item>
        ///   <item>Interactive user mode   → <c>%AppData%\GreenSwampAlpaca</c> / <c>~/.config/GreenSwampAlpaca</c></item>
        /// </list>
        /// </summary>
        public static string GetSettingsRoot()
        {
            // 1. CLI override
            if (!string.IsNullOrWhiteSpace(_cliOverrideRoot))
                return _cliOverrideRoot!;

            // 2. Environment variable override
            var envOverride = Environment.GetEnvironmentVariable(EnvVarName);
            if (!string.IsNullOrWhiteSpace(envOverride))
                return envOverride;

            // 3 & 4. Service mode — platform selects the public-area folder
            if (IsService)
            {
                if (OperatingSystem.IsWindows())
                {
                    // C:\Users\Public\Documents\GreenSwampServer
                    return Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.CommonDocuments),
                        ServiceFolderName);
                }

                // Linux/macOS: use the service account's home directory.
                // When running as user 'greenswamp' this resolves to
                // /home/greenswamp/GreenSwampServer
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ServiceFolderName);
            }

            // 5. Interactive user default
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                UserFolderName);
        }

        /// <summary>
        /// Returns the versioned settings directory:
        /// <c>&lt;SettingsRoot&gt;/&lt;version&gt;</c>.
        /// </summary>
        public static string GetVersionedPath(string version)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(version);
            return Path.Combine(GetSettingsRoot(), version);
        }

        /// <summary>
        /// Returns the logs directory.
        /// <list type="bullet">
        ///   <item>Service mode / overridden → <c>&lt;SettingsRoot&gt;/Logs</c></item>
        ///   <item>Interactive user mode     → <c>%MyDocuments%\GSServer</c> (Windows)
        ///                                     or <c>~/GSServer</c> (Linux)</item>
        /// </list>
        /// </summary>
        public static string GetLogsRoot()
        {
            if (!string.IsNullOrWhiteSpace(_cliOverrideRoot) ||
                !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(EnvVarName)) ||
                IsService)
            {
                return Path.Combine(GetSettingsRoot(), "Logs");
            }

            // Interactive legacy default — mirrors the existing GsFile behaviour
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "GSServer");
        }

        /// <summary>
        /// Reads the assembly version string using the same logic as
        /// <see cref="VersionedSettingsService"/> and <see cref="Models.ServerConfig"/>.
        /// </summary>
        public static string GetAssemblyVersion()
        {
            var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();

            var attr = assembly
                .GetCustomAttributes(typeof(AssemblyInformationalVersionAttribute), false)
                .FirstOrDefault() as AssemblyInformationalVersionAttribute;

            var version = attr?.InformationalVersion
                          ?? assembly.GetName().Version?.ToString()
                          ?? "1.0.0";

            var plusIndex = version.IndexOf('+');
            return plusIndex > 0 ? version[..plusIndex] : version;
        }
    }
}
