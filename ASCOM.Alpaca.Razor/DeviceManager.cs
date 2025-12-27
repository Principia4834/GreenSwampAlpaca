using ASCOM.Alpaca.Discovery;
using ASCOM.Alpaca.Razor;
using ASCOM.Common.DeviceInterfaces;
using ASCOM.Tools;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace ASCOM.Alpaca
{
    public static class DeviceManager
    {
        public static uint RawTransactionID
        {
            get;
            set;
        } = 1;

        /// <summary>
        /// A server wide Transaction ID Counter
        /// </summary>
        public static uint ServerTransactionID
        {
            get
            {
                return RawTransactionID++;
            }
        }

        internal static IAlpacaConfiguration Configuration { get; private set; }

        // These store the actual instance of the device drivers. They are keyed to the Device Number

        public static readonly Dictionary<int, ICameraV4> Cameras = new Dictionary<int, ICameraV4>();
        public static readonly Dictionary<int, ICoverCalibratorV2> CoverCalibrators = new Dictionary<int, ICoverCalibratorV2>();
        public static readonly Dictionary<int, IDomeV3> Domes = new Dictionary<int, IDomeV3>();
        public static readonly Dictionary<int, IFilterWheelV3> FilterWheels = new Dictionary<int, IFilterWheelV3>();
        public static readonly Dictionary<int, IFocuserV4> Focusers = new Dictionary<int, IFocuserV4>();
        public static readonly Dictionary<int, IObservingConditionsV2> ObservingConditions = new Dictionary<int, IObservingConditionsV2>();
        public static readonly Dictionary<int, IRotatorV4> Rotators = new Dictionary<int, IRotatorV4>();
        public static readonly Dictionary<int, ISafetyMonitorV3> SafetyMonitors = new Dictionary<int, ISafetyMonitorV3>();
        public static readonly Dictionary<int, ISwitchV3> Switches = new Dictionary<int, ISwitchV3>();
        public static readonly Dictionary<int, ITelescopeV4> Telescopes = new Dictionary<int, ITelescopeV4>();

        // This is a master list of all device drivers, keyed by (deviceType, deviceID)

        public static Dictionary<(string deviceType, int deviceID), IAscomDeviceV2> DeviceDrivers = new Dictionary<(string deviceType, int deviceID), IAscomDeviceV2>();

        static List<AlpacaConfiguredDevice> AlpacaDevices = new List<AlpacaConfiguredDevice>();

        public static void LoadConfiguration(IAlpacaConfiguration configuration)
        {
            Configuration = configuration;
        }

        // Device loading methods

#if Camera
        public static void LoadCamera(int DeviceID, ICameraV4 Device, string AlpacaName, string UniqueID)
        {
            //Remove if the simulated instance already exists
            Cameras.Remove(DeviceID);
            DeviceDrivers.Remove(("camera", DeviceID));
            //Add the new instance
            Cameras.Add(DeviceID, Device);
            DeviceDrivers.Add(("camera", DeviceID), Device);

            AlpacaDevices.Remove(AlpacaDevices.FirstOrDefault(a => a.UniqueID == UniqueID));
            AlpacaDevices.Add(new AlpacaConfiguredDevice(AlpacaName, "Camera", DeviceID, UniqueID));
        }
#endif

#if CoverCalibrator
        public static void LoadCoverCalibrator(int DeviceID, ICoverCalibratorV2 Device, string AlpacaName, string UniqueID)
        {
            //Remove if the simulated instance already exists
            CoverCalibrators.Remove(DeviceID);
            DeviceDrivers.Remove(("covercalibrator", DeviceID));
            //Add the new instance
            CoverCalibrators.Add(DeviceID, Device);
            DeviceDrivers.Add(("covercalibrator", DeviceID), Device);

            AlpacaDevices.Remove(AlpacaDevices.FirstOrDefault(a => a.UniqueID == UniqueID));
            AlpacaDevices.Add(new AlpacaConfiguredDevice(AlpacaName, "CoverCalibrator", DeviceID, UniqueID));
        }
#endif

#if Dome
        public static void LoadDome(int DeviceID, IDomeV3 Device, string AlpacaName, string UniqueID)
        {
            //Remove if the simulated instance already exists
            Domes.Remove(DeviceID);
            DeviceDrivers.Remove(("dome", DeviceID));
            //Add the new instance
            Domes.Add(DeviceID, Device);
            DeviceDrivers.Add(("dome", DeviceID), Device);

            AlpacaDevices.Remove(AlpacaDevices.FirstOrDefault(a => a.UniqueID == UniqueID));
            AlpacaDevices.Add(new AlpacaConfiguredDevice(AlpacaName, "Dome", DeviceID, UniqueID));
        }
#endif

#if FilterWheel
        public static void LoadFilterWheel(int DeviceID, IFilterWheelV3 Device, string AlpacaName, string UniqueID)
        {
            //Remove if the simulated instance already exists
            FilterWheels.Remove(DeviceID);
            DeviceDrivers.Remove(("filterwheel", DeviceID));
            //Add the new instance
            FilterWheels.Add(DeviceID, Device);
            DeviceDrivers.Add(("filterwheel", DeviceID), Device);

            AlpacaDevices.Remove(AlpacaDevices.FirstOrDefault(a => a.UniqueID == UniqueID));
            AlpacaDevices.Add(new AlpacaConfiguredDevice(AlpacaName, "FilterWheel", DeviceID, UniqueID));
        }
#endif

#if Focuser
        public static void LoadFocuser(int DeviceID, IFocuserV4 Device, string AlpacaName, string UniqueID)
        {
            //Remove if the simulated instance already exists
            Focusers.Remove(DeviceID);
            DeviceDrivers.Remove(("focuser", DeviceID));
            //Add the new instance
            Focusers.Add(DeviceID, Device);
            DeviceDrivers.Add(("focuser", DeviceID), Device);

            AlpacaDevices.Remove(AlpacaDevices.FirstOrDefault(a => a.UniqueID == UniqueID));
            AlpacaDevices.Add(new AlpacaConfiguredDevice(AlpacaName, "Focuser", DeviceID, UniqueID));
        }
#endif

#if ObservingConditions
        public static void LoadObservingConditions(int DeviceID, IObservingConditionsV2 Device, string AlpacaName, string UniqueID)
        {
            //Remove if the simulated instance already exists
            ObservingConditions.Remove(DeviceID);
            DeviceDrivers.Remove(("observingconditions", DeviceID));
            //Add the new instance
            ObservingConditions.Add(DeviceID, Device);
            DeviceDrivers.Add(("observingconditions", DeviceID), Device);

            AlpacaDevices.Remove(AlpacaDevices.FirstOrDefault(a => a.UniqueID == UniqueID));
            AlpacaDevices.Add(new AlpacaConfiguredDevice(AlpacaName, "ObservingConditions", DeviceID, UniqueID));
        }
#endif

#if Rotator
        public static void LoadRotator(int DeviceID, IRotatorV4 Device, string AlpacaName, string UniqueID)
        {
            //Remove if the simulated instance already exists
            Rotators.Remove(DeviceID);
            DeviceDrivers.Remove(("rotator", DeviceID));
            //Add the new instance
            Rotators.Add(DeviceID, Device);
            DeviceDrivers.Add(("rotator", DeviceID), Device);

            AlpacaDevices.Remove(AlpacaDevices.FirstOrDefault(a => a.UniqueID == UniqueID));
            AlpacaDevices.Add(new AlpacaConfiguredDevice(AlpacaName, "Rotator", DeviceID, UniqueID));
        }
#endif

#if SafetyMonitor
        public static void LoadSafetyMonitor(int DeviceID, ISafetyMonitorV3 Device, string AlpacaName, string UniqueID)
        {
            //Remove if the simulated instance already exists
            SafetyMonitors.Remove(DeviceID);
            DeviceDrivers.Remove(("safetymonitor", DeviceID));
            //Add the new instance
            SafetyMonitors.Add(DeviceID, Device);
            DeviceDrivers.Add(("safetymonitor", DeviceID), Device);

            AlpacaDevices.Remove(AlpacaDevices.FirstOrDefault(a => a.UniqueID == UniqueID));
            AlpacaDevices.Add(new AlpacaConfiguredDevice(AlpacaName, "SafetyMonitor", DeviceID, UniqueID));
        }
#endif

#if LoadSwitch
        public static void LoadSwitch(int DeviceID, ISwitchV3 Device, string AlpacaName, string UniqueID)
        {
            //Remove if the simulated instance already exists
            Switches.Remove(DeviceID);
            DeviceDrivers.Remove(("switch", DeviceID));
            //Add the new instance
            Switches.Add(DeviceID, Device);
            DeviceDrivers.Add(("switch", DeviceID), Device);

            AlpacaDevices.Remove(AlpacaDevices.FirstOrDefault(a => a.UniqueID == UniqueID));
            AlpacaDevices.Add(new AlpacaConfiguredDevice(AlpacaName, "Switch", DeviceID, UniqueID));
        }
#endif

#if Telescope
        public static void LoadTelescope(int DeviceID, ITelescopeV4 Device, string AlpacaName, string UniqueID)
        {
            //Remove if the simulated instance already exists
            Telescopes.Remove(DeviceID);
            DeviceDrivers.Remove(("telescope", DeviceID));
            //Add the new instance
            Telescopes.Add(DeviceID, Device);
            DeviceDrivers.Add(("telescope", DeviceID), Device);

            AlpacaDevices.Remove(AlpacaDevices.FirstOrDefault(a => a.UniqueID == UniqueID));
            AlpacaDevices.Add(new AlpacaConfiguredDevice(AlpacaName, "Telescope", DeviceID, UniqueID));
        }
#endif

        //Returns a list of every single device type for the Management API
        internal static List<AlpacaConfiguredDevice> GetDevices()
        {
            return AlpacaDevices;
        }

        //These methods allow access to specific devices for the API controllers and the device Blazor UI Pages

#if Camera
        public static ICameraV4 GetCamera(uint DeviceID)
        {
            if (Cameras.ContainsKey((int)DeviceID))
            {
                return Cameras[(int)DeviceID];
            }
            else
            {
                throw new DeviceNotFoundException(string.Format("Instance {0} does not exist in this server.", DeviceID));
            }
        }
#endif

#if CoverCalibrator
        public static ICoverCalibratorV2 GetCoverCalibrator(uint DeviceID)
        {
            if (CoverCalibrators.ContainsKey((int)DeviceID))
            {
                return CoverCalibrators[(int)DeviceID];
            }
            else
            {
                throw new DeviceNotFoundException(string.Format("Instance {0} does not exist in this server.", DeviceID));
            }
        }
#endif

#if Dome
        public static IDomeV3 GetDome(uint DeviceID)
        {
            if (Domes.ContainsKey((int)DeviceID))
            {
                return Domes[(int)DeviceID];
            }
            else
            {
                throw new DeviceNotFoundException(string.Format("Instance {0} does not exist in this server.", DeviceID));
            }
        }
#endif

#if FilterWheel
        public static IFilterWheelV3 GetFilterWheel(uint DeviceID)
        {
            if (FilterWheels.ContainsKey((int)DeviceID))
            {
                return FilterWheels[(int)DeviceID];
            }
            else
            {
                throw new DeviceNotFoundException(string.Format("Instance {0} does not exist in this server.", DeviceID));
            }
        }
#endif

#if Focuser
		public static IFocuserV4 GetFocuser(uint DeviceID)
        {
            if (Focusers.ContainsKey((int)DeviceID))
            {
                return Focusers[(int)DeviceID];
            }
            else
            {
                throw new DeviceNotFoundException(string.Format("Instance {0} does not exist in this server.", DeviceID));
            }
        }
#endif

#if ObservingConditions
        public static IObservingConditionsV2 GetObservingConditions(uint DeviceID)
        {
            if (ObservingConditions.ContainsKey((int)DeviceID))
            {
                return ObservingConditions[(int)DeviceID];
            }
            else
            {
                throw new DeviceNotFoundException(string.Format("Instance {0} does not exist in this server.", DeviceID));
            }
        }
#endif

#if Rotator
        public static IRotatorV4 GetRotator(uint DeviceID)
        {
            if (Rotators.ContainsKey((int)DeviceID))
            {
                return Rotators[(int)DeviceID];
            }
            else
            {
                throw new DeviceNotFoundException(string.Format("Instance {0} does not exist in this server.", DeviceID));
            }
        }
#endif

#if SafetyMonitor
        public static ISafetyMonitorV3 GetSafetyMonitor(uint DeviceID)
        {
            if (SafetyMonitors.ContainsKey((int)DeviceID))
            {
                return SafetyMonitors[(int)DeviceID];
            }
            else
            {
                throw new DeviceNotFoundException(string.Format("Instance {0} does not exist in this server.", DeviceID));
            }
        }
#endif

#if Switch
        public static ISwitchV3 GetSwitch(uint DeviceID)
        {
            if (Switches.ContainsKey((int)DeviceID))
            {
                return Switches[(int)DeviceID];
            }
            else
            {
                throw new DeviceNotFoundException(string.Format("Instance {0} does not exist in this server.", DeviceID));
            }
        }
#endif

#if Telescope
        public static ITelescopeV4 GetTelescope(uint DeviceID)
        {
            if (Telescopes.ContainsKey((int)DeviceID))
            {
                return Telescopes[(int)DeviceID];
            }
            else
            {
                throw new DeviceNotFoundException(string.Format("Instance {0} does not exist in this server.", DeviceID));
            }
        }
#endif

        //Use only lowercase for case-sensitive OSes

        #region Settings Folder Names

#if Telescope
        public static string Telescope
        {
            get
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    return "Telescope";
                }
                else
                {
                    return "Telescope".ToLowerInvariant();
                }
            }
        }
