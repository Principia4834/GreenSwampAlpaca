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
        private readonly Alpaca.MountControl.Mount _mount;

        #endregion

        /// <summary>
        /// Constructor with device number for multi-instance support
        /// </summary>
        /// <param name="deviceNumber">Device number (0-based)</param>
        /// <param name="mount">Mount instance for this device</param>
        public Telescope(int deviceNumber, Alpaca.MountControl.Mount mount)
        {
            _mount = mount ?? throw new ArgumentNullException(nameof(mount));

            try
            {
                var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Information, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Environment.CurrentManagedThreadId, Message = $" Started|DeviceNumber:{deviceNumber}" };
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
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Error, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Environment.CurrentManagedThreadId, Message = $"Error|DeviceNumber:{deviceNumber}|{ex.Message}|{ex.StackTrace}" };
                MonitorLog.LogToMonitor(monitorItem);

                throw;
            }
        }

        #region Public Properties
        public AlignmentMode AlignmentMode
        {
            get
            {
                CheckCapability(_mount.Settings.CanAlignMode, "AlignmentMode");
                var r = _mount.Settings.AlignmentMode;

                var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Data, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Environment.CurrentManagedThreadId, Message = $"  {r}" };
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
                CheckCapability(_mount.Settings.CanAltAz, "Altitude", false);
                var r = _mount.Altitude;

                var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Data, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Environment.CurrentManagedThreadId, Message = $"  {r}" };
                MonitorLog.LogToMonitor(monitorItem);

                return r;
            }
        }

        public double ApertureArea
        {
            get
            {
                CheckCapability(_mount.Settings.CanOptics, "ApertureArea", false);
                var r = _mount.Settings.ApertureArea;

                var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Data, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Environment.CurrentManagedThreadId, Message = $"  {r}" };
                MonitorLog.LogToMonitor(monitorItem);

                return r;
            }
        }

        public double ApertureDiameter
        {
            get
            {
                CheckCapability(_mount.Settings.CanOptics, "ApertureDiameter", false);
                var r = _mount.Settings.ApertureDiameter;

                var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Data, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Environment.CurrentManagedThreadId, Message = $"  {r}" };
                MonitorLog.LogToMonitor(monitorItem);

                return r;
            }
        }

        public bool AtHome
        {
            get
            {
                var r = _mount.AtHome;

                var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Data, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Environment.CurrentManagedThreadId, Message = $"  {r}" };
                MonitorLog.LogToMonitor(monitorItem);

                return r;
            }
        }

        public bool AtPark
        {
            get
            {
                var r = _mount.Settings.AtPark;

                var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Data, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Environment.CurrentManagedThreadId, Message = $"  {r}" };
                MonitorLog.LogToMonitor(monitorItem);

                return r;
            }
        }

        public double Azimuth
        {
            get
            {
                CheckCapability(_mount.Settings.CanAltAz, "Azimuth", false);
                var r = _mount.Azimuth;

                var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Data, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Environment.CurrentManagedThreadId, Message = $" {r}" };
                MonitorLog.LogToMonitor(monitorItem);

                return r;
            }
        }

        public bool CanFindHome
        {
            get
            {
                var r = _mount.Settings.CanFindHome;

                var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Data, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Environment.CurrentManagedThreadId, Message = $" {r}" };
                MonitorLog.LogToMonitor(monitorItem);

                return r;
            }
        }

        public bool CanPark
        {
            get
            {
                var r = _mount.Settings.CanPark;

                var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Data, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Environment.CurrentManagedThreadId, Message = $" {r}" };
                MonitorLog.LogToMonitor(monitorItem);

                return r;
            }
        }

        public bool CanPulseGuide
        {
            get
            {
                var r = _mount.Settings.CanPulseGuide;

                var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Data, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Environment.CurrentManagedThreadId, Message = $" {r}" };
                MonitorLog.LogToMonitor(monitorItem);

                return r;
            }
        }

        public bool CanSetDeclinationRate
        {
            get
            {
                var r = _mount.Settings.CanSetDeclinationRate;

                var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Data, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Environment.CurrentManagedThreadId, Message = $" {r}" };
                MonitorLog.LogToMonitor(monitorItem);

                return r;
            }
        }

        public bool CanSetGuideRates
        {
            get
            {
                var r = _mount.Settings.CanSetGuideRates;

                var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Data, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Environment.CurrentManagedThreadId, Message = $" {r}" };
                MonitorLog.LogToMonitor(monitorItem);

                return r;
            }
        }

        public bool CanSetPark
        {
            get
            {
                var r = _mount.Settings.CanSetPark;

                var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Data, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Environment.CurrentManagedThreadId, Message = $" {r}" };
                MonitorLog.LogToMonitor(monitorItem);

                return r;
            }
        }

        public bool CanSetPierSide
        {
            get
            {
                var r = _mount.Settings.CanSetPierSide;

                var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Data, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Environment.CurrentManagedThreadId, Message = $" {r}" };
                MonitorLog.LogToMonitor(monitorItem);

                return r;
            }
        }

        public bool CanSetRightAscensionRate
        {
            get
            {
                var r = _mount.Settings.CanSetRightAscensionRate;

                var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Data, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Environment.CurrentManagedThreadId, Message = $" {r}" };
                MonitorLog.LogToMonitor(monitorItem);

                return r;
            }
        }

        public bool CanSetTracking
        {
            get
            {
                var r = _mount.Settings.CanSetTracking;

                var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Data, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Environment.CurrentManagedThreadId, Message = $" {r}" };
                MonitorLog.LogToMonitor(monitorItem);

                return r;
            }
        }

        public bool CanSlew
        {
            get
            {
                var r = _mount.Settings.CanSlew;

                var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Data, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Environment.CurrentManagedThreadId, Message = $" {r}" };
                MonitorLog.LogToMonitor(monitorItem);

                return r;
            }
        }

        public bool CanSlewAltAz
        {
            get
            {
                var r = _mount.Settings.CanSlewAltAz;

                var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Data, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Environment.CurrentManagedThreadId, Message = $" {r}" };
                MonitorLog.LogToMonitor(monitorItem);

                return r;
            }
        }

        public bool CanSlewAltAzAsync
        {
            get
            {
                var r = _mount.Settings.CanSlewAltAzAsync;

                var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Data, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Environment.CurrentManagedThreadId, Message = $" {r}" };
                MonitorLog.LogToMonitor(monitorItem);

                return r;
            }
        }

        public bool CanSlewAsync
        {
            get
            {
                var r = _mount.Settings.CanSlewAsync;

                var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Data, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Environment.CurrentManagedThreadId, Message = $" {r}" };
                MonitorLog.LogToMonitor(monitorItem);

                return r;
            }
        }

        public bool CanSync
        {
            get
            {
                var r = _mount.Settings.CanSync;

                var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Data, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Environment.CurrentManagedThreadId, Message = $" {r}" };
                MonitorLog.LogToMonitor(monitorItem);

                return r;
            }
        }

        public bool CanSyncAltAz
        {
            get
            {
                var r = _mount.Settings.CanSyncAltAz;

                var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Data, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Environment.CurrentManagedThreadId, Message = $" {r}" };
                MonitorLog.LogToMonitor(monitorItem);

                return r;
            }
        }

        public bool CanUnpark
        {
            get
            {
                var r = _mount.Settings.CanUnPark;

                var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Data, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Environment.CurrentManagedThreadId, Message = $" {r}" };
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
                var r = _mount.IsConnected;
                var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Debug, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Environment.CurrentManagedThreadId, Message = $" {r}" };
                MonitorLog.LogToMonitor(monitorItem);
                return r;
            }
            set
            {
                var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Information, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Environment.CurrentManagedThreadId, Message = $" {value}|{_objectId}" };
                MonitorLog.LogToMonitor(monitorItem);
                _mount.SetConnected(_objectId, value);
            }
        }

        /// <remarks>
        /// https://ascom-standards.org/newdocs/telescope.html#Telescope.Connecting
        /// </remarks>
        public bool Connecting
        {
            get
            {
                var r = _mount.Connecting;
                var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Information, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Environment.CurrentManagedThreadId, Message = $" {r}" };
                MonitorLog.LogToMonitor(monitorItem);
                return r;
            }
        }

        public double Declination
        {
            get
            {
                CheckCapability(_mount.Settings.CanEquatorial, "Declination", false);
                var dec = _mount.DeclinationXForm;

                var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Data, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Environment.CurrentManagedThreadId, Message = $"XForm|{Utilities.DegreesToDMS(dec, "\u00B0 ", ":", "", 2)}" };
                MonitorLog.LogToMonitor(monitorItem);

                monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Data, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Environment.CurrentManagedThreadId, Message = $"Internal|{Utilities.DegreesToDMS(_mount.Declination, "\u00B0 ", ":", "", 2)}" };
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
                var r = (TrackingRate == DriveRate.Sidereal) ? _mount.RateDecOrg : 0.0;

                var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Data, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Environment.CurrentManagedThreadId, Message = $"{r}" };
                MonitorLog.LogToMonitor(monitorItem);

                return r;
            }
            set
            {
                var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Information, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Environment.CurrentManagedThreadId, Message = $"{value}" };
                MonitorLog.LogToMonitor(monitorItem);

                CheckCapability(_mount.Settings.CanSetEquRates, "DeclinationRate", true);
                CheckRate(value);
                if (TrackingRate != DriveRate.Sidereal)
                {
                    throw new ASCOM.InvalidOperationException(" DeclinationRate - cannot set rate because TrackingRate is not Sidereal");
                }
                _mount.RateDecOrg = value;
                _mount.SetRateDec(Conversions.ArcSec2Deg(value));
            }
        }

        public string Description
        {
            get
            {
                string r = _mount.Settings.InstrumentDescription;

                var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Data, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Environment.CurrentManagedThreadId, Message = $"{r}" };
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

                    // Add one entry for each operational state, direct access to SkyServer variables, optimise response time to the client <0.1 seconds
                    try { deviceState.Add(new StateValue(nameof(ITelescopeV4.Altitude), _mount.Altitude)); } catch (Exception ex) { LogMessage(MonitorType.Warning, "DeviceState", ex.Message); }
                    try { deviceState.Add(new StateValue(nameof(ITelescopeV4.AtHome), _mount.AtHome)); } catch (Exception ex) { LogMessage(MonitorType.Warning, "DeviceState", ex.Message); }
                    try { deviceState.Add(new StateValue(nameof(ITelescopeV4.AtPark), _mount.AtPark)); } catch (Exception ex) { LogMessage(MonitorType.Warning, "DeviceState", ex.Message); }
                    try { deviceState.Add(new StateValue(nameof(ITelescopeV4.Azimuth), _mount.Azimuth)); } catch (Exception ex) { LogMessage(MonitorType.Warning, "DeviceState", ex.Message); }
                    try { deviceState.Add(new StateValue(nameof(ITelescopeV4.Declination), _mount.Declination)); } catch (Exception ex) { LogMessage(MonitorType.Warning, "DeviceState", ex.Message); }
                    try { deviceState.Add(new StateValue(nameof(ITelescopeV4.IsPulseGuiding), _mount.IsPulseGuiding)); } catch (Exception ex) { LogMessage(MonitorType.Warning, "DeviceState", ex.Message); }
                    try { deviceState.Add(new StateValue(nameof(ITelescopeV4.RightAscension), _mount.RightAscension)); } catch (Exception ex) { LogMessage(MonitorType.Warning, "DeviceState", ex.Message); }
                    try { deviceState.Add(new StateValue(nameof(ITelescopeV4.SideOfPier), _mount.SideOfPier)); } catch (Exception ex) { LogMessage(MonitorType.Warning, "DeviceState", ex.Message); }
                    try { deviceState.Add(new StateValue(nameof(ITelescopeV4.SiderealTime), _mount.SiderealTime)); } catch (Exception ex) { LogMessage(MonitorType.Warning, "DeviceState", ex.Message); }
                    try { deviceState.Add(new StateValue(nameof(ITelescopeV4.Slewing), _mount.IsSlewing)); } catch (Exception ex) { LogMessage(MonitorType.Warning, "DeviceState", ex.Message); }
                    try { deviceState.Add(new StateValue(nameof(ITelescopeV4.Tracking), _mount.Tracking || _mount.SlewState == SlewType.SlewRaDec)); } catch (Exception ex) { LogMessage(MonitorType.Warning, "DeviceState", ex.Message); }
                    try { deviceState.Add(new StateValue(nameof(ITelescopeV4.UTCDate), HiResDateTime.UtcNow)); } catch (Exception ex) { LogMessage(MonitorType.Warning, "DeviceState", ex.Message); }
                    try { deviceState.Add(new StateValue(DateTime.Now)); } catch (Exception ex) { LogMessage(MonitorType.Warning, "DeviceState", ex.Message); }

                    var r = new List<StateValue>(deviceState);

                    for (var index = 0; index < r.Count; index++)
                    {
                        var a = r[index];
                        msg += $"{a.Name}-{a.Value}|";
                    }

                    var monitorItem = new MonitorEntry
                    { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Information, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Environment.CurrentManagedThreadId, Message = $"{msg}" };
                    MonitorLog.LogToMonitor(monitorItem);

                    return r;
                }
                catch (Exception ex)
                {
                    var monitorItem = new MonitorEntry
                    { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Error, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Environment.CurrentManagedThreadId, Message = $"{msg}, {ex.Message}" };
                    MonitorLog.LogToMonitor(monitorItem);
                    throw;
                }
            }
        }

        public bool DoesRefraction
        {
            get
            {
                var r = _mount.Settings.Refraction;

                var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Data, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Environment.CurrentManagedThreadId, Message = $"{r}" };
                MonitorLog.LogToMonitor(monitorItem);

                return r;
            }
            set
            {
                var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Information, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Environment.CurrentManagedThreadId, Message = $"{value}" };
                MonitorLog.LogToMonitor(monitorItem);

                _mount.Settings.Refraction = value;
            }
        }

        public string DriverInfo
        {
            get
            {
                var asm = Assembly.GetExecutingAssembly();
                var r = asm.FullName;

                var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Data, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Environment.CurrentManagedThreadId, Message = $"{r}" };
                MonitorLog.LogToMonitor(monitorItem);

                return r;
            }
        }

        public string DriverVersion
        {
            get
            {
                var asm = Assembly.GetExecutingAssembly();
                var r = asm.GetName().Version.ToString();

                var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Data, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Environment.CurrentManagedThreadId, Message = $"{r}" };
                MonitorLog.LogToMonitor(monitorItem);

                return r;
            }
        }

        public EquatorialCoordinateType EquatorialSystem
        {
            get
            {

                var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Data, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Environment.CurrentManagedThreadId, Message = $"{_mount.Settings.EquatorialCoordinateType}" };
                MonitorLog.LogToMonitor(monitorItem);

                return _mount.Settings.EquatorialCoordinateType;
            }
        }

        public double FocalLength
        {
            get
            {
                CheckCapability(_mount.Settings.CanOptics, "FocalLength", false);
                var r = _mount.Settings.FocalLength;

                var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Data, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Environment.CurrentManagedThreadId, Message = $"{r}" };
                MonitorLog.LogToMonitor(monitorItem);

                return r;
            }
        }

        public double GuideRateDeclination
        {
            get
            {
                var r = _mount.GuideRateDec;

                var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Data, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Environment.CurrentManagedThreadId, Message = $"{r}" };
                MonitorLog.LogToMonitor(monitorItem);

                return r;
            }
            set
            {
                var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Information, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Environment.CurrentManagedThreadId, Message = $"{value}" };
                MonitorLog.LogToMonitor(monitorItem);

                CheckRange(value, 0.0, 0.5, "GuideRateDeclination");
                _mount.GuideRateDec = value;
            }
        }

        public double GuideRateRightAscension
        {
            get
            {
                var r = _mount.GuideRateRa;

                var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Data, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Environment.CurrentManagedThreadId, Message = $"{r}" };
                MonitorLog.LogToMonitor(monitorItem);

                return r;
            }
            set
            {
                var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Information, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Environment.CurrentManagedThreadId, Message = $"{value}" };
                MonitorLog.LogToMonitor(monitorItem);

                CheckRange(value, 0.0, 0.5, "GuideRateRightAscension");
                _mount.GuideRateRa = value;
            }
        }

        public short InterfaceVersion
        {
            get
            {
                var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Data, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Environment.CurrentManagedThreadId, Message = "4" };
                MonitorLog.LogToMonitor(monitorItem);

                return 4;
            }
        }

        public bool IsPulseGuiding
        {
            get
            {
                CheckCapability(_mount.Settings.CanPulseGuide, "IsPulseGuiding", false);
                var r = _mount.IsPulseGuiding;

                var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Data, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Environment.CurrentManagedThreadId, Message = $"{r}" };
                MonitorLog.LogToMonitor(monitorItem);

                return r;
            }
        }

        public string Name
        {
            get
            {
                string r = _mount.Settings.InstrumentName;

                var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Data, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Environment.CurrentManagedThreadId, Message = $"{r}" };
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
                CheckCapability(_mount.Settings.CanEquatorial, "RightAscension", false);
                var ra = _mount.RightAscensionXForm;

                var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Data, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Environment.CurrentManagedThreadId, Message = $"XForm|{Utilities.HoursToHMS(ra, "h ", ":", "", 2)}" };
                MonitorLog.LogToMonitor(monitorItem);


                monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Data, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Environment.CurrentManagedThreadId, Message = $"Internal|{Utilities.HoursToHMS(_mount.RightAscension, "h ", ":", "", 2)}" };
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
                var r = (TrackingRate == DriveRate.Sidereal) ? _mount.RateRaOrg : 0.0;

                var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Data, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Environment.CurrentManagedThreadId, Message = $"{r}" };
                MonitorLog.LogToMonitor(monitorItem);

                return r;
            }
            set
            {

                var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Data, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Environment.CurrentManagedThreadId, Message = $"{value}" };
                MonitorLog.LogToMonitor(monitorItem);

                CheckCapability(_mount.Settings.CanSetEquRates, "RightAscensionRate ", true);
                CheckRate(value);
                if (TrackingRate != DriveRate.Sidereal)
                {
                    throw new InvalidOperationException(" RightAscensionRate - cannot set rate because TrackingRate is not Sidereal");
                }
                _mount.RateRaOrg = value;
                _mount.SetRateRa(Conversions.ArcSec2Deg(Conversions.SideSec2ArcSec(value)));
            }
        }

        public PointingState SideOfPier
        {
            get
            {
                var r = _mount.SideOfPier;

                var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Data, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Environment.CurrentManagedThreadId, Message = $"{r}" };
                MonitorLog.LogToMonitor(monitorItem);

                return r;
            }
            set
            {
                CheckCapability(_mount.Settings.CanSetPierSide, "SideOfPier", true);
                MonitorEntry monitorItem;
                if (value == _mount.SideOfPier)
                {
                    monitorItem = new MonitorEntry
                    { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Information, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Environment.CurrentManagedThreadId, Message = "No Change Needed" };
                    MonitorLog.LogToMonitor(monitorItem);

                    return;
                }
                _mount.SetSideOfPier(value);

                monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Data, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Environment.CurrentManagedThreadId, Message = $"{value}" };
                MonitorLog.LogToMonitor(monitorItem);

            }
        }

        public double SiderealTime
        {
            get
            {
                CheckCapability(_mount.Settings.CanSiderealTime, "SiderealTime", false);
                var r = _mount.SiderealTime;

                var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Data, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Environment.CurrentManagedThreadId, Message = $"{Utilities.HoursToHMS(r)}" };
                MonitorLog.LogToMonitor(monitorItem);

                return r;
            }
        }

        public double SiteElevation
        {
            get
            {
                CheckCapability(_mount.Settings.CanLatLongElev, "SiteElevation", false);
                var r = _mount.Settings.Elevation;

                var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Data, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Environment.CurrentManagedThreadId, Message = $"{r}" };
                MonitorLog.LogToMonitor(monitorItem);

                return r;
            }
            set
            {
                var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Information, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Environment.CurrentManagedThreadId, Message = $"{value}" };
                MonitorLog.LogToMonitor(monitorItem);

                CheckCapability(_mount.Settings.CanLatLongElev, "SiteElevation", true);
                CheckRange(value, -300, 10000, "SiteElevation");
                _mount.Settings.Elevation = value;
            }
        }

        public double SiteLatitude
        {
            get
            {
                CheckCapability(_mount.Settings.CanLatLongElev, "SiteLatitude", false);
                var r = _mount.Settings.Latitude;

                var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Data, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Environment.CurrentManagedThreadId, Message = $"{r}" };
                MonitorLog.LogToMonitor(monitorItem);

                return r;
            }
            set
            {
                var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Information, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Environment.CurrentManagedThreadId, Message = $"{value}" };
                MonitorLog.LogToMonitor(monitorItem);

                CheckCapability(_mount.Settings.CanLatLongElev, "SiteLatitude", true);
                CheckRange(value, -90, 90, "SiteLatitude");
                _mount.Settings.Latitude = value;
            }
        }

        public double SiteLongitude
        {
            get
            {
                CheckCapability(_mount.Settings.CanLatLongElev, "SiteLongitude", false);
                var r = _mount.Settings.Longitude;

                var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Data, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Environment.CurrentManagedThreadId, Message = $"{r}" };
                MonitorLog.LogToMonitor(monitorItem);

                return r;
            }
            set
            {
                var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Information, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Environment.CurrentManagedThreadId, Message = $"{value}" };
                MonitorLog.LogToMonitor(monitorItem);

                CheckCapability(_mount.Settings.CanLatLongElev, "SiteLongitude", true);
                CheckRange(value, -180, 180, "SiteLongitude");
                _mount.Settings.Longitude = value;
            }
        }

        public bool Slewing
        {
            get
            {
                var r = _mount.IsSlewing;

                var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Data, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Environment.CurrentManagedThreadId, Message = $"{r}" };
                MonitorLog.LogToMonitor(monitorItem);

                return r;
            }
        }

        public short SlewSettleTime
        {
            get
            {
                var r = (short)(_mount.SlewSettleTime);

                var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Data, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Environment.CurrentManagedThreadId, Message = $"{r}" };
                MonitorLog.LogToMonitor(monitorItem);

                return r;
            }
            set
            {
                var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Information, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Environment.CurrentManagedThreadId, Message = $"{value}" };
                MonitorLog.LogToMonitor(monitorItem);

                CheckRange(value, 0, 100, "SlewSettleTime");
                var r = value;
                _mount.SlewSettleTime = r;
            }
        }

        public double TargetDeclination
        {
            get
            {
                CheckCapability(_mount.Settings.CanSlew, "TargetDeclination", false);
                CheckRange(_mount.TargetDec, -90, 90, "TargetDeclination");
                var r = _mount.TargetDec;

                var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Data, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Environment.CurrentManagedThreadId, Message = $"{r}" };
                MonitorLog.LogToMonitor(monitorItem);

                return r;
            }
            set
            {
                var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Information, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Environment.CurrentManagedThreadId, Message = $"{Utilities.DegreesToDMS(value, "\u00B0 ", ":", "", 2)}" };
                MonitorLog.LogToMonitor(monitorItem);

                CheckCapability(_mount.Settings.CanSlew, "TargetDeclination", true);
                CheckRange(value, -90, 90, "TargetDeclination");

                _mount.TargetDec = value;
            }
        }

        public double TargetRightAscension
        {
            get
            {
                CheckCapability(_mount.Settings.CanSlew, "TargetRightAscension", false);
                CheckRange(_mount.TargetRa, 0, 24, "TargetRightAscension");
                var r = _mount.TargetRa;

                var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Data, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Environment.CurrentManagedThreadId, Message = $"{r}" };
                MonitorLog.LogToMonitor(monitorItem);

                return r;
            }
            set
            {
                var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Information, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Environment.CurrentManagedThreadId, Message = $"{Utilities.HoursToHMS(value, "h ", ":", "", 2)}" };
                MonitorLog.LogToMonitor(monitorItem);

                CheckCapability(_mount.Settings.CanSlew, "TargetRightAscension", true);
                CheckRange(value, 0, 24, "TargetRightAscension");

                _mount.TargetRa = value;
            }
        }

        public bool Tracking
        {
            get
            {
                var r = _mount.Tracking || _mount.SlewState == SlewType.SlewRaDec;

                var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Data, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Environment.CurrentManagedThreadId, Message = $"{r}" };
                MonitorLog.LogToMonitor(monitorItem);

                return r;
            }
            set
            {
                if (value & _mount.AtPark) { CheckParked("Cannot enable tracking at park"); }

                var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Information, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Environment.CurrentManagedThreadId, Message = $"{value}" };
                MonitorLog.LogToMonitor(monitorItem);

                _mount.ApplyTracking(value);
            }
        }

        public DriveRate TrackingRate
        {
            get
            {
                var r = _mount.Settings.TrackingRate;

                var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Data, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Environment.CurrentManagedThreadId, Message = $"{r}" };
                MonitorLog.LogToMonitor(monitorItem);

                return r;
            }
            set
            {
                var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Information, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Environment.CurrentManagedThreadId, Message = $"{value}" };
                MonitorLog.LogToMonitor(monitorItem);

                CheckTrackingRate("TrackingRate", value);
                _mount.Settings.TrackingRate = value;
            }
        }

        public ITrackingRates TrackingRates
        {
            get
            {
                MonitorEntry monitorItem;
                if (_mount.Settings.CanTrackingRates)
                {
                    monitorItem = new MonitorEntry
                    { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Data, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Environment.CurrentManagedThreadId, Message = $"{_mTrackingRates}" };
                    MonitorLog.LogToMonitor(monitorItem);

                    return _mTrackingRates;
                }
                monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Information, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Environment.CurrentManagedThreadId, Message = $"{_mTrackingRatesSimple}" };
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
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Data, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Environment.CurrentManagedThreadId, Message = $"{r}" };
                MonitorLog.LogToMonitor(monitorItem);

                return r;
            }
            set
            {

                //var r = value.Subtract(HiResDateTime.UtcNow);
                //if (Math.Abs(r.TotalMilliseconds) < 100) r = new TimeSpan();
                //SkySettings.UTCDateOffset = r;

                var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Information, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Environment.CurrentManagedThreadId, Message = $"{value}" };
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
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Data, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Environment.CurrentManagedThreadId, Message = " Started" };
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
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Information, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Environment.CurrentManagedThreadId, Message = FormattableString.Invariant($"{command},{raw}") };
                MonitorLog.LogToMonitor(monitorItem);

                if (string.IsNullOrWhiteSpace(command)) { throw new MethodNotImplementedException("CommandString"); }

                if (_mCommandStrings == null) { _mCommandStrings = new CommandStrings(); }
                return CommandStrings.ProcessCommand(_mount, command, raw);
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
                    Thread = Environment.CurrentManagedThreadId,
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
            { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Information, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Environment.CurrentManagedThreadId, Message = "true" };
            MonitorLog.LogToMonitor(monitorItem);
        }

        public void Disconnect()
        {
            Connected = false;
            var monitorItem = new MonitorEntry
            { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Information, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Environment.CurrentManagedThreadId, Message = "false" };
            MonitorLog.LogToMonitor(monitorItem);
        }

        public void AbortSlew()
        {
            var monitorItem = new MonitorEntry
            { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Information, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Environment.CurrentManagedThreadId, Message = " Started" };
            MonitorLog.LogToMonitor(monitorItem);

            CheckParked("AbortSlew");
            _mount.AbortSlewAsync(true);
        }

        public IAxisRates AxisRates(TelescopeAxis Axis)
        {
            var monitorItem = new MonitorEntry
            { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Data, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Environment.CurrentManagedThreadId, Message = $"   {Axis}" };
            MonitorLog.LogToMonitor(monitorItem);

            switch (Axis)
            {
                case TelescopeAxis.Primary:
                    return new AxisRates(TelescopeAxis.Primary, _mount);
                case TelescopeAxis.Secondary:
                    return new AxisRates(TelescopeAxis.Secondary, _mount);
                case TelescopeAxis.Tertiary:
                    return new AxisRates(TelescopeAxis.Tertiary, _mount);
                default:
                    return null;
            }
        }

        public bool CanMoveAxis(TelescopeAxis Axis)
        {
            var r = _mount.CanMoveAxis(Axis);

            var monitorItem = new MonitorEntry
            { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Data, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Environment.CurrentManagedThreadId, Message = $" {r}" };
            MonitorLog.LogToMonitor(monitorItem);

            return r;
        }

        public PointingState DestinationSideOfPier(double RightAscension, double Declination)
        {
            var monitorItem = new MonitorEntry
            { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Information, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Environment.CurrentManagedThreadId, Message = $"RA|{RightAscension}|Dec|{Declination}" };
            MonitorLog.LogToMonitor(monitorItem);

            var raDec = Transforms.CoordTypeToInternal(RightAscension, Declination);
            CheckRange(raDec.X, 0, 24, "SlewToCoordinatesAsync", "RightAscension");
            CheckRange(raDec.Y, -90, 90, "SlewToCoordinatesAsync", "Declination");
            CheckReachable(raDec.X, raDec.Y, SlewType.SlewRaDec);
            var r = _mount.DetermineSideOfPier(raDec.X, raDec.Y);
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
                Thread = Environment.CurrentManagedThreadId,
                Message = $" ActionName:{actionName}, ActionParameters:'{actionParameters}'"
            };
            MonitorLog.LogToMonitor(monitorItem);

            switch (actionName)
            {
                // ReSharper disable once StringLiteralTypo
                case string str when str.Equals("telescope:setparkposition", StringComparison.InvariantCultureIgnoreCase):
                    if (_mount.IsMountRunning == false) { throw new NotConnectedException("Mount Not Connected"); }
                    var found = _mount.Settings.ParkPositions.Find(x => string.Equals(x.Name, actionParameters, StringComparison.InvariantCultureIgnoreCase));
                    if (found == null)
                    {
                        var parkPositions = _mount.Settings.ParkPositions.OrderBy(parkPosition => parkPosition.Name).ToList();
                        var output = JsonConvert.SerializeObject(parkPositions);
                        throw new Exception($"Param Not Found:'{actionParameters}', {output}");
                    }
                    _mount.ParkSelected = found;
                    return found.Name;
                default:
                    throw new ActionNotImplementedException($"Not Found:'{actionName}'");
            }
        }

        public void FindHome()
        {
            CheckCapability(_mount.Settings.CanFindHome, "FindHome");
            CheckParked("FindHome");

            var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Information, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Environment.CurrentManagedThreadId, Message = "Started" };
            MonitorLog.LogToMonitor(monitorItem);

            // Block until ExecuteSlewAsync returns (after IsSlewing=true is set, before movement completes).
            // This ensures Slewing=true is visible to polling clients before the HTTP response is sent.
            _mount.GoToHome().GetAwaiter().GetResult();
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
                Thread = Environment.CurrentManagedThreadId,
                Message = $"{Axis}|{Rate}"
            };
            MonitorLog.LogToMonitor(monitorItem);

            CheckRate(Axis, Rate);
            if (!CanMoveAxis(Axis)){throw new MethodNotImplementedException("CanMoveAxis " + Enum.GetName(typeof(TelescopeAxis), Axis));}
            CheckParked("MoveAxis");

            switch (Axis)
            {
                case TelescopeAxis.Primary:
                    _mount.RateMovePrimaryAxis = Rate;
                    break;
                case TelescopeAxis.Secondary:
                    _mount.RateMoveSecondaryAxis = Rate;
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
            CheckCapability(_mount.Settings.CanPark, "Park");

            var monitorItem = new MonitorEntry
            {
                Datetime = HiResDateTime.UtcNow,
                Device = MonitorDevice.Telescope,
                Category = MonitorCategory.Driver,
                Type = MonitorType.Information,
                Method = MethodBase.GetCurrentMethod()?.Name,
                Thread = Environment.CurrentManagedThreadId,
                Message = "Started"
            };

            if (_mount.AtPark)
            {
                monitorItem.Message = "Already Parked";
                MonitorLog.LogToMonitor(monitorItem);
            }
            else
            {
                MonitorLog.LogToMonitor(monitorItem);

                // Block until ExecuteSlewAsync returns (after IsSlewing=true is set, before movement completes).
                // This ensures Slewing=true is visible to polling clients before the HTTP response is sent.
                _mount.GoToParkAsync().GetAwaiter().GetResult();
            }
        }

        public void PulseGuide(GuideDirection Direction, int Duration)
        {
            try
            {
                if (_mount.AtPark) { throw new ParkedException(); }

                var monitorItem = new MonitorEntry
                    { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Data, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Environment.CurrentManagedThreadId, Message = FormattableString.Invariant($"{Direction},{Duration}") };
                MonitorLog.LogToMonitor(monitorItem);

                CheckCapability(_mount.Settings.CanPulseGuide, "PulseGuide");
                CheckRange(Duration, 0, 30000, "PulseGuide", "Duration");

                switch (Direction)
                {
                    case GuideDirection.North:
                    case GuideDirection.South:
                        _mount.IsPulseGuidingDec = true;
                        break;
                    case GuideDirection.East:
                    case GuideDirection.West:
                        _mount.IsPulseGuidingRa = true;
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(Direction), Direction, null);
                }

                _mount.PulseGuide(Direction, Duration, 0);
            }
            catch (Exception e)
            {
                _mount.IsPulseGuidingRa = false;
                _mount.IsPulseGuidingDec = false;
                var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Warning, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Environment.CurrentManagedThreadId, Message = FormattableString.Invariant($"{e.Message}") };
                MonitorLog.LogToMonitor(monitorItem);
                throw;
            }
        }

        public void SlewToAltAz(double Azimuth, double Altitude)
        {
            var monitorItem = new MonitorEntry
            { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Information, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Environment.CurrentManagedThreadId, Message = $"{Utilities.DegreesToDMS(Azimuth, "\u00B0 ", ":", "", 2)}|{Utilities.DegreesToDMS(Altitude, "\u00B0 ", ":", "", 2)}" };
            MonitorLog.LogToMonitor(monitorItem);

            CheckCapability(_mount.Settings.CanSlewAltAz, "SlewToAltAz");
            CheckParked("SlewToAltAz");
            CheckTracking(false, "SlewToAltAz");
            CheckRange(Azimuth, 0, 360, "SlewToAltAz", "azimuth");
            CheckRange(Altitude, -90, 90, "SlewToAltAz", "Altitude");
            _mount.SlewAltAz(Altitude, Azimuth);
            Thread.Sleep(250);
            while (_mount.SlewState == SlewType.SlewAltAz || _mount.SlewState == SlewType.SlewSettle)
            {
                Thread.Sleep(10);
            }
            DelayInterval();
        }

        public void SlewToAltAzAsync(double Azimuth, double Altitude)
        {
            var monitorItem = new MonitorEntry
            { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Information, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Environment.CurrentManagedThreadId, Message = $"{Utilities.DegreesToDMS(Azimuth, "\u00B0 ", ":", "", 2)}|{Utilities.DegreesToDMS(Altitude, "\u00B0 ", ":", "", 2)}" };
            MonitorLog.LogToMonitor(monitorItem);

            CheckCapability(_mount.Settings.CanSlewAltAzAsync, "SlewToAltAzAsync");
            CheckParked("SlewToAltAz");
            CheckTracking(false, "SlewToAltAzAsync");
            CheckRange(Azimuth, 0, 360, "SlewToAltAzAsync", "Azimuth");
            CheckRange(Altitude, -90, 90, "SlewToAltAzAsync", "Altitude");
            CheckReachable(Azimuth, Altitude, SlewType.SlewAltAz);
            // Block until ExecuteSlewAsync returns (after IsSlewing=true is set, before movement completes).
            // This ensures Slewing=true is visible to polling clients before the HTTP response is sent.
            _mount.SlewAltAzAsync(Altitude, Azimuth).GetAwaiter().GetResult();
        }

        public void SlewToCoordinates(double RightAscension, double Declination)
        {
            var monitorItem = new MonitorEntry
            {
                Datetime = HiResDateTime.UtcNow,
                Device = MonitorDevice.Telescope,
                Category = MonitorCategory.Driver,
                Type = MonitorType.Information,
                Method = MethodBase.GetCurrentMethod()?.Name,
                Thread = Environment.CurrentManagedThreadId,
                Message = $"{Utilities.HoursToHMS(RightAscension, "h ", ":", "", 2)}|{Utilities.DegreesToDMS(Declination, "\u00B0 ", ":", "", 2)}"
            };
            MonitorLog.LogToMonitor(monitorItem);

            CheckCapability(_mount.Settings.CanSlew, "SlewToCoordinates");
            CheckRange(RightAscension, 0, 24, "SlewToCoordinates", "RightAscension");
            CheckRange(Declination, -90, 90, "SlewToCoordinates", "Declination");
            CheckParked("SlewToCoordinates");
            CheckTracking(true, "SlewToCoordinates");
            CheckReachable(RightAscension, Declination, SlewType.SlewRaDec);

            TargetRightAscension = RightAscension;
            TargetDeclination = Declination;
            var raDec = Transforms.CoordTypeToInternal(RightAscension, Declination);

            // Use SlewController via SlewRaDec (blocks until complete)
            _mount.SlewRaDec(raDec.X, raDec.Y, true);

            // Brief delay for position updates
            DelayInterval();
        }

        public void SlewToCoordinatesAsync(double RightAscension, double Declination)
        {
            var monitorItem = new MonitorEntry
            {
                Datetime = HiResDateTime.UtcNow,
                Device = MonitorDevice.Telescope,
                Category = MonitorCategory.Driver,
                Type = MonitorType.Information,
                Method = MethodBase.GetCurrentMethod()?.Name,
                Thread = Environment.CurrentManagedThreadId,
                Message = $"{Utilities.HoursToHMS(RightAscension, "h ", ":", "", 2)}|{Utilities.DegreesToDMS(Declination, "\u00B0 ", ":", "", 2)}"
            };
            MonitorLog.LogToMonitor(monitorItem);

            CheckCapability(_mount.Settings.CanSlewAsync, "SlewToCoordinatesAsync");
            CheckRange(RightAscension, 0, 24, "SlewToCoordinatesAsync", "RightAscension");
            CheckRange(Declination, -90, 90, "SlewToCoordinatesAsync", "Declination");
            CheckParked("SlewToCoordinatesAsync");
            CheckReachable(RightAscension, Declination, SlewType.SlewRaDec);

            TargetRightAscension = RightAscension;
            TargetDeclination = Declination;
            var raDec = Transforms.CoordTypeToInternal(RightAscension, Declination);

            // Enable tracking before starting slew
            // _mount.CycleOnTracking(true);
            if(!Tracking) Tracking = true;

            // Block until ExecuteSlewAsync returns (after IsSlewing=true is set, before movement completes).
            // This ensures Slewing=true is visible to polling clients before the HTTP response is sent.
            _mount.SlewRaDecAsync(raDec.X, raDec.Y, tracking: true).GetAwaiter().GetResult();
        }

        public void SlewToTarget()
        {
            var ra = TargetRightAscension;
            var dec = TargetDeclination;

            var monitorItem = new MonitorEntry
            {
                Datetime = HiResDateTime.UtcNow,
                Device = MonitorDevice.Telescope,
                Category = MonitorCategory.Driver,
                Type = MonitorType.Information,
                Method = MethodBase.GetCurrentMethod()?.Name,
                Thread = Environment.CurrentManagedThreadId,
                Message = FormattableString.Invariant($"{ra}|{dec}")
            };
            MonitorLog.LogToMonitor(monitorItem);

            CheckCapability(_mount.Settings.CanSlew, "SlewToTarget");
            CheckRange(ra, 0, 24, "SlewToTarget", "TargetRightAscension");
            CheckRange(dec, -90, 90, "SlewToTarget", "TargetDeclination");
            CheckParked("SlewToTarget");
            CheckTracking(true, "SlewToTarget");
            CheckReachable(ra, dec, SlewType.SlewRaDec);

            var xy = Transforms.CoordTypeToInternal(ra, dec);

            // Use SlewController via SlewRaDec (blocks until complete)
            _mount.SlewRaDec(xy.X, xy.Y, true);

            // Brief delay for position updates
            DelayInterval();
        }

        public void SlewToTargetAsync()
        {
            var ra = TargetRightAscension;
            var dec = TargetDeclination;

            var monitorItem = new MonitorEntry
            {
                Datetime = HiResDateTime.UtcNow,
                Device = MonitorDevice.Telescope,
                Category = MonitorCategory.Driver,
                Type = MonitorType.Information,
                Method = MethodBase.GetCurrentMethod()?.Name,
                Thread = Environment.CurrentManagedThreadId,
                Message = FormattableString.Invariant($"{ra}|{dec}")
            };
            MonitorLog.LogToMonitor(monitorItem);

            CheckCapability(_mount.Settings.CanSlewAsync, "SlewToTargetAsync");
            CheckRange(ra, 0, 24, "SlewToTargetAsync", "TargetRightAscension");
            CheckRange(dec, -90, 90, "SlewToTargetAsync", "TargetDeclination");
            CheckParked("SlewToTargetAsync");
            CheckReachable(ra, dec, SlewType.SlewRaDec);

            var xy = Transforms.CoordTypeToInternal(ra, dec);

            // Enable tracking before starting slew
            if (!Tracking) Tracking = true;

            // Block until ExecuteSlewAsync returns (after IsSlewing=true is set, before movement completes).
            // This ensures Slewing=true is visible to polling clients before the HTTP response is sent.
            _mount.SlewRaDecAsync(xy.X, xy.Y, tracking: true).GetAwaiter().GetResult();
        }

        public void Unpark()
        {
            CheckCapability(_mount.Settings.CanUnPark, "UnPark");

            var monitorItem = new MonitorEntry
            {
                Datetime = HiResDateTime.UtcNow,
                Device = MonitorDevice.Telescope,
                Category = MonitorCategory.Driver,
                Type = MonitorType.Information,
                Method = MethodBase.GetCurrentMethod()?.Name,
                Thread = Environment.CurrentManagedThreadId,
                Message = $"Unparking - AtPark was: {_mount.AtPark}"  // âœ… Log before
            };
            MonitorLog.LogToMonitor(monitorItem);

            _mount.AtPark = false;
            _mount.ApplyTracking(AlignmentMode != AlignmentMode.AltAz);

            monitorItem = new MonitorEntry
            {
                Datetime = HiResDateTime.UtcNow,
                Device = MonitorDevice.Telescope,
                Category = MonitorCategory.Driver,
                Type = MonitorType.Information,
                Method = MethodBase.GetCurrentMethod()?.Name,
                Thread = Environment.CurrentManagedThreadId,
                Message = $"Unparked - AtPark now: {_mount.AtPark}"    // âœ… Log after
            };
            MonitorLog.LogToMonitor(monitorItem);
        }

        public void SetPark()
        {
            var monitorItem = new MonitorEntry
            { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Information, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Environment.CurrentManagedThreadId, Message = "Started" };
            MonitorLog.LogToMonitor(monitorItem);

            CheckCapability(_mount.Settings.CanSetPark, "SetPark");
            _mount.SetParkAxis("External");
        }

        public void SyncToAltAz(double Azimuth, double Altitude)
        {
            var monitorItem = new MonitorEntry
            { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Information, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Environment.CurrentManagedThreadId, Message = $"{Utilities.DegreesToDMS(Azimuth, "\u00B0 ", ":", "", 2)}|{Utilities.DegreesToDMS(Altitude, "\u00B0 ", ":", "", 2)}" };
            MonitorLog.LogToMonitor(monitorItem);

            CheckCapability(_mount.Settings.CanSyncAltAz, "SyncToAltAz");
            CheckRange(Azimuth, 0, 360, "SyncToAltAz", "Azimuth");
            CheckRange(Altitude, -90, 90, "SyncToAltAz", "Altitude");
            CheckParked("SyncToAltAz");
            CheckTracking(false, "SyncToAltAz");
            CheckAltAzSync(Altitude, Azimuth, "SyncToAltAz");
            _mount.AtPark = false;
            _mount.SyncToAltAzm(Azimuth, Altitude);
            _mount.WaitMountPositionUpdated();
        }

        public void SyncToCoordinates(double RightAscension, double Declination)
        {
            var monitorItem = new MonitorEntry
            {
                Datetime = HiResDateTime.UtcNow,
                Device = MonitorDevice.Telescope,
                Category = MonitorCategory.Driver,
                Type = MonitorType.Information,
                Method = MethodBase.GetCurrentMethod()?.Name,
                Thread = Environment.CurrentManagedThreadId,
                Message = $"{Utilities.HoursToHMS(RightAscension, "h ", ":", "", 2)}|{Utilities.DegreesToDMS(Declination, "\u00B0 ", ":", "", 2)}"
            };
            MonitorLog.LogToMonitor(monitorItem);

            CheckCapability(_mount.Settings.CanSync, "SyncToCoordinates");
            CheckRange(RightAscension, 0, 24, "SyncToCoordinates", "RightAscension");
            CheckRange(Declination, -90, 90, "SyncToCoordinates", "Declination");
            CheckParked("SyncToCoordinates");
            CheckTracking(true, "SyncToCoordinates");

            _mount.TargetDec = Declination;
            _mount.TargetRa = RightAscension;
            var a = Transforms.CoordTypeToInternal(RightAscension, Declination);
            CheckRaDecSync(a.X, a.Y, "SyncToCoordinates");

            _mount.AtPark = false;
            _mount.SyncToTargetRaDec();
            _mount.WaitMountPositionUpdated();
        }

        public void SyncToTarget()
        {
            var monitorItem = new MonitorEntry
            {
                Datetime = HiResDateTime.UtcNow,
                Device = MonitorDevice.Telescope,
                Category = MonitorCategory.Driver,
                Type = MonitorType.Information,
                Method = MethodBase.GetCurrentMethod()?.Name,
                Thread = Environment.CurrentManagedThreadId,
                Message = $"{Utilities.HoursToHMS(_mount.TargetRa, "h ", ":", "", 2)}|{Utilities.DegreesToDMS(_mount.TargetDec, "\u00B0 ", ":", "", 2)}"
            };
            MonitorLog.LogToMonitor(monitorItem);

            CheckCapability(_mount.Settings.CanSync, "SyncToTarget");
            CheckRange(_mount.TargetRa, 0, 24, "SyncToTarget", "TargetRightAscension");
            CheckRange(_mount.TargetDec, -90, 90, "SyncToTarget", "TargetDeclination");
            CheckParked("SyncToTarget");
            CheckTracking(true, "SyncToTarget");

            var a = Transforms.CoordTypeToInternal(_mount.TargetRa, _mount.TargetDec);  
            CheckRaDecSync(a.X, a.Y, "SyncToTarget");

            _mount.AtPark = false;
            _mount.SyncToTargetRaDec();
            _mount.WaitMountPositionUpdated();
        }

        #region Private Methods

        private static void CheckTrackingRate(string propertyOrMethod, DriveRate enumValue)
        {
            var success = Enum.IsDefined(typeof(DriveRate), enumValue);
            if (success) return;
            var monitorItem = new MonitorEntry
            { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Warning, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Environment.CurrentManagedThreadId, Message = FormattableString.Invariant($"{propertyOrMethod}|{enumValue}") };
            MonitorLog.LogToMonitor(monitorItem);

            throw new InvalidValueException("TrackingRate invalid");
        }

        private static void CheckRange(double value, double min, double max, string propertyOrMethod, string valueName)
        {
            if (double.IsNaN(value))
            {
                var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Data, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Environment.CurrentManagedThreadId, Message = $"{value}|{min}|{max}|{propertyOrMethod}|{valueName}" };
                MonitorLog.LogToMonitor(monitorItem);

                throw new ValueNotSetException(propertyOrMethod + ":" + valueName);
            }

            if (value < min || value > max)
            {
                var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Warning, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Environment.CurrentManagedThreadId, Message = $"{value}|{min}|{max}|{propertyOrMethod}|{valueName}" };
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
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Data, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Environment.CurrentManagedThreadId, Message = $"{value}|{min}|{max}|{propertyOrMethod}" };
                MonitorLog.LogToMonitor(monitorItem);

                throw new ValueNotSetException(propertyOrMethod);
            }

            if (value < min || value > max)
            {
                var monitorItem = new MonitorEntry
                { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Warning, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Environment.CurrentManagedThreadId, Message = $"{value}|{min}|{max}|{propertyOrMethod}" };
                MonitorLog.LogToMonitor(monitorItem);

                throw new InvalidValueException(propertyOrMethod, value.ToString(CultureInfo.CurrentCulture),
                    string.Format(CultureInfo.CurrentCulture, "{0} to {1}", min, max));
            }
        }

        private static void CheckCapability(bool capability, string method)
        {
            if (capability) return;

            var monitorItem = new MonitorEntry
            { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Warning, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Environment.CurrentManagedThreadId, Message = $"{method}" };
            MonitorLog.LogToMonitor(monitorItem);

            throw new MethodNotImplementedException(method);
        }

        private static void CheckCapability(bool capability, string property, bool setNotGet)
        {
            if (capability) return;

            var monitorItem = new MonitorEntry
            { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Warning, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Environment.CurrentManagedThreadId, Message = $"{property}|{setNotGet}" };
            MonitorLog.LogToMonitor(monitorItem);

            throw new PropertyNotImplementedException(property, setNotGet);
        }

        private void CheckParked(string property)
        {
            if (!_mount.AtPark) return;

            var monitorItem = new MonitorEntry
            { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Information, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Environment.CurrentManagedThreadId, Message = $"{property}" };
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
            { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Information, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Environment.CurrentManagedThreadId, Message = $"{rate}" };
            MonitorLog.LogToMonitor(monitorItem);
            var deg = Conversions.ArcSec2Deg(rate);
            var slewSpeedEight = _mount.SlewSpeedEight;
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
            { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Information, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Environment.CurrentManagedThreadId, Message = $"{axis}|{rate}" };
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
            var tracking = _mount.Tracking;
            if (raDecSlew == tracking) return;

            var monitorItem = new MonitorEntry
            { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Information, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Environment.CurrentManagedThreadId, Message = FormattableString.Invariant($"{tracking}|{raDecSlew}|{method}") };
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
            var pass = _mount.CheckRaDecSyncLimit(ra, dec);
            if (pass) return;

            var monitorItem = new MonitorEntry
            { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Warning, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Environment.CurrentManagedThreadId, Message = FormattableString.Invariant($"{ra}|{dec}|{method}") };
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
            var pass = _mount.CheckAltAzSyncLimit(alt, az);
            if (pass) return;

            var monitorItem = new MonitorEntry
            { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Information, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Environment.CurrentManagedThreadId, Message = FormattableString.Invariant($"{alt}|{az}|{method}") };
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
            if (_mount.Settings.AlignmentMode != AlignmentMode.Polar ||
                _mount.IsTargetReachable(new[] { axisX, axisY }, slewType)) return;

            var monitorItem = new MonitorEntry
            { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = MonitorType.Warning, Method = MethodBase.GetCurrentMethod()?.Name, Thread = Environment.CurrentManagedThreadId, Message = FormattableString.Invariant($"{axisX}|{axisY}|{slewType}") };
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
            var delay = additional;
            switch (_mount.Settings.Mount)
            {
                case MountType.Simulator:
                    delay += _mount.Settings.DisplayInterval;
                    break;
                case MountType.SkyWatcher:
                    delay += 20;  // some go tos have been off .10 to .70 seconds, not sure exactly why
                    delay += _mount.Settings.DisplayInterval;
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
            { Datetime = HiResDateTime.UtcNow, Device = MonitorDevice.Telescope, Category = MonitorCategory.Driver, Type = type, Method = $"{method}", Thread = Environment.CurrentManagedThreadId, Message = $"{msg}" };
            MonitorLog.LogToMonitor(monitorItem);
        }

        #endregion
    }
}
