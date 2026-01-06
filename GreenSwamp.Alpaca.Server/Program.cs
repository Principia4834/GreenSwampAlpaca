using ASCOM.Alpaca;
using ASCOM.Common;
using GreenSwamp.Alpaca.MountControl;
using GreenSwamp.Alpaca.Settings.Extensions;
using GreenSwamp.Alpaca.Settings.Services;
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
            // Register VersionedSettingsService for IVersionedSettingsService
            builder.Services.AddSingleton<IVersionedSettingsService>(sp =>
                new VersionedSettingsService(
                    builder.Configuration,
                    sp.GetService<ILogger<VersionedSettingsService>>()
                )
            );
            // Configure Server Settings from configuration
            builder.Services.AddSingleton(sp =>
            {
                // Phase 4.2: Create instance with default (static) settings
                var settingsService = sp.GetRequiredService<IVersionedSettingsService>();
                return new GreenSwamp.Alpaca.MountControl.SkySettingsInstance(settingsService);
            });
            Logger.LogInformation("✅ Phase 4.2: SkySettingsInstance registered in DI container");
            #endregion Startup and Logging

            //ToDo you can add devices here

            //Attach the logger
            Logging.AttachLogger(Logger);

            //Load the configuration
            DeviceManager.LoadConfiguration(new AlpacaConfiguration());

            //Add telescope device id 0
            //You may want to inject settings and logging here to the Driver Instance.
            //For each device you add you should add or edit an existing settings page in the settings folder and an entry in the Shared NavMenu.
            //There are pages already included for the first device of each device type.
            DeviceManager.LoadTelescope(0, new TelescopeDriver.Telescope(), "Green Swamp Telescope",
                ServerSettings.GetDeviceUniqueId("Telescope", 0));

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

                // Phase 2: Create instance-based settings (no bridge)
                var settingsInstance = new GreenSwamp.Alpaca.MountControl.SkySettingsInstance(settingsService);
                Logger.LogInformation("✅ Phase 2: SkySettingsInstance created with direct JSON persistence");

                // Initialize SkyServer with instance settings
                GreenSwamp.Alpaca.MountControl.SkyServer.Initialize(settingsInstance);
                Logger.LogInformation("✅ SkyServer initialized with instance settings");

                // OPTIONAL: Initialize static facade for backward compatibility
                // (Only if you're keeping static SkySettings temporarily)
                GreenSwamp.Alpaca.MountControl.SkySettings.Initialize(settingsInstance);
                Logger.LogInformation("✅ Static SkySettings facade initialized (temporary backward compatibility)");

                // Initialize SkySystem with instance settings (not static!)
                GreenSwamp.Alpaca.MountControl.SkySystem.Initialize(settingsInstance);
                Logger.LogInformation("✅ SkySystem initialized with instance settings");

                // Initialize Monitor settings (direct JSON access, no bridge needed)
                GreenSwamp.Alpaca.Shared.Settings.Initialize(settingsService);
                GreenSwamp.Alpaca.Shared.Settings.Load();
                Logger.LogInformation("✅ Monitor settings initialized - direct JSON access");

                Logger.LogInformation("Instance-based settings active - no bridge required");
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