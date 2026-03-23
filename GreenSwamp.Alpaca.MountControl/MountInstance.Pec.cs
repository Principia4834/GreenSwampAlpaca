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

using GreenSwamp.Alpaca.Mount.SkyWatcher;
using GreenSwamp.Alpaca.Principles;
using GreenSwamp.Alpaca.Shared;
using System.Globalization;
using System.Reflection;
using Range = GreenSwamp.Alpaca.Principles.Range;

namespace GreenSwamp.Alpaca.MountControl
{
    public partial class MountInstance
    {
        #region PEC Inner Types

        private class PecBinData
        {
            public int BinNumber { get; set; }
            public double BinFactor { get; set; }
            public int BinUpdates { get; set; }
        }

        private enum PecStatus { Good = 0, Ok = 1, Warning = 2, NotSoGood = 3, Bad = 4 }

        private enum PecMergeType { Replace = 0, Merge = 1 }

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

        #endregion

        #region PEC State

        internal bool _pecShow;
        internal Tuple<int, double, int> _pecBinNow;
        internal bool _pPecTraining;
        internal bool _pPecTrainInProgress;
        internal int PecBinCount { get; set; }
        internal double PecBinSteps { get => _pecBinSteps; set => _pecBinSteps = value; }
        internal SortedList<int, Tuple<double, int>> Pec360Master;
        private SortedList<int, Tuple<double, int>> _pecBinsSubs;
        internal SortedList<int, Tuple<double, int>> PecWormMaster;

        #endregion

        #region PEC Methods

