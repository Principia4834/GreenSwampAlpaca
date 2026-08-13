using ApexCharts;
using ASCOM.Alpaca;
using GreenSwamp.Alpaca.MountControl;
using GreenSwamp.Alpaca.Server.Hubs;
using GreenSwamp.Alpaca.Server.Middleware;
using GreenSwamp.Alpaca.Server.Models;
using GreenSwamp.Alpaca.Server.Services;
using GreenSwamp.Alpaca.Settings.Extensions;
using GreenSwamp.Alpaca.Settings.Models;
using GreenSwamp.Alpaca.Settings.Services;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.Logging;
using MudBlazor.Services;
using H.NotifyIcon.Core;
using System.Diagnostics;
using System.Drawing;
using System.IO.Compression;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Runtime.InteropServices;

#nullable enable
namespace GreenSwamp.Alpaca.Server
{
    public class Program
    {
        // Driver name
        internal const string DriverID = "GreenSwamp.Alpaca";

        // This supports --urls=http://*:port by default.
        internal const int DefaultPort = 31416;

        // Driver information
        internal const string Manufacturer = "Green Swamp Software";
        internal const string ServerName = "Green Swamp Alpaca Server";

        internal static readonly string serverVersion = ResolveServerVersion();

        private static string ResolveServerVersion()
        {
            try
            {
                var processPath = Environment.ProcessPath;
                if (!string.IsNullOrWhiteSpace(processPath) && File.Exists(processPath))
                {
                    var fileVersion = FileVersionInfo.GetVersionInfo(processPath).FileVersion;
                    if (!string.IsNullOrWhiteSpace(fileVersion))
                    {
                        return fileVersion;
                    }
                }
            }
            catch
            {
                // Ignore and fall back
            }

            return Assembly.GetEntryAssembly()?.GetName().Version?.ToString()
                ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString()
                ?? "0.0.0";
        }
        internal static ILogger? Logger;

        internal static IHostApplicationLifetime? Lifetime;

        /// <summary>Bootstrap server configuration read before the DI container is built.</summary>
        internal static ServerConfig BootstrapConfig { get; private set; } = new ServerConfig();

        private static TrayIconWithContextMenu? _trayIcon;

        private static Icon? _trayIconImage;

        private enum ConsoleDisplayOption
        {
            StartNormally = 0,
            StartMinimized = 1,
            NoConsole = 2
        }

        private const int SW_RESTORE = 9;
        private const int SW_MINIMIZE = 6;
        private const int SW_HIDE = 0;

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetConsoleWindow();

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsIconic(IntPtr hWnd);
        internal static string ServerVersion => serverVersion;

