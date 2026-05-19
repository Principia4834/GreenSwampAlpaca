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

using System.Text.Json;
using System.Text.Json.Serialization;

namespace GreenSwamp.Alpaca.Settings.Models
{
    /// <summary>
    /// Alpaca server configuration — replaces ASCOM.Tools.XMLProfile-backed ServerSettings.
    /// Persisted as appsettings.server.user.json in the versioned AppData folder.
    /// All defaults match the values previously held in the ASCOM XML profile.
    /// </summary>
    public class ServerConfig
    {
        // ── Network ───────────────────────────────────────────────────────────

        /// <summary>TCP port the Alpaca server listens on.</summary>
        public ushort ServerPort { get; set; } = 31416;

        /// <summary>Bind to all network interfaces when true; localhost-only when false.</summary>
        public bool AllowRemoteAccess { get; set; } = true;

        /// <summary>Respond to localhost-sourced discovery on the loopback adapter only.</summary>
        public bool LocalRespondOnlyToLocalHost { get; set; } = true;

        /// <summary>Advertise this server via Alpaca UDP discovery.</summary>
        public bool AllowDiscovery { get; set; } = true;

        // ── Alpaca behaviour ─────────────────────────────────────────────────

        /// <summary>Reject non-compliant Alpaca requests when true.</summary>
        public bool RunInStrictAlpacaMode { get; set; } = true;

        /// <summary>Prevent remote clients from disconnecting devices.</summary>
        public bool PreventRemoteDisconnects { get; set; } = false;

        /// <summary>Allow the image-bytes binary download endpoint.</summary>
        public bool AllowImageBytesDownload { get; set; } = true;

        // ── Identity / UI ─────────────────────────────────────────────────────

        /// <summary>Human-readable location shown in discovery and the setup page.</summary>
        public string Location { get; set; } = "Unknown";

        /// <summary>Open the default browser automatically when the server starts.</summary>
        public bool AutoStartBrowser { get; set; } = true;

        /// <summary>Expose the OpenAPI / Swagger UI at /swagger.</summary>
        public bool RunSwagger { get; set; } = true;

        // ── Authentication ────────────────────────────────────────────────────

        /// <summary>Require HTTP Basic / Cookie authentication.</summary>
        public bool UseAuth { get; set; } = false;

        /// <summary>Login user name (only used when UseAuth is true).</summary>
        public string UserName { get; set; } = "User";

        /// <summary>
        /// Hashed password produced by Hash.GetStoragePassword().
        /// Never store a plain-text password here.
        /// </summary>
        public string Password { get; set; } = string.Empty;

        // ── Bootstrap helper ──────────────────────────────────────────────────

        private static readonly JsonSerializerOptions _readOptions =
            new() { PropertyNameCaseInsensitive = true };

        private static readonly JsonSerializerOptions _writeOptions =
            new() { WriteIndented = true };

        /// <summary>
        /// Resolves the current assembly version string, matching the logic used by
        /// VersionedSettingsService so bootstrap and service agree on the path.
        /// </summary>
        public static string GetVersion()
        {
            var assembly = System.Reflection.Assembly.GetEntryAssembly()
                           ?? System.Reflection.Assembly.GetExecutingAssembly();

            var attr = assembly
                .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
                .FirstOrDefault() as System.Reflection.AssemblyInformationalVersionAttribute;

            var version = attr?.InformationalVersion
                          ?? assembly.GetName().Version?.ToString()
                          ?? "1.0.0";

            var plusIndex = version.IndexOf('+');
            return plusIndex > 0 ? version[..plusIndex] : version;
        }

        /// <summary>
        /// Reads appsettings.server.user.json directly from disk without the DI container.
        /// Used during early Program startup (before WebApplicationBuilder is built) to
        /// obtain the server port and remote-access flag for URL binding.
        /// Falls back to defaults if the file is absent or unparseable.
        /// </summary>
        /// <param name="filePath">
        /// Full path to appsettings.server.user.json in the versioned AppData folder.
        /// </param>
        public static ServerConfig LoadBootstrap(string filePath)
        {
            if (!File.Exists(filePath))
                return new ServerConfig();

            try
            {
                var json = File.ReadAllText(filePath);
                return JsonSerializer.Deserialize<ServerConfig>(json, _readOptions)
                       ?? new ServerConfig();
            }
            catch
            {
                // Corrupt file — return defaults; VersionedSettingsService will repair on next save
                return new ServerConfig();
            }
        }

        /// <summary>
        /// Writes the config to disk synchronously.  Used only by the --reset-auth CLI path
        /// before the DI container exists.
        /// </summary>
        public static void SaveBootstrap(string filePath, ServerConfig config)
        {
            try
            {
                var json = JsonSerializer.Serialize(config, _writeOptions);
                var tempPath = filePath + ".tmp";
                File.WriteAllText(tempPath, json, System.Text.Encoding.UTF8);
                File.Move(tempPath, filePath, overwrite: true);
            }
            catch
            {
                // Best-effort; errors surface on next startup
            }
        }
    }
}
