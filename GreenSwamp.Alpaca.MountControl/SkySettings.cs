/* Copyright(C) 2019-2025 Rob Morgan (robert.morgan.e@gmail.com)

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
using ASCOM.Common.DeviceInterfaces;
using GreenSwamp.Alpaca.MountControl.Pulses;
using GreenSwamp.Alpaca.Shared;
using GreenSwamp.Alpaca.Shared.Transport;
using Newtonsoft.Json;
using System.ComponentModel;
using System.Configuration;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Handshake = System.IO.Ports.Handshake;

namespace GreenSwamp.Alpaca.MountControl
{
    public static class SkySettings
    {
        #region Events

        public static event PropertyChangedEventHandler StaticPropertyChanged;

        #endregion

        #region Capabilities

        private static bool _canAlignMode;
        public static bool CanAlignMode
        {
            get => _canAlignMode;
            private set
            {
                if (_canAlignMode == value) return;
                _canAlignMode = value;
                LogSetting(MethodBase.GetCurrentMethod()?.Name, value.ToString());
                OnStaticPropertyChanged();
            }
        }

        private static bool _canAltAz;
        public static bool CanAltAz
        {
            get => _canAltAz;
            private set
            {
                if (_canAltAz == value) return;
                _canAltAz = value;
                LogSetting(MethodBase.GetCurrentMethod()?.Name, value.ToString());
                OnStaticPropertyChanged();
            }
        }

        private static bool _canEquatorial;
        public static bool CanEquatorial
        {
            get => _canEquatorial;
            private set
            {
                if (_canEquatorial == value) return;
                _canEquatorial = value;
                LogSetting(MethodBase.GetCurrentMethod()?.Name, value.ToString());
                OnStaticPropertyChanged();
            }
        }

        private static bool _canFindHome;
        public static bool CanFindHome
        {
            get => _canFindHome;
            private set
            {
                if (_canFindHome == value) return;
                _canFindHome = value;
                LogSetting(MethodBase.GetCurrentMethod()?.Name, value.ToString());
                OnStaticPropertyChanged();
            }
        }

        private static bool _canLatLongElev;
        public static bool CanLatLongElev
        {
            get => _canLatLongElev;
            private set
            {
                if (_canLatLongElev == value) return;
                _canLatLongElev = value;
                LogSetting(MethodBase.GetCurrentMethod()?.Name, value.ToString());
                OnStaticPropertyChanged();
            }
        }

        private static bool _canOptics;
        public static bool CanOptics
        {
            get => _canOptics;
            private set
            {
                if (_canOptics == value) return;
                _canOptics = value;
                LogSetting(MethodBase.GetCurrentMethod()?.Name, value.ToString());
                OnStaticPropertyChanged();
            }
        }

        private static bool _canPark;
        public static bool CanPark
        {
            get => _canPark;
            private set
            {
                if (_canPark == value) return;
                _canPark = value;
                LogSetting(MethodBase.GetCurrentMethod()?.Name, value.ToString());
                OnStaticPropertyChanged();
            }
        }

        private static bool _canPulseGuide;
        public static bool CanPulseGuide
        {
            get => _canPulseGuide;
            private set
            {
                if (_canPulseGuide == value) return;
                _canPulseGuide = value;
                LogSetting(MethodBase.GetCurrentMethod()?.Name, value.ToString());
                OnStaticPropertyChanged();
            }
        }

        private static bool _canSetEquRates;
        public static bool CanSetEquRates
        {
            get => _canSetEquRates;
            private set
            {
                if (_canSetEquRates == value) return;
                _canSetEquRates = value;
                LogSetting(MethodBase.GetCurrentMethod()?.Name, value.ToString());
                OnStaticPropertyChanged();
            }
        }

        private static bool _canSetDeclinationRate;
        public static bool CanSetDeclinationRate
        {
            get => _canSetDeclinationRate;
            private set
            {
                if (_canSetDeclinationRate == value) return;
                _canSetDeclinationRate = value;
                LogSetting(MethodBase.GetCurrentMethod()?.Name, value.ToString());
                OnStaticPropertyChanged();
            }
        }

        private static bool _canSetGuideRates;
        public static bool CanSetGuideRates
        {
            get => _canSetGuideRates;
            private set
            {
                if (_canSetGuideRates == value) return;
                _canSetGuideRates = value;
                LogSetting(MethodBase.GetCurrentMethod()?.Name, value.ToString());
                OnStaticPropertyChanged();
            }
        }

        private static bool _canSetPark;
        public static bool CanSetPark
        {
            get => _canSetPark;
            set
            {
                if (_canSetPark == value) return;
                _canSetPark = value;
                LogSetting(MethodBase.GetCurrentMethod()?.Name, value.ToString());
                OnStaticPropertyChanged();
            }
        }

        private static bool _canSetPierSide;
        public static bool CanSetPierSide
        {
            get => _canSetPierSide;
            set
            {
                if (_canSetPierSide == value) return;
                _canSetPierSide = value;
                LogSetting(MethodBase.GetCurrentMethod()?.Name, value.ToString());
                OnStaticPropertyChanged();
            }
        }

        private static bool _canSetRightAscensionRate;
        public static bool CanSetRightAscensionRate
        {
            get => _canSetRightAscensionRate;
            private set
            {
                if (_canSetRightAscensionRate == value) return;
                _canSetRightAscensionRate = value;
                LogSetting(MethodBase.GetCurrentMethod()?.Name, value.ToString());
                OnStaticPropertyChanged();
            }
        }

        private static bool _canSetTracking;
        public static bool CanSetTracking
        {
            get => _canSetTracking;
            private set
            {
                if (_canSetTracking == value) return;
                _canSetTracking = value;
                LogSetting(MethodBase.GetCurrentMethod()?.Name, value.ToString());
                OnStaticPropertyChanged();
            }
        }

        private static bool _canSiderealTime;
        public static bool CanSiderealTime
        {
            get => _canSiderealTime;
            private set
            {
                if (_canSiderealTime == value) return;
                _canSiderealTime = value;
                LogSetting(MethodBase.GetCurrentMethod()?.Name, value.ToString());
                OnStaticPropertyChanged();
            }
        }

        private static bool _canSlew;
        public static bool CanSlew
        {
            get => _canSlew;
            private set
            {
                if (_canSlew == value) return;
                _canSlew = value;
                LogSetting(MethodBase.GetCurrentMethod()?.Name, value.ToString());
                OnStaticPropertyChanged();
            }
        }

        private static bool _canSlewAltAz;
        public static bool CanSlewAltAz
        {
            get => _canSlewAltAz;
            private set
            {
                if (_canSlewAltAz == value) return;
                _canSlewAltAz = value;
                LogSetting(MethodBase.GetCurrentMethod()?.Name, value.ToString());
                OnStaticPropertyChanged();
            }
        }

        private static bool _canSlewAltAzAsync;
        public static bool CanSlewAltAzAsync
        {
            get => _canSlewAltAzAsync;
            private set
            {
                if (_canSlewAltAzAsync == value) return;
                _canSlewAltAzAsync = value;
                LogSetting(MethodBase.GetCurrentMethod()?.Name, value.ToString());
                OnStaticPropertyChanged();
            }
        }

        private static bool _canSlewAsync;
        public static bool CanSlewAsync
        {
            get => _canSlewAsync;
            private set
            {
                if (_canSlewAsync == value) return;
                _canSlewAsync = value;
                LogSetting(MethodBase.GetCurrentMethod()?.Name, value.ToString());
                OnStaticPropertyChanged();
            }
        }

        private static bool _canSync;
        public static bool CanSync
        {
            get => _canSync;
            private set
            {
                if (_canSync == value) return;
                _canSync = value;
                LogSetting(MethodBase.GetCurrentMethod()?.Name, value.ToString());
                OnStaticPropertyChanged();
            }
        }

        private static bool _canSyncAltAz;
        public static bool CanSyncAltAz
        {
            get => _canSyncAltAz;
            private set
            {
                if (_canSyncAltAz == value) return;
                _canSyncAltAz = value;
                LogSetting(MethodBase.GetCurrentMethod()?.Name, value.ToString());
                OnStaticPropertyChanged();
            }
        }

        private static bool _canTrackingRates;
        public static bool CanTrackingRates
        {
            get => _canTrackingRates;
            private set
            {
                if (_canTrackingRates == value) return;
                _canTrackingRates = value;
                LogSetting(MethodBase.GetCurrentMethod()?.Name, value.ToString());
                OnStaticPropertyChanged();
            }
        }

        private static bool _canUnPark;
        public static bool CanUnPark
        {
            get => _canUnPark;
            private set
            {
                if (_canUnPark == value) return;
                _canUnPark = value;
                LogSetting(MethodBase.GetCurrentMethod()?.Name, value.ToString());
                OnStaticPropertyChanged();
            }
        }

        private static bool _noSyncPastMeridian;
        public static bool NoSyncPastMeridian
        {
            get => _noSyncPastMeridian;
            private set
            {
                if (_noSyncPastMeridian == value) return;
                _noSyncPastMeridian = value;
                LogSetting(MethodBase.GetCurrentMethod()?.Name, value.ToString());
                OnStaticPropertyChanged();
            }
        }

        private static int _numMoveAxis;
        public static int NumMoveAxis
        {
            get => _numMoveAxis;
            private set
            {
                if (_numMoveAxis == value) return;
                _numMoveAxis = value;
                LogSetting(MethodBase.GetCurrentMethod()?.Name, value.ToString());
                OnStaticPropertyChanged();
            }
        }

        private static bool _versionOne;
        public static bool VersionOne
        {
            get => _versionOne;
            private set
            {
                if (_versionOne == value) return;
                _versionOne = value;
                LogSetting(MethodBase.GetCurrentMethod()?.Name, value.ToString());
                OnStaticPropertyChanged();
            }
        }

        #endregion

        #region Server Settings

        private static AlignmentMode _alignmentMode;
        public static AlignmentMode AlignmentMode
        {
            get => _alignmentMode;
            set
            {
                if (_alignmentMode == value) return;
                _alignmentMode = value;
                LogSetting(MethodBase.GetCurrentMethod()?.Name, value.ToString());
                OnStaticPropertyChanged();
            }
        }

        private static Vector3 _axisModelOffsets;

        public static Vector3 AxisModelOffsets
        {
            get => _axisModelOffsets;

            private set
            {
                _axisModelOffsets = value;
                LogSetting(MethodBase.GetCurrentMethod()?.Name, value.ToString());
            }
        }

        private static SerialSpeed _baudRate;
        public static SerialSpeed BaudRate
        {
            get => _baudRate;
            set
            {
                if (_baudRate == value) return;
                _baudRate = value;
                LogSetting(MethodBase.GetCurrentMethod()?.Name, value.ToString());
                OnStaticPropertyChanged();
            }
        }

        private static bool _allowAdvancedCommandSet;
        public static bool AllowAdvancedCommandSet
        {
            get => _allowAdvancedCommandSet;
            set
            {
                if (_allowAdvancedCommandSet == value) return;
                _allowAdvancedCommandSet = value;
                LogSetting(MethodBase.GetCurrentMethod()?.Name, value.ToString());
                OnStaticPropertyChanged();
            }
        }

        private static int _customDec360Steps;
        public static int CustomDec360Steps
        {
            get => _customDec360Steps;
            set
            {
                if (_customDec360Steps == value) return;
                _customDec360Steps = value;
                LogSetting(MethodBase.GetCurrentMethod()?.Name, $"{value}");
                OnStaticPropertyChanged();
            }
        }

        private static int _customDecTrackingOffset;
        public static int CustomDecTrackingOffset
        {
            get => _customDecTrackingOffset;
            set
            {
                if (_customDecTrackingOffset == value) return;
                _customDecTrackingOffset = value;
                LogSetting(MethodBase.GetCurrentMethod()?.Name, $"{value}");
                OnStaticPropertyChanged();
            }
        }

        private static int _customDecWormTeeth;
        public static int CustomDecWormTeeth
        {
            get => _customDecWormTeeth;
            set
            {
                if (_customDecWormTeeth == value) return;
                _customDecWormTeeth = value;
                LogSetting(MethodBase.GetCurrentMethod()?.Name, $"{value}");
                OnStaticPropertyChanged();
            }
        }

        private static int _customRa360Steps;
        public static int CustomRa360Steps
        {
            get => _customRa360Steps;
            set
            {
                if (_customRa360Steps == value) return;
                _customRa360Steps = value;
                LogSetting(MethodBase.GetCurrentMethod()?.Name, $"{value}");
                OnStaticPropertyChanged();
            }
        }

        private static int _customRaTrackingOffset;
        public static int CustomRaTrackingOffset
        {
            get => _customRaTrackingOffset;
            set
            {
                if (_customRaTrackingOffset == value) return;
                _customRaTrackingOffset = value;
                LogSetting(MethodBase.GetCurrentMethod()?.Name, $"{value}");
                OnStaticPropertyChanged();
            }
        }

        private static int _customRaWormTeeth;
        public static int CustomRaWormTeeth
        {
            get => _customRaWormTeeth;
            set
            {
                if (_customRaWormTeeth == value) return;
                _customRaWormTeeth = value;
                LogSetting(MethodBase.GetCurrentMethod()?.Name, $"{value}");
                OnStaticPropertyChanged();
            }
        }

        private static bool _customGearing;
        public static bool CustomGearing
        {
            get => _customGearing;
            set
            {
                if (_customGearing == value) return;
                _customGearing = value;
                LogSetting(MethodBase.GetCurrentMethod()?.Name, value.ToString());
                OnStaticPropertyChanged();
            }
        }

        private static string _gpsComPort;
        public static string GpsComPort
        {
            get => _gpsComPort;
            set
            {
                if (_gpsComPort == value) return;
                _gpsComPort = value;
                var i = Strings.GetNumberFromString(value);
                var vi = i ?? 0;
                LogSetting(MethodBase.GetCurrentMethod()?.Name, $"{vi}");
                OnStaticPropertyChanged();
            }
        }

        private static SerialSpeed _gpsBaudRate;
        public static SerialSpeed GpsBaudRate
        {
            get => _gpsBaudRate;
            set
            {
                if (_gpsBaudRate == value) return;
                _gpsBaudRate = value;
                LogSetting(MethodBase.GetCurrentMethod()?.Name, value.ToString());
            }
        }

        private static EquatorialCoordinateType _equatorialCoordinateType;
        public static EquatorialCoordinateType EquatorialCoordinateType
        {
            get => _equatorialCoordinateType;
            set
            {
                if (_equatorialCoordinateType == value) return;
                _equatorialCoordinateType = value;
                LogSetting(MethodBase.GetCurrentMethod()?.Name, value.ToString());
                OnStaticPropertyChanged();
            }
        }

        private static DriveRate _trackingRate;
        public static DriveRate TrackingRate
        {
            get => _trackingRate;
            set
            {
                if (_trackingRate == value) return;
                _trackingRate = value;
                if (value != DriveRate.Sidereal)
                {
                    SkyServer.RateDecOrg = 0;
                    SkyServer.RateDec = 0;
                    SkyServer.RateRaOrg = 0;
                    SkyServer.RateRa = 0;
                }
                LogSetting(MethodBase.GetCurrentMethod()?.Name, value.ToString());
                OnStaticPropertyChanged();
            }
        }

        private static SlewSpeed _hcSpeed;
        public static SlewSpeed HcSpeed
        {
            get => _hcSpeed;
            set
            {
                if (_hcSpeed == value) return;
                _hcSpeed = value;
                LogSetting(MethodBase.GetCurrentMethod()?.Name, value.ToString());
                OnStaticPropertyChanged();
            }
        }

        private static Handshake _handShake;
        public static Handshake HandShake
        {
            get => _handShake;
            private set
            {
                if (_handShake == value) return;
                _handShake = value;
                LogSetting(MethodBase.GetCurrentMethod()?.Name, value.ToString());
            }
        }

        private static HcMode _hcMode;
        public static HcMode HcMode
        {
            get => _hcMode;
            set
            {
                if (_hcMode == value) return;
                _hcMode = value;
                LogSetting(MethodBase.GetCurrentMethod()?.Name, value.ToString());
                OnStaticPropertyChanged();
            }
        }

        private static MountType _mount;
        public static MountType Mount
        {
            get => _mount;
            set
            {
                if (_mount == value) return;
                _mount = value;
                LogSetting(MethodBase.GetCurrentMethod()?.Name, value.ToString());
                SkyServer.IsMountRunning = false;
                OnStaticPropertyChanged();
            }
        }

        private static PecMode _pecMode;
        public static PecMode PecMode
        {
            get => _pecMode;
            set
            {
                if (_pecMode == value) return;
                _pecMode = value;
                LogSetting(MethodBase.GetCurrentMethod()?.Name, value.ToString());
                OnStaticPropertyChanged();
            }
        }

        private static bool _alternatingPPec;
        public static bool AlternatingPPec
        {
            get => _alternatingPPec;
            set
            {
                if (_alternatingPPec == value) return;
                _alternatingPPec = value;
                LogSetting(MethodBase.GetCurrentMethod()?.Name, value.ToString());
                OnStaticPropertyChanged();
                SkyServer.SkyTasks(MountTaskName.AlternatingPpec);
            }
        }

        private static double _apertureArea;
        public static double ApertureArea
        {
            get => _apertureArea;
            private set
            {
                if (Math.Abs(_apertureArea - value) < 0.0) return;
                _apertureArea = value;
                LogSetting(MethodBase.GetCurrentMethod()?.Name, $"{value}");
                OnStaticPropertyChanged();
            }
        }

        private static double _apertureDiameter;
        public static double ApertureDiameter
        {
            get => _apertureDiameter;
            private set
            {
                if (Math.Abs(_apertureDiameter - value) < 0.0) return;
                _apertureDiameter = value;
                LogSetting(MethodBase.GetCurrentMethod()?.Name, $"{value}");
                OnStaticPropertyChanged();
            }
        }

        private static bool _atPark;
        public static bool AtPark
        {
            get => _atPark;
            set
            {
                if (_atPark == value) return;
                _atPark = value;
                LogSetting(MethodBase.GetCurrentMethod()?.Name, $"{value}");
                OnStaticPropertyChanged();
            }
        }

        private static double _autoHomeAxisX = double.NaN;
        /// <summary>
        /// AutoHome X Axis sensor position in degrees
        /// </summary>
        public static double AutoHomeAxisX
        {
            get => _autoHomeAxisX;
            set
            {
                _autoHomeAxisX = value;
                LogSetting(MethodBase.GetCurrentMethod()?.Name, $"{value}");
                OnStaticPropertyChanged();
            }
        }


        private static double _autoHomeAxisY = double.NaN;
        /// <summary>
        /// AutoHome Y Axis sensor position in degrees
        /// </summary>
        public static double AutoHomeAxisY
        {
            get => _autoHomeAxisY;
            set
            {
                _autoHomeAxisY = value;
                LogSetting(MethodBase.GetCurrentMethod()?.Name, $"{value}");
                OnStaticPropertyChanged();
            }
        }

        private static bool _autoTrack;
        public static bool AutoTrack
        {
            get => _autoTrack;
            private set
            {
                if (_autoTrack == value) return;
                _autoTrack = value;
                LogSetting(MethodBase.GetCurrentMethod()?.Name, $"{value}");
                OnStaticPropertyChanged();
            }
        }

        private static double _axisTrackingLimit;
        public static double AxisTrackingLimit
        {
            get => _axisTrackingLimit;
            set
            {
                if (Math.Abs(_axisTrackingLimit - value) < 0.0000000000001) return;
                _axisTrackingLimit = value;
                LogSetting(MethodBase.GetCurrentMethod()?.Name, $"{value}");
                OnStaticPropertyChanged();
            }
        }

        private static double _axisHzTrackingLimit;
        public static double AxisHzTrackingLimit
        {
            get => _axisHzTrackingLimit;
            set
            {
                if (Math.Abs(_axisHzTrackingLimit - value) < 0.0000000000001) return;
                _axisHzTrackingLimit = value;
                LogSetting(MethodBase.GetCurrentMethod()?.Name, $"{value}");
                OnStaticPropertyChanged();
            }
        }

        private static double _cameraHeight;
        public static double CameraHeight
        {
            get => _cameraHeight;
            set
            {
                if (Math.Abs(_cameraHeight - value) < 0.0) return;
                _cameraHeight = value;
                LogSetting(MethodBase.GetCurrentMethod()?.Name, $"{value}");
                OnStaticPropertyChanged();
            }
        }

        private static double _cameraWidth;
        public static double CameraWidth
        {
            get => _cameraWidth;
            set
            {
                if (Math.Abs(_cameraWidth - value) < 0.0) return;
                _cameraWidth = value;
                LogSetting(MethodBase.GetCurrentMethod()?.Name, $"{value}");
                OnStaticPropertyChanged();
            }
        }

        private static string _port;
        public static string Port
        {
            get => _port;
            set
            {
                if (_port == value){return;}
                _port = value;
                LogSetting(MethodBase.GetCurrentMethod()?.Name, value);
                OnStaticPropertyChanged();
            }
        }

        private static int _dataBits;
        public static int DataBits
        {
            get => _dataBits;
            private set
            {
                if (_dataBits == value) return;
                _dataBits = value;
                LogSetting(MethodBase.GetCurrentMethod()?.Name, $"{value}");
                OnStaticPropertyChanged();
            }
        }

        private static int _decBacklash;
        public static int DecBacklash
        {
            get => _decBacklash;
            set
            {
                if (_decBacklash == value) return;
                _decBacklash = value;
                LogSetting(MethodBase.GetCurrentMethod()?.Name, $"{value}");
                OnStaticPropertyChanged();
            }
        }

        private static bool _decPulseToGoTo;
        public static bool DecPulseToGoTo
        {
            get => _decPulseToGoTo;
            set
            {
                if (_decPulseToGoTo == value) return;
                _decPulseToGoTo = value;
                LogSetting(MethodBase.GetCurrentMethod()?.Name, $"{value}");
                OnStaticPropertyChanged();
                SkyServer.SkyTasks(MountTaskName.DecPulseToGoTo);
            }
        }

        private static bool _disableKeysOnGoTo;
        public static bool DisableKeysOnGoTo
        {
            get => _disableKeysOnGoTo;
            set
            {
                if (_disableKeysOnGoTo == value) return;
                _disableKeysOnGoTo = value;
                LogSetting(MethodBase.GetCurrentMethod()?.Name, $"{value}");
                OnStaticPropertyChanged();
            }
        }

        private static bool _dtrEnable;
        public static bool DtrEnable
        {
            get => _dtrEnable;
            private set
            {
                if (_dtrEnable == value) return;
                _dtrEnable = value;
                LogSetting(MethodBase.GetCurrentMethod()?.Name, $"{value}");
                OnStaticPropertyChanged();
            }
        }

        private static double _elevation = double.NaN;
        public static double Elevation
        {
            get => _elevation;
            set
            {
                if (Math.Abs(_elevation - value) <= 0) return;
                _elevation = value;
                LogSetting(MethodBase.GetCurrentMethod()?.Name, $"{value}");
                OnStaticPropertyChanged();
            }
        }

        private static bool _encoders;
        public static bool Encoders
        {
            get => _encoders;
            set
            {
                if (_encoders == value) return;
                _encoders = value;
                LogSetting(MethodBase.GetCurrentMethod()?.Name, $"{value}");
                SkyServer.SkyTasks(MountTaskName.Encoders);
            }
        }

        private static double _eyepieceFs;
        public static double EyepieceFs
        {
            get => _eyepieceFs;
            set
            {
                if (Math.Abs(_eyepieceFs - value) < 0.0) return;
                _eyepieceFs = value;
                LogSetting(MethodBase.GetCurrentMethod()?.Name, $"{value}");
                OnStaticPropertyChanged();
            }
        }

        private static bool _fullCurrent;
        public static bool FullCurrent
        {
            get => _fullCurrent;
            set
            {
                if (_fullCurrent == value) return;
                _fullCurrent = value;
                LogSetting(MethodBase.GetCurrentMethod()?.Name, $"{value}");
                SkyServer.SkyTasks(MountTaskName.FullCurrent);
            }
        }

        private static double _focalLength;
        public static double FocalLength
        {
            get => _focalLength;
            set
            {
                if (Math.Abs(_focalLength - value) <= 0) return;
                _focalLength = value;
                LogSetting(MethodBase.GetCurrentMethod()?.Name, $"{value}");
                OnStaticPropertyChanged();
            }
        }

        private static bool _globalStopOn;
        public static bool GlobalStopOn
        {
            get => _globalStopOn;
            set
            {
                if (_globalStopOn == value) return;
                _globalStopOn = value;
                LogSetting(MethodBase.GetCurrentMethod()?.Name, $"{value}");
            }
        }

        private static double _gotoPrecision;
        public static double GotoPrecision
        {
            get => _gotoPrecision;
            private set
            {
                _gotoPrecision = value;
                LogSetting(MethodBase.GetCurrentMethod()?.Name, $"{value}");
                OnStaticPropertyChanged();
            }
        }

        private static double _guideRateOffsetX;
        public static double GuideRateOffsetX
        {
            get => _guideRateOffsetX;
            set
            {
                if (Math.Abs(_guideRateOffsetX - value) < 0.0000000000001) return;
                _guideRateOffsetX = value;
                LogSetting(MethodBase.GetCurrentMethod()?.Name, $"{value}");
                OnStaticPropertyChanged();
                SkyServer.SetGuideRates();
            }
        }

        private static double _guideRateOffsetY;
        public static double GuideRateOffsetY
        {
            get => _guideRateOffsetY;
            set
            {
                if (Math.Abs(_guideRateOffsetY - value) < 0.0000000000001) return;
                _guideRateOffsetY = value;
                LogSetting(MethodBase.GetCurrentMethod()?.Name, $"{value}");
                OnStaticPropertyChanged();
                SkyServer.SetGuideRates();
            }
        }

        private static bool _hcAntiDec;
        public static bool HcAntiDec
        {
            get => _hcAntiDec;
            set
            {
                if (_hcAntiDec == value) return;
                _hcAntiDec = value;
                LogSetting(MethodBase.GetCurrentMethod()?.Name, $"{value}");
                OnStaticPropertyChanged();
            }
        }

        private static bool _hcAntiRa;
        public static bool HcAntiRa
        {
            get => _hcAntiRa;
            set
            {
                if (_hcAntiRa == value) return;
                _hcAntiRa = value;
                LogSetting(MethodBase.GetCurrentMethod()?.Name, $"{value}");
                OnStaticPropertyChanged();
            }
        }

        private static bool _hcFlipEw;
        public static bool HcFlipEw
        {
            get => _hcFlipEw;
            set
            {
                if (_hcFlipEw == value) return;
                _hcFlipEw = value;
                LogSetting(MethodBase.GetCurrentMethod()?.Name, $"{value}");
                OnStaticPropertyChanged();
            }
        }

        private static bool _hcFlipNs;
        public static bool HcFlipNs
        {
            get => _hcFlipNs;
            set
            {
                if (_hcFlipNs == value) return;
                _hcFlipNs = value;
                LogSetting(MethodBase.GetCurrentMethod()?.Name, $"{value}");
                OnStaticPropertyChanged();
            }
        }

        private static List<HcPulseGuide> _hcPulseGuides;
        public static List<HcPulseGuide> HcPulseGuides
        {
            get => _hcPulseGuides;
            set
            {
                // if (_hcPulseGuides == value) return;
                _hcPulseGuides = value.OrderBy(hcPulseGuide => hcPulseGuide.Speed).ToList();
                var output = JsonConvert.SerializeObject(_hcPulseGuides);
                LogSetting(MethodBase.GetCurrentMethod()?.Name, $"{output}");
                OnStaticPropertyChanged();
            }
        }

        private static double _homeAxisX = double.NaN;
        /// <summary>
        /// Home position in user.config are app axes values
        /// Home position for polar is stored as Alt / Az in user.config, adjusted for southern hemisphere
        /// Getter returns the X axis as mount axis value
        /// </summary>
        public static double HomeAxisX
        {
            get => _homeAxisX;

            set
            {
                if (Math.Abs(_homeAxisX - value) <= 0.0000000000001) return;
                _homeAxisX = value;
                LogSetting(MethodBase.GetCurrentMethod()?.Name, $"{value}");
                OnStaticPropertyChanged();
            }
        }

        private static double _homeAxisY = double.NaN;
        /// <summary>
        /// Home position in user.config are app axes values
        /// Home position for polar is stored as Alt / Az in user.config, adjusted for southern hemisphere
        /// Getter returns the Y axis as mount axis value
        /// </summary>
        public static double HomeAxisY
        {
            get => _homeAxisY;

            set
            {
                if (Math.Abs(_homeAxisY - value) <= 0.0000000000001) return;
                _homeAxisY = value;
                LogSetting(MethodBase.GetCurrentMethod()?.Name, $"{value}");
                OnStaticPropertyChanged();
            }
        }

        private static double _hourAngleLimit = double.NaN;
        public static double HourAngleLimit
        {
            get => _hourAngleLimit;
            set
            {
                if (Math.Abs(_hourAngleLimit - value) < 0.0000000000001) return;
                _hourAngleLimit = value;
                LogSetting(MethodBase.GetCurrentMethod()?.Name, $"{value}");
                OnStaticPropertyChanged();
            }
        }

        private static bool _hzLimitTracking;
        public static bool HzLimitTracking
        {
            get => _hzLimitTracking;
            set
            {
                if (_hzLimitTracking == value) return;
                _hzLimitTracking = value;
                LogSetting(MethodBase.GetCurrentMethod()?.Name, $"{value}");
                OnStaticPropertyChanged();
            }
        }

        private static bool _hzLimitPark;
        public static bool HzLimitPark
        {
            get => _hzLimitPark;
            set
            {
                if (_hzLimitPark == value) return;
                _hzLimitPark = value;
                LogSetting(MethodBase.GetCurrentMethod()?.Name, $"{value}");
                OnStaticPropertyChanged();
            }
        }
        
        private static string _instrumentDescription;
        public static string InstrumentDescription
        {
            get => _instrumentDescription;
            private set
            {
                if (_instrumentDescription == value) return;
                _instrumentDescription = value;
                LogSetting(MethodBase.GetCurrentMethod()?.Name, $"{value}");
                OnStaticPropertyChanged();
            }
        }

        private static string _instrumentName;
        public static string InstrumentName
        {
            get => _instrumentName;
            private set
            {
                if (_instrumentName == value) return;
                _instrumentName = value;
                LogSetting(MethodBase.GetCurrentMethod()?.Name, $"{value}");
                OnStaticPropertyChanged();
            }
        }

        private static double _kingRate;
        public static double KingRate
        {
            get => _kingRate;
            set
            {
                if (Math.Abs(_kingRate - value) < 0.0000000000001) return;
                _kingRate = value;
                LogSetting(MethodBase.GetCurrentMethod()?.Name, $"{value}");
                OnStaticPropertyChanged();
            }
        }

        private static double _latitude;
        public static double Latitude
        {
            get => _latitude;
            set
            {
                if (Math.Abs(_latitude - value) < 0.0000000000001) return;
                _latitude = value;
                LogSetting(MethodBase.GetCurrentMethod()?.Name, $"{value}");
                SkyServer.SkyTasks(MountTaskName.SetSouthernHemisphere);
                OnStaticPropertyChanged();
            }
        }

        private static bool _limitTracking;
        public static bool LimitTracking
        {
            get => _limitTracking;
            set
            {
                if (_limitTracking == value) return;
                _limitTracking = value;
                LogSetting(MethodBase.GetCurrentMethod()?.Name, $"{value}");
                OnStaticPropertyChanged();
            }
        }

        private static bool _limitPark;
        public static bool LimitPark
        {
            get => _limitPark;
            set
            {
                if (_limitPark == value) return;
                _limitPark = value;
                LogSetting(MethodBase.GetCurrentMethod()?.Name, $"{value}");
                OnStaticPropertyChanged();
            }
        }

        private static double _longitude;
        public static double Longitude
        {
            get => _longitude;
            set
            {
                if (Math.Abs(_longitude - value) < 0.0000000000001) return;
                _longitude = value;
                LogSetting(MethodBase.GetCurrentMethod()?.Name, $"{value}");
                OnStaticPropertyChanged();
            }
        }

        private static double _lunarRate;
        public static double LunarRate
        {
            get => _lunarRate;
            set
            {
                if (Math.Abs(_lunarRate - value) < 0.0000000000001) return;
                _lunarRate = value;
                LogSetting(MethodBase.GetCurrentMethod()?.Name, $"{value}");
                OnStaticPropertyChanged();
            }
        }

        private static double _maxSlewRate;
        public static double MaxSlewRate
        {
            get => _maxSlewRate;
            set
            {
                if (Math.Abs(_maxSlewRate - value) < 0.0000000000001) return;
                _maxSlewRate = value;
                LogSetting(MethodBase.GetCurrentMethod()?.Name, $"{value}");
                OnStaticPropertyChanged();
                SkyServer.SetSlewRates(value);
            }
        }

        private static int _minPulseDec;
        public static int MinPulseDec
        {
            get => _minPulseDec;
            set
            {
                if (_minPulseDec == value) return;
                _minPulseDec = value;
                LogSetting(MethodBase.GetCurrentMethod()?.Name, $"{value}");
                OnStaticPropertyChanged();
                SkyServer.SkyTasks(MountTaskName.MinPulseDec);
            }
        }

        private static int _minPulseRa;
        public static int MinPulseRa
        {
            get => _minPulseRa;
            set
            {
                if (_minPulseRa == value) return;
                _minPulseRa = value;
                LogSetting(MethodBase.GetCurrentMethod()?.Name, $"{value}");
                OnStaticPropertyChanged();
                SkyServer.SkyTasks(MountTaskName.MinPulseRa);
            }
        }

        private static double[] _parkAxes = {double.NaN, double.NaN};
        /// <summary>
        /// Park axes position in mount axes values
        /// </summary>
        public static double[] ParkAxes
        {
            get => _parkAxes;
            set
            {
                if (Math.Abs(_parkAxes[0] - value[0]) <= 0.0000000000001 && Math.Abs(_parkAxes[1] - value[1]) <= 0.0000000000001)
                    return;
                _parkAxes = value;
                value[0] = Math.Round(value[0], 6);
                value[1] = Math.Round(value[1], 6);
                LogSetting(MethodBase.GetCurrentMethod()?.Name, $"{value}");
                OnStaticPropertyChanged();
            }
        }

        private static bool _pecOn;
        public static bool PecOn
        {
            get => _pecOn;
            set
            {
                if (_pecOn == value) return;
                _pecOn = value;
                LogSetting(MethodBase.GetCurrentMethod()?.Name, $"{value}");
                OnStaticPropertyChanged();
            }
        }

        private static int _pecOffSet;
        public static int PecOffSet
        {
            get => _pecOffSet;
            set
            {
                if (_pecOffSet == value) return;
                _pecOffSet = value;
                LogSetting(MethodBase.GetCurrentMethod()?.Name, $"{value}");
                OnStaticPropertyChanged();
            }
        }

        private static string _pecWormFile;
        public static string PecWormFile
        {
            get => _pecWormFile;
            set
            {
                if (_pecWormFile == value) return;
                _pecWormFile = value;
                LogSetting(MethodBase.GetCurrentMethod()?.Name, $"{value}");
                OnStaticPropertyChanged();
            }
        }

        private static string _pec360File;
        public static string Pec360File
        {
            get => _pec360File;
            set
            {
                if (_pec360File == value) return;
                _pec360File = value;
                LogSetting(MethodBase.GetCurrentMethod()?.Name, $"{value}");
                OnStaticPropertyChanged();
            }
        }

        private static bool _pPecOn;
        public static bool PPecOn
        {
            get => _pPecOn;
            set
            {
                if (_pPecOn == value) return;
                _pPecOn = value;
                LogSetting(MethodBase.GetCurrentMethod()?.Name, $"{value}");
                OnStaticPropertyChanged();
            }
        }

        private static int _polarLedLevel;
        public static int PolarLedLevel
        {
            get => _polarLedLevel;
            set
            {
                if (_polarLedLevel == value) return;
                _polarLedLevel = value;
                LogSetting(MethodBase.GetCurrentMethod()?.Name, $"{value}");
                OnStaticPropertyChanged();
            }
        }

        private static PolarMode _polarMode;
        public static PolarMode PolarMode
        {
            get => _polarMode;
            set
            {
                if (_polarMode == value) return;
                _polarMode = value;
                LogSetting(MethodBase.GetCurrentMethod()?.Name, $"{value}");
                OnStaticPropertyChanged();
            }
        }

        //private static bool _polarModeEast;
        //public static bool PolarModeEast
        //{
        //    get => _polarModeEast;
        //    set
        //    {
        //        if (_polarModeEast == value) return;
        //        _polarModeEast = value;
        //        LogSetting(MethodBase.GetCurrentMethod()?.Name, $"{value}");
        //        OnStaticPropertyChanged();
        //    }
        //}

        private static int _raBacklash;
        public static int RaBacklash
        {
            get => _raBacklash;
            set
            {
                if (_raBacklash == value) return;
                _raBacklash = value;
                LogSetting(MethodBase.GetCurrentMethod()?.Name, $"{value}");
                OnStaticPropertyChanged();
            }
        }

        private static int _readTimeout;
        public static int ReadTimeout
        {
            get => _readTimeout;
            private set
            {
                if (_readTimeout == value) return;
                _readTimeout = value;
                LogSetting(MethodBase.GetCurrentMethod()?.Name, $"{value}");
                OnStaticPropertyChanged();
            }
        }

        private static bool _refraction;
        public static bool Refraction
        {
            get => _refraction;
            set
            {
                if (_refraction == value) return;
                _refraction = value;
                LogSetting(MethodBase.GetCurrentMethod()?.Name, $"{value}");
                OnStaticPropertyChanged();
            }
        }

        private static int _raTrackingOffset;
        public static int RaTrackingOffset
        {
            get => _raTrackingOffset;
            private set
            {
                if (_raTrackingOffset.Equals(value)) return;
                _raTrackingOffset = value;
                LogSetting(MethodBase.GetCurrentMethod()?.Name, $"{value}");
                OnStaticPropertyChanged();
            }
        }

        private static bool _rtsEnable;
        public static bool RtsEnable
        {
            get => _rtsEnable;
            private set
            {
                if (_rtsEnable == value) return;
                _rtsEnable = value;
                LogSetting(MethodBase.GetCurrentMethod()?.Name, $"{value}");
                OnStaticPropertyChanged();
            }
        }

        private static double _axisLimitX;
        public static double AxisLimitX
        {
            get => _axisLimitX;
            set
            {
                if (Math.Abs(_axisLimitX - value) < 0.0000000000001) return;
                _axisLimitX = value;
                LogSetting(MethodBase.GetCurrentMethod()?.Name, $"{value}");
                OnStaticPropertyChanged();
            }
        }

        private static double _siderealRate;
        public static double SiderealRate
        {
            get => _siderealRate;
            set
            {
                if (Math.Abs(_siderealRate - value) < 0.0000000000001) return;
                _siderealRate = value;
                LogSetting(MethodBase.GetCurrentMethod()?.Name, $"{value}");
                OnStaticPropertyChanged();
            }
        }

        //private static int _spiralFov;
        //public static int SpiralFov
        //{
        //    get => _spiralFov;
        //    set
        //    {
        //        if (_spiralFov == value) return;
        //        _spiralFov = value;
        //        LogSetting(MethodBase.GetCurrentMethod()?.Name, $"{value}");
        //        OnStaticPropertyChanged();
        //    }
        //}

        //private static int _spiralPause;
        //public static int SpiralPause
        //{
        //    get => _spiralPause;
        //    set
        //    {
        //        if (_spiralPause  ==  value) return;
        //        _spiralPause = value;
        //        LogSetting(MethodBase.GetCurrentMethod()?.Name, $"{value}");
        //        OnStaticPropertyChanged();
        //    }
        //}

        //private static int _spiralSpeed;
        //public static int SpiralSpeed
        //{
        //    get => _spiralSpeed;
        //    set
        //    {
        //        if (_spiralSpeed == value) return;
        //        _spiralSpeed = value;
        //        LogSetting(MethodBase.GetCurrentMethod()?.Name, $"{value}");
        //        OnStaticPropertyChanged();
        //    }
        //}

        // ToDo: Remove if not needed
        //private static bool _spiralLimits;
        //public static bool SpiralLimits
        //{
        //    get => _spiralLimits;
        //    set
        //    {
        //        if (_spiralLimits == value) return;
        //        _spiralLimits = value;
        //        LogSetting(MethodBase.GetCurrentMethod()?.Name, $"{value}");
        //        OnStaticPropertyChanged();
        //    }
        //}

        //private static int _spiralHeight;
        //public static int SpiralHeight
        //{
        //    get => _spiralHeight;
        //    set
        //    {
        //        if (_spiralHeight == value) return;
        //        _spiralHeight = value;
        //        LogSetting(MethodBase.GetCurrentMethod()?.Name, $"{value}");
        //        OnStaticPropertyChanged();
        //    }
        //}

        //private static double _spiralDistance;
        //public static double SpiralDistance
        //{
        //    get => _spiralDistance;
        //    set
        //    {
        //        if (Math.Abs(_spiralDistance - value) < 0.0) return;
        //        _spiralDistance = value;
        //        LogSetting(MethodBase.GetCurrentMethod()?.Name, $"{value}");
        //        OnStaticPropertyChanged();
        //    }
        //}

        //private static int _spiralWidth;
        //public static int SpiralWidth
        //{
        //    get => _spiralWidth;
        //    set
        //    {
        //        if (_spiralWidth == value) return;
        //        _spiralWidth = value;
        //        LogSetting(MethodBase.GetCurrentMethod()?.Name, $"{value}");
        //        OnStaticPropertyChanged();
        //    }
        //}
        
        private static int _displayInterval;
        public static int DisplayInterval
        {
            get => _displayInterval;
            set
            {
                if (_displayInterval == value) return;
                _displayInterval = value;
                LogSetting(MethodBase.GetCurrentMethod()?.Name, $"{value}");
                OnStaticPropertyChanged();
            }
        }

        private static int _st4GuideRate;
        public static int St4GuideRate
        {
            get => _st4GuideRate;
            set
            {
                if (_st4GuideRate == value) return;
                _st4GuideRate = value;
                LogSetting(MethodBase.GetCurrentMethod()?.Name, $"{value}");
                OnStaticPropertyChanged();
                SkyServer.SkyTasks(MountTaskName.SetSt4Guiderate);
            }
        }

        private static double _solarRate;
        public static double SolarRate
        {
            get => _solarRate;
            set
            {
                if (Math.Abs(_solarRate - value) < 0.0000000000001) return;
                _solarRate = value;
                LogSetting(MethodBase.GetCurrentMethod()?.Name, $"{value}");
                OnStaticPropertyChanged();
            }
        }

        private static int _syncLimit;
        public static int SyncLimit
        {
            get => _syncLimit;
            private set
            {
                if (_syncLimit == value) return;
                _syncLimit = value;
                OnStaticPropertyChanged();
            }
        }

        private static bool _syncLimitOn;
        public static bool SyncLimitOn
        {
            get => _syncLimitOn;
            set
            {
                if (_syncLimitOn == value) return;
                _syncLimitOn = value;
                OnStaticPropertyChanged();
            }
        }

        private static double _temperature;
        public static double Temperature
        {
            get => _temperature;
            set
            {
                if (Math.Abs(_temperature - value) < 0) return;
                _temperature = value;
                LogSetting(MethodBase.GetCurrentMethod()?.Name, $"{value}");
                OnStaticPropertyChanged();
            }
        }

        //private static TimeSpan _uTCDateOffset;
        //public static TimeSpan UTCDateOffset
        // {}
        //    get => _uTCDateOffset;
        //    set
        //    {}
        //        if (_uTCDateOffset == value) return;
        //        _uTCDateOffset = value;
        //        LogSetting(MethodBase.GetCurrentMethod()?.Name, $"{value}");
        //        OnStaticPropertyChanged();
        //    }
        // }

        private static List<ParkPosition> _parkPositions;
        /// <summary>
        /// Park positions in mount axes values
        /// Polar values are stored as Az / Alt in user.config
        /// </summary>
        public static List<ParkPosition> ParkPositions
        {
            get => _parkPositions;
            set
            {
                {
                    _parkPositions = value.OrderBy(parkPosition => parkPosition.Name).ToList();
                    var output = JsonConvert.SerializeObject(_parkPositions);
                    LogSetting(MethodBase.GetCurrentMethod()?.Name, $"{output}");
                    OnStaticPropertyChanged();
                }
            }
        }

        private static string _parkName;
        public static string ParkName
        {
            get => _parkName;
            set
            {
                if (_parkName == value) return;
                _parkName = value;
                LogSetting(MethodBase.GetCurrentMethod()?.Name, $"{value}");
                OnStaticPropertyChanged();
            }
        }

        private static string _parkLimitName;
        public static string ParkLimitName
        {
            get => _parkLimitName;
            set
            {
                if (_parkLimitName == value) return;
                _parkLimitName = value;
                LogSetting(MethodBase.GetCurrentMethod()?.Name, $"{value}");
                OnStaticPropertyChanged();
            }
        }

        private static string _parkHzLimitName;
        public static string ParkHzLimitName
        {
            get => _parkHzLimitName;
            set
            {
                if (_parkHzLimitName == value) return;
                _parkHzLimitName = value;
                LogSetting(MethodBase.GetCurrentMethod()?.Name, $"{value}");
                OnStaticPropertyChanged();
            }
        }

        private static int _altAzTrackingUpdateInterval;
        public static int AltAzTrackingUpdateInterval
        {
            get => _altAzTrackingUpdateInterval;
            set
            {
                if (_altAzTrackingUpdateInterval == value) return;
                _altAzTrackingUpdateInterval = value;
                LogSetting(MethodBase.GetCurrentMethod()?.Name, $"{value}");
                OnStaticPropertyChanged();
            }
        }

        private static double _axisLowerLimitY;
        public static double AxisLowerLimitY
        {
            get => _axisLowerLimitY;
            set
            {
                if (Math.Abs(_axisLowerLimitY - value) < 0.000001) return;
                _axisLowerLimitY = value;
                LogSetting(MethodBase.GetCurrentMethod()?.Name, $"{value}");
                OnStaticPropertyChanged();
            }
        }

        private static double _axisUpperLimitY;
        public static double AxisUpperLimitY
        {
            get => _axisUpperLimitY + (AlignmentMode == AlignmentMode.Polar ? Math.Abs(Latitude) : 0);
            set
            {
                if (Math.Abs(_axisUpperLimitY - value) < 0.000001) return;
                _axisUpperLimitY = value;
                LogSetting(MethodBase.GetCurrentMethod()?.Name, $"{value}");
                OnStaticPropertyChanged();
            }
        }

        #endregion

        #region Methods

        /// <summary>
        /// Loads the application settings and initializes various configuration properties.
        /// </summary>
        /// <remarks>This method retrieves settings from the user configuration file and applies them to
        /// initialize  the application's state. It sets various capabilities, server configurations, and mount
        /// properties  based on the loaded settings. This method also handles deserialization of complex settings and 
        /// ensures that all required properties are properly initialized.</remarks>
        // [Obsolete("Settings loaded via SkySettingsBridge. This method is no longer needed.")]
        public static void Load()
        {
        }

        /// <summary>
        /// Resets the park positions to their default values based on the current latitude setting.
        /// </summary>
        /// <remarks>This method adjusts the Y-coordinates of the park positions to account for the
        /// absolute value of the latitude  and sets the park state to its default configuration. The updated park
        /// positions are serialized and saved  to the application's settings.</remarks>
        public static void ResetParkPositions()
        {
            // Phase A.5: Updated to use static properties and hardcoded defaults instead of app.config
            // Default park positions for Polar mount: (0, 0) and (0, 5) adjusted for latitude
            
            var parkPositions = new List<ParkPosition>
            {
                new ParkPosition
                {
                    Name = "Default",
                    X = 0.0,
                    Y = Math.Round(Math.Abs(Latitude) - 90.0, 6)
                },
                new ParkPosition
                {
                    Name = "Home",
                    X = 0.0,
                    Y = Math.Round(Math.Abs(Latitude) - 85.0, 6) // 5 degrees from default
                }
            };
            
            // Update static properties directly (bridge will handle JSON persistence)
            ParkPositions = parkPositions;
            AtPark = false;
            ParkName = "Default";
        }

        /// <summary>
        /// save and reload using current SettingsKey
        /// </summary>
        [Obsolete("Settings loaded via SkySettingsBridge. This method is no longer needed.")]
        public static void Save()
        {
            
            //// Phase 2: Sync to new settings system via bridge
            //SkySettingsBridge.OnOldSettingsSaved();
        }

        /// <summary>
        /// output to session log
        /// </summary>
        /// <param name="method"></param>
        /// <param name="value"></param>
        private static void LogSetting(string method, string value)
        {
            var monitorItem = new MonitorEntry
            { Datetime = Principles.HiResDateTime.UtcNow, Device = MonitorDevice.Server, Category = MonitorCategory.Server, Type = MonitorType.Information, Method = $"{method}", Thread = Thread.CurrentThread.ManagedThreadId, Message = $"{value}" };
            MonitorLog.LogToMonitor(monitorItem);
        }

        /// <summary>
        /// property event notification
        /// </summary>
        /// <param name="propertyName"></param>
        private static void OnStaticPropertyChanged([CallerMemberName] string propertyName = null)
        {
            StaticPropertyChanged?.Invoke(null, new PropertyChangedEventArgs(propertyName));
        }

        /// <summary>
        /// Internal method for SkySettingsBridge to set properties with private setters from JSON.
        /// This bypasses private setters by directly accessing backing fields.
        /// Phase A.6: Bridge capability sync - handles 28 capabilities + 13 read-only properties = 41 total
        /// </summary>
        /// <param name="instance">The SkySettingsInstance containing values loaded from JSON</param>
        internal static void SetCapabilitiesAndReadOnlyFromInstance(SkySettingsInstance instance)
        {
            try
            {
                // =================================================================
                // 28 CAPABILITY PROPERTIES (all have private setters)
                // =================================================================
                _canAlignMode = instance.CanAlignMode;
                _canAltAz = instance.CanAltAz;
                _canEquatorial = instance.CanEquatorial;
                _canFindHome = instance.CanFindHome;
                _canLatLongElev = instance.CanLatLongElev;
                _canOptics = instance.CanOptics;
                _canPark = instance.CanPark;
                _canPulseGuide = instance.CanPulseGuide;
                _canSetEquRates = instance.CanSetEquRates;
                _canSetDeclinationRate = instance.CanSetDeclinationRate;
                _canSetGuideRates = instance.CanSetGuideRates;
                _canSetRightAscensionRate = instance.CanSetRightAscensionRate;
                _canSetTracking = instance.CanSetTracking;
                _canSiderealTime = instance.CanSiderealTime;
                _canSlew = instance.CanSlew;
                _canSlewAltAz = instance.CanSlewAltAz;
                _canSlewAltAzAsync = instance.CanSlewAltAzAsync;
                _canSlewAsync = instance.CanSlewAsync;
                _canSync = instance.CanSync;
                _canSyncAltAz = instance.CanSyncAltAz;
                _canTrackingRates = instance.CanTrackingRates;
                _canUnPark = instance.CanUnPark;
                _noSyncPastMeridian = instance.NoSyncPastMeridian;
                _numMoveAxis = instance.NumMoveAxis;
                _versionOne = instance.VersionOne;

                // Note: CanSetPark and CanSetPierSide have public setters - 
                // handled by bridge's normal sync (no backing field access needed)

                // =================================================================
                // 13 SERVER SETTINGS WITH PRIVATE SETTERS
                // =================================================================

                // Vector3 (no PropertyChanged event, just direct assignment)
                _axisModelOffsets = instance.AxisModelOffsets;

                // Handshake enum - directly accessible as read-only property
                _handShake = instance.HandShake;

                // Optics properties
                _apertureArea = instance.ApertureArea;
                _apertureDiameter = instance.ApertureDiameter;

                // Behavior properties
                _autoTrack = instance.AutoTrack;
                _gotoPrecision = instance.GotoPrecision;
                _syncLimit = instance.SyncLimit;

                // Serial communication properties (FIXED property names)
                _dataBits = instance.DataBits;
                _dtrEnable = instance.DtrEnable;       // FIXED: was DTREnable
                _readTimeout = instance.ReadTimeout;
                _rtsEnable = instance.RtsEnable;        // FIXED: was RTSEnable

                // Tracking offset (FIXED property name)
                _raTrackingOffset = instance.RaTrackingOffset;  // FIXED: was RATrackingOffset

                // Instrument metadata
                _instrumentDescription = instance.InstrumentDescription;
                _instrumentName = instance.InstrumentName;

                var monitorItem = new MonitorEntry
                {
                    Datetime = Principles.HiResDateTime.UtcNow,
                    Device = MonitorDevice.Server,
                    Category = MonitorCategory.Server,
                    Type = MonitorType.Information,
                    Method = MethodBase.GetCurrentMethod()?.Name,
                    Thread = Thread.CurrentThread.ManagedThreadId,
                    Message = "Set 41 properties via backing fields (28 capabilities + 13 read-only)"
                };
                MonitorLog.LogToMonitor(monitorItem);
            }
            catch (Exception ex)
            {
                var monitorItem = new MonitorEntry
                {
                    Datetime = Principles.HiResDateTime.UtcNow,
                    Device = MonitorDevice.Server,
                    Category = MonitorCategory.Server,
                    Type = MonitorType.Error,
                    Method = MethodBase.GetCurrentMethod()?.Name,
                    Thread = Thread.CurrentThread.ManagedThreadId,
                    Message = $"Failed to set capabilities/read-only properties: {ex.Message}"
                };
                MonitorLog.LogToMonitor(monitorItem);
                throw;
            }
        }

        /// <summary>
        /// Internal method for SkySettingsBridge to set properties with private setters directly from JSON.
        /// This bypasses the SkySettingsInstance wrapper and reads directly from the JSON settings model.
        /// Phase A.6: Bridge capability sync - handles 28 capabilities + 13 read-only properties = 41 total
        /// </summary>
        /// <param name="jsonSettings">The Settings.Models.SkySettings loaded from JSON</param>
        internal static void SetCapabilitiesAndReadOnlyFromJson(Settings.Models.SkySettings jsonSettings)
        {
            try
            {
                // =================================================================
                // 28 CAPABILITY PROPERTIES (all have private setters)
                // =================================================================
                _canAlignMode = jsonSettings.CanAlignMode;
                _canAltAz = jsonSettings.CanAltAz;
                _canEquatorial = jsonSettings.CanEquatorial;
                _canFindHome = jsonSettings.CanFindHome;
                _canLatLongElev = jsonSettings.CanLatLongElev;
                _canOptics = jsonSettings.CanOptics;
                _canPark = jsonSettings.CanPark;
                _canPulseGuide = jsonSettings.CanPulseGuide;
                _canSetEquRates = jsonSettings.CanSetEquRates;
                _canSetDeclinationRate = jsonSettings.CanSetDeclinationRate;
                _canSetGuideRates = jsonSettings.CanSetGuideRates;
                _canSetRightAscensionRate = jsonSettings.CanSetRightAscensionRate;
                _canSetTracking = jsonSettings.CanSetTracking;
                _canSiderealTime = jsonSettings.CanSiderealTime;
                _canSlew = jsonSettings.CanSlew;
                _canSlewAltAz = jsonSettings.CanSlewAltAz;
                _canSlewAltAzAsync = jsonSettings.CanSlewAltAzAsync;
                _canSlewAsync = jsonSettings.CanSlewAsync;
                _canSync = jsonSettings.CanSync;
                _canSyncAltAz = jsonSettings.CanSyncAltAz;
                _canTrackingRates = jsonSettings.CanTrackingRates;
                _canUnPark = jsonSettings.CanUnpark;  // Note: JSON has "CanUnpark" not "CanUnPark"
                _noSyncPastMeridian = jsonSettings.NoSyncPastMeridian;
                _numMoveAxis = jsonSettings.NumMoveAxis;
                _versionOne = jsonSettings.VersionOne;

                // Note: CanSetPark and CanSetPierSide have public setters in static SkySettings
                // They're handled by bridge's normal sync

                // =================================================================
                // 13 SERVER SETTINGS WITH PRIVATE SETTERS
                // =================================================================

                // Vector3 - needs conversion from AxisModelOffset class
                _axisModelOffsets = new System.Numerics.Vector3(
                    (float)jsonSettings.AxisModelOffsets.X,
                    (float)jsonSettings.AxisModelOffsets.Y,
                    (float)jsonSettings.AxisModelOffsets.Z
                );

                // Handshake enum - parse from string (use bridge's method or inline parse)
                if (Enum.TryParse<Handshake>(jsonSettings.Handshake, true, out var handshakeResult))
                {
                    _handShake = handshakeResult;
                }
                else
                {
                    _handShake = Handshake.None; // Default fallback
                }

                // Optics properties (calculated, but can be set from JSON if present)
                _apertureArea = jsonSettings.ApertureArea;
                _apertureDiameter = jsonSettings.ApertureDiameter;

                // Behavior properties
                _autoTrack = jsonSettings.AutoTrack;
                _gotoPrecision = jsonSettings.GotoPrecision;
                _syncLimit = jsonSettings.SyncLimit;

                // Serial communication properties
                _dataBits = jsonSettings.DataBits;
                _dtrEnable = jsonSettings.DTREnable;       // JSON has DTREnable
                _readTimeout = jsonSettings.ReadTimeout;
                _rtsEnable = jsonSettings.RTSEnable;       // JSON has RTSEnable

                // Tracking offset
                _raTrackingOffset = jsonSettings.RATrackingOffset;  // JSON has RATrackingOffset

                // Instrument metadata
                _instrumentDescription = jsonSettings.InstrumentDescription;
                _instrumentName = jsonSettings.InstrumentName;

                var monitorItem = new MonitorEntry
                {
                    Datetime = Principles.HiResDateTime.UtcNow,
                    Device = MonitorDevice.Server,
                    Category = MonitorCategory.Server,
                    Type = MonitorType.Information,
                    Method = MethodBase.GetCurrentMethod()?.Name,
                    Thread = Thread.CurrentThread.ManagedThreadId,
                    Message = "Set 41 properties from JSON (28 capabilities + 13 read-only)"
                };
                MonitorLog.LogToMonitor(monitorItem);
            }
            catch (Exception ex)
            {
                var monitorItem = new MonitorEntry
                {
                    Datetime = Principles.HiResDateTime.UtcNow,
                    Device = MonitorDevice.Server,
                    Category = MonitorCategory.Server,
                    Type = MonitorType.Error,
                    Method = MethodBase.GetCurrentMethod()?.Name,
                    Thread = Thread.CurrentThread.ManagedThreadId,
                    Message = $"Failed to set capabilities/read-only from JSON: {ex.Message}"
                };
                MonitorLog.LogToMonitor(monitorItem);
                throw;
            }
        }
        
        #endregion
    }
}
