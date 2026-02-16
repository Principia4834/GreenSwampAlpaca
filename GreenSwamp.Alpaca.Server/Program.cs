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

            // Configure Server Settings from configuration with profile support
            builder.Services.AddSingleton(sp =>
            {
                // Phase 4.2: Create instance with profile loading support
                var settingsService = sp.GetRequiredService<IVersionedSettingsService>();
                var profileLoader = sp.GetService<IProfileLoaderService>(); // Optional - may be null
                return new GreenSwamp.Alpaca.MountControl.SkySettingsInstance(settingsService, profileLoader);
            });
            Logger.LogInformation("✅ Phase 4.2: SkySettingsInstance registered in DI container");
            Logger.LogInformation("✅ Settings services registered: VersionedSettings, Template, Profile");
            #endregion Startup and Logging

            //ToDo you can add devices here

            //Attach the logger
            Logging.AttachLogger(Logger);

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
            Logger.LogInformation("? TelescopeStateService registered for real-time state updates");

            // Register DeviceManagementService with HttpClient for device manager UI (Phase 4.11)
            // Uses typed client pattern - HttpClient is automatically injected and lifecycle-managed
            builder.Services.AddHttpClient<GreenSwamp.Alpaca.Server.Services.DeviceManagementService>(client =>
            {
                // Configure base address to call the same server (for Blazor Server self-calls)
                // The service will make HTTP calls to /setup endpoints on the same server
                client.BaseAddress = new Uri($"http://localhost:{ServerSettings.ServerPort}");
                client.Timeout = TimeSpan.FromSeconds(30);
            });
            Logger.LogInformation("? DeviceManagementService registered with HttpClient for device manager UI");

            var app = builder.Build();

            // Phase 1: Test new settings system initialization
            #if DEBUG
            try
            {
                var settingsService = app.Services.GetRequiredService<IVersionedSettingsService>();
                var testSettings = settingsService.GetSettings();
                Logger.LogInformation("? Phase 1: New settings system initialized successfully");
                Logger.LogInformation($"  Settings Version: {settingsService.CurrentVersion}");
                Logger.LogInformation($"  Mount Type: {testSettings.Mount}");
                Logger.LogInformation($"  Serial Port: {testSettings.Port}");
                Logger.LogInformation($"  Settings Path: {settingsService.UserSettingsPath}");
                Logger.LogInformation($"  Available Versions: {string.Join(", ", settingsService.AvailableVersions)}");
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
                Logger.LogInformation("✅ Settings.Initialize() completed");

                // Force MonitorQueue initialization (creates BlockingCollections and background tasks)
                GreenSwamp.Alpaca.Shared.MonitorQueue.EnsureInitialized();
                Logger.LogInformation("✅ MonitorQueue initialized");

                // CRITICAL: Load settings BEFORE Load_Settings() to populate filter checklists
                GreenSwamp.Alpaca.Shared.Settings.Load();
                Logger.LogInformation("✅ Settings.Load() completed");

                // Populate filter checklists (now that Settings properties have values)
                GreenSwamp.Alpaca.Shared.MonitorLog.Load_Settings();
                Logger.LogInformation("✅ Monitor filters loaded");
                Logger.LogInformation($"📁 Monitor log path: {GreenSwamp.Alpaca.Shared.GsFile.GetLogPath()}");

                // Phase 4.2: Create instance with profile loading support
                var profileLoader = app.Services.GetService<IProfileLoaderService>();
                var settingsInstance = new GreenSwamp.Alpaca.MountControl.SkySettingsInstance(settingsService, profileLoader);

                if (profileLoader != null)
                {
                    Logger.LogInformation("✅ SkySettingsInstance created with profile loading support");
                }
                else
                {
                    Logger.LogInformation("✅ SkySettingsInstance created (JSON-only)");
                }

                // Initialize SkyServer with instance settings
                GreenSwamp.Alpaca.MountControl.SkyServer.Initialize(settingsInstance);
                Logger.LogInformation("✅ SkyServer initialized");

                // Initialize static facade for backward compatibility
                GreenSwamp.Alpaca.MountControl.SkySettings.Initialize(settingsInstance);
                Logger.LogInformation("✅ Static SkySettings facade initialized");

                // Initialize SkySystem with instance settings
                GreenSwamp.Alpaca.MountControl.SkySystem.Initialize(settingsInstance);
                Logger.LogInformation("✅ SkySystem initialized");

                Logger.LogInformation("✅ Instance-based settings active");

                // Phase 4.11: Initialize device registry with reserved slots
                // Load device configurations from appsettings.json
                var deviceConfigs = builder.Configuration
                    .GetSection("AlpacaDevices")
                    .Get<List<AlpacaDeviceConfig>>();

                // Phase 4.9: Get profile service for loading per-device profiles
                var profileService = app.Services.GetRequiredService<ISettingsProfileService>();

                if (deviceConfigs == null || !deviceConfigs.Any())
                {
                    Logger.LogInformation("No AlpacaDevices configured in appsettings.json");
                    Logger.LogInformation("Creating default reserved slots (0=Simulator, 1=SkyWatcher)");

                    // Phase 4.9: Load profiles for reserved slots
                    GreenSwamp.Alpaca.Settings.Models.SkySettings simulatorProfileSettings;
                    GreenSwamp.Alpaca.Settings.Models.SkySettings physicalMountProfileSettings;

                    try
                    {
                        simulatorProfileSettings = await profileService.LoadProfileByNameAsync("simulator-altaz");
                        Logger.LogInformation("✅ Phase 4.9: Loaded profile for slot 0: simulator-altaz");
                    }
                    catch (FileNotFoundException)
                    {
                        Logger.LogInformation("Profile 'simulator-altaz' not found, using default settings");
                        simulatorProfileSettings = settingsService.GetSettings();
                    }

                    try
                    {
                        physicalMountProfileSettings = await profileService.LoadProfileByNameAsync("eq6-default");
                        Logger.LogInformation("✅ Phase 4.9: Loaded profile for slot 1: eq6-default");
                    }
                    catch (FileNotFoundException)
                    {
                        Logger.LogInformation("Profile 'eq6-default' not found, using default settings");
                        physicalMountProfileSettings = settingsService.GetSettings();
                    }

                    // Create instances with loaded profile settings
                    var simulatorSettings = new GreenSwamp.Alpaca.MountControl.SkySettingsInstance(settingsService, profileLoader);
                    simulatorSettings.ApplySettings(simulatorProfileSettings);

                    var physicalMountSettings = new GreenSwamp.Alpaca.MountControl.SkySettingsInstance(settingsService, profileLoader);
                    physicalMountSettings.ApplySettings(physicalMountProfileSettings);

                    GreenSwamp.Alpaca.Server.Services.UnifiedDeviceRegistry.InitializeReservedSlots(
                        simulatorSettings,
                        "Simulator (AltAz)",
                        "sim-altaz-default-guid",
                        new TelescopeDriver.Telescope(0),
                        physicalMountSettings,
                        "SkyWatcher Mount",
                        "skywatcher-default-guid",
                        new TelescopeDriver.Telescope(1)
                    );

                    Logger.LogInformation("✅ Reserved slots initialized (0=Simulator, 1=SkyWatcher) with profiles");
                }
                else
                {
                    Logger.LogInformation($"Loading {deviceConfigs.Count} device(s) from configuration");

                    // Ensure we have exactly 2 reserved slots (0 and 1) in configuration
                    var slot0 = deviceConfigs.FirstOrDefault(d => d.DeviceNumber == 0);
                    var slot1 = deviceConfigs.FirstOrDefault(d => d.DeviceNumber == 1);

                    if (slot0 == null || slot1 == null)
                    {
                        throw new InvalidOperationException(
                            "Configuration must include reserved slots 0 and 1. Please update appsettings.json AlpacaDevices section.");
                    }

                    // Phase 4.9: Load profiles for configured reserved slots
                    GreenSwamp.Alpaca.Settings.Models.SkySettings simulatorProfileSettings;
                    GreenSwamp.Alpaca.Settings.Models.SkySettings physicalMountProfileSettings;

                    try
                    {
                        var profileName0 = !string.IsNullOrWhiteSpace(slot0.ProfileName) ? slot0.ProfileName : "simulator-altaz";
                        simulatorProfileSettings = await profileService.LoadProfileByNameAsync(profileName0);
                        Logger.LogInformation($"✅ Phase 4.9: Loaded profile for slot 0: {profileName0}");
                    }
                    catch (FileNotFoundException)
                    {
                        Logger.LogInformation($"Profile '{slot0.ProfileName}' not found, using default settings");
                        simulatorProfileSettings = settingsService.GetSettings();
                    }

                    try
                    {
                        var profileName1 = !string.IsNullOrWhiteSpace(slot1.ProfileName) ? slot1.ProfileName : "eq6-default";
                        physicalMountProfileSettings = await profileService.LoadProfileByNameAsync(profileName1);
                        Logger.LogInformation($"✅ Phase 4.9: Loaded profile for slot 1: {profileName1}");
                    }
                    catch (FileNotFoundException)
                    {
                        Logger.LogInformation($"Profile '{slot1.ProfileName}' not found, using default settings");
                        physicalMountProfileSettings = settingsService.GetSettings();
                    }

                    // Initialize reserved slots with loaded profiles
                    var simulatorSettings = new GreenSwamp.Alpaca.MountControl.SkySettingsInstance(settingsService, profileLoader);
                    simulatorSettings.ApplySettings(simulatorProfileSettings);

                    var physicalMountSettings = new GreenSwamp.Alpaca.MountControl.SkySettingsInstance(settingsService, profileLoader);
                    physicalMountSettings.ApplySettings(physicalMountProfileSettings);

                    GreenSwamp.Alpaca.Server.Services.UnifiedDeviceRegistry.InitializeReservedSlots(
                        simulatorSettings,
                        slot0.DeviceName,
                        slot0.UniqueId,
                        new TelescopeDriver.Telescope(0),
                        physicalMountSettings,
                        slot1.DeviceName,
                        slot1.UniqueId,
                        new TelescopeDriver.Telescope(1)
                    );

                    Logger.LogInformation($"✅ Reserved slot 0: {slot0.DeviceName} (profile: {slot0.ProfileName})");
                    Logger.LogInformation($"✅ Reserved slot 1: {slot1.DeviceName} (profile: {slot1.ProfileName})");

                    // Load dynamic devices (slots 2+)
                    var dynamicDevices = deviceConfigs.Where(d => d.DeviceNumber >= 2).ToList();

                    if (dynamicDevices.Any())
                    {
                        Logger.LogInformation($"Loading {dynamicDevices.Count} dynamic device(s) (slots 2+)");

                        foreach (var config in dynamicDevices)
                        {
                            try
                            {
                                // Phase 4.9: Load profile for dynamic device if specified
                                GreenSwamp.Alpaca.Settings.Models.SkySettings? deviceProfileSettings = null;

                                if (!string.IsNullOrWhiteSpace(config.ProfileName))
                                {
                                    try
                                    {
                                        deviceProfileSettings = await profileService.LoadProfileByNameAsync(config.ProfileName);
                                        Logger.LogInformation($"✅ Phase 4.9: Loaded profile '{config.ProfileName}' for device {config.DeviceNumber}");
                                    }
                                    catch (FileNotFoundException)
                                    {
                                        Logger.LogInformation($"Profile '{config.ProfileName}' not found for device {config.DeviceNumber}, using default settings");
                                    }
                                }

                                var deviceSettings = new GreenSwamp.Alpaca.MountControl.SkySettingsInstance(settingsService, profileLoader);

                                // Apply profile settings if loaded
                                if (deviceProfileSettings != null)
                                {
                                    deviceSettings.ApplySettings(deviceProfileSettings);
                                }

                                GreenSwamp.Alpaca.Server.Services.UnifiedDeviceRegistry.RegisterDevice(
                                    config.DeviceNumber,
                                    config.DeviceName,
                                    config.UniqueId,
                                    deviceSettings,
                                    new TelescopeDriver.Telescope(config.DeviceNumber)
                                );

                                Logger.LogInformation($"✅ Device {config.DeviceNumber}: {config.DeviceName} (profile: {config.ProfileName ?? "default"})");
                            }
                            catch (Exception ex)
                            {
                                Logger.LogError($"❌ Failed to register device {config.DeviceNumber}: {config.DeviceName} - {ex.Message}");
                            }
                        }
                    }
                }

                Logger.LogInformation("✅ Device registry initialization complete");
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to initialize settings: {ex.Message}");
                throw; // Re-throw to prevent app startup with broken settings
            }

            // Migrate user settings if needed
            try
            {
                var settingsService = app.Services.GetRequiredService<IVersionedSettingsService>();
                await settingsService.MigrateFromPreviousVersionAsync();
            }
            catch (Exception ex)
            {
                app.Logger.LogWarning(ex, "Could not migrate settings");
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