        public static async Task Main(string[] args)
        {
            // Bootstrap logger — active before the DI container is built.
            // Log level is read from the "Logging" section in appsettings.json after the host is built.
            // To get verbose output set "Logging:LogLevel:Default" to "Debug" in appsettings.json.
            using var bootstrapLoggerFactory = LoggerFactory.Create(b =>
                b.AddSimpleConsole(o => { o.TimestampFormat = "yyyy-MM-dd HH:mm:ss "; o.SingleLine = true; })
                 .SetMinimumLevel(LogLevel.Debug));
            Logger = bootstrapLoggerFactory.CreateLogger<Program>();

            // Detect service mode before any path is resolved — covers Windows SCM, Linux systemd and macOS launchd.
            // Detection is done here (not in SettingsPathResolver) to avoid pulling platform packages into the Settings project.
            var isService = args?.Any(a => string.Equals(a, "--service", StringComparison.OrdinalIgnoreCase)) == true;
            var canLaunchBrowser = Environment.UserInteractive && !isService;

            SettingsPathResolver.ApplyCommandLineArgs(args ?? [], isService);

            // Bootstrap: read ServerConfig from disk before the DI container exists.
            // Used for port-collision detection and --urls binding below.
            var bootstrapConfigPath = Path.Combine(
                SettingsPathResolver.GetVersionedPath(ServerConfig.GetVersion()),
                "appsettings.server.user.json");

            // LoadBootstrap returns new ServerConfig() when the file is absent (first run).
            // ServerConfig defaults match appsettings.json, so --urls binding is always correct
            // on first run without needing to read appsettings.json before the host is built.
            // VersionedSettingsService seeds the file on its first GetServerConfig() call.
            BootstrapConfig = ServerConfig.LoadBootstrap(bootstrapConfigPath);

            Logger.LogInformation($"Bootstrap server config loaded from: {bootstrapConfigPath}");

            if (OperatingSystem.IsWindows() && !isService)
            {
                if (!Enum.TryParse(BootstrapConfig.ConsoleDisplayDefault, true, out ConsoleDisplayOption startupConsoleMode))
                {
                    startupConsoleMode = ConsoleDisplayOption.NoConsole;
                }

                ShowConsole(startupConsoleMode);
            }

            // Add custom Command Line arguments here
            #region Startup and Logging

            Logger.LogInformation($"{ServerName} version {ServerVersion}");
            Logger.LogInformation($"Running on: {RuntimeInformation.OSDescription}.");

            // When running as a managed service (Windows SCM, Linux systemd, or macOS launchd), the process manager
            // guarantees a single instance and there is no desktop to launch a browser into,
            // so skip duplicate-instance detection entirely.
            try
            {
                if (canLaunchBrowser)
                {
                    //Already running, start the browser, detects based on port in use
                    if (IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpConnections().Any(con => con.LocalEndPoint.Port == BootstrapConfig.ServerPort && (con.State == TcpState.Listen || con.State == TcpState.Established)))
                    {
                        Logger.LogInformation("Detected driver port already open, starting web browser on IP and Port. If this fails something else is using the port");
                        StartBrowser(BootstrapConfig.ServerPort);
                        return;
                    }
                }
                if (canLaunchBrowser)
                {
                    // Environment.ProcessPath works correctly for single-file published executables;
                    // Assembly.Location returns "" in that scenario, causing false-positive detection.
                    var processName = Path.GetFileNameWithoutExtension(
                        Environment.ProcessPath ?? Assembly.GetEntryAssembly()?.GetName().Name ?? string.Empty);
                    if (!string.IsNullOrWhiteSpace(processName) &&
                        Process.GetProcessesByName(processName).Length > 1)
                    {
                        Logger.LogInformation("Detected driver already running. Open http://localhost:{Port} manually.", BootstrapConfig.ServerPort);
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex.Message);
                return;
            }

            // Reset all stored settings if requested
            if (args?.Any(str => str.Contains("--reset")) ?? false)
            {
                Logger.LogInformation("Resetting Settings");
                // Deleting appsettings.server.user.json resets server config; VersionedSettingsService
                // will recreate it from factory defaults on next startup.
                if (File.Exists(bootstrapConfigPath))
                    File.Delete(bootstrapConfigPath);
                Logger.LogInformation($"Deleted {bootstrapConfigPath} — factory defaults will apply on next start.");
                return;
            }

            // Turn off Authentication. Once off the user can change the password and re-enable authentication
            if (args?.Any(str => str.Contains("--reset-auth")) ?? false)
            {
                Logger.LogInformation("Turning off Authentication to allow password reset.");
                BootstrapConfig.UseAuth = false;
                ServerConfig.SaveBootstrap(bootstrapConfigPath, BootstrapConfig);
                Logger.LogInformation("Authentication off, you can change the password and then re-enable Authentication.");
            }

            if (args?.Any(str => str.Contains("--local-address")) ?? false)
            {
                Console.WriteLine($"http://localhost:{BootstrapConfig.ServerPort}");
            }

            if (!args?.Any(str => str.Contains("--urls")) ?? true)
            {
                args ??= [];

                Logger.LogInformation("No startup url args detected, binding to saved server settings.");

                var temparray = new string[args.Length + 1];

                args.CopyTo(temparray, 0);

                string startupUrlArg = "--urls=http://";

                //If set to allow remote access bind to all local ips, otherwise bind only to localhost
                if (BootstrapConfig.AllowRemoteAccess)
                {
                    startupUrlArg += "*";
                }
                else
                {
                    startupUrlArg += "localhost";
                }

                startupUrlArg += ":" + BootstrapConfig.ServerPort;

                Logger.LogInformation("Startup URL args: " + startupUrlArg);

                temparray[args.Length] = startupUrlArg;

                args = temparray;
            }

            var builder = WebApplication.CreateBuilder(args ?? []);

            // Configure response compression with Brotli and Gzip providers, enabling for HTTPS
            builder.Services.AddResponseCompression(options =>
            {
                options.EnableForHttps = true;
                options.Providers.Add<BrotliCompressionProvider>();
                options.Providers.Add<GzipCompressionProvider>();
            });

            // Register the appropriate managed-service lifetime for Windows SCM and Linux systemd.
            // Both calls are no-ops when the process is not started by the respective service manager.
            if (OperatingSystem.IsWindows())
            {
                builder.Host.UseWindowsService(options =>
                    options.ServiceName = "GreenSwampAlpacaServer");
            }
            else if (OperatingSystem.IsLinux())
            {
                builder.Host.UseSystemd();
            }

            // Register FloatingWindowManager for DI injection
            builder.Services.AddScoped<FloatingWindowManager>();

            // SignalR is used by ChartHub for real-time chart data streaming
            builder.Services.AddSignalR();

            // ApexCharts Blazor wrapper service (manages global options and chart instances)
            builder.Services.AddApexCharts();

            // ChartDataService is singleton so MonitorQueue subscriptions and ring buffers
            // persist across Blazor circuit lifetimes (i.e. multiple chart windows).
            builder.Services.AddSingleton<ChartDataService>();

            // ChartLoggingService is scoped — each chart window circuit tracks its own
            // logging state (file path, active flag) independently.
            builder.Services.AddScoped<ChartLoggingService>();

            // Apply the same timestamp format to the host's console logger.
            builder.Logging.AddSimpleConsole(o => { o.TimestampFormat = "yyyy-MM-dd HH:mm:ss "; o.SingleLine = true; });

            // Load versioned user settings support
            builder.Configuration.AddVersionedUserSettings();

            // Register all settings services (VersionedSettings, Template, Profile)
            builder.Services.AddVersionedSettings(builder.Configuration);

            // Configure Server Settings from configuration
            builder.Services.AddSingleton(sp =>
            {
                // Create instance with settings service
                var settingsService = sp.GetRequiredService<IVersionedSettingsService>();
                return new GreenSwamp.Alpaca.MountControl.SkySettings(settingsService);
            });

            // Register Settings Backup Service
            builder.Services.AddScoped<GreenSwamp.Alpaca.Settings.Services.ISettingsExportService,
                GreenSwamp.Alpaca.Settings.Services.SettingsExportService>();
            Logger.LogInformation("SkySettings registered in DI container");
            Logger.LogInformation("Settings services registered: VersionedSettings, Template");
            #endregion Startup and Logging

            //Load the configuration — pass BootstrapConfig so AlpacaConfiguration has no ASCOM XMLProfile dependency
            DeviceManager.LoadConfiguration(new AlpacaConfiguration(BootstrapConfig));

            #region Finish Building and Start server

            // Register ActiveDeviceViewRegistry as singleton for DI injection
            builder.Services.AddSingleton<ActiveDeviceViewRegistry>();

            // Add Text To Speech browser service
            builder.Services.AddScoped<GreenSwamp.Alpaca.Server.Services.BrowserTtsService>();

            // Add services to the container.
            builder.Services.AddRazorPages();
            builder.Services.AddServerSideBlazor();
            builder.Services.AddMudServices();
            builder.Services.AddHttpContextAccessor();

            //Load any xml comments for this program, this helps with swagger
            var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
            var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);

            // Add Swagger for the APIs
            ASCOM.Alpaca.Razor.StartupHelpers.ConfigureSwagger(builder.Services, xmlPath);
            // Set default behaviors for Alpaca APIs
            ASCOM.Alpaca.Razor.StartupHelpers.ConfigureAlpacaAPIBehavoir(builder.Services);
            // Use Authentication
            ASCOM.Alpaca.Razor.StartupHelpers.ConfigureAuthentication(builder.Services);
            // Add User Service
            builder.Services.AddScoped<IUserService, Data.UserService>();

            // Register TelescopeStateService for real-time state updates
            builder.Services.AddSingleton<GreenSwamp.Alpaca.Server.Services.TelescopeStateService>();
            Logger.LogInformation("TelescopeStateService registered for real-time state updates");

            // Register UnifiedDeviceRegistry as singleton for DI injection
            builder.Services.AddSingleton<GreenSwamp.Alpaca.Server.Services.UnifiedDeviceRegistry>();

            // Register MonitorDisplayService for live monitor-record display
            builder.Services.AddSingleton<GreenSwamp.Alpaca.Server.Services.MonitorDisplayService>();

            // Register MountNotificationService for global voice and limit-warning notifications
            builder.Services.AddSingleton<GreenSwamp.Alpaca.Server.Services.MountNotificationService>();

            var app = builder.Build();

            // Only enable response compression in non-Development environments.
            // Browser Link and Hot Reload browser refresh require uncompressed HTML
            // responses to inject their scripts; Brotli/gzip encoding blocks that.
            if (!app.Environment.IsDevelopment())
            {
                app.UseResponseCompression();
                app.UsePreCompressedStaticFiles();
            }

            // Replace bootstrap logger with the DI-resolved logger so the host's configured
            // log levels (from appsettings.json "Logging" section) take effect from this point.
            Logger = app.Services.GetRequiredService<ILogger<Program>>();
            ASCOM.Alpaca.Logging.AttachLogger(Logger);

            // Initialize settings service for bidirectional sync
            try
            {
                var settingsService = app.Services.GetRequiredService<IVersionedSettingsService>();
                var deviceRegistry = app.Services.GetRequiredService<GreenSwamp.Alpaca.Server.Services.UnifiedDeviceRegistry>();

                // Initialize Monitor settings system in correct order
                GreenSwamp.Alpaca.Shared.Settings.Initialize(settingsService);
                Logger.LogInformation("Settings.Initialize() completed");

                // Force MonitorQueue initialization (creates BlockingCollections and background tasks)
                GreenSwamp.Alpaca.Shared.MonitorQueue.EnsureInitialized();
                Logger.LogInformation("MonitorQueue initialized");

                // Eagerly resolve MonitorDisplayService so it starts capturing entries immediately
                app.Services.GetRequiredService<GreenSwamp.Alpaca.Server.Services.MonitorDisplayService>();
                Logger.LogInformation("MonitorDisplayService started");

                // CRITICAL: Load settings BEFORE Load_Settings() to populate filter checklists
                GreenSwamp.Alpaca.Shared.Settings.Load();
                Logger.LogInformation("Settings.Load() completed");

                // Fire-and-forget environment log — written once at startup, never blocks the server
                GreenSwamp.Alpaca.Shared.EnvironmentLog.EnvironmentHelper.LogToDefaultLocationAsync()
                    .ContinueWith(t =>
                    {
                        if (t.Exception is not null)
                            Logger.LogWarning(t.Exception, "Environment log failed");
                        else
                            Logger.LogInformation("Environment log written to: {Path}", t.Result ?? "(unknown)");
                    }, System.Threading.Tasks.TaskContinuationOptions.ExecuteSynchronously);

                // Fire-and-forget settings backup — zips JSON files from AppData to the log directory at startup
                GreenSwamp.Alpaca.Shared.EnvironmentLog.EnvironmentHelper.BackupSettingsToLogsAsync()
                    .ContinueWith(t =>
                    {
                        if (t.Exception is not null)
                            Logger.LogWarning(t.Exception, "Settings backup failed");
                        else if (t.Result is not null)
                            Logger.LogInformation("Settings backup written to: {Path}", t.Result);
                    }, System.Threading.Tasks.TaskContinuationOptions.ExecuteSynchronously);

                // Populate filter checklists (now that Settings properties have values)
                GreenSwamp.Alpaca.Shared.MonitorLog.Load_Settings();
                Logger.LogInformation("Monitor filters loaded");
                Logger.LogInformation($"Monitor log path: {GreenSwamp.Alpaca.Shared.GsFile.GetLogPath()}");

                // Validate settings at startup and log results
                Logger.LogInformation("Validating settings at startup...");
                var validationResult = settingsService.ValidateDeviceSettings(0);

                if (validationResult.IsValid)
                {
                    if (validationResult.HasWarnings)
                    {
                        Logger.LogWarning($"Settings validation completed with {validationResult.Warnings.Count} warning(s):");
                        foreach (var warning in validationResult.Warnings)
                        {
                            var deviceInfo = warning.DeviceNumber.HasValue ? $" [Device {warning.DeviceNumber}]" : "";
                            Logger.LogWarning($"  {warning.ErrorCode}{deviceInfo}: {warning.Message}");
                        }
                    }
                    else
                    {
                        Logger.LogInformation("Settings validation: All settings are valid");
                    }
                }
                else
                {
                    Logger.LogError($"Settings validation failed with {validationResult.Errors.Count} error(s):");
                    foreach (var error in validationResult.Errors)
                    {
                        var deviceInfo = error.DeviceNumber.HasValue ? $" [Device {error.DeviceNumber}]" : "";
                        Logger.LogError($"  {error.ErrorCode}{deviceInfo}: {error.Message}");
                        Logger.LogError($"    Resolution: {error.Resolution}");
                    }

                    Logger.LogWarning("Invalid devices will be quarantined and not advertised to ASCOM clients");
                    Logger.LogWarning("Visit /settings-health in the web UI to view details and repair settings");
                }

                // Load all devices from per-device settings files (device-nn.settings.json)
                var allDevices = settingsService.GetAllDeviceSettings();

                // Trigger first-run creation of observatory.settings.json if not present
                settingsService.GetObservatorySettings();
                Logger.LogInformation("Observatory settings initialised");
                var enabledDevices = allDevices.Where(d => d.Enabled).ToList();

                if (enabledDevices.Count > 0)
                {
                    Logger.LogInformation($"Found {enabledDevices.Count} enabled device(s) in settings");
                }
                else
                {
                    Logger.LogWarning("No valid enabled devices found in settings");
                    Logger.LogWarning("Application will continue running with no active devices");
                    Logger.LogWarning("Visit /settings-health in the web UI to view and repair configuration errors");
                    // Continue execution without throwing - graceful degradation
                }

                // Get AlpacaDevices array to obtain correct UniqueIds
                var alpacaDevices = settingsService.GetAlpacaDevices();
                var alpacaDeviceMap = alpacaDevices.ToDictionary(d => d.DeviceNumber);

                // Track successful registrations for SkyServer initialization
                var registeredDeviceCount = 0;

                // Register each enabled device
                foreach (var device in enabledDevices)
                {
                    try
                    {
                        // Pass device settings directly to constructor
                        var deviceSettings = new GreenSwamp.Alpaca.MountControl.SkySettings(
                            device,              // Device-specific configuration (all 137 properties)
                            settingsService      // Settings service for persistence
                        );

                        // Get UniqueId from AlpacaDevices array (or generate if missing)
                        string uniqueId;
                        if (alpacaDeviceMap.TryGetValue(device.DeviceNumber, out var alpacaDevice))
                        {
                            uniqueId = alpacaDevice.UniqueId;
                            Logger.LogInformation($"Using UniqueId from AlpacaDevices: {uniqueId}");
                        }
                        else
                        {
                            // Fallback: generate new GUID if AlpacaDevice entry missing
                            uniqueId = Guid.NewGuid().ToString();
                            Logger.LogWarning($"AlpacaDevice entry not found for device {device.DeviceNumber}, generated new UniqueId: {uniqueId}");
                        }

                        deviceRegistry.RegisterDevice(
                            device.DeviceNumber,
                            device.DeviceName,
                            uniqueId,
                            deviceSettings
                        );

                        Logger.LogInformation($"Device {device.DeviceNumber}: {device.DeviceName} (Mount: {device.Mount})");
                        registeredDeviceCount++;
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError($"Failed to register device {device.DeviceNumber}: {device.DeviceName} - {ex.Message}");
                        Logger.LogError($"Skipping device {device.DeviceNumber}, continuing with remaining devices");
                        // Continue with next device instead of throwing - graceful degradation
                    }
                }

                Logger.LogInformation($"Device registry initialization complete - {registeredDeviceCount} device(s) registered successfully");

                // Per-device initialization -- wire settings listeners for each registered device
                if (registeredDeviceCount > 0)
                {
                    foreach (var kvp in MountRegistry.GetAllInstances())
                    {
                        kvp.Value.InitializeSettings();
                        Logger.LogInformation($"Device {kvp.Key}: settings initialized");
                    }
                    Logger.LogInformation($"SkyServer initialization complete - {registeredDeviceCount} device(s) initialized");
                }
                else
                {
                    Logger.LogWarning("SkyServer initialization skipped - no devices registered");
                    Logger.LogWarning("Mount control functionality unavailable until devices are configured");
                }
            }
            catch (Exception ex)
            {
                // Distinguish between settings validation errors (allow continuation) and critical failures (must crash)
                Logger.LogError($"Error during initialization: {ex.Message}");
                Logger.LogError($"Exception type: {ex.GetType().Name}");

                // Check if this is a settings-related error that should allow graceful degradation
                bool isSettingsError = ex.Message.Contains("settings") ||
                                       ex.Message.Contains("validation") ||
                                       ex.Message.Contains("device") ||
                                       ex.Message.Contains("configuration");

                if (isSettingsError)
                {
                    Logger.LogWarning("Settings-related error detected - application will continue with degraded functionality");
                    Logger.LogWarning("Visit /settings-health in the web UI to diagnose and repair settings");
                    // Allow app to continue - graceful degradation
                }
                else
                {
                    Logger.LogError("Critical initialization failure - cannot continue");
                    throw; // Re-throw only for non-settings critical failures (DI, filesystem, etc.)
                }
            }

            // Configure the HTTP request pipeline.
            if (!app.Environment.IsDevelopment())
            {
                app.UseExceptionHandler("/Error");
            }

            // Start Swagger on the Swagger endpoints if enabled.
            ASCOM.Alpaca.Razor.StartupHelpers.ConfigureSwagger(app);

            // Configure Discovery
            ASCOM.Alpaca.Razor.StartupHelpers.ConfigureDiscovery(app);

            // Allow authentication, either Cookie or Basic HTTP Auth
            ASCOM.Alpaca.Razor.StartupHelpers.ConfigureAuthentication(app);

            app.UseStaticFiles();

            app.UseRouting();

            app.MapBlazorHub();

            // Chart data streaming endpoint
            app.MapHub<ChartHub>("/charthub");

            app.MapControllers();

            app.MapFallbackToPage("/_Host");

            // Re-read server config post-build so any first-run seed is reflected
            var startupConfig = app.Services.GetRequiredService<IVersionedSettingsService>().GetServerConfig();

            // Eagerly resolve ChartDataService so its MonitorQueue subscription is active from startup,
            // not lazily on first hub connection. Safe: it is a singleton registered above.
            _ = app.Services.GetRequiredService<ChartDataService>();
            Logger.LogInformation("ChartDataService initialized — MonitorQueue subscriptions active.");
            if (startupConfig.AutoStartBrowser && canLaunchBrowser)
            {
                try
                {
                    StartBrowser(startupConfig.ServerPort);
                }
                catch (Exception ex)
                {
                    Logger.LogWarning(ex.Message);
                }
            }

            #endregion Finish Building and Start server

            Lifetime = app.Lifetime;

            // Replace the tray icon creation block (lines ~585-623)
            if (OperatingSystem.IsWindows() && !isService)
            {
                try
                {
                    var extracted = !string.IsNullOrWhiteSpace(Environment.ProcessPath)
                        ? Icon.ExtractAssociatedIcon(Environment.ProcessPath!)
                        : null;

                    _trayIconImage = extracted is not null
                        ? (Icon)extracted.Clone()
                        : (Icon)SystemIcons.Application.Clone();

                    extracted?.Dispose();

                    _trayIcon = new TrayIconWithContextMenu
                    {
                        Icon = _trayIconImage.Handle,
                        ToolTip = "Green Swamp Alpaca Server",
                        ContextMenu = new PopupMenu
                        {
                            Items =
                        {
                            new PopupMenuItem("Show Browser UI", (_, _) => StartBrowser(startupConfig.ServerPort)),
                            new PopupMenuSeparator(),
                            new PopupMenuItem("Show Console", (_, _) => ShowConsole(ConsoleDisplayOption.StartNormally)),
                            new PopupMenuItem("Hide Console", (_, _) => ShowConsole(ConsoleDisplayOption.NoConsole)),
                            new PopupMenuSeparator(),
                            new PopupMenuItem("Exit", (_, _) =>
                            {
                                _trayIcon?.Dispose();
                                _trayIconImage?.Dispose();
                                Lifetime?.StopApplication();
                            }),
                        }
                        }
                    };

                    _trayIcon.Create();
                    Logger?.LogInformation("Tray icon created.");
                }
                catch (Exception ex)
                {
                    Logger?.LogWarning(ex, "Failed to initialize tray icon.");
                }
            }

            //Put code here that should run at shutdown
            Lifetime.ApplicationStopping.Register(() =>
            {
                Logger.LogInformation($"{ServerName} Stopping");
                _trayIcon?.Dispose();
                _trayIconImage?.Dispose();
            });

            //Start the Alpaca Server
            app.Run();
        }

