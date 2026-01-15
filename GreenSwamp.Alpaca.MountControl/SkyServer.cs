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

        #endregion

        #region Property Settings 

        #region Backers
        private static double _actualAxisX;
        private static double _actualAxisY;
        // private static bool _alertState;
        private static Vector _altAzm;
        private static int _autoHomeProgressBar;
        private static bool _autoHomeStop;
        // private static bool _asComOn;
        private static bool _canHomeSensor;
        private static string _capabilities;
        private static double _declinationXForm;
        private static bool _isAutoHomeRunning;
        private static bool _isHome;
        private static bool _isPulseGuidingDec;
        private static bool _isPulseGuidingRa;
        private static PointingState _isSideOfPier;
        private static double _lha;
        // private static bool _limitAlarm;
        private static bool _lowVoltageEventState;
        private static bool _mountRunning;
        private static bool _monitorPulse;
        private static double _appAxisX;
        private static double _appAxisY;
        private static Exception _mountError;
        private static ParkPosition _parkSelected;
        private static bool _canPPec;
        private static bool _canPolarLed;
        private static bool _canAdvancedCmdSupport;
        private static Vector _raDec;
        private static Vector _rateMoveAxes;
        private static bool _moveAxisActive;
        private static double _rightAscensionXForm;
        // private static bool _rotate3DModel;
        private static double _slewSettleTime;
        private static double _siderealTime;
        private static TrackingMode _trackingMode;
        private static bool _tracking; //off
        private static bool _snapPort1Result;
        private static bool _snapPort2Result;
        private static double[] _steps = { 0.0, 0.0 };
        private static bool _mountPositionUpdated;
        private static readonly object MountPositionUpdatedLock = new object();
        #endregion

        #region PEC

        /// <summary>
        /// Can mount do PPec
        /// </summary>
        internal static bool CanPPec
        {
            get => _canPPec;
            set
            {
                if (_canPPec == value) return;
                _canPPec = value;
                OnStaticPropertyChanged();
            }
        }

        private static bool _pecShow;
        /// <summary>
        /// sets up bool to load a test tab
        /// </summary>
        public static bool PecShow
        {
            get => _pecShow;
            set
            {
                _pecShow = value;
                OnStaticPropertyChanged();
            }
        }

        /// <summary>
        /// Pec status
        /// </summary>
        internal static bool PecOn
        {
            get => _settings!.PecOn;
            set
            {
                _settings!.PecOn = value;
                // set back to normal tracking
                if (!value && Tracking) { SetTracking(); }
                OnStaticPropertyChanged();
            }
        }

        private static Tuple<int, double, int> _pecBinNow;

        /// <summary>
        /// Pec Currently used bin for Pec
        /// </summary>
        public static Tuple<int, double, int> PecBinNow
        {
            get => _pecBinNow;
            private set
            {
                if (Equals(_pecBinNow, value)) { return; }
                _pecBinNow = value;
                OnStaticPropertyChanged();
            }
        }

        /// <summary>
        /// Pec Worm count bins
        /// </summary>
        internal static int PecBinCount { get; set; }

        /// <summary>
        /// Pec size by steps
        /// </summary>
        private static double PecBinSteps { get; set; }

        /// <summary>
        /// Turn on/off mount PPec
        /// </summary>
        private static bool PPecOn
        {
            get => _settings!.PPecOn;
            set
            {
                _settings!.PPecOn = value;
                SkyTasks(MountTaskName.Pec);
                OnStaticPropertyChanged();
            }
        }

        /// <summary>
        /// turn on/off mount training
        /// </summary>
        private static bool _pPecTraining;
        private static bool PecTraining
        {
            get => _pPecTraining;
            set
            {
                if (PecTraining == value) return;
                _pPecTraining = value;
                // ToDo reimplement voice synthesis later
                // Synthesizer.Speak(value ? Application.Current.Resources["vcePeckTrainOn"].ToString() : Application.Current.Resources["vcePeckTrainOff"].ToString());

                var monitorItem = new MonitorEntry
                {
                    Datetime = HiResDateTime.UtcNow,
                    Device = MonitorDevice.Server,
                    Category = MonitorCategory.Server,
                    Type = MonitorType.Information,
                    Method = MethodBase.GetCurrentMethod()?.Name,
                    Thread = Thread.CurrentThread.ManagedThreadId,
                    Message = $"{value}"
                };
                MonitorLog.LogToMonitor(monitorItem);

                SkyTasks(MountTaskName.PecTraining);
                OnStaticPropertyChanged();
            }
        }

        /// <summary>
        /// Tracks training within mount
        /// </summary>
        private static bool _pPecTrainInProgress;
        public static bool PecTrainInProgress
        {
            get => _pPecTrainInProgress;
            private set
            {
                if (_pPecTrainInProgress == value) return;
                _pPecTrainInProgress = value;

                var monitorItem = new MonitorEntry
                {
                    Datetime = HiResDateTime.UtcNow,
                    Device = MonitorDevice.Server,
                    Category = MonitorCategory.Server,
                    Type = MonitorType.Information,
                    Method = MethodBase.GetCurrentMethod()?.Name,
                    Thread = Thread.CurrentThread.ManagedThreadId,
                    Message = $"{value}"
                };
                MonitorLog.LogToMonitor(monitorItem);

                OnStaticPropertyChanged();
            }
        }

        /// <summary>
        /// Pec 360 mode, list that holds all pec rate factors
        /// </summary>
        public static SortedList<int, Tuple<double, int>> Pec360Master { get; private set; }

        /// <summary>
        /// Pec bin list that holds subset of the mater list, used as a cache
        /// </summary>
        private static SortedList<int, Tuple<double, int>> PecBinsSubs { get; set; }


        private class PecBinData
        {
            public int BinNumber { get; set; }
            public double BinFactor { get; set; }
            public int BinUpdates { get; set; }
        }

        private enum PecStatus
        {
            Good = 0,
            Ok = 1,
            Warning = 2,
            NotSoGood = 3,
            Bad = 4
        }

        private enum PecMergeType
        {
            Replace = 0,
            Merge = 1,
        }

        private class PecTrainingDefinition
        {
            public PecFileType FileType { get; set; }
            public int Index { get; set; }
            public int Cycles { get; set; }
            public DateTime StartTime { get; set; }
            public DateTime EndTime { get; set; }
            public double StartPosition { get; set; }
            public double EndPosition { get; set; }
            public double PositionOffset { get; set; }
            public double Ra { get; set; }
            public double Dec { get; set; }
            public double TrackingRate { get; set; }
            public int BinCount { get; set; }
            public double BinSteps { get; set; }
            public double BinTime { get; set; }
            public double WormPeriod { get; set; }
            public int WormTeeth { get; set; }
            public double WormSteps { get; set; }
            public double StepsPerSec { get; set; }
            public double StepsPerRev { get; set; }
            public string FileName { get; set; }
            public bool InvertCapture { get; set; }
            public List<PecLogData> Log { get; set; }
            public List<PecBinData> Bins { get; set; }
        }

        private class PecLogData
        {
            int Index { get; set; }
            DateTime TimeStamp { get; set; }
            double Position { get; set; }
            double DeltaSteps { get; set; }
            TimeSpan DeltaTime { get; set; }
            double Normalized { get; set; }
            double RateEstimate { get; set; }
            double BinNumber { get; set; }
            double BinEstimate { get; set; }
            double BinFactor { get; set; }
            private PecStatus Status { get; set; }
        }

        /// <summary>
        /// Pec worm mode, list that holds all pec rate factors
        /// </summary>
        private static SortedList<int, Tuple<double, int>> PecWormMaster { get; set; }

        /// <summary>
        /// Loads both types of pec files
        /// </summary>
        /// <param name="fileName"></param>
        public static void LoadPecFile(string fileName)
        {
            var def = new PecTrainingDefinition();
            var bins = new List<PecBinData>();

            // load file
            var lines = File.ReadAllLines(fileName);
            for (var i = 0; i < lines.Length; i += 1)
            {
                var line = lines[i];
                if (line.Length == 0) { continue; }

                switch (line[0])
                {
                    case '#':
                        var keys = line.Split('=');
                        if (keys.Length != 2) { break; }

                        switch (keys[0].Trim())
                        {
                            case "#StartTime":
                                if (DateTime.TryParseExact(keys[1].Trim(), "yyyy:MM:dd:HH:mm:ss.fff",
                                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var startTime))
                                {
                                    def.StartTime = startTime;
                                }
                                break;
                            case "#StartPosition":
                                if (double.TryParse(keys[1].Trim(), out var startPosition))
                                {
                                    def.StartPosition = startPosition;
                                }
                                break;
                            case "#EndTime":
                                if (DateTime.TryParseExact(keys[1].Trim(), "yyyy:MM:dd:HH:mm:ss.fff",
                                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var endTime))
                                {
                                    def.EndTime = endTime;
                                }
                                break;
                            case "#EndPosition":
                                if (double.TryParse(keys[1].Trim(), out var endPosition))
                                {
                                    def.StartPosition = endPosition;
                                }
                                break;
                            case "#Index":
                                if (int.TryParse(keys[1].Trim(), out var index))
                                {
                                    def.Index = index;
                                }
                                break;
                            case "#Cycles":
                                if (int.TryParse(keys[1].Trim(), out var cycles))
                                {
                                    def.Cycles = cycles;
                                }
                                break;
                            case "#WormPeriod":
                                if (double.TryParse(keys[1].Trim(), out var wormPeriod))
                                {
                                    def.WormPeriod = wormPeriod;
                                }
                                break;
                            case "#WormTeeth":
                                if (int.TryParse(keys[1].Trim(), out var wormTeeth))
                                {
                                    def.WormTeeth = wormTeeth;
                                }
                                break;
                            case "#WormSteps":
                                if (double.TryParse(keys[1].Trim(), out var wormSteps))
                                {
                                    def.WormSteps = wormSteps;
                                }
                                break;
                            case "#TrackingRate":
                                if (double.TryParse(keys[1].Trim(), out var trackingRate1))
                                {
                                    def.TrackingRate = trackingRate1;
                                }
                                break;
                            case "#PositionOffset":
                                if (double.TryParse(keys[1].Trim(), out var positionOffset))
                                {
                                    def.PositionOffset = positionOffset;
                                }
                                break;
                            case "#Ra":
                                if (double.TryParse(keys[1].Trim(), out var ra))
                                {
                                    def.Ra = ra;
                                }
                                break;
                            case "#Dec":
                                if (double.TryParse(keys[1].Trim(), out var dec))
                                {
                                    def.Dec = dec;
                                }
                                break;
                            case "#BinCount":
                                if (int.TryParse(keys[1].Trim(), out var binCount))
                                {
                                    def.BinCount = binCount;
                                }
                                break;
                            case "#BinSteps":
                                if (double.TryParse(keys[1].Trim(), out var binSteps))
                                {
                                    def.BinSteps = binSteps;
                                }
                                break;
                            case "#BinTime":
                                if (double.TryParse(keys[1].Trim(), out var binTime))
                                {
                                    def.BinTime = binTime;
                                }
                                break;
                            case "#StepsPerSec":
                                if (double.TryParse(keys[1].Trim(), out var stepsPerSec))
                                {
                                    def.StepsPerSec = stepsPerSec;
                                }
                                break;
                            case "#StepsPerRev":
                                if (double.TryParse(keys[1].Trim(), out var stepsPerRev))
                                {
                                    def.StepsPerRev = stepsPerRev;
                                }
                                break;
                            case "#InvertCapture":
                                if (bool.TryParse(keys[1].Trim(), out var invertCapture))
                                {
                                    def.InvertCapture = invertCapture;
                                }
                                break;
                            case "#FileName":
                                if (File.Exists(keys[1].Trim()))
                                {
                                    def.FileName = keys[1].Trim();
                                }
                                break;
                            case "#FileType":
                                if (Enum.TryParse<PecFileType>(keys[1].Trim(), true, out var fileType))
                                {
                                    def.FileType = fileType;
                                }
                                break;
                        }
                        break;
                    default:
                        var data = line.Split('|');
                        if (data.Length != 3) { break; }
                        var bin = new PecBinData();
                        if (int.TryParse(data[0].Trim(), out var binNumber))
                        {
                            bin.BinNumber = binNumber;
                        }
                        if (double.TryParse(data[1].Trim(), out var binFactor))
                        {
                            bin.BinFactor = binFactor;
                        }
                        if (int.TryParse(data[2].Trim(), out var binUpdates))
                        {
                            bin.BinUpdates = binUpdates;
                        }
                        if (binFactor > 0 && binFactor < 2) { bins.Add(bin); }
                        break;
                }
            }

            // validate
            var msg = string.Empty;
            var paramError = false;

            if (def.FileType != PecFileType.GsPecWorm && def.FileType != PecFileType.GsPec360)
            {
                paramError = true;
                msg = $"FileType {def.FileType}";
            }
            if (def.BinCount != PecBinCount)
            {
                paramError = true;
                msg = $"BinCount {def.BinCount}|{PecBinCount}";
            }
            if (Math.Abs(def.BinSteps - PecBinSteps) > 0.000000001)
            {
                paramError = true;
                msg = $"BinSteps {def.BinSteps}|{PecBinSteps}";
            }
            if (Math.Abs((long)def.StepsPerRev - StepsPerRevolution[0]) > 0.000000001)
            {
                paramError = true;
                msg = $"StepsPerRev{def.StepsPerRev}|{StepsPerRevolution[0]}";
            }
            if (def.WormTeeth != WormTeethCount[0])
            {
                paramError = true;
                msg = $"WormTeeth {def.WormTeeth}|{WormTeethCount[0]}";
            }
            switch (def.FileType)
            {
                case PecFileType.GsPecWorm:
                    if (def.BinCount == bins.Count) { break; }
                    paramError = true;
                    msg = $"BinCount {PecFileType.GsPecWorm}";
                    break;
                case PecFileType.GsPec360:
                    if (bins.Count == (int)(def.StepsPerRev / def.BinSteps)) { break; }
                    paramError = true;
                    msg = $"BinCount {PecFileType.GsPec360}";
                    break;
                case PecFileType.GsPecDebug:
                    paramError = true;
                    msg = $"BinCount {PecFileType.GsPecDebug}";
                    break;
                default:
                    paramError = true;
                    msg = $"FileType Error";
                    break;
            }

            if (paramError) { throw new Exception($"Error Loading Pec File ({msg})"); }

            bins = CleanUpBins(bins);

            // load to master
            switch (def.FileType)
            {
                case PecFileType.GsPecWorm:
                    var master = MakeWormMaster(bins);
                    UpdateWormMaster(master, PecMergeType.Replace);
                    _settings!.PecWormFile = fileName;
                    break;
                case PecFileType.GsPec360:
                    _settings!.Pec360File = fileName;
                    break;
                case PecFileType.GsPecDebug:
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

        }

        /// <summary>
        /// Corrects missing bins
        /// </summary>
        /// <returns>new list of bins</returns>
        private static List<PecBinData> CleanUpBins(IReadOnlyCollection<PecBinData> bins)
        {
            if (bins == null) { return null; }

            // Correct for missing bins
            var sortedList = bins.OrderBy(o => o.BinNumber).ToList();
            var validBins = new List<PecBinData>();
            for (var i = sortedList[0].BinNumber; i <= sortedList[sortedList.Count - 1].BinNumber; i++)
            {
                var result = sortedList.Find(o => o.BinNumber == i);
                validBins.Add(result ?? new PecBinData { BinFactor = 1.0, BinNumber = i });
            }

            validBins = validBins.OrderBy(o => o.BinNumber).ToList();
            return validBins;
        }

        /// <summary>
        /// Creates a new master list using 100 bins
        /// </summary>
        /// <param name="bins"></param>
        /// <returns></returns>
        private static SortedList<int, Tuple<double, int>> MakeWormMaster(IReadOnlyList<PecBinData> bins)
        {
            // find the start of a worm period
            var index = 0;
            for (var i = 0; i < bins.Count; i++)
            {
                var binNo = bins[i].BinNumber * 1.0 / PecBinCount;
                var remainder = binNo % 1;
                if (remainder != 0) { continue; }
                index = i;
                break;
            }
            if (double.IsNaN(index)) { return null; }

            // create new bin set, zero based on worm start position
            var orderBins = new List<PecBinData>();
            for (var i = index; i < PecBinCount; i++)
            {
                orderBins.Add(bins[i]);
            }
            for (var i = 0; i < index; i++)
            {
                orderBins.Add(bins[i]);
            }

            // create master set of bins using train data
            var binsMaster = new SortedList<int, Tuple<double, int>>();
            for (var j = 0; j < PecBinCount; j++)
            {
                binsMaster.Add(j, new Tuple<double, int>(orderBins[j].BinFactor, 1));
            }
            return binsMaster;
        }

        /// <summary>
        /// Updates the server pec master list with applied bins
        /// </summary>
        /// <param name="mBins"></param>
        /// <param name="mergeType"></param>
        private static void UpdateWormMaster(SortedList<int, Tuple<double, int>> mBins, PecMergeType mergeType)
        {
            if (mBins == null) { return; }
            if (PecWormMaster == null) { mergeType = PecMergeType.Replace; }
            if (PecWormMaster?.Count != mBins.Count) { mergeType = PecMergeType.Replace; }

            switch (mergeType)
            {
                case PecMergeType.Replace:
                    PecWormMaster = mBins;
                    _settings!.PecOffSet = 0; // reset offset
                    return;
                case PecMergeType.Merge:
                    var pecBins = PecWormMaster;
                    if (pecBins == null)
                    {
                        PecWormMaster = mBins;
                        _settings!.PecOffSet = 0;
                        return;
                    }
                    for (var i = 0; i < mBins.Count; i++)
                    {
                        if (double.IsNaN(pecBins[i].Item1))
                        {
                            pecBins[i] = new Tuple<double, int>(mBins[i].Item1, 1);
                            continue;
                        }

                        var updateCount = pecBins[i].Item2;
                        if (updateCount < 1) { updateCount = 1; }
                        updateCount++;
                        var newFactor = (pecBins[i].Item1 * updateCount + mBins[i].Item1) / (updateCount + 1);
                        var newBin = new Tuple<double, int>(newFactor, updateCount);
                        pecBins[i] = newBin;

                    }
                    PecWormMaster = pecBins;
                    return;
                default:
                    throw new ArgumentOutOfRangeException(nameof(mergeType), mergeType, null);
            }
        }
        #endregion

        public static TrackingMode TrackingMode
        {
            get => _trackingMode;
            internal set  // Make setter internal if it's private
            {
                _trackingMode = value;
                OnStaticPropertyChanged();
            }
        }

        /// <summary>
        /// Actual positions in degrees
        /// </summary>
        internal static double ActualAxisX
        {
            get => _actualAxisX;
            set
            {
                if (Math.Abs(value - _actualAxisX) < 0.0001) { return; }
                _actualAxisX = value;
                OnStaticPropertyChanged();
            }
        }

        /// <summary>
        /// Actual positions in degrees
        /// </summary>
        internal static double ActualAxisY
        {
            get => _actualAxisY;
            set
            {
                if (Math.Abs(value - _actualAxisY) < 0.0001) { return; }
                _actualAxisY = value;
                OnStaticPropertyChanged();
            }
        }

        /// <summary>
        /// X-axis position in local app coordinates
        /// </summary>
        public static double AppAxisX
        {
            get => _appAxisX;
            private set
            {
                if (Math.Abs(value - _appAxisX) < 0.000000000000001) return;
                _appAxisX = value;
                OnStaticPropertyChanged();
            }
        }

        /// <summary>
        ///Y-axis position in local app coordinates
        /// </summary>
        public static double AppAxisY
        {
            get => _appAxisY;
            private set
            {
                if (Math.Abs(value - _appAxisY) < 0.000000000000001) return;
                _appAxisY = value;
                OnStaticPropertyChanged();
            }
        }

        /// <summary>
        /// Low voltage event from mount status
        /// </summary>
        public static bool LowVoltageEventState
        {
            get => _lowVoltageEventState;
            private set
            {
                _lowVoltageEventState = value;
                OnStaticPropertyChanged();
            }
        }

        /// <summary>
        /// Get the controller voltage
        /// </summary>
        public static double ControllerVoltage
        {
            get
            {
                try
                {
                    var status = new SkyGetControllerVoltage(SkyQueue.NewId, Axis.Axis1);
                    return SkyQueue.GetCommandResult(status).Result;
                }
                catch (Exception)
                {
                    return double.NaN;
                }
            }
        }

        /// <summary>
        /// Gets the current polar mode based on the alignment mode and mount type.
        /// </summary>
        public static PolarMode PolarMode
        {
            get
            {
                if (_settings!.AlignmentMode == AlignmentMode.Polar)
                {
                    return _settings!.Mount == MountType.SkyWatcher ? _settings!.PolarMode : PolarMode.Right;
                }
                else
                {
                    // default to right
                    return PolarMode.Right;
                }
            }
        }

        /// <summary>
        /// UI indicator for at home
        /// </summary>
        public static bool IsHome
        {
            get => _isHome;
            private set
            {
                if (value == _isHome) { return; }
                _isHome = value;
                OnStaticPropertyChanged();
            }
        }

        /// <summary>
        /// Factor to convert steps, Sky Watcher in rad
        /// Static for backward compatibility only
        /// Multi-telescope: Use MountInstance._factorStep instead
        /// </summary>
        internal static double[] FactorStep { get; set; } = { 0.0, 0.0 };

        /// <summary>
        /// applies backlash to pulse
        /// </summary>
        private static GuideDirection LastDecDirection { get; set; }

        /// <summary>
        /// Count number of times server loop is executed
        /// </summary>
        public static ulong LoopCounter { get; private set; }

        /// <summary>
        /// use monitoring for charts
        /// </summary>
        public static bool MonitorPulse
        {
            private get => _monitorPulse;
            set
            {
                if (_monitorPulse == value) return;
                _monitorPulse = value;

                var monitorItem = new MonitorEntry
                {
                    Datetime = HiResDateTime.UtcNow,
                    Device = MonitorDevice.Server,
                    Category = MonitorCategory.Server,
                    Type = MonitorType.Information,
                    Method = MethodBase.GetCurrentMethod()?.Name,
                    Thread = Thread.CurrentThread.ManagedThreadId,
                    Message = $"{value}"
                };
                MonitorLog.LogToMonitor(monitorItem);

                SimTasks(MountTaskName.MonitorPulse);
                SkyTasks(MountTaskName.MonitorPulse);
            }
        }

        /// <summary>
        /// Used to inform and show error on the UI thread
        /// </summary>
        public static Exception MountError
        {
            get => _mountError;
            internal set
            {
                _mountError = value;
                OnStaticPropertyChanged();
            }
        }

        /// <summary>
        /// Is Dec pulse guiding
        /// </summary>
        public static bool IsPulseGuidingDec
        {
            get => _isPulseGuidingDec;
            set
            {
                if (_isPulseGuidingDec != value)
                {
                    _isPulseGuidingDec = value;
                    // reset Dec pulse guiding cancellation token source
                    if (!_isPulseGuidingDec && _ctsPulseGuideDec != null)
                    {
                        _ctsPulseGuideDec?.Dispose();
                        _ctsPulseGuideDec = null;
                    }
                }
            }
        }

        /// <summary>
        /// Is Ra pulse guiding
        /// </summary>
        public static bool IsPulseGuidingRa
        {
            get => _isPulseGuidingRa;
            set
            {
                if (_isPulseGuidingRa != value)
                {
                    _isPulseGuidingRa = value;
                    // reset Ra pulse guiding cancellation token source
                    if (!_isPulseGuidingRa && _ctsPulseGuideRa != null)
                    {
                        _ctsPulseGuideRa?.Dispose();
                        _ctsPulseGuideRa = null;
                    }
                }
            }
        }

        /// <summary>
        /// Move Secondary axis at the given rate in degrees, MoveAxis
        /// Tracking if enabled:
        /// - is restored for the Secondary axis when MoveAxis is called with rate = 0
        /// - continues for the Primary axis unless it is also executing a MoveAxis command
        /// </summary>
        public static double RateMoveSecondaryAxis
        {
            private get => _rateMoveAxes.Y;
            set
            {
                if (Math.Abs(_rateMoveAxes.Y - value) < .0000000001) return;
                _rateMoveAxes.Y = value;
                CancelAllAsync();
                // Set slewing state
                SetRateMoveSlewState();
                // Move axis at requested rate
                switch (_settings!.Mount)
                {
                    case MountType.Simulator:
                        _ = new CmdMoveAxisRate(0, Axis.Axis2, -_rateMoveAxes.Y);
                        break;
                    case MountType.SkyWatcher:
                        _ = new SkyAxisSlew(0, Axis.Axis2, _rateMoveAxes.Y);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
                // Update tracking if required
                if (Tracking) SetTracking();

                var monitorItem = new MonitorEntry
                {
                    Datetime = HiResDateTime.UtcNow,
                    Device = MonitorDevice.Server,
                    Category = MonitorCategory.Server,
                    Type = MonitorType.Information,
                    Method = MethodBase.GetCurrentMethod()?.Name,
                    Thread = Thread.CurrentThread.ManagedThreadId,
                    Message = $"{_rateMoveAxes.Y}|{SkyTrackingOffset[1]}"
                };
                MonitorLog.LogToMonitor(monitorItem);
            }
        }

        /// <summary>
        /// Move Primary axis at the given rate in degrees, MoveAxis
        /// Tracking if enabled:
        /// - is restored for the Primary axis when MoveAxis is called with rate = 0
        /// - continues for the Secondary axis unless it is also executing a MoveAxis command
        /// </summary>
        public static double RateMovePrimaryAxis
        {
            private get => _rateMoveAxes.X;
            set
            {
                if (Math.Abs(_rateMoveAxes.X - value) < 0.0000000001) return;
                _rateMoveAxes.X = value;
                CancelAllAsync();
                // Set slewing state
                SetRateMoveSlewState();
                // Move axis at requested rate
                switch (_settings!.Mount)
                {
                    case MountType.Simulator:
                        _ = new CmdMoveAxisRate(0, Axis.Axis1, _rateMoveAxes.X);
                        break;
                    case MountType.SkyWatcher:
                        _ = new SkyAxisSlew(0, Axis.Axis1, _rateMoveAxes.X);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
                // Update tracking if required
                if (Tracking) SetTracking();
                var monitorItem = new MonitorEntry
                {
                    Datetime = HiResDateTime.UtcNow,
                    Device = MonitorDevice.Server,
                    Category = MonitorCategory.Server,
                    Type = MonitorType.Information,
                    Method = MethodBase.GetCurrentMethod()?.Name,
                    Thread = Thread.CurrentThread.ManagedThreadId,
                    Message = $"{_rateMoveAxes.X}|{SkyTrackingOffset[0]}"
                };
                MonitorLog.LogToMonitor(monitorItem);
            }
        }

        

        /// <summary>
        /// time in seconds for mount to settle after slew
        /// </summary>
        public static double SlewSettleTime
        {
            get => _slewSettleTime;
            set
            {
                if (Math.Abs(_slewSettleTime - value) <= 0) return;
                _slewSettleTime = value;
            }
        }

        /// <summary>
        /// Total steps per 360
        /// Static for backward compatibility only
        /// Multi-telescope: Use MountInstance._stepsPerRevolution instead
        /// </summary>
        public static long[] StepsPerRevolution { get; private set; } = { 0, 0 };

        /// <summary>
        /// :b Timer Freq
        /// </summary>
        public static long[] StepsTimeFreq { get; private set; } = { 0, 0 };

        /// <summary>
        /// current micro steps, used to update SkyServer and UI
        /// </summary>
        public static double[] Steps 
        {
            get => _steps;
            set
            {
                _steps = value;

                // Set context from current settings
                var context = AxesContext.FromSettings(_settings);

                //Implement Pec
                PecCheck();

                //Convert Positions to degrees
                var rawPositions = new[] { ConvertStepsToDegrees(_steps[0], 0), ConvertStepsToDegrees(_steps[1], 1) };
                UpdateMountLimitStatus(rawPositions);

                // Convert to axis position from physical position 
                rawPositions = GetUnsyncedAxes(rawPositions);


                // UI diagnostics in degrees
                ActualAxisX = rawPositions[0];
                ActualAxisY = rawPositions[1];

                // convert positions to local app axes
                var axes = Axes.AxesMountToApp(rawPositions, context  );

                // store local app axes to track positions
                _appAxes.X = axes[0];
                _appAxes.Y = axes[1];

                // UI diagnostics for local app exes
                AppAxisX = axes[0];
                AppAxisY = axes[1];

                // Calculate mount Alt/Az
                var altAz = Axes.AxesXyToAzAlt(axes, context);
                Azimuth = altAz[0];
                Altitude = altAz[1];

                // Calculate top-o-centric Ra/Dec
                var raDec = Axes.AxesXyToRaDec(axes, context);
                RightAscension = raDec[0];
                Declination = raDec[1];

                // Calculate EquatorialSystem Property Ra/Dec for UI
                var xy = Transforms.InternalToCoordType(raDec[0], raDec[1]);
                RightAscensionXForm = xy.X;
                DeclinationXForm = xy.Y;

                OnStaticPropertyChanged();
            }
        }

        /// <summary>
        /// Total worm teeth
        /// </summary>
        public static int[] WormTeethCount { get; private set; } = { 0, 0 };

        /// <summary>
        /// Total worm step per 360
        /// Static for backward compatibility only
        /// Multi-telescope: Use MountInstance._stepsWormPerRevolution instead
        /// </summary>
        public static double[] StepsWormPerRevolution { get; private set; } = { 0.0, 0.0 };

        /// <summary>
        /// Southern alignment status
        /// </summary>
        public static bool SouthernHemisphere => _settings!.Latitude < 0;

        /// <summary>
        /// Counts any overlapping events with updating UI that might occur
        /// should always be 0 or event interval is too fast
        /// </summary>
        private static int TimerOverruns { get; set; }

        /// <summary>
        /// Has mount position been updated 
        /// </summary>
        public static bool MountPositionUpdated
        {
            get
            {
                lock (MountPositionUpdatedLock)
                {
                    return _mountPositionUpdated;
                }
            }
            set
            {
                lock (MountPositionUpdatedLock)
                {
                    _mountPositionUpdated = value;
                }
            }
        }

        /// <summary>
        /// Current Alt/Az tracking mode - RA/Dec predictor or calculated tracking rate
        /// </summary>
        public static AltAzTrackingType AltAzTrackingMode { get; set; }

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

        #region Resync
        /// <summary>
        /// Reset positions for the axes.
        /// </summary>
        /// <param name="parkPosition">ParkPosition or Null for home</param>
        public static void ReSyncAxes(ParkPosition parkPosition = null, bool saveParkPosition = true)
        {
            if (!IsMountRunning) { return; }
            Tracking = false;
            StopAxes();

            //set to home position
            double[] position = { _homeAxes.X, _homeAxes.Y };
            var name = "home";

            //set to park position
            if (parkPosition != null)
            {
                // Set context from current settings
                var context = AxesContext.FromSettings(_settings);
                position = Axes.AxesAppToMount(new[] { parkPosition.X, parkPosition.Y }, context);
                name = parkPosition.Name;
            }

            //log
            var monitorItem = new MonitorEntry
            {
                Datetime = HiResDateTime.UtcNow,
                Device = MonitorDevice.Server,
                Category = MonitorCategory.Server,
                Type = MonitorType.Information,
                Method = MethodBase.GetCurrentMethod()?.Name,
                Thread = Thread.CurrentThread.ManagedThreadId,
                Message = $"{name}|{position[0]}|{position[1]}"
            };
            MonitorLog.LogToMonitor(monitorItem);

            switch (_settings!.Mount) // mount type check
            {
                case MountType.Simulator:
                    SimTasks(MountTaskName.StopAxes);
                    _ = new CmdAxisToDegrees(0, Axis.Axis1, position[0]);
                    _ = new CmdAxisToDegrees(0, Axis.Axis2, position[1]);
                    break;
                case MountType.SkyWatcher:
                    SkyTasks(MountTaskName.StopAxes);
                    _ = new SkySetAxisPosition(0, Axis.Axis1, position[0]);
                    _ = new SkySetAxisPosition(0, Axis.Axis2, position[1]);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            //all good, go ahead and set dropdown to the park position and park
            if (parkPosition != null && saveParkPosition)
            {
                ParkSelected = parkPosition;
                GoToPark();
            }

            //reset any hc moves
            HcResetPrevMove(MountAxis.Ra);
            HcResetPrevMove(MountAxis.Dec);
        }

        #endregion

        #region Alignment

        // internal static double DegToRad(double degree) { return (degree / 180.0 * Math.PI); }
        // internal static double RadToDeg(double rad) { return (rad / Math.PI * 180.0); }

        #endregion

        #region Snap Ports

        /// <summary>
        /// Camera Port
        /// </summary>
        public static bool SnapPort1 { get; set; }

        public static bool SnapPort2 { get; set; }

        public static bool SnapPort1Result
        {
            get => _snapPort1Result;
            set
            {
                if (_snapPort1Result == value) { return; }
                _snapPort1Result = value;
                OnStaticPropertyChanged();
            }
        }

        public static bool SnapPort2Result
        {
            get => _snapPort2Result;
            set
            {
                if (_snapPort2Result == value) { return; }
                _snapPort2Result = value;
                OnStaticPropertyChanged();
            }
        }

        // SnapPort1, SnapPort2, SnapPort1Result, SnapPort2Result will be moved here

        #endregion

        #region Park Management

        public static ParkPosition GetStoredParkPosition()
        {
            var p = new ParkPosition { Name = _settings!.ParkName, X = _settings!.ParkAxes[0], Y = _settings!.ParkAxes[1] };
            return p;
        }

        // ParkSelected, SetParkAxis will be moved here

        #endregion

        #region Auto Home

        /// <summary>
        /// UI progress bar for autoHome 
        /// </summary>
        public static int AutoHomeProgressBar
        {
            get => _autoHomeProgressBar;
            set
            {
                _autoHomeProgressBar = value;
                OnStaticPropertyChanged();
            }
        }

        /// <summary>
        /// Cancel button status for auto home
        /// </summary>
        public static bool AutoHomeStop
        {
            get => _autoHomeStop;
            set
            {
                _autoHomeStop = value;
                OnStaticPropertyChanged();
            }
        }

        /// <summary>
        /// Checks if the auto home async process is running
        /// </summary>
        public static bool IsAutoHomeRunning
        {
            get => _isAutoHomeRunning;
            private set
            {
                _isAutoHomeRunning = value;
                OnStaticPropertyChanged();
            }
        }

        // AutoHomeAsync, GetAutoHomeResultMessage, IsAutoHomeRunning, etc. will be moved here

        #endregion

        #region Server Items

        /// <summary>
        /// Mount name
        /// </summary>
        public static string MountName { get; private set; }

        /// <summary>
        /// Controller board version
        /// </summary>
        public static string MountVersion { get; private set; }

        /// <summary>
        /// Starts/Stops current selected mount
        /// </summary>
        public static bool IsMountRunning
        {
            get
            {
                switch (_settings!.Mount)
                {
                    case MountType.Simulator:
                        _mountRunning = MountQueue.IsRunning;
                        break;
                    case MountType.SkyWatcher:
                        _mountRunning = SkyQueue.IsRunning;
                        break;
                }

                return _mountRunning;
            }
            set
            {
                _mountRunning = value;
                LoopCounter = 0;
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

        public static bool CanPolarLed
        {
            get => _canPolarLed;
            private set
            {
                if (_canPolarLed == value) { return; }
                _canPolarLed = value;
                OnStaticPropertyChanged();
            }
        }

        public static bool CanAdvancedCmdSupport
        {
            get => _canAdvancedCmdSupport;
            private set
            {
                if (_canAdvancedCmdSupport == value) { return; }
                _canAdvancedCmdSupport = value;
                OnStaticPropertyChanged();
            }
        }

        /// <summary>
        /// Support for a home sensor
        /// </summary>
        public static bool CanHomeSensor
        {
            get => _canHomeSensor;
            private set
            {
                if (_canHomeSensor == value) { return; }
                _canHomeSensor = value;
                OnStaticPropertyChanged();
            }
        }

        public static string Capabilities
        {
            get => _capabilities;
            set
            {
                if (_capabilities == value) { return; }
                _capabilities = value;
                OnStaticPropertyChanged();
            }
        }

        #endregion

    }
}
