using ASCOM.Alpaca;
using GreenSwamp.Alpaca.MountControl;
using Microsoft.Extensions.Logging;
using GreenSwamp.Alpaca.Server.Models;
using GreenSwamp.Alpaca.Settings.Extensions;
using GreenSwamp.Alpaca.Settings.Models;
using GreenSwamp.Alpaca.Settings.Services;
using MudBlazor.Services;
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Runtime.InteropServices;

#nullable enable
namespace GreenSwamp.Alpaca.Server
{
    public class Program
    {
        //Driver name
        internal const string DriverID = "GreenSwamp.Alpaca";

        //Change this to a unique value
        //You should offer a way for the end user to customize this via the command line so it can be changed in the case of a collision.
        //This supports --urls=http://*:port by default.
        internal const int DefaultPort = 31416;

        //Driver information
        internal const string Manufacturer = "Green Swamp Software";

        internal const string ServerName = "Green Swamp Alpaca Server";
        internal const string ServerVersion = "1.0";

        internal static ILogger? Logger;

        internal static IHostApplicationLifetime? Lifetime;

        /// <summary>Bootstrap server configuration read before the DI container is built.</summary>
        internal static ServerConfig BootstrapConfig { get; private set; } = new ServerConfig();

        public static async Task Main(string[] args)
        {
            // Bootstrap logger — active before the DI container is built.
            // Log level is read from the "Logging" section in appsettings.json after the host is built.
            // To get verbose output set "Logging:LogLevel:Default" to "Debug" in appsettings.json.
            using var bootstrapLoggerFactory = LoggerFactory.Create(b =>
                b.AddSimpleConsole(o => { o.TimestampFormat = "yyyy-MM-dd HH:mm:ss "; o.SingleLine = true; })
                 .SetMinimumLevel(LogLevel.Debug));
            Logger = bootstrapLoggerFactory.CreateLogger<Program>();

            // Bootstrap: read ServerConfig from disk before the DI container exists.
            // Used for port-collision detection and --urls binding below.
            var bootstrapConfigPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "GreenSwampAlpaca",
                ServerConfig.GetVersion(),
                "appsettings.server.user.json");

            // LoadBootstrap returns new ServerConfig() when the file is absent (first run).
            // ServerConfig defaults now match appsettings.json, so --urls binding is always correct
            // on first run without needing to read appsettings.json before the host is built.
            // VersionedSettingsService seeds the file on its first GetServerConfig() call.
            BootstrapConfig = ServerConfig.LoadBootstrap(bootstrapConfigPath);
            Logger.LogInformation($"Bootstrap server config loaded from: {bootstrapConfigPath}");

            //This region contains startup and logging features, most of the time you shouldn't need to customize this
            //You can add custom Command Line arguments here
            #region Startup and Logging

            Logger.LogInformation($"{ServerName} version {ServerVersion}");
            Logger.LogInformation($"Running on: {RuntimeInformation.OSDescription}.");

            //If already running start browser
            // When running as a Windows service, SCM guarantees a single instance and there is
            // no desktop session to launch a browser into, so skip duplicate detection entirely.
            var isWindowsService = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                && Microsoft.Extensions.Hosting.WindowsServices.WindowsServiceHelpers.IsWindowsService();

            try
            {
                if (!isWindowsService && RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    //Already running, start the browser, detects based on port in use
                    if (IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpConnections().Any(con => con.LocalEndPoint.Port == BootstrapConfig.ServerPort && (con.State == TcpState.Listen || con.State == TcpState.Established)))
                    {
                        Logger.LogInformation("Detected driver port already open, starting web browser on IP and Port. If this fails something else is using the port");
                        StartBrowser(BootstrapConfig.ServerPort);
                        return;
                    }
                }
                else if (!isWindowsService)
                {
                    // Environment.ProcessPath works correctly for single-file published executables;
                    // Assembly.Location returns "" in that scenario, causing false-positive detection.
                    var processName = Path.GetFileNameWithoutExtension(
                        Environment.ProcessPath ?? Assembly.GetEntryAssembly()?.GetName().Name ?? string.Empty);
                    if (!string.IsNullOrWhiteSpace(processName) &&
                        Process.GetProcessesByName(processName).Length > 1)
                    {
                        Logger.LogInformation("Detected driver already running, starting web browser on IP and Port");
                        StartBrowser(BootstrapConfig.ServerPort);
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex.Message);
                return;
            }

            //Reset all stored settings if requested
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

            //Turn off Authentication. Once off the user can change the password and re-enable authentication
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

            // When running as a Windows SCM service, use the Windows service lifetime so the host
            // responds correctly to Start/Stop/Pause commands from the service control manager.
            // UseWindowsService() is a no-op when the process is NOT started by SCM (interactive run)
            // and must only be called on Windows — calling it on Linux throws PlatformNotSupportedException.
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                builder.Host.UseWindowsService(options =>
                    options.ServiceName = "GreenSwampAlpacaServer");
            }

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
            Logger.LogInformation("SkySettings registered in DI container");
            Logger.LogInformation("Settings services registered: VersionedSettings, Template");
            #endregion Startup and Logging