        /// <summary>pPEC Monitors the mount doing pPEC training</summary>
        internal void CheckPecTraining()
        {
            switch (_settings.Mount)
            {
                case MountType.Simulator:
                    break;
                case MountType.SkyWatcher:
                    if (!_pPecTraining)
                    {
                        SkyServer.PecTrainInProgress = false;
                        return;
                    }
                    var ppectrain = new SkyIsPPecInTrainingOn(SkyQueueInstance.NewId, SkyQueueInstance);
                    if (bool.TryParse(Convert.ToString(SkyQueueInstance.GetCommandResult(ppectrain).Result), out bool bTrain))
                    {
                        SkyServer.PecTraining = bTrain;
                        SkyServer.PecTrainInProgress = bTrain;
                        if (!bTrain && _settings.PPecOn) //restart pec
                        {
                            _settings.PPecOn = false;
                            SkyServer.SkyTasks(MountTaskName.Pec, this);
                            _settings.PPecOn = true;
                            SkyServer.SkyTasks(MountTaskName.Pec, this);
                        }
                    }
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        /// <summary>Pec Implement</summary>
        internal void PecCheck()
        {
            try
            {
                if (!_settings.PecOn || !Tracking || PecBinCount < 0 || IsSlewing || !_pecShow) return;

                var position = (int)Range.RangeDouble(_steps[0], Convert.ToDouble(_stepsPerRevolution[0]));
                var newBinNo = (int)((position + _settings.PecOffSet) / PecBinSteps);
                Tuple<double, int> pecBin = null;

                switch (_settings.PecMode)
                {
                    case PecMode.PecWorm:
                        newBinNo %= 100;
                        if (_pecBinNow?.Item1 == newBinNo) return;
                        if (PecWormMaster == null || PecWormMaster?.Count == 0) { return; }
                        PecWormMaster?.TryGetValue(newBinNo, out pecBin);
                        break;
                    case PecMode.Pec360:
                        if (_pecBinNow?.Item1 == newBinNo) return;
                        if (Pec360Master == null || Pec360Master?.Count == 0) { return; }
                        if (_pecBinsSubs == null) { _pecBinsSubs = new SortedList<int, Tuple<double, int>>(); }
                        var count = 0;
                        while (_pecBinsSubs.TryGetValue(newBinNo, out pecBin) == false && count < 2)
                        {
                            var binStart = newBinNo - 100 < 0 ? 0 : newBinNo - 100;
                            var binEnd = newBinNo + 100 > _stepsPerRevolution[0] - 1
                                ? (int)_stepsPerRevolution[0] - 1
                                : newBinNo + 100;
                            _pecBinsSubs.Clear();
                            for (var i = binStart; i <= binEnd; i++)
                            {
                                var mi = Tuple.Create(0.0, 0);
                                var masterResult = Pec360Master != null && Pec360Master.TryGetValue(i, out mi);
                                if (masterResult) _pecBinsSubs.Add(i, mi);
                            }
                            count++;
                        }
                        if (_pecBinsSubs.Count == 0) { throw new Exception("Pec sub not found"); }
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }

                if (pecBin == null) { throw new Exception("Pec not found"); }

                var binNew = new Tuple<int, double, int>(newBinNo, pecBin.Item1, pecBin.Item2);
                SkyServer.PecBinNow = binNew;
                SkyServer.SetTracking();
            }
            catch (Exception ex)
            {
                SkyServer.PecOn = false;
                var monitorItem = new MonitorEntry
                {
                    Datetime = HiResDateTime.UtcNow,
                    Device = MonitorDevice.Server,
                    Category = MonitorCategory.Mount,
                    Type = MonitorType.Error,
                    Method = MethodBase.GetCurrentMethod()?.Name,
                    Thread = Thread.CurrentThread.ManagedThreadId,
                    Message = $"{ex.Message}|{ex.StackTrace}"
                };
                MonitorLog.LogToMonitor(monitorItem);
                _mountError = ex;
            }
        }

        /// <summary>Loads both types of pec files</summary>
        public void LoadPecFile(string fileName)
        {
            var def = new PecTrainingDefinition();
            var bins = new List<PecBinData>();
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
                                    def.StartTime = startTime;
                                break;
                            case "#StartPosition":
                                if (double.TryParse(keys[1].Trim(), out var startPosition))
                                    def.StartPosition = startPosition;
                                break;
                            case "#EndTime":
                                if (DateTime.TryParseExact(keys[1].Trim(), "yyyy:MM:dd:HH:mm:ss.fff",
                                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var endTime))
                                    def.EndTime = endTime;
                                break;
                            case "#EndPosition":
                                if (double.TryParse(keys[1].Trim(), out var endPosition))
                                    def.StartPosition = endPosition;
                                break;
                            case "#Index":
                                if (int.TryParse(keys[1].Trim(), out var index))
                                    def.Index = index;
                                break;
                            case "#Cycles":
                                if (int.TryParse(keys[1].Trim(), out var cycles))
                                    def.Cycles = cycles;
                                break;
                            case "#WormPeriod":
                                if (double.TryParse(keys[1].Trim(), out var wormPeriod))
                                    def.WormPeriod = wormPeriod;
                                break;
                            case "#WormTeeth":
                                if (int.TryParse(keys[1].Trim(), out var wormTeeth))
                                    def.WormTeeth = wormTeeth;
                                break;
                            case "#WormSteps":
                                if (double.TryParse(keys[1].Trim(), out var wormSteps))
                                    def.WormSteps = wormSteps;
                                break;
                            case "#TrackingRate":
                                if (double.TryParse(keys[1].Trim(), out var trackingRate1))
                                    def.TrackingRate = trackingRate1;
                                break;
                            case "#PositionOffset":
                                if (double.TryParse(keys[1].Trim(), out var positionOffset))
                                    def.PositionOffset = positionOffset;
                                break;
                            case "#Ra":
                                if (double.TryParse(keys[1].Trim(), out var ra))
                                    def.Ra = ra;
                                break;
                            case "#Dec":
                                if (double.TryParse(keys[1].Trim(), out var dec))
                                    def.Dec = dec;
                                break;
                            case "#BinCount":
                                if (int.TryParse(keys[1].Trim(), out var binCount))
                                    def.BinCount = binCount;
                                break;
                            case "#BinSteps":
                                if (double.TryParse(keys[1].Trim(), out var binSteps))
                                    def.BinSteps = binSteps;
                                break;
                            case "#BinTime":
                                if (double.TryParse(keys[1].Trim(), out var binTime))
                                    def.BinTime = binTime;
                                break;
                            case "#StepsPerSec":
                                if (double.TryParse(keys[1].Trim(), out var stepsPerSec))
                                    def.StepsPerSec = stepsPerSec;
                                break;
                            case "#StepsPerRev":
                                if (double.TryParse(keys[1].Trim(), out var stepsPerRev))
                                    def.StepsPerRev = stepsPerRev;
                                break;
                            case "#InvertCapture":
                                if (bool.TryParse(keys[1].Trim(), out var invertCapture))
                                    def.InvertCapture = invertCapture;
                                break;
                            case "#FileName":
                                if (File.Exists(keys[1].Trim()))
                                    def.FileName = keys[1].Trim();
                                break;
                            case "#FileType":
                                if (Enum.TryParse<PecFileType>(keys[1].Trim(), true, out var fileType))
                                    def.FileType = fileType;
                                break;
                        }
                        break;
                    default:
                        var data = line.Split('|');
                        if (data.Length != 3) { break; }
                        var bin = new PecBinData();
                        if (int.TryParse(data[0].Trim(), out var binNumber)) bin.BinNumber = binNumber;
                        if (double.TryParse(data[1].Trim(), out var binFactor)) bin.BinFactor = binFactor;
                        if (int.TryParse(data[2].Trim(), out var binUpdates)) bin.BinUpdates = binUpdates;
                        if (binFactor > 0 && binFactor < 2) { bins.Add(bin); }
                        break;
                }
            }