        /// <summary>
        /// Starts the system default handler (normally a browser) for localhost and the current port.
        /// </summary>
        /// <param name="port"></param>
        internal static void StartBrowser(int port)
        {
            string url = $"http://localhost:{port}/";

            if (OperatingSystem.IsWindows())
            {
                ProcessStartInfo psi = new()
                {
                    FileName = url,
                    UseShellExecute = true
                };
                Process.Start(psi);
            }
            else if (OperatingSystem.IsLinux())
            {
                Process.Start("xdg-open", url);
            }
            else if (OperatingSystem.IsMacOS())
            {
                Process.Start("open", url);
            }
            else
            {
                Logger?.LogWarning(
                    "Unknown OS platform, cannot start browser. Framework: {FrameworkDescription}, Process architecture: {ProcessArchitecture}, OS architecture: {OSArchitecture}, Runtime identifier: {RuntimeIdentifier}",
                    RuntimeInformation.FrameworkDescription,
                    RuntimeInformation.ProcessArchitecture,
                    RuntimeInformation.OSArchitecture,
                    RuntimeInformation.RuntimeIdentifier);
            }
        }

        private static void ShowConsole(ConsoleDisplayOption newConsoleState)
        {
            if (!OperatingSystem.IsWindows())
            {
                return;
            }

            var hWnd = GetConsoleWindow();
            if (hWnd == IntPtr.Zero)
            {
                return;
            }

            var currentState = GetConsoleWindowState(hWnd);
            if (currentState == newConsoleState)
            {
                return;
            }

            switch (newConsoleState)
            {
                case ConsoleDisplayOption.StartNormally:
                    ShowWindow(hWnd, SW_RESTORE);
                    break;
                case ConsoleDisplayOption.StartMinimized:
                    ShowWindow(hWnd, SW_MINIMIZE);
                    break;
                case ConsoleDisplayOption.NoConsole:
                    ShowWindow(hWnd, SW_HIDE);
                    break;
            }
        }

        private static ConsoleDisplayOption GetConsoleWindowState(IntPtr hWnd)
        {
            if (!IsWindowVisible(hWnd))
            {
                return ConsoleDisplayOption.NoConsole;
            }

            if (IsIconic(hWnd))
            {
                return ConsoleDisplayOption.StartMinimized;
            }

            return ConsoleDisplayOption.StartNormally;
        }
    }
}