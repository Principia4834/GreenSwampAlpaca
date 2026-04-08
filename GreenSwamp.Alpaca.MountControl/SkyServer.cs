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
// ReSharper disable RedundantAssignment
using ASCOM.Common.DeviceInterfaces;
using GreenSwamp.Alpaca.Mount.Commands;
using GreenSwamp.Alpaca.Mount.Simulator;
using GreenSwamp.Alpaca.Mount.SkyWatcher;
using GreenSwamp.Alpaca.Principles;
using GreenSwamp.Alpaca.Server.MountControl;
using GreenSwamp.Alpaca.Shared;
using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using static System.Net.Mime.MediaTypeNames;


namespace GreenSwamp.Alpaca.MountControl
{
    public static partial class SkyServer
    {
        #region Events

        public static event PropertyChangedEventHandler StaticPropertyChanged;

        /// <summary>
        /// Fires StaticPropertyChanged("Steps") for Blazor UI observers.
        /// Called by MountInstance.ReceiveSteps instead of routing through the static Steps setter.
        /// </summary>
        internal static void NotifyStepsChanged()
        {
            StaticPropertyChanged?.Invoke(null, new PropertyChangedEventArgs("Steps"));
        }

        #endregion

        #region Property Settings 

        /// <summary>Mount hardware type for the default instance (Simulator or SkyWatcher)</summary>
        public static MountType Mount => _settings?.Mount ?? default;

        #region Backers
        // private static bool _alertState;
        // private static bool _asComOn;
        // private static bool _limitAlarm;
        // private static bool _rotate3DModel;
        // Phase 6: _autoHomeProgressBar, _autoHomeStop, _canHomeSensor, _capabilities,
        //          _isAutoHomeRunning, _isPulseGuidingDec, _isPulseGuidingRa, _canPPec,
        //          _canPolarLed, _canAdvancedCmdSupport, _rateMoveAxes, _moveAxisActive,
        //          _snapPort1Result, _snapPort2Result moved to MountInstance backing fields.
        // Phase 6: _parkSelected moved to MountInstance backing field
        private static ParkPosition? _parkSelected
        {
            get => _defaultInstance?._parkSelected;
            set { if (_defaultInstance != null) _defaultInstance._parkSelected = value; }
        }
        // Step 7: _steps moved to MountInstance backing field

        #endregion

        #endregion

        #region Simulator Items

        #endregion

        #region SkyWatcher Items

        #endregion

        #region Shared Mount Items

        ///// <summary>
        ///// Convert the move rate in hour angle and declination to a move rate in altitude and azimuth
        ///// </summary>
        ///// <param name="haRate">The ha rate.</param>
        ///// <param name="decRate">The dec rate </param>
        ///// <returns></returns>
        //private static Vector ConvertRateToAltAz(double haRate, double decRate)
        //{
        //    return ConvertRateToAltAz(haRate, decRate, TargetDec);
        //}

        ///// <summary>
        ///// gets HC speed in degrees
        ///// </summary>
        ///// <param name="speed"></param>
        ///// <returns></returns>
        //public static double GetSlewSpeed(SlewSpeed speed)
        //{
        //    switch (speed)
        //    {
        //        case SlewSpeed.One:
        //            return SlewSpeedOne;
        //        case SlewSpeed.Two:
        //            return SlewSpeedTwo;
        //        case SlewSpeed.Three:
        //            return SlewSpeedThree;
        //        case SlewSpeed.Four:
        //            return SlewSpeedFour;
        //        case SlewSpeed.Five:
        //            return SlewSpeedFive;
        //        case SlewSpeed.Six:
        //            return SlewSpeedSix;
        //        case SlewSpeed.Seven:
        //            return SlewSpeedSeven;
        //        case SlewSpeed.Eight:
        //            return SlewSpeedEight;
        //        default:
        //            return 0.0;
        //    }
        //}

        /// <summary>
        /// Resets the anti-backlash for the hand controller
        /// </summary>
        private static void HcResetPrevMove(MountAxis axis)
        {
            switch (axis)
            {
                case MountAxis.Dec:
                    _hcPrevMoveDec = null;
                    break;
                case MountAxis.Ra:
                    _hcPrevMoveRa = null;
                    break;
            }
        }
        #endregion

        #region Alignment

        // internal static double DegToRad(double degree) { return (degree / 180.0 * Math.PI); }
        // internal static double RadToDeg(double rad) { return (rad / Math.PI * 180.0); }

        #endregion

        #region Snap Ports

        // SnapPort1, SnapPort2, SnapPort1Result, SnapPort2Result will be moved here

        #endregion

        #region Park Management

        // ParkSelected, SetParkAxis will be moved here

        #endregion

        #region Server Items

        /// <summary>
        /// Starts/Stops current selected mount
        /// </summary>
        public static bool IsMountRunning
        {
            get => _defaultInstance?.IsMountRunning ?? false;
            set
            {
                if (_defaultInstance != null) _defaultInstance._loopCounter = 0;
                if (value)
                {
                    MountStart();
                }
                else
                {
                    MountStop();
                }

                OnStaticPropertyChanged();
            }
        }

        /// <summary>
        /// 
        /// </summary>
        public struct LimitStatusType
        {
            public bool AtLowerLimitAxisX { get; set; }
            public bool AtUpperLimitAxisX { get; set; }
            public bool AtLowerLimitAxisY { get; set; }
            public bool AtUpperLimitAxisY { get; set; }
            public bool AtLimit
            {
                get => AtLowerLimitAxisX || AtUpperLimitAxisX || AtLowerLimitAxisY || AtUpperLimitAxisY;
            }
        }

        public static LimitStatusType LimitStatus = new LimitStatusType();

        #endregion

    }
}
