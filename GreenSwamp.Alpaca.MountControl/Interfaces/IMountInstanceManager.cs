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

namespace GreenSwamp.Alpaca.MountControl.Interfaces
{
    /// <summary>
    /// Mount instance manager interface for managing multiple mount instances.
    /// Enables creation, retrieval, and lifecycle management of multiple telescope mounts.
    /// </summary>
    public interface IMountInstanceManager
    {
        /// <summary>
        /// Get or create a mount instance with the specified ID
        /// </summary>
        /// <param name="id">Unique identifier for the mount instance</param>
        /// <returns>Mount instance, or null if creation failed</returns>
        IMountController? GetOrCreate(string id);

        /// <summary>
        /// Get an existing mount instance by ID
        /// </summary>
        /// <param name="id">Unique identifier for the mount instance</param>
        /// <returns>Mount instance if exists, null otherwise</returns>
        IMountController? Get(string id);

        /// <summary>
        /// Get the default mount instance (for backward compatibility)
        /// </summary>
        /// <returns>Default mount instance</returns>
        IMountController GetDefault();

        /// <summary>
        /// Check if a mount instance with the specified ID exists
        /// </summary>
        /// <param name="id">Unique identifier to check</param>
        /// <returns>True if instance exists, false otherwise</returns>
        bool Exists(string id);

        /// <summary>
        /// Remove and dispose a mount instance
        /// </summary>
        /// <param name="id">Unique identifier of the instance to remove</param>
        /// <returns>True if removed, false if not found</returns>
        bool Remove(string id);

        /// <summary>
        /// Get all active mount instance IDs
        /// </summary>
        /// <returns>Collection of mount instance IDs</returns>
        IReadOnlyCollection<string> GetActiveInstances();

        /// <summary>
        /// Get count of active mount instances
        /// </summary>
        int Count { get; }

        /// <summary>
        /// Remove all mount instances and release resources
        /// </summary>
        void RemoveAll();
    }
}
