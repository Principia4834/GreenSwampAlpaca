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
using GreenSwamp.Alpaca.Mount.AutoHome;
using GreenSwamp.Alpaca.Mount.Commands;
using GreenSwamp.Alpaca.Mount.Simulator;
using GreenSwamp.Alpaca.Mount.SkyWatcher;
using GreenSwamp.Alpaca.Principles;
using GreenSwamp.Alpaca.Shared;
using System.Reflection;
using GreenSwamp.Alpaca.MountControl.AutoHome;

namespace GreenSwamp.Alpaca.MountControl
{
    /// <summary>Mount partial — core operations (Stop, Abort, Park, Home, Sync, AutoHome). Phase M2.</summary>
    public partial class Mount
    {
        #region Core Operations

        /// <summary>Stop axes in a normal motion.</summary>
        private void StopAxes()
        {
            if (!IsMountRunning) return;
            AutoHomeStop = true;
            var monitorItem = new MonitorEntry
            {
                Datetime = HiResDateTime.UtcNow,
                Device = MonitorDevice.Server,
                Category = MonitorCategory.Server,
                Type = MonitorType.Information,
                Method = MethodBase.GetCurrentMethod()?.Name,
                Thread = Thread.CurrentThread.ManagedThreadId,
                Message = $"{_slewState}"
            };
            MonitorLog.LogToMonitor(monitorItem);
            CancelAllAsync();
            _moveAxisActive = false;
            _rateMoveAxes.X = 0.0;
            _rateMoveAxes.Y = 0.0;
            RateRa = 0.0;
            RateDec = 0.0;
            if (!SkyServer.AxesStopValidate(this))
            {
                switch (Settings.Mount)
                {
                    case MountType.Simulator:
                        SkyServer.SimTasks(MountTaskName.StopAxes, this);
                        break;
                    case MountType.SkyWatcher:
                        SkyServer.SkyTasks(MountTaskName.StopAxes, this);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
            _slewState = SlewType.SlewNone;
            Tracking = false;
            TrackingMode = TrackingMode.Off;
        }

        /// <summary>Abort any active slew with optional start notification — instance version.</summary>
        public void AbortSlew(bool speak, EventWaitHandle? abortSlewStarted = null)
        {
            if (!IsMountRunning)
            {
                abortSlewStarted?.Set();
                return;
            }
            MonitorLog.LogToMonitor(new MonitorEntry
            {
                Datetime = HiResDateTime.UtcNow,
                Device = MonitorDevice.Server,
                Category = MonitorCategory.Server,
                Type = MonitorType.Information,
                Method = MethodBase.GetCurrentMethod()?.Name,
                Thread = Thread.CurrentThread.ManagedThreadId,
                Message = $"{_slewState}|{Tracking}"
            });
            abortSlewStarted?.Set();
            var tracking = Tracking || _slewState == SlewType.SlewRaDec || _moveAxisActive;
            ApplyTracking(false);
            if (_slewController != null)
            {
                MonitorLog.LogToMonitor(new MonitorEntry
                {
                    Datetime = HiResDateTime.UtcNow,
                    Device = MonitorDevice.Server,
                    Category = MonitorCategory.Server,
                    Type = MonitorType.Information,
                    Method = MethodBase.GetCurrentMethod()?.Name,
                    Thread = Thread.CurrentThread.ManagedThreadId,
                    Message = "Cancelling SlewController operation"
                });
                _slewController.CancelCurrentSlewAsync().Wait();
            }
            CancelAllAsync();
            _moveAxisActive = false;
            _rateMoveAxes.X = 0.0;
            _rateMoveAxes.Y = 0.0;
            RateRa = 0.0;
            RateDec = 0.0;
            switch (Settings.Mount)
            {
                case MountType.Simulator:
                    SkyServer.SimTasks(MountTaskName.StopAxes, this);
                    break;
                case MountType.SkyWatcher:
                    SkyServer.SkyTasks(MountTaskName.StopAxes, this);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
            if (Settings.AlignmentMode == AlignmentMode.AltAz)
            {
                AxesRateOfChange.Reset();
                SkyPredictor.Set(RightAscensionXForm, DeclinationXForm);
            }
            ApplyTracking(tracking);
            MonitorLog.LogToMonitor(new MonitorEntry
            {
                Datetime = HiResDateTime.UtcNow,
                Device = MonitorDevice.Server,
                Category = MonitorCategory.Server,
                Type = MonitorType.Information,
                Method = MethodBase.GetCurrentMethod()?.Name,
                Thread = Thread.CurrentThread.ManagedThreadId,
                Message = "AbortSlew completed"
            });
        }

        /// <summary>GoTo park slew — synchronous version.</summary>
        private void GoToPark()
        {
            ApplyTracking(false);
            var ps = _parkSelected;
            if (ps == null || double.IsNaN(ps.X) || double.IsNaN(ps.Y)) return;
            SetParkAxis(ps.Name, ps.X, ps.Y);
            Settings.ParkAxes = [ps.X, ps.Y];
            Settings.ParkName = ps.Name;
            MonitorLog.LogToMonitor(new MonitorEntry
            {
                Datetime = HiResDateTime.UtcNow,
                Device = MonitorDevice.Server,
                Category = MonitorCategory.Server,
                Type = MonitorType.Information,
                Method = MethodBase.GetCurrentMethod()?.Name,
                Thread = Thread.CurrentThread.ManagedThreadId,
                Message = $"Slew to Park: {ps.Name}|{ps.X}|{ps.Y}"
            });
            SlewSync([ps.X, ps.Y], SlewType.SlewPark, tracking: false);
        }

        /// <summary>Complete park — delegates to InstanceCompletePark.</summary>
        public void CompletePark() => InstanceCompletePark();

        /// <summary>Auto home using mount home sensor — instance version.</summary>
        public async void AutoHomeAsync(int degreeLimit = 100, int offSetDec = 0)
        {
            try
            {
                if (!IsMountRunning) return;
                IsAutoHomeRunning = true;
                LastAutoHomeError = null;
                MonitorLog.LogToMonitor(new MonitorEntry
                {
                    Datetime = HiResDateTime.UtcNow,
                    Device = MonitorDevice.Server,
                    Category = MonitorCategory.Server,
                    Type = MonitorType.Information,
                    Method = MonitorLog.GetCurrentMethod(),
                    Thread = Thread.CurrentThread.ManagedThreadId,
                    Message = "Started"
                });
                if (degreeLimit < 20) degreeLimit = 100;
                AutoHomeProgressBar = 0;
                var encoderTemp = Settings.Encoders;
                if (Tracking) ApplyTracking(false);
                AutoHomeResult raResult, decResult;
                switch (Settings.Mount)
                {
                    case MountType.Simulator:
                        var autoHomeSim = new AutoHomeSim(Settings, SimQueue!, this);
                        raResult = await Task.Run(() => autoHomeSim.StartAutoHome(Axis.Axis1, degreeLimit));
                        AutoHomeProgressBar = 50;
                        decResult = await Task.Run(() => autoHomeSim.StartAutoHome(Axis.Axis2, degreeLimit, offSetDec));
                        break;
                    case MountType.SkyWatcher:
                        var autoHomeSky = new AutoHomeSky(Settings, SkyQueue!, this);
                        raResult = await Task.Run(() => autoHomeSky.StartAutoHome(Axis.Axis1, degreeLimit));
                        AutoHomeProgressBar = 50;
                        decResult = await Task.Run(() => autoHomeSky.StartAutoHome(Axis.Axis2, degreeLimit, offSetDec));
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
                Settings.Encoders = encoderTemp;
                StopAxes();
                MonitorLog.LogToMonitor(new MonitorEntry
                {
                    Datetime = HiResDateTime.UtcNow,
                    Device = MonitorDevice.Server,
                    Category = MonitorCategory.Server,
                    Type = MonitorType.Information,
                    Method = MonitorLog.GetCurrentMethod(),
                    Thread = Thread.CurrentThread.ManagedThreadId,
                    Message = $"Complete: {raResult}|{decResult}"
                });
                if (raResult == AutoHomeResult.Success && decResult == AutoHomeResult.Success)
                {
                    ReSyncAxes(new ParkPosition("AutoHome", Settings.AutoHomeAxisX, Settings.AutoHomeAxisY), false);
                    Thread.Sleep(1500);
                }
                else if (raResult == AutoHomeResult.StopRequested || decResult == AutoHomeResult.StopRequested)
                {
                    return;
                }
                else
                {
                    string raMsg = GetAutoHomeResultMessage(raResult, "RA");
                    string decMsg = GetAutoHomeResultMessage(decResult, "Dec");
                    var ex = new Exception($"Incomplete: {raMsg} ({raResult}), {decMsg} ({decResult})");
                    LastAutoHomeError = ex;
                    _mountError = ex;
                    throw ex;
                }
            }
            catch (Exception ex)
            {
                MonitorLog.LogToMonitor(new MonitorEntry
                {
                    Datetime = HiResDateTime.UtcNow,
                    Device = MonitorDevice.Server,
                    Category = MonitorCategory.Server,
                    Type = MonitorType.Error,
                    Method = MonitorLog.GetCurrentMethod(),
                    Thread = Thread.CurrentThread.ManagedThreadId,
                    Message = $"{ex.Message}|{ex.StackTrace}"
                });
                LastAutoHomeError = ex;
                _mountError = ex;
            }
            finally
            {
                AutoHomeProgressBar = 100;
                IsAutoHomeRunning = false;
            }
        }

        private static string GetAutoHomeResultMessage(AutoHomeResult result, string axisName)
        {
            switch (result)
            {
                case AutoHomeResult.Success: return $"{axisName} homed successfully";
                case AutoHomeResult.FailedHomeSensorReset: return $"{axisName} failed home sensor reset";
                case AutoHomeResult.HomeSensorNotFound: return $"{axisName} home sensor not found";
                case AutoHomeResult.TooManyRestarts: return $"{axisName} too many restarts";
                case AutoHomeResult.HomeCapabilityCheckFailed: return $"{axisName} home capability check failed";
                case AutoHomeResult.StopRequested: return $"{axisName} auto home stopped";
                default: return $"{axisName} unknown error";
            }
        }

        /// <summary>Reset axes positions — instance version.</summary>
        private void ReSyncAxes(ParkPosition? parkPosition = null, bool saveParkPosition = true)
        {
            if (!IsMountRunning) return;
            ApplyTracking(false);
            StopAxes();
            double[] position = [_homeAxes.X, _homeAxes.Y];
            var name = "home";
            if (parkPosition != null)
            {
                var context = AxesContext.FromSettings(Settings);
                position = Axes.AxesAppToMount([parkPosition.X, parkPosition.Y], context);
                name = parkPosition.Name;
            }
            MonitorLog.LogToMonitor(new MonitorEntry
            {
                Datetime = HiResDateTime.UtcNow,
                Device = MonitorDevice.Server,
                Category = MonitorCategory.Server,
                Type = MonitorType.Information,
                Method = MethodBase.GetCurrentMethod()?.Name,
                Thread = Thread.CurrentThread.ManagedThreadId,
                Message = $"{name}|{position[0]}|{position[1]}"
            });
            switch (Settings.Mount)
            {
                case MountType.Simulator:
                    SkyServer.SimTasks(MountTaskName.StopAxes, this);
                    var mq = SimQueue!;
                    _ = new CmdAxisToDegrees(mq.NewId, mq, Axis.Axis1, position[0]);
                    _ = new CmdAxisToDegrees(mq.NewId, mq, Axis.Axis2, position[1]);
                    break;
                case MountType.SkyWatcher:
                    SkyServer.SkyTasks(MountTaskName.StopAxes, this);
                    var sq = SkyQueue!;
                    _ = new SkySetAxisPosition(sq.NewId, sq, Axis.Axis1, position[0]);
                    _ = new SkySetAxisPosition(sq.NewId, sq, Axis.Axis2, position[1]);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
            if (parkPosition != null && saveParkPosition)
            {
                _parkSelected = parkPosition;
                GoToPark();
            }
            _hcPrevMoveRa = null;
            _hcPrevMoveDec = null;
        }

        /// <summary>Get default startup positions — instance version of GetDefaultPositions_Internal.</summary>
        private double[] GetDefaultPositions()
        {
            double[] positions = [0, 0];
            var homeAxes = GetHomeAxes(Settings.HomeAxisX, Settings.HomeAxisY);
            MonitorLog.LogToMonitor(new MonitorEntry
            {
                Datetime = HiResDateTime.UtcNow,
                Device = MonitorDevice.Server,
                Category = MonitorCategory.Server,
                Type = MonitorType.Information,
                Method = MethodBase.GetCurrentMethod()?.Name,
                Thread = Thread.CurrentThread.ManagedThreadId,
                Message = $"Home|{homeAxes.X}|{homeAxes.Y}|{Settings.HomeAxisX}|{Settings.HomeAxisY}"
            });
            if (AtPark)
            {
                if (Settings.AutoTrack)
                {
                    AtPark = false;
                    ApplyTracking(Settings.AutoTrack);
                }
                var context = AxesContext.FromSettings(Settings);
                positions = Axes.AxesAppToMount(Settings.ParkAxes, context);
                _parkSelected = GetStoredParkPosition();
                MonitorLog.LogToMonitor(new MonitorEntry
                {
                    Datetime = HiResDateTime.UtcNow,
                    Device = MonitorDevice.Server,
                    Category = MonitorCategory.Server,
                    Type = MonitorType.Information,
                    Method = MethodBase.GetCurrentMethod()?.Name,
                    Thread = Thread.CurrentThread.ManagedThreadId,
                    Message = $"Parked,{Settings.ParkName}|{Settings.ParkAxes[0]}|{Settings.ParkAxes[1]}"
                });
            }
            else
            {
                positions = [homeAxes.X, homeAxes.Y];
            }
            MonitorLog.LogToMonitor(new MonitorEntry
            {
                Datetime = HiResDateTime.UtcNow,
                Device = MonitorDevice.Server,
                Category = MonitorCategory.Server,
                Type = MonitorType.Information,
                Method = MethodBase.GetCurrentMethod()?.Name,
                Thread = Thread.CurrentThread.ManagedThreadId,
                Message = $"Load:{positions[0]}|{positions[1]}"
            });
            return positions;
        }

        /// <summary>Get stored park position from settings — instance version.</summary>
        private ParkPosition GetStoredParkPosition()
            => new ParkPosition(Settings.ParkName, Settings.ParkAxes[0], Settings.ParkAxes[1]);

        /// <summary>Set park axis by coordinates — private instance helper.</summary>
        private void SetParkAxis(string name, double x, double y)
        {
            if (string.IsNullOrEmpty(name)) name = "Empty";
            _parkSelected = new ParkPosition(name, x, y);
            MonitorLog.LogToMonitor(new MonitorEntry
            {
                Datetime = HiResDateTime.UtcNow,
                Device = MonitorDevice.Server,
                Category = MonitorCategory.Server,
                Type = MonitorType.Information,
                Method = MethodBase.GetCurrentMethod()?.Name,
                Thread = Thread.CurrentThread.ManagedThreadId,
                Message = $"{name}|{x}|{y}"
            });
        }

        /// <summary>Start axes slew using SlewController.</summary>
        public void SlewAxes(double primaryAxis, double secondaryAxis, SlewType slewState, bool slewAsync = true)
        {
            if (!IsMountRunning) return;
            if (slewAsync)
                _ = SlewAsync([primaryAxis, secondaryAxis], slewState);
            else
                SlewSync([primaryAxis, secondaryAxis], slewState);
        }

        #endregion
    }
}
