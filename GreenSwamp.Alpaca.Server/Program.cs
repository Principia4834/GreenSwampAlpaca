using ASCOM.Alpaca;
using ASCOM.Common;
using GreenSwamp.Alpaca.MountControl;
using GreenSwamp.Alpaca.Server.Models;
using GreenSwamp.Alpaca.Settings.Extensions;
using GreenSwamp.Alpaca.Settings.Models;
using GreenSwamp.Alpaca.Settings.Services;
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;

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
        internal const int DefaultPort = 31426;

        //Driver information
        internal const string Manufacturer = "Green Swamp Software";

        internal const string ServerName = "Green Swamp Alpaca Server";
        internal const string ServerVersion = "1.0";

        internal static ASCOM.Common.Interfaces.ILogger? Logger;

        internal static IHostApplicationLifetime? Lifetime;

        public static async Task Main(string[] args)
        {
            //First fill in information for your driver in the Alpaca Configuration Class. Some of these you may want to store in a user changeable settings file.
            //Then fill in the ToDos in this file. Each is marked with a //ToDo
            //You shouldn't need to do anything in the Startup and Logging or Finish Building and Start Server regions

            //For Debug ConsoleLogger is very nice. For production TraceLogger is recommended.
            Logger = new ASCOM.Tools.ConsoleLogger();
            // Logger = new ASCOM.Tools.TraceLogger("txt", true);

            //This region contains startup and logging features, most of the time you shouldn't need to customize this
            //You can add custom Command Line arguments here
            #region Startup and Logging

            Logger.LogInformation($"{ServerName} version {ServerVersion}");
            Logger.LogInformation($"Running on: {RuntimeInformation.OSDescription}.");

            //If already running start browser
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    //Already running, start the browser, detects based on port in use
                    if (IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpConnections().Any(con => con.LocalEndPoint.Port == ServerSettings.ServerPort && (con.State == TcpState.Listen || con.State == TcpState.Established)))
                    {
                        Logger.LogInformation("Detected driver port already open, starting web browser on IP and Port. If this fails something else is using the port");
                        StartBrowser(ServerSettings.ServerPort);
                        return;
                    }
                }
                else
                {
                    Assembly? entryAssembly = Assembly.GetEntryAssembly();
                    if (entryAssembly != null)
                    {
                        if(Process.GetProcessesByName(entryAssembly.Location).Length > 1)
                        {
                            Logger.LogInformation("Detected driver already running, starting web browser on IP and Port");
                            StartBrowser(ServerSettings.ServerPort);
                            return;
                        }
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
                ServerSettings.Reset();

                //If you have any device settings you should reset them as well or add a specific reset command.

                return;
            }

            //Turn off Authentication. Once off the user can change the password and re-enable authentication
            if (args?.Any(str => str.Contains("--reset-auth")) ?? false)
            {
                Logger.LogInformation("Turning off Authentication to allow password reset.");
                ServerSettings.UseAuth = false;
                Logger.LogInformation("Authentication off, you can change the password and then re-enable Authentication.");
            }

            if (args?.Any(str => str.Contains("--local-address")) ?? false)
            {
                Console.WriteLine($"http://localhost:{ServerSettings.ServerPort}");
            }

            if (!args?.Any(str => str.Contains("--urls")) ?? true)
            {
                args ??= [];

                Logger.LogInformation("No startup url args detected, binding to saved server settings.");

                var temparray = new string[args.Length + 1];

                args.CopyTo(temparray, 0);

                string startupUrlArg = "--urls=http://";

                //If set to allow remote access bind to all local ips, otherwise bind only to localhost
                if (ServerSettings.AllowRemoteAccess)
                {
                    startupUrlArg += "*";
                }
                else
                {
                    startupUrlArg += "localhost";
                }

                startupUrlArg += ":" + ServerSettings.ServerPort;

                Logger.LogInformation("Startup URL args: " + startupUrlArg);

                temparray[args.Length] = startupUrlArg;

                args = temparray;
            }

            var builder = WebApplication.CreateBuilder(args ?? []);

            // Load versioned user settings support
            builder.Configuration.AddVersionedUserSettings();
            
            // Register all settings services (VersionedSettings, Template, Profile)
            builder.Services.AddVersionedSettings(builder.Configuration);

            // Configure Server Settings from configuration
            builder.Services.AddSingleton(sp =>
            {
                // Phase 4.2: Create instance with settings service
                var settingsService = sp.GetRequiredService<IVersionedSettingsService>();
                return new GreenSwamp.Alpaca.MountControl.SkySettingsInstance(settingsService);
            });
            Logger.LogInformation("Phase 4.2: SkySettingsInstance registered in DI container");
            Logger.LogInformation("Settings services registered: VersionedSettings, Template");
            #endregion Startup and Logging

            //ToDo you can add devices here

            //Attach the logger
            ASCOM.Alpaca.Logging.AttachLogger(Logger);

            //Load the configuration
            DeviceManager.LoadConfiguration(new AlpacaConfiguration());

            // Phase 4.11: Device registration moved to after app.Build() to use UnifiedDeviceRegistry
            // This ensures proper synchronization between DeviceManager and MountInstanceRegistry
            // Reserved slots (0=Simulator, 1=Physical Mount) are initialized first

            #region Finish Building and Start server

            // Add services to the container.
            builder.Services.AddRazorPages();
            builder.Services.AddServerSideBlazor();

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

            // Register DeviceManagementService with HttpClient for device manager UI (Phase 4.11)
            // Uses typed client pattern - HttpClient is automatically injected and lifecycle-managed
            builder.Services.AddHttpClient<GreenSwamp.Alpaca.Server.Services.DeviceManagementService>(client =>
            {
                // Configure base address to call the same server (for Blazor Server self-calls)
                // The service will make HTTP calls to /setup endpoints on the same server
                client.BaseAddress = new Uri($"http://localhost:{ServerSettings.ServerPort}");
                client.Timeout = TimeSpan.FromSeconds(30);
            });
            Logger.LogInformation("DeviceManagementService registered with HttpClient for device manager UI");

            var app = builder.Build();

            // Phase 1: Test new settings system initialization
            #if DEBUG
            try
            {
                var settingsService = app.Services.GetRequiredService<IVersionedSettingsService>();
                var testSettings = settingsService.GetDeviceSettings(0);
                Logger.LogInformation("Phase 1: New settings system initialized successfully");
                Logger.LogInformation($"  Settings Version: {settingsService.CurrentVersion}");
                Logger.LogInformation($"  Mount Type: {testSettings?.Mount}");
                Logger.LogInformation($"  Serial Port: {testSettings?.Port}");
                Logger.LogInformation($"  Settings Path: {settingsService.UserSettingsPath}");
                Logger.LogInformation($"  Alpaca Path: {settingsService.AlpacaSettingsPath}");
            }
            catch (Exception ex)
            {
                Logger.LogInformation($"Phase 1 settings check: {ex.Message}");
            }
#endif

            // Phase 2: Initialize settings bridges for bidirectional sync
            try
            {
                var settingsService = app.Services.GetRequiredService<IVersionedSettingsService>();

                // Initialize Monitor settings system in correct order
                GreenSwamp.Alpaca.Shared.Settings.Initialize(settingsService);
                Logger.LogInformation("Settings.Initialize() completed");

                // Force MonitorQueue initialization (creates BlockingCollections and background tasks)
                GreenSwamp.Alpaca.Shared.MonitorQueue.EnsureInitialized();
                Logger.LogInformation("MonitorQueue initialized");

                // CRITICAL: Load settings BEFORE Load_Settings() to populate filter checklists
                GreenSwamp.Alpaca.Shared.Settings.Load();
                Logger.LogInformation("Settings.Load() completed");

                // Populate filter checklists (now that Settings properties have values)
                GreenSwamp.Alpaca.Shared.MonitorLog.Load_Settings();
                Logger.LogInformation("Monitor filters loaded");
                Logger.LogInformation($"Monitor log path: {GreenSwamp.Alpaca.Shared.GsFile.GetLogPath()}");

                // Step 9: Validate settings at startup and log results
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

                // Trigger first-run creation of observatory.settings.json if not present (Behaviour B4)
                settingsService.GetObservatorySettings();
                Logger.LogInformation("Observatory settings initialised");
                var enabledDevices = allDevices.Where(d => d.Enabled).ToList();

                if (!enabledDevices.Any())
                {
                    Logger.LogWarning("No valid enabled devices found in settings");
                    Logger.LogWarning("Application will continue running with no active devices");
                    Logger.LogWarning("Visit /settings-health in the web UI to view and repair configuration errors");
                    // Continue execution without throwing - graceful degradation
                }
                else
                {
                    Logger.LogInformation($"Found {enabledDevices.Count} enabled device(s) in settings");
                }

                // Get AlpacaDevices array to obtain correct UniqueIds
                var alpacaDevices = settingsService.GetAlpacaDevices();
                var alpacaDeviceMap = alpacaDevices.ToDictionary(d => d.DeviceNumber);

                // Track successful registrations for SkyServer initialization
                int registeredDeviceCount = 0;

                // Register each enabled device
                foreach (var device in enabledDevices)
                {
                    try
                    {
                        // Phase 3 baseline (v1.0.0+): Pass device settings directly to constructor
                        var deviceSettings = new GreenSwamp.Alpaca.MountControl.SkySettingsInstance(
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

                        GreenSwamp.Alpaca.Server.Services.UnifiedDeviceRegistry.RegisterDevice(
                            device.DeviceNumber,
                            device.DeviceName,
                            uniqueId,
                            deviceSettings,
                            new TelescopeDriver.Telescope(device.DeviceNumber)
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

                // Initialize SkyServer only if at least one device was registered
                if (registeredDeviceCount > 0)
                {
                    GreenSwamp.Alpaca.MountControl.SkyServer.Initialize();
                    Logger.LogInformation("SkyServer initialized (using registered slot 0 settings)");
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

            if (ServerSettings.AutoStartBrowser)
            {
                try
                {
                    StartBrowser(ServerSettings.ServerPort);
                }
                catch (Exception ex)
                {
                    Logger.LogWarning(ex.Message);
                }
            }

            #endregion Finish Building and Start server

            Lifetime = app.Lifetime;

            //ToDo Put code here that should run at shutdown
            Lifetime.ApplicationStopping.Register(() =>
            {
                Logger.LogInformation($"{ServerName} Stopping");
            });

            //Start the Alpaca Server
            app.Run();
        }

        /// <summary>
        /// Starts the system default handler (normally a browser) for local host and the current port.
        /// </summary>
        /// <param name="port"></param>
        internal static void StartBrowser(int port)
        {
            ProcessStartInfo psi = new ProcessStartInfo
            {
                FileName = string.Format("http://localhost:{0}", port),
                UseShellExecute = true
            };
            Process.Start(psi);
        }
    }
}