#endif

#if Camera
        public static string Camera
        {
            get
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    return "Camera";
                }
                else
                {
                    return "Camera".ToLowerInvariant();
                }
            }
        }
#endif

#if Dome
        public static string Dome
        {
            get
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    return "Dome";
                }
                else
                {
                    return "Dome".ToLowerInvariant();
                }
            }
        }
#endif

#if FilterWheel
        public static string FilterWheel
        {
            get
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    return "FilterWheel";
                }
                else
                {
                    return "FilterWheel".ToLowerInvariant();
                }
            }
        }
#endif
#if Focuser
        public static string Focuser
        {
            get
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    return "Focuser";
                }
                else
                {
                    return "Focuser".ToLowerInvariant();
                }
            }
        }
#endif

#if ObservingConditions
        public static string ObservingCondition
        {
            get
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    return "ObservingConditions";
                }
                else
                {
                    return "ObservingConditions".ToLowerInvariant();
                }
            }
        }
#endif

#if Rotator
        public static string Rotator
        {
            get
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    return "Rotator";
                }
                else
                {
                    return "Rotator".ToLowerInvariant();
                }
            }
        }
#endif

#if SafetyMonitor
        public static string SafetyMonitor
        {
            get
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    return "SafetyMonitor";
                }
                else
                {
                    return "SafetyMonitor".ToLowerInvariant();
                }
            }
        }
#endif

#if Switch
        public static string Switch
        {
            get
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    return "Switch";
                }
                else
                {
                    return "Switch".ToLowerInvariant();
                }
            }
        }
#endif

#if CoverCalibrator
        public static string CoverCalibrator
        {
            get
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    return "CoverCalibrator";
                }
                else
                {
                    return "CoverCalibrator".ToLowerInvariant();
                }
            }
        }
#endif
        #endregion Settings Folder Names
    }
}