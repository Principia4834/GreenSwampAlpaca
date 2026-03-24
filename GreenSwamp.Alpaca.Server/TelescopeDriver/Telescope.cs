using ASCOM;
using ASCOM.Common.DeviceInterfaces;
using ASCOM.Tools;
using GreenSwamp.Alpaca.Principles;
using GreenSwamp.Alpaca.Shared;
using GreenSwamp.Alpaca.MountControl;
using Microsoft.AspNetCore.Components;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using GreenSwamp.Alpaca.Server.MountControl;
using Newtonsoft.Json;
using InvalidOperationException = ASCOM.InvalidOperationException;

namespace GreenSwamp.Alpaca.Server.TelescopeDriver
{
    public class Telescope : ITelescopeV4, IDisposable
    {
        #region Fields
        // Driver private data (rate collections)
        private AxisRates[] _mAxisRates;
        private TrackingRates _mTrackingRates;
        private TrackingRatesSimple _mTrackingRatesSimple;
        private CommandStrings _mCommandStrings;
        private readonly long _objectId;

        // Phase 4.8: Device number for multi-instance support
        private readonly int _deviceNumber;

        #endregion

        public Telescope()
        {
            _deviceNumber = 0; // Default to device 0 for backward compatibility

            try
            {
                var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Information, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = " Started" };
                MonitorLog.LogToMonitor(monitorItem);

                _mAxisRates = new AxisRates[3];
                _mAxisRates[0] = new AxisRates(TelescopeAxis.Primary);
                _mAxisRates[1] = new AxisRates(TelescopeAxis.Secondary);
                _mAxisRates[2] = new AxisRates(TelescopeAxis.Tertiary);
                _mTrackingRates = new TrackingRates();
                _mTrackingRatesSimple = new TrackingRatesSimple();

                // ToDo - fix driverId
                //monitorItem = new MonitorEntry
                //{ Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Information, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = $"Instance ID|{_objectId}|Driver ID|{driverId}" };
                //MonitorLog.LogToMonitor(monitorItem);

            }
            catch (Exception ex)
            {
                var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Error, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = $"Error|{ex.Message}|{ex.StackTrace}" };
                MonitorLog.LogToMonitor(monitorItem);