            var msg = string.Empty;
            var paramError = false;
            if (def.FileType != PecFileType.GsPecWorm && def.FileType != PecFileType.GsPec360)
            { paramError = true; msg = $"FileType {def.FileType}"; }
            if (def.BinCount != PecBinCount)
            { paramError = true; msg = $"BinCount {def.BinCount}|{PecBinCount}"; }
            if (Math.Abs(def.BinSteps - PecBinSteps) > 0.000000001)
            { paramError = true; msg = $"BinSteps {def.BinSteps}|{PecBinSteps}"; }
            if (Math.Abs((long)def.StepsPerRev - _stepsPerRevolution[0]) > 0.000000001)
            { paramError = true; msg = $"StepsPerRev{def.StepsPerRev}|{_stepsPerRevolution[0]}"; }
            if (def.WormTeeth != _wormTeethCount[0])
            { paramError = true; msg = $"WormTeeth {def.WormTeeth}|{_wormTeethCount[0]}"; }
            switch (def.FileType)
            {
                case PecFileType.GsPecWorm:
                    if (def.BinCount != bins.Count) { paramError = true; msg = $"BinCount {PecFileType.GsPecWorm}"; }
                    break;
                case PecFileType.GsPec360:
                    if (bins.Count != (int)(def.StepsPerRev / def.BinSteps)) { paramError = true; msg = $"BinCount {PecFileType.GsPec360}"; }
                    break;
                case PecFileType.GsPecDebug:
                    paramError = true; msg = $"BinCount {PecFileType.GsPecDebug}";
                    break;
                default:
                    paramError = true; msg = "FileType Error";
                    break;
            }
            if (paramError) { throw new Exception($"Error Loading Pec File ({msg})"); }

            bins = CleanUpBins(bins);
            switch (def.FileType)
            {
                case PecFileType.GsPecWorm:
                    var master = MakeWormMaster(bins);
                    UpdateWormMaster(master, PecMergeType.Replace);
                    _settings.PecWormFile = fileName;
                    break;
                case PecFileType.GsPec360:
                    _settings.Pec360File = fileName;
                    break;
                case PecFileType.GsPecDebug:
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        private List<PecBinData> CleanUpBins(IReadOnlyCollection<PecBinData> bins)
        {
            if (bins == null) { return null; }
            var sortedList = bins.OrderBy(o => o.BinNumber).ToList();
            var validBins = new List<PecBinData>();
            for (var i = sortedList[0].BinNumber; i <= sortedList[sortedList.Count - 1].BinNumber; i++)
            {
                var result = sortedList.Find(o => o.BinNumber == i);
                validBins.Add(result ?? new PecBinData { BinFactor = 1.0, BinNumber = i });
            }
            return validBins.OrderBy(o => o.BinNumber).ToList();
        }

        private SortedList<int, Tuple<double, int>> MakeWormMaster(IReadOnlyList<PecBinData> bins)
        {
            var index = 0;
            for (var i = 0; i < bins.Count; i++)
            {
                var binNo = bins[i].BinNumber * 1.0 / PecBinCount;
                if (binNo % 1 != 0) { continue; }
                index = i;
                break;
            }
            var orderBins = new List<PecBinData>();
            for (var i = index; i < PecBinCount; i++) { orderBins.Add(bins[i]); }
            for (var i = 0; i < index; i++) { orderBins.Add(bins[i]); }
            var binsMaster = new SortedList<int, Tuple<double, int>>();
            for (var j = 0; j < PecBinCount; j++)
                binsMaster.Add(j, new Tuple<double, int>(orderBins[j].BinFactor, 1));
            return binsMaster;
        }

        private void UpdateWormMaster(SortedList<int, Tuple<double, int>> mBins, PecMergeType mergeType)
        {
            if (mBins == null) { return; }
            if (PecWormMaster == null) { mergeType = PecMergeType.Replace; }
            if (PecWormMaster?.Count != mBins.Count) { mergeType = PecMergeType.Replace; }
            switch (mergeType)
            {
                case PecMergeType.Replace:
                    PecWormMaster = mBins;
                    _settings.PecOffSet = 0;
                    return;
                case PecMergeType.Merge:
                    var pecBins = PecWormMaster;
                    if (pecBins == null) { PecWormMaster = mBins; _settings.PecOffSet = 0; return; }
                    for (var i = 0; i < mBins.Count; i++)
                    {
                        if (double.IsNaN(pecBins[i].Item1))
                        { pecBins[i] = new Tuple<double, int>(mBins[i].Item1, 1); continue; }
                        var updateCount = pecBins[i].Item2 < 1 ? 1 : pecBins[i].Item2;
                        updateCount++;
                        var newFactor = (pecBins[i].Item1 * updateCount + mBins[i].Item1) / (updateCount + 1);
                        pecBins[i] = new Tuple<double, int>(newFactor, updateCount);
                    }
                    PecWormMaster = pecBins;
                    return;
                default:
                    throw new ArgumentOutOfRangeException(nameof(mergeType), mergeType, null);
            }
        }

        #endregion
    }
}
