/* Copyright(C) 2019-2025 Rob  Morgan (robert.morgan.e@gmail.com)

    This program is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published
    by the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY{ } without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with this program.  If not, see <https://www.gnu.org/licenses/>.
 */

namespace GreenSwamp.Alpaca.MountControl
{

    public class MountCommand
    {
        public MountCommand(CommandQueue commandQueue)
        {
            _commandQueue = commandQueue;
        }

        internal CommandQueue _commandQueue;

        /// <summary>
        /// Start tracking based degree rate.  Use for tracking and guiding, not go tos
        /// </summary>
        /// <param name="axis">>AxisId.Axis1 or AxisId.Axis2</param>
        /// <param name="rate">Rate in degrees per arc sec</param>
        internal virtual void AxisSlew(AxisId axis, double rate){ }

        /// <summary>
        /// Directs a pulse guide command to an axis, hemisphere and tracking rate needs to be set first
        /// </summary>
        /// <param name="axis">Axis 1 or 2</param>
        /// <param name="guideRate">Guide rate degrees, 15.041/3600*.5, negative value denotes direction</param>
        /// <param name="duration">length of pulse in milliseconds, always positive numbers</param>
        /// <param name="backlashSteps">Positive micro steps added for backlash</param>
        /// <param name="token">Token source used to cancel pulse guide operation</param>
        internal virtual void AxisPulse(AxisId axis, double guideRate, int duration, int backlashSteps,
            CancellationToken token){ }

        /// <summary>
        /// Stop the target axis normally
        /// </summary>
        /// <param name="axis">>AxisId.Axis1 or AxisId.Axis2</param>
        internal virtual void AxisStop(AxisId axis)
        { }

        /// <summary>
        /// Stop the target axis instantly
        /// </summary>
        /// <param name="axis">>AxisId.Axis1 or AxisId.Axis2</param>
        internal virtual void AxisStopInstant(AxisId axis){ }

        /// <summary>
        /// Use goto mode to target position
        /// </summary>
        /// <param name="axis">>AxisId.Axis1 or AxisId.Axis2</param>
        /// <param name="movingSteps">Micro steps to move</param>
        internal virtual void AxisMoveSteps(AxisId axis, long movingSteps){ }

        /// <summary>
        /// Use goto mode to target position 
        /// </summary>
        /// <param name="axis">>AxisId.Axis1 or AxisId.Axis2</param>
        /// <param name="targetPosition">Total radians of target position</param>
        internal virtual void AxisGoToTarget(AxisId axis, double targetPosition){ }

        /// <summary>
        /// Bypass for mount commands
        /// </summary>
        /// <param name="axis">1 or 2</param>
        /// <param name="cmd">The command char set</param>
        /// <param name="cmdData">The data need to send</param>
        /// <param name="ignoreWarnings">ignore serial response issues?</param>
        /// <returns>mount data, null for IsNullOrEmpty</returns>
        /// <example>CmdToMount(1,"X","0003","true")</example>
        internal virtual string CmdToMount(int axis, string cmd, string cmdData, string ignoreWarnings)
        {
            return String.Empty;
        }

        /// <summary>
        /// Supports the new advanced command set
        /// </summary>
        /// <returns></returns>
        internal virtual bool GetAdvancedCmdSupport()
        {
            return false;
        }

        /// <summary>
        /// Get axis position in degrees
        /// </summary>
        /// <returns>array in degrees, could return array of NaN if no responses returned</returns>
        internal virtual double[] GetPositionsInDegrees()
        {
            return [double.NaN];
        }

        /// <summary>
        /// Get axis position in steps
        /// </summary>
        /// <returns>array in steps, could return array of NaN if no responses returned</returns>
        internal virtual double[] GetSteps()
        {
            return [double.NaN];
        }

        /// <summary>
        /// Get axis positions in steps and update the property
        /// </summary>
        /// <returns>array in steps</returns>
        internal virtual void UpdateSteps()
        {
        }

        /// <summary>
        /// Gets axes board versions in a readable format
        /// </summary>
        /// <returns></returns>
        internal virtual string[] GetAxisStringVersions()
        {
            return [String.Empty];
        }

        /// <summary>
        /// Gets the long format of the axes board versions
        /// </summary>
        /// <returns></returns>
        internal virtual long[] GetAxisVersions()
        {
            return [0];
        }

        /// <summary>
        /// Gets a struct of status information from an axis
        /// </summary>
        /// <param name="axis">AxisId.Axis1 or AxisId.Axis2</param>
        /// <returns></returns>
        internal virtual AxisStatus GetAxisStatus(AxisId axis)
        {
            return new AxisStatus();
        }

        /// <summary>
        /// Gets last struct of status information but does not run a new mount command
        /// </summary>
        /// <param name="axis">AxisId.Axis1 or AxisId.Axis2</param>
        /// <returns></returns>
        internal virtual AxisStatus GetCacheAxisStatus(AxisId axis)
        {
            return new AxisStatus();
        }

        /// <summary>
        /// Get the sidereal rate in step counts
        /// </summary>
        /// <returns></returns>
        internal virtual long GetSiderealRate(AxisId axis)
        {
            return 0;
        }

        /// <summary>
        /// reset the position of an axis
        /// </summary>
        /// <param name="axis">AxisId.Axis1 or AxisId.Axis2</param>
        /// <param name="newValue">radians</param>
        internal virtual void SetAxisPosition(AxisId axis, double newValue)
        {

        }

        /// <summary>
        /// reset the position of an axis
        /// </summary>
        /// <param name="axis">AxisId.Axis1 or AxisId.Axis2</param>
        /// <param name="newValue">steps</param>
        internal virtual void SetAxisPositionCounter(AxisId axis, int newValue)
        {

        }

        /// <summary>
        /// Turn on/off individual axis encoders
        /// </summary>
        /// <param name="axis">AxisId.Axis1 or AxisId.Axis2</param>
        /// <param name="on">on=true,off=false</param>
        internal virtual void SetEncoder(AxisId axis, bool on)
        {

        }

        /// <summary>
        /// Turn on/off pPEC
        /// </summary>
        /// <param name="axis">AxisId.Axis1 or AxisId.Axis2</param>
        /// <param name="on">on=true,off=false</param>
        internal virtual string SetPPec(AxisId axis, bool on)
        {
            return String.Empty;
        }

        /// <summary>
        /// Turn on/off training pPEC
        /// </summary>
        /// <param name="axis">AxisId.Axis1 or AxisId.Axis2</param>
        /// <param name="on">on=true,off=false</param>
        internal virtual void SetPPecTrain(AxisId axis, bool on)
        {

        }

        /// <summary>
        /// Enable or Disable Full Current Low speed
        /// </summary>
        /// <param name="axis">AxisId.Axis1 or AxisId.Axis2</param>
        /// <param name="on">on=true,off=false</param>
        internal virtual void SetFullCurrent(AxisId axis, bool on){ }

        /// <summary>
        /// Loads default settings directly from the mount
        /// </summary>
        internal virtual void LoadDefaultMountSettings(){ }

    }
}