                throw;
            }
        }

        /// <summary>
        /// Phase 4.8: Constructor with device number for multi-instance support
        /// </summary>
        /// <param name="deviceNumber">Device number (0-based)</param>
        public Telescope(int deviceNumber)
        {
            _deviceNumber = deviceNumber;

            try
            {
                var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Information, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = $" Started|DeviceNumber:{deviceNumber}" };
                MonitorLog.LogToMonitor(monitorItem);

                _mAxisRates = new AxisRates[3];
                _mAxisRates[0] = new AxisRates(TelescopeAxis.Primary);
                _mAxisRates[1] = new AxisRates(TelescopeAxis.Secondary);
                _mAxisRates[2] = new AxisRates(TelescopeAxis.Tertiary);
                _mTrackingRates = new TrackingRates();
                _mTrackingRatesSimple = new TrackingRatesSimple();

            }
            catch (Exception ex)
            {
                var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Error, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = $"Error|DeviceNumber:{deviceNumber}|{ex.Message}|{ex.StackTrace}" };
                MonitorLog.LogToMonitor(monitorItem);

                throw;
            }
        }

        /// <summary>
        /// Phase 4.8: Get the MountInstance for this device number
        /// </summary>
        private MountInstance GetInstance()
        {
            var instance = MountInstanceRegistry.GetInstance(_deviceNumber);
            if (instance == null)
            {
                throw new InvalidOperationException($"Device number {_deviceNumber} not found in registry");
            }
            return instance;
        }

        #region Public Properties
        public AlignmentMode AlignmentMode
        {
            get
            {
                var inst = GetInstance();
                CheckCapability(inst.Settings.CanAlignMode, "AlignmentMode");
                var r = inst.Settings.AlignmentMode;

                var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Data, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = $"  {r}" };
                MonitorLog.LogToMonitor(monitorItem);

                switch (r)
                {
                    case AlignmentMode.AltAz:
                        return AlignmentMode.AltAz;
                    case AlignmentMode.GermanPolar:
                        return AlignmentMode.GermanPolar;
                    case AlignmentMode.Polar:
                        return AlignmentMode.Polar;
                    default:
                        return AlignmentMode.GermanPolar;
                }
            }
        }

        public double Altitude
        {
            get
            {
                var inst = GetInstance();
                CheckCapability(inst.Settings.CanAltAz, "Altitude", false);
                var r = inst.Altitude;

                var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Data, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = $"  {r}" };
                MonitorLog.LogToMonitor(monitorItem);

                return r;
            }
        }

        public double ApertureArea
        {
            get
            {
                var inst = GetInstance();
                CheckCapability(inst.Settings.CanOptics, "ApertureArea", false);
                var r = inst.Settings.ApertureArea;

                var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Data, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = $"  {r}" };
                MonitorLog.LogToMonitor(monitorItem);

                return r;
            }
        }

        public double ApertureDiameter
        {
            get
            {
                var inst = GetInstance();
                CheckCapability(inst.Settings.CanOptics, "ApertureDiameter", false);
                var r = inst.Settings.ApertureDiameter;

                var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Data, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = $"  {r}" };
                MonitorLog.LogToMonitor(monitorItem);

                return r;
            }
        }

        public bool AtHome
        {
            get
            {
                var inst = GetInstance();
                CheckVersionOne("AtHome", false);
                var r = inst.AtHome;

                var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Data, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = $"  {r}" };
                MonitorLog.LogToMonitor(monitorItem);

                return r;
            }
        }

        public bool AtPark
        {
            get
            {
                var inst = GetInstance();
                CheckVersionOne("AtPark", false);
                var r = inst.Settings.AtPark;

                var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Data, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = $"  {r}" };
                MonitorLog.LogToMonitor(monitorItem);

                return r;
            }
        }

        public double Azimuth
        {
            get
            {
                var inst = GetInstance();
                CheckCapability(inst.Settings.CanAltAz, "Azimuth", false);
                var r = inst.Azimuth;

                var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Data, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = $" {r}" };
                MonitorLog.LogToMonitor(monitorItem);

                return r;
            }
        }

        public bool CanFindHome
        {
            get
            {
                var inst = GetInstance();
                var r = inst.Settings.CanFindHome;

                var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Data, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = $" {r}" };
                MonitorLog.LogToMonitor(monitorItem);

                return r;
            }
        }

        public bool CanPark
        {
            get
            {
                var inst = GetInstance();
                var r = inst.Settings.CanPark;

                var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Data, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = $" {r}" };
                MonitorLog.LogToMonitor(monitorItem);

                return r;
            }
        }

        public bool CanPulseGuide
        {
            get
            {
                var inst = GetInstance();
                var r = inst.Settings.CanPulseGuide;

                var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Data, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = $" {r}" };
                MonitorLog.LogToMonitor(monitorItem);

                return r;
            }
        }

        public bool CanSetDeclinationRate
        {
            get
            {
                var inst = GetInstance();
                CheckVersionOne("CanSetDeclinationRate", false);
                var r = inst.Settings.CanSetDeclinationRate;

                var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Data, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = $" {r}" };
                MonitorLog.LogToMonitor(monitorItem);

                return r;
            }
        }

        public bool CanSetGuideRates
        {
            get
            {
                var inst = GetInstance();
                var r = inst.Settings.CanSetGuideRates;

                var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Data, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = $" {r}" };
                MonitorLog.LogToMonitor(monitorItem);

                return r;
            }
        }

        public bool CanSetPark
        {
            get
            {
                var inst = GetInstance();
                var r = inst.Settings.CanSetPark;

                var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Data, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = $" {r}" };
                MonitorLog.LogToMonitor(monitorItem);

                return r;
            }
        }

        public bool CanSetPierSide
        {
            get
            {
                var inst = GetInstance();
                CheckVersionOne("CanSetPierSide", false);
                var r = inst.Settings.CanSetPierSide;

                var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Data, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = $" {r}" };
                MonitorLog.LogToMonitor(monitorItem);

                return r;
            }
        }

        public bool CanSetRightAscensionRate
        {
            get
            {
                var inst = GetInstance();
                CheckVersionOne("CanSetRightAscensionRate", false);
                var r = inst.Settings.CanSetRightAscensionRate;

                var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Data, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = $" {r}" };
                MonitorLog.LogToMonitor(monitorItem);

                return r;
            }
        }

        public bool CanSetTracking
        {
            get
            {
                var inst = GetInstance();
                var r = inst.Settings.CanSetTracking;

                var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Data, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = $" {r}" };
                MonitorLog.LogToMonitor(monitorItem);

                return r;
            }
        }

        public bool CanSlew
        {
            get
            {
                var inst = GetInstance();
                var r = inst.Settings.CanSlew;

                var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Data, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = $" {r}" };
                MonitorLog.LogToMonitor(monitorItem);

                return r;
            }
        }

        public bool CanSlewAltAz
        {
            get
            {
                var inst = GetInstance();
                CheckVersionOne("CanSlewAltAz", false);
                var r = inst.Settings.CanSlewAltAz;

                var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Data, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = $" {r}" };
                MonitorLog.LogToMonitor(monitorItem);

                return r;
            }
        }

        public bool CanSlewAltAzAsync
        {
            get
            {
                var inst = GetInstance();
                CheckVersionOne("CanSlewAltAzAsync", false);
                var r = inst.Settings.CanSlewAltAzAsync;

                var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Data, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = $" {r}" };
                MonitorLog.LogToMonitor(monitorItem);

                return r;
            }
        }

        public bool CanSlewAsync
        {
            get
            {
                var inst = GetInstance();
                var r = inst.Settings.CanSlewAsync;

                var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Data, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = $" {r}" };
                MonitorLog.LogToMonitor(monitorItem);

                return r;
            }
        }

        public bool CanSync
        {
            get
            {
                var inst = GetInstance();
                var r = inst.Settings.CanSync;

                var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Data, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = $" {r}" };
                MonitorLog.LogToMonitor(monitorItem);

                return r;
            }
        }

        public bool CanSyncAltAz
        {
            get
            {
                var inst = GetInstance();
                var r = inst.Settings.CanSyncAltAz;

                var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Data, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = $" {r}" };
                MonitorLog.LogToMonitor(monitorItem);

                return r;
            }
        }

        public bool CanUnpark
        {
            get
            {
                var inst = GetInstance();
                var r = inst.Settings.CanUnPark;

                var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Data, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = $" {r}" };
                MonitorLog.LogToMonitor(monitorItem);

                return r;
            }
        }

        /// <remarks>
        /// https://ascom-standards.org/newdocs/telescope.html#Telescope.Connected
        /// </remarks>
        public bool Connected
        {
            get
            {
                var r = SkySystem.Connected;
                var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Debug, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = $" {r}" };
                MonitorLog.LogToMonitor(monitorItem);
                return r;
            }
            set
            {
                var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Information, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = $" {value}|{_objectId}" };
                MonitorLog.LogToMonitor(monitorItem);
                SkySystem.SetConnected(_objectId, value);
            }
        }

        /// <remarks>
        /// https://ascom-standards.org/newdocs/telescope.html#Telescope.Connecting
        /// </remarks>
        public bool Connecting
        {
            get
            {
                var r = SkySystem.Connecting;
                var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Information, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = $" {r}" };
                MonitorLog.LogToMonitor(monitorItem);
                return r;
            }
        }

        public double Declination
        {
            get
            {
                var inst = GetInstance();
                CheckCapability(inst.Settings.CanEquatorial, "Declination", false);
                var dec = inst.DeclinationXForm;

                var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Data, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = $"XForm|{Utilities.DegreesToDMS(dec, "\u00B0 ", ":", "", 2)}" };
                MonitorLog.LogToMonitor(monitorItem);

                monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Data, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = $"Internal|{Utilities.DegreesToDMS(inst.Declination, "\u00B0 ", ":", "", 2)}" };
                MonitorLog.LogToMonitor(monitorItem);

                return dec;
            }
        }

        /// <remarks>
        /// The declination tracking rate (arc seconds per second, default = 0.0)
        /// </remarks>
        public double DeclinationRate
        {
            get
            {
                var inst = GetInstance();
                var r = inst.RateDecOrg;

                var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Data, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = $"{r}" };
                MonitorLog.LogToMonitor(monitorItem);

                return r;
            }
            set
            {
                var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Information, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = $"{value}" };
                MonitorLog.LogToMonitor(monitorItem);

                var inst = GetInstance();
                CheckCapability(inst.Settings.CanSetEquRates, "DeclinationRate", true);
                CheckRate(value);
                if (TrackingRate != DriveRate.Sidereal)
                {
                    throw new ASCOM.InvalidOperationException(" DeclinationRate - cannot set rate because TrackingRate is not Sidereal");
                }
                inst.RateDecOrg = value;
                inst.SetRateDec(Conversions.ArcSec2Deg(value));
            }
        }

        public string Description
        {
            get
            {
                var inst = GetInstance();
                string r = inst.Settings.InstrumentDescription;

                var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Data, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = $"{r}" };
                MonitorLog.LogToMonitor(monitorItem);

                return r;
            }
        }

        /// <summary>
        /// Return the device's operational state in one call
        /// </summary>
        public List<StateValue> DeviceState
        {
            get
            {
                string msg = null;
                try
                {
                    // Create an array list to hold the IStateValue entries
                    var deviceState = new List<StateValue>();
                    var inst = GetInstance();

                    // Add one entry for each operational state, direct access to SkyServer variables, optimise response time to the client <0.1 seconds
                    try { deviceState.Add(new StateValue(nameof(ITelescopeV4.Altitude), inst.Altitude)); } catch (Exception ex) { LogMessage(MonitorType.Warning, "DeviceState", ex.Message); }
                    try { deviceState.Add(new StateValue(nameof(ITelescopeV4.AtHome), inst.AtHome)); } catch (Exception ex) { LogMessage(MonitorType.Warning, "DeviceState", ex.Message); }
                    try { deviceState.Add(new StateValue(nameof(ITelescopeV4.AtPark), inst.AtPark)); } catch (Exception ex) { LogMessage(MonitorType.Warning, "DeviceState", ex.Message); }
                    try { deviceState.Add(new StateValue(nameof(ITelescopeV4.Azimuth), inst.Azimuth)); } catch (Exception ex) { LogMessage(MonitorType.Warning, "DeviceState", ex.Message); }
                    try { deviceState.Add(new StateValue(nameof(ITelescopeV4.Declination), inst.Declination)); } catch (Exception ex) { LogMessage(MonitorType.Warning, "DeviceState", ex.Message); }
                    try { deviceState.Add(new StateValue(nameof(ITelescopeV4.IsPulseGuiding), inst.IsPulseGuiding)); } catch (Exception ex) { LogMessage(MonitorType.Warning, "DeviceState", ex.Message); }
                    try { deviceState.Add(new StateValue(nameof(ITelescopeV4.RightAscension), inst.RightAscension)); } catch (Exception ex) { LogMessage(MonitorType.Warning, "DeviceState", ex.Message); }
                    try { deviceState.Add(new StateValue(nameof(ITelescopeV4.SideOfPier), inst.SideOfPier)); } catch (Exception ex) { LogMessage(MonitorType.Warning, "DeviceState", ex.Message); }
                    try { deviceState.Add(new StateValue(nameof(ITelescopeV4.SiderealTime), inst.SiderealTime)); } catch (Exception ex) { LogMessage(MonitorType.Warning, "DeviceState", ex.Message); }
                    try { deviceState.Add(new StateValue(nameof(ITelescopeV4.Slewing), inst.IsSlewing)); } catch (Exception ex) { LogMessage(MonitorType.Warning, "DeviceState", ex.Message); }
                    try { deviceState.Add(new StateValue(nameof(ITelescopeV4.Tracking), inst.Tracking || inst.SlewState == SlewType.SlewRaDec)); } catch (Exception ex) { LogMessage(MonitorType.Warning, "DeviceState", ex.Message); }
                    try { deviceState.Add(new StateValue(nameof(ITelescopeV4.UTCDate), HiResDateTime.UtcNow)); } catch (Exception ex) { LogMessage(MonitorType.Warning, "DeviceState", ex.Message); }
                    try { deviceState.Add(new StateValue(DateTime.Now)); } catch (Exception ex) { LogMessage(MonitorType.Warning, "DeviceState", ex.Message); }

                    var r = new List<StateValue>(deviceState);

                    for (var index = 0; index < r.Count; index++)
                    {
                        var a = r[index];
                        msg += $"{a.Name}-{a.Value}|";
                    }

                    var monitorItem = new MonitorEntry
                    { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Information, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = $"{msg}" };
                    MonitorLog.LogToMonitor(monitorItem);

                    return r;
                }
                catch (Exception ex)
                {
                    var monitorItem = new MonitorEntry
                    { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Error, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = $"{msg}, {ex.Message}" };
                    MonitorLog.LogToMonitor(monitorItem);
                    throw;
                }
            }
        }

        public bool DoesRefraction
        {
            get
            {
                var inst = GetInstance();
                var r = inst.Settings.Refraction;
                CheckVersionOne("DoesRefraction", false);

                var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Data, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = $"{r}" };
                MonitorLog.LogToMonitor(monitorItem);

                return r;
            }
            set
            {
                var inst = GetInstance();
                var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Information, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = $"{value}" };
                MonitorLog.LogToMonitor(monitorItem);

                CheckVersionOne("DoesRefraction", true);
                inst.Settings.Refraction = value;
            }
        }

        public string DriverInfo
        {
            get
            {
                var asm = Assembly.GetExecutingAssembly();
                var r = asm.FullName;

                var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Data, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = $"{r}" };
                MonitorLog.LogToMonitor(monitorItem);

                return r;
            }
        }

        public string DriverVersion
        {
            get
            {
                CheckVersionOne("DriverVersion", false);
                var asm = Assembly.GetExecutingAssembly();
                var r = asm.GetName().Version.ToString();

                var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Data, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = $"{r}" };
                MonitorLog.LogToMonitor(monitorItem);

                return r;
            }
        }

        public EquatorialCoordinateType EquatorialSystem
        {
            get
            {
                var inst = GetInstance();
                CheckVersionOne("EquatorialSystem", false);

                var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Data, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = $"{inst.Settings.EquatorialCoordinateType}" };
                MonitorLog.LogToMonitor(monitorItem);

                return inst.Settings.EquatorialCoordinateType;
            }
        }

        public double FocalLength
        {
            get
            {
                var inst = GetInstance();
                CheckVersionOne("FocalLength", false);
                CheckCapability(inst.Settings.CanOptics, "FocalLength", false);
                var r = inst.Settings.FocalLength;

                var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Data, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = $"{r}" };
                MonitorLog.LogToMonitor(monitorItem);

                return r;
            }
        }

        public double GuideRateDeclination
        {
            get
            {
                var inst = GetInstance();
                CheckVersionOne("GuideRateDeclination", false);
                var r = inst.GuideRateDec;

                var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Data, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = $"{r}" };
                MonitorLog.LogToMonitor(monitorItem);

                return r;
            }
            set
            {
                var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Information, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = $"{value}" };
                MonitorLog.LogToMonitor(monitorItem);

                var inst = GetInstance();
                CheckVersionOne("GuideRateDeclination", true);
                CheckRange(value, 0.0, 0.5, "GuideRateDeclination");
                inst.GuideRateDec = value;
            }
        }

        public double GuideRateRightAscension
        {
            get
            {
                var inst = GetInstance();
                CheckVersionOne("GuideRateRightAscension", false);
                var r = inst.GuideRateRa;

                var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Data, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = $"{r}" };
                MonitorLog.LogToMonitor(monitorItem);

                return r;
            }
            set
            {
                var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Information, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = $"{value}" };
                MonitorLog.LogToMonitor(monitorItem);

                var inst = GetInstance();
                CheckVersionOne("GuideRateRightAscension", true);
                CheckRange(value, 0.0, 0.5, "GuideRateRightAscension");
                inst.GuideRateRa = value;
            }
        }

        public short InterfaceVersion
        {
            get
            {
                CheckVersionOne("InterfaceVersion", false);

                var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Data, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = "4" };
                MonitorLog.LogToMonitor(monitorItem);

                return 4;
            }
        }

        public bool IsPulseGuiding
        {
            get
            {
                var inst = GetInstance();
                CheckCapability(inst.Settings.CanPulseGuide, "IsPulseGuiding", false);
                var r = inst.IsPulseGuiding;

                var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Data, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = $"{r}" };
                MonitorLog.LogToMonitor(monitorItem);

                return r;
            }
        }

        public string Name
        {
            get
            {
                var inst = GetInstance();
                string r = inst.Settings.InstrumentName;

                var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Data, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = $"{r}" };
                MonitorLog.LogToMonitor(monitorItem);

                return r;
            }
        }

        /// <inheritdoc />
        /// <summary>
        /// The right ascension (hours) of the telescope's current equatorial coordinates,
        /// in the coordinate system given by the EquatorialSystem property
        /// </summary>
        public double RightAscension
        {
            get
            {
                var inst = GetInstance();
                CheckCapability(inst.Settings.CanEquatorial, "RightAscension", false);
                var ra = inst.RightAscensionXForm;

                var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Data, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = $"XForm|{Utilities.HoursToHMS(ra, "h ", ":", "", 2)}" };
                MonitorLog.LogToMonitor(monitorItem);


                monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Data, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = $"Internal|{Utilities.HoursToHMS(inst.RightAscension, "h ", ":", "", 2)}" };
                MonitorLog.LogToMonitor(monitorItem);

                return ra;
            }
        }

        /// <inheritdoc />
        /// <summary>
        /// The right ascension tracking rate offset from sidereal (seconds per sidereal second, default = 0.0)
        /// This property, together with DeclinationRate, provides support for "offset tracking".
        /// Offset tracking is used primarily for tracking objects that move relatively slowly against
        /// the equatorial coordinate system. It also may be used by a software guiding system that
        /// controls rates instead of using the PulseGuide method.
        /// </summary>
        public double RightAscensionRate
        {
            get
            {
                var inst = GetInstance();
                var r = inst.RateRaOrg;

                var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Data, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = $"{r}" };
                MonitorLog.LogToMonitor(monitorItem);

                return r;
            }
            set
            {

                var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Data, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = $"{value}" };
                MonitorLog.LogToMonitor(monitorItem);

                var inst = GetInstance();
                CheckCapability(inst.Settings.CanSetEquRates, "RightAscensionRate ", true);
                CheckRate(value);
                if (TrackingRate != DriveRate.Sidereal)
                {
                    throw new InvalidOperationException(" RightAscensionRate - cannot set rate because TrackingRate is not Sidereal");
                }
                inst.RateRaOrg = value;
                inst.SetRateRa(Conversions.ArcSec2Deg(Conversions.SideSec2ArcSec(value)));
            }
        }

        public PointingState SideOfPier
        {
            get
            {
                var inst = GetInstance();
                var r = inst.SideOfPier;

                var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Data, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = $"{r}" };
                MonitorLog.LogToMonitor(monitorItem);

                return r;
            }
            set
            {
                var inst = GetInstance();
                CheckCapability(inst.Settings.CanSetPierSide, "SideOfPier", true);
                MonitorEntry monitorItem;
                if (value == inst.SideOfPier)
                {
                    monitorItem = new MonitorEntry
                    { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Information, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = "No Change Needed" };
                    MonitorLog.LogToMonitor(monitorItem);

                    return;
                }
                inst.SetSideOfPier(value);

                monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Data, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = $"{value}" };
                MonitorLog.LogToMonitor(monitorItem);

            }
        }

        public double SiderealTime
        {
            get
            {
                var inst = GetInstance();
                CheckCapability(inst.Settings.CanSiderealTime, "SiderealTime", false);
                var r = inst.SiderealTime;

                var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Data, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = $"{Utilities.HoursToHMS(r)}" };
                MonitorLog.LogToMonitor(monitorItem);

                return r;
            }
        }

        public double SiteElevation
        {
            get
            {
                var inst = GetInstance();
                CheckCapability(inst.Settings.CanLatLongElev, "SiteElevation", false);
                var r = inst.Settings.Elevation;

                var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Data, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = $"{r}" };
                MonitorLog.LogToMonitor(monitorItem);

                return r;
            }
            set
            {
                var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Information, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = $"{value}" };
                MonitorLog.LogToMonitor(monitorItem);

                var inst = GetInstance();
                CheckCapability(inst.Settings.CanLatLongElev, "SiteElevation", true);
                CheckRange(value, -300, 10000, "SiteElevation");
                inst.Settings.Elevation = value;
            }
        }

        public double SiteLatitude
        {
            get
            {
                var inst = GetInstance();
                CheckCapability(inst.Settings.CanLatLongElev, "SiteLatitude", false);
                var r = inst.Settings.Latitude;

                var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Data, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = $"{r}" };
                MonitorLog.LogToMonitor(monitorItem);

                return r;
            }
            set
            {
                var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Information, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = $"{value}" };
                MonitorLog.LogToMonitor(monitorItem);

                var inst = GetInstance();
                CheckCapability(inst.Settings.CanLatLongElev, "SiteLatitude", true);
                CheckRange(value, -90, 90, "SiteLatitude");
                inst.Settings.Latitude = value;
            }
        }

        public double SiteLongitude
        {
            get
            {
                var inst = GetInstance();
                CheckCapability(inst.Settings.CanLatLongElev, "SiteLongitude", false);
                var r = inst.Settings.Longitude;

                var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Data, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = $"{r}" };
                MonitorLog.LogToMonitor(monitorItem);

                return r;
            }
            set
            {
                var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Information, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = $"{value}" };
                MonitorLog.LogToMonitor(monitorItem);

                var inst = GetInstance();
                CheckCapability(inst.Settings.CanLatLongElev, "SiteLongitude", true);
                CheckRange(value, -180, 180, "SiteLongitude");
                inst.Settings.Longitude = value;
            }
        }

        public bool Slewing
        {
            get
            {
                var inst = GetInstance();
                var r = inst.IsSlewing;

                var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Data, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = $"{r}" };
                MonitorLog.LogToMonitor(monitorItem);

                return r;
            }
        }

        public short SlewSettleTime
        {
            get
            {
                var inst = GetInstance();
                var r = (short)(inst.SlewSettleTime);

                var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Data, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = $"{r}" };
                MonitorLog.LogToMonitor(monitorItem);

                return r;
            }
            set
            {
                var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Information, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = $"{value}" };
                MonitorLog.LogToMonitor(monitorItem);

                var inst = GetInstance();
                CheckRange(value, 0, 100, "SlewSettleTime");
                var r = value;
                inst.SlewSettleTime = r;
            }
        }

        public double TargetDeclination
        {
            get
            {
                var inst = GetInstance();
                CheckCapability(inst.Settings.CanSlew, "TargetDeclination", false);
                CheckRange(inst.TargetDec, -90, 90, "TargetDeclination");
                var r = inst.TargetDec;

                var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Data, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = $"{r}" };
                MonitorLog.LogToMonitor(monitorItem);

                return r;
            }
            set
            {
                var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Information, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = $"{Utilities.DegreesToDMS(value, "\u00B0 ", ":", "", 2)}" };
                MonitorLog.LogToMonitor(monitorItem);

                var inst = GetInstance();
                CheckCapability(inst.Settings.CanSlew, "TargetDeclination", true);
                CheckRange(value, -90, 90, "TargetDeclination");

                inst.TargetDec = value;
            }
        }

        public double TargetRightAscension
        {
            get
            {
                var inst = GetInstance();
                CheckCapability(inst.Settings.CanSlew, "TargetRightAscension", false);
                CheckRange(inst.TargetRa, 0, 24, "TargetRightAscension");
                var r = inst.TargetRa;

                var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Data, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = $"{r}" };
                MonitorLog.LogToMonitor(monitorItem);

                return r;
            }
            set
            {
                var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Information, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = $"{Utilities.HoursToHMS(value, "h ", ":", "", 2)}" };
                MonitorLog.LogToMonitor(monitorItem);

                var inst = GetInstance();
                CheckCapability(inst.Settings.CanSlew, "TargetRightAscension", true);
                CheckRange(value, 0, 24, "TargetRightAscension");

                inst.TargetRa = value;
            }
        }

        public bool Tracking
        {
            get
            {
                var inst = GetInstance();
                var r = inst.Tracking || inst.SlewState == SlewType.SlewRaDec;

                var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Data, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = $"{r}" };
                MonitorLog.LogToMonitor(monitorItem);

                return r;
            }
            set
            {
                var inst = GetInstance();
                if (value & inst.AtPark) { CheckParked("Cannot enable tracking at park"); }

                var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Information, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = $"{value}" };
                MonitorLog.LogToMonitor(monitorItem);

                inst.ApplyTracking(value);
            }
        }

        public DriveRate TrackingRate
        {
            get
            {
                var inst = GetInstance();
                var r = inst.Settings.TrackingRate;
                CheckVersionOne("TrackingRate", false);

                var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Data, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = $"{r}" };
                MonitorLog.LogToMonitor(monitorItem);

                return r;
            }
            set
            {
                var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Information, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = $"{value}" };
                MonitorLog.LogToMonitor(monitorItem);

                var inst = GetInstance();
                CheckVersionOne("TrackingRate", true);
                CheckTrackingRate("TrackingRate", value);
                inst.Settings.TrackingRate = value;
            }
        }

        public ITrackingRates TrackingRates
        {
            get
            {
                var inst = GetInstance();
                MonitorEntry monitorItem;
                if (inst.Settings.CanTrackingRates)
                {
                    monitorItem = new MonitorEntry
                    { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Data, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = $"{_mTrackingRates}" };
                    MonitorLog.LogToMonitor(monitorItem);

                    return _mTrackingRates;
                }
                monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Information, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = $"{_mTrackingRatesSimple}" };
                MonitorLog.LogToMonitor(monitorItem);

                return _mTrackingRatesSimple;
            }
        }

        public DateTime UTCDate
        {
            get
            {
                // var r = HiResDateTime.UtcNow.Add(SkySettings.UTCDateOffset);
                var r = HiResDateTime.UtcNow;

                var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Data, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = $"{r}" };
                MonitorLog.LogToMonitor(monitorItem);

                return r;
            }
            set
            {

                //var r = value.Subtract(HiResDateTime.UtcNow);
                //if (Math.Abs(r.TotalMilliseconds) < 100) r = new TimeSpan();
                //SkySettings.UTCDateOffset = r;

                var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Information, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = $"{value}" };
                MonitorLog.LogToMonitor(monitorItem);

                throw new PropertyNotImplementedException(MethodBase.GetCurrentMethod()?.Name);
            }
        }

        #endregion

        public void CommandBlind(string command, bool raw = false)
        {
            throw new MethodNotImplementedException("CommandBlind");
        }

        public bool CommandBool(string command, bool raw = false)
        {
            throw new MethodNotImplementedException("CommandBool");
        }

        public IList<string> SupportedActions
        {
            get
            {
                var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Data, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = " Started" };
                MonitorLog.LogToMonitor(monitorItem);

                // ReSharper disable once StringLiteralTypo
                var sa = new List<string> { @"Telescope:SetParkPosition" };

                return sa;
            }
        }
        public string CommandString(string command, bool raw = false)
        {
            try
            {
                var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Information, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = FormattableString.Invariant($"{command},{raw}") };
                MonitorLog.LogToMonitor(monitorItem);

                if (string.IsNullOrWhiteSpace(command)) { throw new MethodNotImplementedException("CommandString"); }

                if (_mCommandStrings == null) { _mCommandStrings = new CommandStrings(); }
                return CommandStrings.ProcessCommand(command, raw);
            }
            catch (Exception ex)
            {
                var monitorItem = new MonitorEntry
                {
                    Datetime = HiResDateTime.UtcNow,
                    Device = MonitorDevice.Telescope,
                    Category = MonitorCategory.Driver,
                    Type = MonitorType.Warning,
                    Method = MethodBase.GetCurrentMethod()?.Name,
                    Thread = Thread.CurrentThread.ManagedThreadId,
                    Message = FormattableString.Invariant($"{ex.Message},{ex.StackTrace}")
                };
                MonitorLog.LogToMonitor(monitorItem);
                throw;
            }
        }

        public void Dispose()
        {
            Dispose(true);
        }

        private void Dispose(bool disposing)
        {
            if (!disposing) return;
            Connected = false;
            _mAxisRates[0].Dispose();
            _mAxisRates[1].Dispose();
            _mAxisRates[2].Dispose();
            _mAxisRates = null;
            _mTrackingRates.Dispose();
            _mTrackingRates = null;
            _mTrackingRatesSimple.Dispose();
            _mTrackingRatesSimple = null;
        }

        public void Connect()
        {
            Connected = true;
            var monitorItem = new MonitorEntry
            { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Information, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = "true" };
            MonitorLog.LogToMonitor(monitorItem);
        }

        public void Disconnect()
        {
            Connected = false;
            var monitorItem = new MonitorEntry
            { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Information, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = "false" };
            MonitorLog.LogToMonitor(monitorItem);
        }

        public void AbortSlew()
        {
            var monitorItem = new MonitorEntry
            { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Information, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = " Started" };
            MonitorLog.LogToMonitor(monitorItem);

            CheckParked("AbortSlew");
            GetInstance().AbortSlewAsync(true);
        }

        public IAxisRates AxisRates(TelescopeAxis Axis)
        {
            var monitorItem = new MonitorEntry
            { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Data, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = $"   {Axis}" };
            MonitorLog.LogToMonitor(monitorItem);

            switch (Axis)
            {
                case TelescopeAxis.Primary:
                    return new AxisRates(TelescopeAxis.Primary);
                case TelescopeAxis.Secondary:
                    return new AxisRates(TelescopeAxis.Secondary);
                case TelescopeAxis.Tertiary:
                    return new AxisRates(TelescopeAxis.Tertiary);
                default:
                    return null;
            }
        }

        public bool CanMoveAxis(TelescopeAxis Axis)
        {
            CheckVersionOne("CanMoveAxis");
            var r = GetInstance().CanMoveAxis(Axis);

            var monitorItem = new MonitorEntry
            { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Data, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = $" {r}" };
            MonitorLog.LogToMonitor(monitorItem);

            return r;
        }

        public PointingState DestinationSideOfPier(double RightAscension, double Declination)
        {
            CheckVersionOne("DestinationSideOfPier");

            var monitorItem = new MonitorEntry
            { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Information, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = $"RA|{RightAscension}|Dec|{Declination}" };
            MonitorLog.LogToMonitor(monitorItem);

            var raDec = Transforms.CoordTypeToInternal(RightAscension, Declination);
            CheckRange(raDec.X, 0, 24, "SlewToCoordinatesAsync", "RightAscension");
            CheckRange(raDec.Y, -90, 90, "SlewToCoordinatesAsync", "Declination");
            CheckReachable(raDec.X, raDec.Y, SlewType.SlewRaDec);
            var r = GetInstance().DetermineSideOfPier(raDec.X, raDec.Y);
            return r;
        }

        public string Action(string actionName, string actionParameters)
        {
            actionName = actionName?.Trim();
            actionParameters = actionParameters?.Trim();

            var monitorItem = new MonitorEntry
            {
                Datetime = HiResDateTime.UtcNow,
                Device = MonitorDevice.Telescope,
                Category = MonitorCategory.Driver,
                Type = MonitorType.Information,
                Method = MethodBase.GetCurrentMethod()?.Name,
                Thread = Thread.CurrentThread.ManagedThreadId,
                Message = $" ActionName:{actionName}, ActionParameters:'{actionParameters}'"
            };
            MonitorLog.LogToMonitor(monitorItem);

            switch (actionName)
            {
                // ReSharper disable once StringLiteralTypo
                case string str when str.Equals("telescope:setparkposition", StringComparison.InvariantCultureIgnoreCase):
                    var inst = GetInstance();
                    if (inst.IsMountRunning == false) { throw new NotConnectedException("Mount Not Connected"); }
                    var found = inst.Settings.ParkPositions.Find(x => string.Equals(x.Name, actionParameters, StringComparison.InvariantCultureIgnoreCase));
                    if (found == null)
                    {
                        var parkPositions = inst.Settings.ParkPositions.OrderBy(parkPosition => parkPosition.Name).ToList();
                        var output = JsonConvert.SerializeObject(parkPositions);
                        throw new Exception($"Param Not Found:'{actionParameters}', {output}");
                    }
                    inst.ParkSelected = found;
                    return found.Name;
                default:
                    throw new ActionNotImplementedException($"Not Found:'{actionName}'");
            }
        }

        public void FindHome()
        {
            var inst = GetInstance();
            CheckCapability(inst.Settings.CanFindHome, "FindHome");
            CheckParked("FindHome");

            var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Information, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = "Started" };
            MonitorLog.LogToMonitor(monitorItem);

            inst.GoToHome();

            // ToDo check for timing window
            //if (InterfaceVersion >= 4) return;
            //while (SkyServer.SlewState == SlewType.SlewHome || SkyServer.SlewState == SlewType.SlewSettle)
            //{
            //    Thread.Sleep(1);
            //    DoEvents();
            //}
        }

        public void MoveAxis(TelescopeAxis Axis, double Rate)
        {
            var monitorItem = new MonitorEntry
            {
                Datetime = HiResDateTime.UtcNow,
                Device = MonitorDevice.Telescope,
                Category = MonitorCategory.Driver,
                Type = MonitorType.Information,
                Method = MethodBase.GetCurrentMethod()?.Name,
                Thread = Thread.CurrentThread.ManagedThreadId,
                Message = $"{Axis}, {Rate}"
            };
            MonitorLog.LogToMonitor(monitorItem);

            CheckVersionOne("MoveAxis");
            CheckRate(Axis, Rate);
            if (!CanMoveAxis(Axis)){throw new MethodNotImplementedException("CanMoveAxis " + Enum.GetName(typeof(TelescopeAxis), Axis));}
            CheckParked("MoveAxis");

            var inst = GetInstance();
            switch (Axis)
            {
                case TelescopeAxis.Primary:
                    inst.RateMovePrimaryAxis = Rate;
                    break;
                case TelescopeAxis.Secondary:
                    inst.RateMoveSecondaryAxis = Rate;
                    break;
                case TelescopeAxis.Tertiary:
                default:
                    // not implemented
                    break;
            }
        }

        /// <summary>
        /// Park the telescope - ASCOM async method (returns immediately, client polls Slewing).
        /// </summary>
        /// <exception cref="ASCOM.NotImplementedException">If the telescope cannot be parked</exception>
        /// <exception cref="ASCOM.ParkedException">If the telescope is already parked</exception>
        public void Park()
        {
            var inst = GetInstance();
            CheckCapability(inst.Settings.CanPark, "Park");

            var monitorItem = new MonitorEntry
            {
                Datetime = HiResDateTime.UtcNow,
                Device = MonitorDevice.Telescope,
                Category = MonitorCategory.Driver,
                Type = MonitorType.Information,
                Method = MethodBase.GetCurrentMethod()?.Name,
                Thread = Thread.CurrentThread.ManagedThreadId,
                Message = "Started"
            };

            if (inst.AtPark)
            {
                monitorItem.Message = "Already Parked";
                MonitorLog.LogToMonitor(monitorItem);
            }
            else
            {
                MonitorLog.LogToMonitor(monitorItem);

                // Just initiate park - SlewController handles completion
                // No background task needed - GoToParkAsync is already async
                _ = inst.GoToParkAsync();

                // Returns immediately - client polls Slewing and AtPark
                // SlewController will set AtPark when movement actually completes
            }
        }

        public void PulseGuide(GuideDirection Direction, int Duration)
        {
            try
            {
                var inst = GetInstance();
                if (inst.AtPark) { throw new ParkedException(); }

                var monitorItem = new MonitorEntry
                    { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Data, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = FormattableString.Invariant($"{Direction},{Duration}") };
                MonitorLog.LogToMonitor(monitorItem);

                CheckCapability(inst.Settings.CanPulseGuide, "PulseGuide");
                CheckRange(Duration, 0, 30000, "PulseGuide", "Duration");

                switch (Direction)
                {
                    case GuideDirection.North:
                    case GuideDirection.South:
                        inst.IsPulseGuidingDec = true;
                        break;
                    case GuideDirection.East:
                    case GuideDirection.West:
                        inst.IsPulseGuidingRa = true;
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(Direction), Direction, null);
                }

                inst.PulseGuide(Direction, Duration, 0);
            }
            catch (Exception e)
            {
                var inst = GetInstance();
                inst.IsPulseGuidingRa = false;
                inst.IsPulseGuidingDec = false;
                var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Warning, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = FormattableString.Invariant($"{e.Message}") };
                MonitorLog.LogToMonitor(monitorItem);
                throw;
            }
        }

        public void SlewToAltAz(double Azimuth, double Altitude)
        {
            var inst = GetInstance();
            var monitorItem = new MonitorEntry
            { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Information, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = $"{Utilities.DegreesToDMS(Azimuth, "\u00B0 ", ":", "", 2)}|{Utilities.DegreesToDMS(Altitude, "\u00B0 ", ":", "", 2)}" };
            MonitorLog.LogToMonitor(monitorItem);

            CheckCapability(inst.Settings.CanSlewAltAz, "SlewToAltAz");
            CheckParked("SlewToAltAz");
            CheckTracking(false, "SlewToAltAz");
            CheckRange(Azimuth, 0, 360, "SlewToAltAz", "azimuth");
            CheckRange(Altitude, -90, 90, "SlewToAltAz", "Altitude");
            inst.SlewAltAz(Altitude, Azimuth);
            Thread.Sleep(250);
            while (inst.SlewState == SlewType.SlewAltAz || inst.SlewState == SlewType.SlewSettle)
            {
                Thread.Sleep(10);
            }
            DelayInterval();
        }

        public void SlewToAltAzAsync(double Azimuth, double Altitude)
        {
            var inst = GetInstance();
            var monitorItem = new MonitorEntry
            { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Information, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = $"{Utilities.DegreesToDMS(Azimuth, "\u00B0 ", ":", "", 2)}|{Utilities.DegreesToDMS(Altitude, "\u00B0 ", ":", "", 2)}" };
            MonitorLog.LogToMonitor(monitorItem);

            CheckCapability(inst.Settings.CanSlewAltAzAsync, "SlewToAltAzAsync");
            CheckParked("SlewToAltAz");
            CheckTracking(false, "SlewToAltAzAsync");
            CheckRange(Azimuth, 0, 360, "SlewToAltAzAsync", "Azimuth");
            CheckRange(Altitude, -90, 90, "SlewToAltAzAsync", "Altitude");
            CheckReachable(Azimuth, Altitude, SlewType.SlewAltAz);
            // Direct fire-and-forget call - SlewController handles async execution
            // Returns when setup completes (<1s) with IsSlewing=true already set
            _ = inst.SlewAltAzAsync(Altitude, Azimuth);
            // Movement continues in background via SlewController._movementTask
            // Client polls Slewing property to detect completion
        }

        public void SlewToCoordinates(double RightAscension, double Declination)
        {
            var inst = GetInstance();
            var monitorItem = new MonitorEntry
            {
                Datetime = HiResDateTime.UtcNow,
                Device = MonitorDevice.Telescope,
                Category = MonitorCategory.Driver,
                Type = MonitorType.Information,
                Method = MethodBase.GetCurrentMethod()?.Name,
                Thread = Thread.CurrentThread.ManagedThreadId,
                Message = $"{Utilities.HoursToHMS(RightAscension, "h ", ":", "", 2)}|{Utilities.DegreesToDMS(Declination, "\u00B0 ", ":", "", 2)}"
            };
            MonitorLog.LogToMonitor(monitorItem);

            CheckCapability(inst.Settings.CanSlew, "SlewToCoordinates");
            CheckRange(RightAscension, 0, 24, "SlewToCoordinates", "RightAscension");
            CheckRange(Declination, -90, 90, "SlewToCoordinates", "Declination");
            CheckParked("SlewToCoordinates");
            CheckTracking(true, "SlewToCoordinates");
            CheckReachable(RightAscension, Declination, SlewType.SlewRaDec);

            TargetRightAscension = RightAscension;
            TargetDeclination = Declination;
            var raDec = Transforms.CoordTypeToInternal(RightAscension, Declination);

            // Use SlewController via SlewRaDec (blocks until complete)
            inst.SlewRaDec(raDec.X, raDec.Y, true);

            // Brief delay for position updates
            DelayInterval();
        }

        public void SlewToCoordinatesAsync(double RightAscension, double Declination)
        {
            var inst = GetInstance();
            var monitorItem = new MonitorEntry
            {
                Datetime = HiResDateTime.UtcNow,
                Device = MonitorDevice.Telescope,
                Category = MonitorCategory.Driver,
                Type = MonitorType.Information,
                Method = MethodBase.GetCurrentMethod()?.Name,
                Thread = Thread.CurrentThread.ManagedThreadId,
                Message = $"{Utilities.HoursToHMS(RightAscension, "h ", ":", "", 2)}|{Utilities.DegreesToDMS(Declination, "\u00B0 ", ":", "", 2)}"
            };
            MonitorLog.LogToMonitor(monitorItem);

            CheckCapability(inst.Settings.CanSlewAsync, "SlewToCoordinatesAsync");
            CheckRange(RightAscension, 0, 24, "SlewToCoordinatesAsync", "RightAscension");
            CheckRange(Declination, -90, 90, "SlewToCoordinatesAsync", "Declination");
            CheckParked("SlewToCoordinatesAsync");
            CheckReachable(RightAscension, Declination, SlewType.SlewRaDec);

            TargetRightAscension = RightAscension;
            TargetDeclination = Declination;
            var raDec = Transforms.CoordTypeToInternal(RightAscension, Declination);

            // Enable tracking before starting slew
            inst.CycleOnTracking(true);

            // Direct fire-and-forget call - SlewController handles async execution
            // Returns when setup completes (<1s) with IsSlewing=true already set
            _ = inst.SlewRaDecAsync(raDec.X, raDec.Y, tracking: true);
            // Movement continues in background via SlewController._movementTask
            // Client polls Slewing property to detect completion
        }

        public void SlewToTarget()
        {
            var inst = GetInstance();
            var ra = TargetRightAscension;
            var dec = TargetDeclination;

            var monitorItem = new MonitorEntry
            {
                Datetime = HiResDateTime.UtcNow,
                Device = MonitorDevice.Telescope,
                Category = MonitorCategory.Driver,
                Type = MonitorType.Information,
                Method = MethodBase.GetCurrentMethod()?.Name,
                Thread = Thread.CurrentThread.ManagedThreadId,
                Message = FormattableString.Invariant($"{ra}|{dec}")
            };
            MonitorLog.LogToMonitor(monitorItem);

            CheckCapability(inst.Settings.CanSlew, "SlewToTarget");
            CheckRange(ra, 0, 24, "SlewToTarget", "TargetRightAscension");
            CheckRange(dec, -90, 90, "SlewToTarget", "TargetDeclination");
            CheckParked("SlewToTarget");
            CheckTracking(true, "SlewToTarget");
            CheckReachable(ra, dec, SlewType.SlewRaDec);

            var xy = Transforms.CoordTypeToInternal(ra, dec);

            // Use SlewController via SlewRaDec (blocks until complete)
            inst.SlewRaDec(xy.X, xy.Y, true);

            // Brief delay for position updates
            DelayInterval();
        }

        public void SlewToTargetAsync()
        {
            var inst = GetInstance();
            var ra = TargetRightAscension;
            var dec = TargetDeclination;

            var monitorItem = new MonitorEntry
            {
                Datetime = HiResDateTime.UtcNow,
                Device = MonitorDevice.Telescope,
                Category = MonitorCategory.Driver,
                Type = MonitorType.Information,
                Method = MethodBase.GetCurrentMethod()?.Name,
                Thread = Thread.CurrentThread.ManagedThreadId,
                Message = FormattableString.Invariant($"{ra}|{dec}")
            };
            MonitorLog.LogToMonitor(monitorItem);

            CheckCapability(inst.Settings.CanSlewAsync, "SlewToTargetAsync");
            CheckRange(ra, 0, 24, "SlewToTargetAsync", "TargetRightAscension");
            CheckRange(dec, -90, 90, "SlewToTargetAsync", "TargetDeclination");
            CheckParked("SlewToTargetAsync");
            CheckTracking(true, "SlewToTargetAsync");
            CheckReachable(ra, dec, SlewType.SlewRaDec);

            var xy = Transforms.CoordTypeToInternal(ra, dec);

            // Enable tracking before starting slew
            inst.CycleOnTracking(true);

            // Direct fire-and-forget call - SlewController handles async execution
            // Returns when setup completes (<1s) with IsSlewing=true already set
            _ = inst.SlewRaDecAsync(xy.X, xy.Y, tracking: true);
            // Movement continues in background via SlewController._movementTask
            // Client polls Slewing property to detect completion
        }

        public void Unpark()
        {
            var inst = GetInstance();
            CheckCapability(inst.Settings.CanUnPark, "UnPark");

            var monitorItem = new MonitorEntry
            {
                Datetime = HiResDateTime.UtcNow,
                Device = MonitorDevice.Telescope,
                Category = MonitorCategory.Driver,
                Type = MonitorType.Information,
                Method = MethodBase.GetCurrentMethod()?.Name,
                Thread = Thread.CurrentThread.ManagedThreadId,
                Message = $"Unparking - AtPark was: {inst.AtPark}"  // ✅ Log before
            };
            MonitorLog.LogToMonitor(monitorItem);

            inst.AtPark = false;
            inst.ApplyTracking(AlignmentMode != AlignmentMode.AltAz);

            monitorItem = new MonitorEntry
            {
                Datetime = HiResDateTime.UtcNow,
                Device = MonitorDevice.Telescope,
                Category = MonitorCategory.Driver,
                Type = MonitorType.Information,
                Method = MethodBase.GetCurrentMethod()?.Name,
                Thread = Thread.CurrentThread.ManagedThreadId,
                Message = $"Unparked - AtPark now: {inst.AtPark}"    // ✅ Log after
            };
            MonitorLog.LogToMonitor(monitorItem);
        }

        public void SetPark()
        {
            var monitorItem = new MonitorEntry
            { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Information, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = "Started" };
            MonitorLog.LogToMonitor(monitorItem);

            var inst = GetInstance();
            CheckCapability(inst.Settings.CanSetPark, "SetPark");
            inst.SetParkAxis("External");
        }

        public void SyncToAltAz(double Azimuth, double Altitude)
        {
            var inst = GetInstance();
            var monitorItem = new MonitorEntry
            { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Information, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = $"{Utilities.DegreesToDMS(Azimuth, "\u00B0 ", ":", "", 2)}|{Utilities.DegreesToDMS(Altitude, "\u00B0 ", ":", "", 2)}" };
            MonitorLog.LogToMonitor(monitorItem);

            CheckCapability(inst.Settings.CanSyncAltAz, "SyncToAltAz");
            CheckRange(Azimuth, 0, 360, "SyncToAltAz", "Azimuth");
            CheckRange(Altitude, -90, 90, "SyncToAltAz", "Altitude");
            CheckParked("SyncToAltAz");
            CheckTracking(false, "SyncToAltAz");
            CheckAltAzSync(Altitude, Azimuth, "SyncToAltAz");
            inst.AtPark = false;
            inst.SyncToAltAzm(Azimuth, Altitude);
            inst.WaitMountPositionUpdated();
        }

        public void SyncToCoordinates(double RightAscension, double Declination)
        {
            var inst = GetInstance();
            var monitorItem = new MonitorEntry
            {
                Datetime = HiResDateTime.UtcNow,
                Device = MonitorDevice.Telescope,
                Category = MonitorCategory.Driver,
                Type = MonitorType.Information,
                Method = MethodBase.GetCurrentMethod()?.Name,
                Thread = Thread.CurrentThread.ManagedThreadId,
                Message = $"{Utilities.HoursToHMS(RightAscension, "h ", ":", "", 2)}|{Utilities.DegreesToDMS(Declination, "\u00B0 ", ":", "", 2)}"
            };
            MonitorLog.LogToMonitor(monitorItem);

            CheckCapability(inst.Settings.CanSync, "SyncToCoordinates");
            CheckRange(RightAscension, 0, 24, "SyncToCoordinates", "RightAscension");
            CheckRange(Declination, -90, 90, "SyncToCoordinates", "Declination");
            CheckParked("SyncToCoordinates");
            CheckTracking(true, "SyncToCoordinates");

            inst.TargetDec = Declination;
            inst.TargetRa = RightAscension;
            var a = Transforms.CoordTypeToInternal(RightAscension, Declination);
            CheckRaDecSync(a.X, a.Y, "SyncToCoordinates");

            inst.AtPark = false;
            inst.SyncToTargetRaDec();
            inst.WaitMountPositionUpdated();
        }

        public void SyncToTarget()
        {
            var inst = GetInstance();
            var monitorItem = new MonitorEntry
            {
                Datetime = HiResDateTime.UtcNow,
                Device = MonitorDevice.Telescope,
                Category = MonitorCategory.Driver,
                Type = MonitorType.Information,
                Method = MethodBase.GetCurrentMethod()?.Name,
                Thread = Thread.CurrentThread.ManagedThreadId,
                Message = $"{Utilities.HoursToHMS(inst.TargetRa, "h ", ":", "", 2)}|{Utilities.DegreesToDMS(inst.TargetDec, "\u00B0 ", ":", "", 2)}"
            };
            MonitorLog.LogToMonitor(monitorItem);

            CheckCapability(inst.Settings.CanSync, "SyncToTarget");
            CheckRange(inst.TargetRa, 0, 24, "SyncToTarget", "TargetRightAscension");
            CheckRange(inst.TargetDec, -90, 90, "SyncToTarget", "TargetDeclination");
            CheckParked("SyncToTarget");
            CheckTracking(true, "SyncToTarget");

            var a = Transforms.CoordTypeToInternal(RightAscension, Declination);
            CheckRaDecSync(a.X, a.Y, "SyncToTarget");

            inst.AtPark = false;
            inst.SyncToTargetRaDec();
            inst.WaitMountPositionUpdated();
        }

        #region Private Methods

        private static void CheckTrackingRate(string propertyOrMethod, DriveRate enumValue)
        {
            var success = Enum.IsDefined(typeof(DriveRate), enumValue);
            if (success) return;
            var monitorItem = new MonitorEntry
            { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Warning, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = FormattableString.Invariant($"{propertyOrMethod}|{enumValue}") };
            MonitorLog.LogToMonitor(monitorItem);

            throw new InvalidValueException("TrackingRate invalid");
        }

        private static void CheckRange(double value, double min, double max, string propertyOrMethod, string valueName)
        {
            if (double.IsNaN(value))
            {
                var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Data, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = $"{value}|{min}|{max}|{propertyOrMethod}|{valueName}" };
                MonitorLog.LogToMonitor(monitorItem);

                throw new ValueNotSetException(propertyOrMethod + ":" + valueName);
            }

            if (value < min || value > max)
            {
                var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Warning, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = $"{value}|{min}|{max}|{propertyOrMethod}|{valueName}" };
                MonitorLog.LogToMonitor(monitorItem);
                throw new InvalidValueException(propertyOrMethod, value.ToString(CultureInfo.CurrentCulture),
                    string.Format(CultureInfo.CurrentCulture, "{0}, {1} to {2}", valueName, min, max));
            }
        }

        private static void CheckRange(double value, double min, double max, string propertyOrMethod)
        {
            if (double.IsNaN(value))
            {
                var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Data, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = $"{value}|{min}|{max}|{propertyOrMethod}" };
                MonitorLog.LogToMonitor(monitorItem);

                throw new ValueNotSetException(propertyOrMethod);
            }

            if (value < min || value > max)
            {
                var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Warning, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = $"{value}|{min}|{max}|{propertyOrMethod}" };
                MonitorLog.LogToMonitor(monitorItem);

                throw new InvalidValueException(propertyOrMethod, value.ToString(CultureInfo.CurrentCulture),
                    string.Format(CultureInfo.CurrentCulture, "{0} to {1}", min, max));
            }
        }

        private void CheckVersionOne(string property, bool accessorSet)
        {
            CheckVersionOne(property);
            if (accessorSet)
            {
                //nothing
            }
            if (!GetInstance().Settings.VersionOne) return;

            var monitorItem = new MonitorEntry
            { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Information, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = $"{property}|{accessorSet}" };
            MonitorLog.LogToMonitor(monitorItem);

            throw new PropertyNotImplementedException(property, accessorSet);
        }

        private void CheckVersionOne(string property)
        {
            if (!GetInstance().Settings.VersionOne) return;

            var monitorItem = new MonitorEntry
            { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Information, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = $"{property}" };
            MonitorLog.LogToMonitor(monitorItem);

            throw new PropertyNotImplementedException(property);
        }

        private static void CheckCapability(bool capability, string method)
        {
            if (capability) return;

            var monitorItem = new MonitorEntry
            { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Warning, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = $"{method}" };
            MonitorLog.LogToMonitor(monitorItem);

            throw new MethodNotImplementedException(method);
        }

        private static void CheckCapability(bool capability, string property, bool setNotGet)
        {
            if (capability) return;

            var monitorItem = new MonitorEntry
            { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Warning, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = $"{property}|{setNotGet}" };
            MonitorLog.LogToMonitor(monitorItem);

            throw new PropertyNotImplementedException(property, setNotGet);
        }

        private void CheckParked(string property)
        {
            if (!GetInstance().AtPark) return;

            var monitorItem = new MonitorEntry
            { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Information, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = $"{property}" };
            MonitorLog.LogToMonitor(monitorItem);

            throw new ParkedException(property + @": Telescope parked");
        }

        /// <summary>
        /// Check slew rate for amount limit
        /// </summary>
        /// <param name="rate"></param>
        private void CheckRate(double rate)
        {
            var monitorItem = new MonitorEntry
            { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Information, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = $"{rate}" };
            MonitorLog.LogToMonitor(monitorItem);
            var deg = Conversions.ArcSec2Deg(rate);
            var slewSpeedEight = GetInstance().SlewSpeedEight;
            if (deg > slewSpeedEight || deg < -slewSpeedEight)
            {
                throw new InvalidValueException($"{rate} is out of limits");
            }
        }

        /// <summary>
        /// CheckRate in degrees against the axis rates
        /// </summary>
        /// <param name="axis"></param>
        /// <param name="rate"></param>
        private void CheckRate(TelescopeAxis axis, double rate)
        {
            var monitorItem = new MonitorEntry
            { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Information, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = $"{axis}|{rate}" };
            MonitorLog.LogToMonitor(monitorItem);

            var rates = AxisRates(axis);
            var ratesStr = string.Empty;
            foreach (Rate item in rates)
            {
                if (Math.Abs(rate) >= item.Minimum && Math.Abs(rate) <= item.Maximum)
                {
                    return;
                }
                ratesStr = $"{ratesStr}, {item.Minimum} to {item.Maximum}";
            }
            throw new InvalidValueException("MoveAxis", rate.ToString(CultureInfo.InvariantCulture), ratesStr);
        }

        /// <summary>
        /// Checks the slew type and tracking state and raises an exception if they don't match.
        /// </summary>
        /// <param name="raDecSlew">if set to <c>true</c> this is a Ra Dec slew is <c>false</c> an Alt Az slew.</param>
        /// <param name="method">The method name.</param>
        private void CheckTracking(bool raDecSlew, string method)
        {
            var tracking = GetInstance().Tracking;
            if (raDecSlew == tracking) return;

            var monitorItem = new MonitorEntry
            { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Information, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = FormattableString.Invariant($"{tracking}|{raDecSlew}|{method}") };
            MonitorLog.LogToMonitor(monitorItem);

            throw new InvalidOperationException($"{method} is not allowed when tracking is {tracking}");
        }

        /// <summary>
        /// Checks the sync is too far from the current position
        /// </summary>
        /// <param name="ra">Syncing Ra to check</param>
        /// <param name="dec">Syncing Dec to check</param>
        /// <param name="method">The method name</param>
        private void CheckRaDecSync(double ra, double dec, string method)
        {
            var pass = GetInstance().CheckRaDecSyncLimit(ra, dec);
            if (pass) return;

            var monitorItem = new MonitorEntry
            { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Warning, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = FormattableString.Invariant($"{ra}|{dec}|{method}") };
            MonitorLog.LogToMonitor(monitorItem);

            throw new InvalidOperationException($"{method} out of sync limits");
        }

        /// <summary>
        /// Checks the sync is too far from the current Alt/Az position
        /// </summary>
        /// <param name="alt">Syncing Ra to check</param>
        /// <param name="az">Syncing az to check</param>
        /// <param name="method">The method name</param>
        private void CheckAltAzSync(double alt, double az, string method)
        {
            var pass = GetInstance().CheckAltAzSyncLimit(alt, az);
            if (pass) return;

            var monitorItem = new MonitorEntry
            { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Information, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = FormattableString.Invariant($"{alt}|{az}|{method}") };
            MonitorLog.LogToMonitor(monitorItem);

            throw new InvalidOperationException($"{method} out of sync limits");
        }

        /// <summary>
        /// Validates whether the specified target coordinates are reachable based on the current alignment mode and
        /// slew type.
        /// </summary>
        /// <remarks>This method checks the reachability of the target coordinates based on the current
        /// alignment mode and slew type. If the alignment mode is not polar or the target is reachable, the method
        /// completes without error. Otherwise, an <see cref="InvalidOperationException"/> is thrown, indicating that
        /// the target is outside the hardware limits.</remarks>
        /// <param name="axisX">The X-axis coordinate of the target position.</param>
        /// <param name="axisY">The Y-axis coordinate of the target position.</param>
        /// <param name="slewType">The type of slew operation to perform, indicating the coordinate system used.</param>
        /// <exception cref="InvalidOperationException">Thrown if the target coordinates are outside the hardware limits for the specified slew type.</exception>
        private void CheckReachable(double axisX, double axisY, SlewType slewType)
        {
            string method;
            switch (slewType)
            {
                case SlewType.SlewAltAz:
                    method = "SlewToAltAz";
                    break;
                case SlewType.SlewRaDec:
                    method = "SlewToCoordinates";
                    break;
                default:
                    method = "Unknown Slew Type";
                    break;
            }
            // Only check for polar alignment mode
            var inst = GetInstance();
            if (inst.Settings.AlignmentMode != AlignmentMode.Polar ||
                inst.IsTargetReachable(new[] { axisX, axisY }, slewType)) return;

            var monitorItem = new MonitorEntry
            { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Warning, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Thread.CurrentThread.ManagedThreadId, Message = FormattableString.Invariant($"{axisX}|{axisY}|{slewType}") };
            MonitorLog.LogToMonitor(monitorItem);

            throw new InvalidOperationException($"{method} outside hardware limits");
        }

        /// <summary>
        /// Allows the UI and Server time for the Event to update positions from the mount
        /// </summary>
        /// <para>additional milliseconds</para>
        /// <returns></returns>
        private void DelayInterval(int additional = 0)
        {
            var inst = GetInstance();
            var delay = additional;
            switch (inst.Settings.Mount)
            {
                case MountType.Simulator:
                    delay += inst.Settings.DisplayInterval;
                    break;
                case MountType.SkyWatcher:
                    delay += 20;  // some go tos have been off .10 to .70 seconds, not sure exactly why
                    delay += inst.Settings.DisplayInterval;
                    break;
            }
            //Thread.Sleep(delay);
            var sw = Stopwatch.StartNew();
            while (sw.Elapsed.TotalMilliseconds < delay) { }
            sw.Stop();
        }

        private static void LogMessage(MonitorType type, string method, string msg)
        {
            var monitorItem = new MonitorEntry
            { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = type, Method = $"{method}", Thread = Thread.CurrentThread.ManagedThreadId, Message = $"{msg}" };
            MonitorLog.LogToMonitor(monitorItem);
        }

        #endregion
    }
}
