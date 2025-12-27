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

namespace GreenSwamp.Alpaca.Mount.Commands
{
    /// <summary>
    /// Interface for command queue that manages command execution
    /// </summary>
    /// <typeparam name="TExecutor">The type of executor that will process commands</typeparam>
    public interface ICommandQueue<TExecutor>
    {
        /// <summary>
        /// Indicates whether the queue is currently running
        /// </summary>
        bool IsRunning { get; }

        /// <summary>
        /// Gets a new unique command ID
        /// </summary>
        long NewId { get; }

        /// <summary>
        /// Add a command to the queue for execution
        /// </summary>
        /// <param name="command">The command to add</param>
        void AddCommand(ICommand<TExecutor> command);

        /// <summary>
        /// Get the result of a command execution
        /// </summary>
        /// <param name="command">The command to get results for</param>
        /// <returns>The command with updated status and results</returns>
        ICommand<TExecutor> GetCommandResult(ICommand<TExecutor> command);

        /// <summary>
        /// Start the command queue
        /// </summary>
        void Start();

        /// <summary>
        /// Stop the command queue
        /// </summary>
        void Stop();
    }
}
