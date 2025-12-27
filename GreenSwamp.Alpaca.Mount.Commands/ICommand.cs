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
    /// Generic command interface that can be executed against a specific executor type
    /// </summary>
    /// <typeparam name="TExecutor">The type of executor that will process this command</typeparam>
    public interface ICommand<TExecutor>
    {
        /// <summary>
        /// Unique identifier for this command
        /// </summary>
        long Id { get; }

        /// <summary>
        /// UTC timestamp when the command was created
        /// </summary>
        DateTime CreatedUtc { get; }

        /// <summary>
        /// Indicates whether the command executed successfully
        /// </summary>
        bool Successful { get; set; }

        /// <summary>
        /// Exception that occurred during execution, if any
        /// </summary>
        Exception Exception { get; set; }

        /// <summary>
        /// Result of the command execution (null for action commands)
        /// </summary>
        dynamic Result { get; }

        /// <summary>
        /// Execute the command against the specified executor
        /// </summary>
        /// <param name="executor">The executor to run the command against</param>
        void Execute(TExecutor executor);
    }
}