            //Load the configuration — pass BootstrapConfig so AlpacaConfiguration has no ASCOM XMLProfile dependency
            DeviceManager.LoadConfiguration(new AlpacaConfiguration(BootstrapConfig));

            // Device registration moved to after app.Build() to use UnifiedDeviceRegistry
            // This ensures proper synchronization between DeviceManager and MountRegistry
            // Reserved slots (0=Simulator, 1=Physical Mount) are initialized first

            #region Finish Building and Start server

            // Add services to the container.
            builder.Services.AddRazorPages();
            builder.Services.AddServerSideBlazor();
            builder.Services.AddMudServices();
            builder.Services.AddHttpContextAccessor();

            //Load any xml comments for this program, this helps with swagger
            var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
            var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);

            //Add Swagger for the APIs
            ASCOM.Alpaca.Razor.StartupHelpers.ConfigureSwagger(builder.Services, xmlPath);
            //Set default behaviors for Alpaca APIs
            ASCOM.Alpaca.Razor.StartupHelpers.ConfigureAlpacaAPIBehavoir(builder.Services);
            //Use Authentication
            ASCOM.Alpaca.Razor.StartupHelpers.ConfigureAuthentication(builder.Services);
            //Add User Service
            builder.Services.AddScoped<IUserService, Data.UserService>();
            
            // Register TelescopeStateService for real-time state updates
            builder.Services.AddSingleton<GreenSwamp.Alpaca.Server.Services.TelescopeStateService>();
            Logger.LogInformation("TelescopeStateService registered for real-time state updates");

            // Register UnifiedDeviceRegistry as singleton for DI injection
            builder.Services.AddSingleton<GreenSwamp.Alpaca.Server.Services.UnifiedDeviceRegistry>();

            // Register MonitorDisplayService for live monitor-record display
            builder.Services.AddSingleton<GreenSwamp.Alpaca.Server.Services.MonitorDisplayService>();

            var app = builder.Build();

            // Replace bootstrap logger with the DI-resolved logger so the host's configured
            // log levels (from appsettings.json "Logging" section) take effect from this point.
            Logger = app.Services.GetRequiredService<ILogger<Program>>();
            ASCOM.Alpaca.Logging.AttachLogger(Logger);

            // Initialize settings bridges for bidirectional sync
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

            //Start Swagger on the Swagger endpoints if enabled.
            ASCOM.Alpaca.Razor.StartupHelpers.ConfigureSwagger(app);

            //Configure Discovery
            ASCOM.Alpaca.Razor.StartupHelpers.ConfigureDiscovery(app);

            //Allow authentication, either Cookie or Basic HTTP Auth
            ASCOM.Alpaca.Razor.StartupHelpers.ConfigureAuthentication(app);

            app.UseStaticFiles();

            app.UseRouting();

            app.MapBlazorHub();

            app.MapControllers();

            app.MapFallbackToPage("/_Host");

            // Re-read server config post-build so any first-run seed is reflected
            var startupConfig = app.Services.GetRequiredService<IVersionedSettingsService>().GetServerConfig();
            if (startupConfig.AutoStartBrowser && RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && !isWindowsService)
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

            //Put code here that should run at shutdown
            Lifetime.ApplicationStopping.Register(() =>
            {
                Logger.LogInformation($"{ServerName} Stopping");
            });

            //Start the Alpaca Server
            app.Run();
        }

        /// <summary>
        /// Starts the system default handler (normally a browser) for local host and the current port.
        /// Only called on Windows; on Linux/Raspberry Pi the UI is accessed via a network browser.
        /// </summary>
        /// <param name="port"></param>
        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
        internal static void StartBrowser(int port)
        {
            ProcessStartInfo psi = new()
            {
                FileName = $"http://localhost:{port}",
                UseShellExecute = true
            };
            Process.Start(psi);
        }
    }
}