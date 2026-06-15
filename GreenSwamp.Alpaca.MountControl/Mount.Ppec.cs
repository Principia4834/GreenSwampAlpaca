/* Copyright(C) 2019-2026 Rob Morgan (robert.morgan.e@gmail.com)

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

namespace GreenSwamp.Alpaca.MountControl
{
    public partial class Mount
    {
        #region PPEC State

        internal bool _pPecTraining;
        internal bool _pPecTrainInProgress;

        #endregion

        #region PPEC Methods

        /// <summary>pPEC Monitors the mount doing pPEC training</summary>
        internal void CheckPecTraining()
        {
            switch (Settings.Mount)
            {
                case MountType.Simulator:
                    break;
                case MountType.SkyWatcher:
                    if (!_pPecTraining)
                    {
                        _pPecTrainInProgress = false;
                        return;
                    }
                    var ppectrain = new SkyIsPPecInTrainingOn(SkyQueue.NewId, SkyQueue);
                    if (bool.TryParse(Convert.ToString(SkyQueue.GetCommandResult(ppectrain).Result), out bool bTrain))
                    {
                        _pPecTraining = bTrain;
                        SkyTasks(MountTaskName.PecTraining);
                        _pPecTrainInProgress = bTrain;
                        if (!bTrain && Settings.PPecOn) //restart pec
                        {
                            Settings.PPecOn = false;
                            SkyTasks(MountTaskName.Pec);
                            Settings.PPecOn = true;
                            SkyTasks(MountTaskName.Pec);
                        }
                    }
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        #endregion
    }
}
