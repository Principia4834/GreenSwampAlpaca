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

// Queue-based AltAz tracking command model.
// All writes to the AltAz timer, SkyPredictor, and tracking state are routed
// through a Channel<ITrackingCommand> and processed by a single consumer
// (TrackingCommandProcessor) to eliminate the race conditions documented in
// AltAz-Tracking-Race-Condition-Analysis-2026-04-26.md.

namespace GreenSwamp.Alpaca.MountControl
{
    /// <summary>
    /// Marker interface for all commands that flow through the per-Mount
    /// AltAz tracking command channel.
    /// </summary>
    internal interface ITrackingCommand { }

    /// <summary>
    /// Signals the consumer to apply the current tracking state and mode,
    /// optionally starting or stopping the AltAz timer.
    /// Corresponds to <c>ApplyTracking(bool)</c> entry points.
    /// </summary>
    internal sealed record TrackingStateCommand(bool Tracking) : ITrackingCommand;

    /// <summary>
    /// Timer tick notification from the multimedia timer callback.
    /// The callback sets <c>_tickPending</c> and returns immediately (D5);
    /// the consumer clears the flag and calls <c>UpdateAltAzRates()</c>.
    /// </summary>
    internal sealed record TimerTickCommand : ITrackingCommand;

    /// <summary>
    /// Carries an updated RA/Dec rate pair. The writer merges the pending
    /// value of the other axis before enqueuing so the consumer always applies
    /// both axes atomically (D2). The resulting <see cref="TrackingSnapshot"/>
    /// is published immediately after the consumer processes this command (D1).
    /// </summary>
    internal sealed record RateChangeCommand(double RateRa, double RateDec) : ITrackingCommand;

    /// <summary>
    /// Routes the AltAz predictor adjustment for a single pulse-guide axis
    /// through the queue (D4/D8). The hardware pulse action remains on
    /// <c>Task.Run</c>; only the two <c>SkyPredictor.Set</c> calls are queued.
    /// </summary>
    internal sealed record PulseGuideCommand(int Axis, double GuideRate, int DurationMs) : ITrackingCommand;

    /// <summary>
    /// Signals that tracking should be seeded with new predictor coordinates
    /// and then re-enabled (used for sync paths S7/S8 per Option A/D6).
    /// </summary>
    internal sealed record SeedAndEnableCommand(double Ra, double Dec, double RateRa, double RateDec) : ITrackingCommand;

    /// <summary>
    /// Signals a slew or pulse-guide boundary (D6/Option A). The consumer stops the
    /// AltAz timer and signals the ACK, making it safe for the caller to write
    /// <c>SkyPredictor</c> directly. Post-boundary tracking restore is handled by
    /// the <see cref="TrackingStateCommand"/> or <see cref="ResumeTrackingCommand"/>
    /// posted after the boundary work completes.
    /// </summary>
    internal sealed record SlewBoundaryCommand(TaskCompletionSource Ack) : ITrackingCommand;

    /// <summary>
    /// Requests an immediate, unconditional stop: timer stopped, tracking off, predictor
    /// reset, hardware axes stopped. Used by both AltAz abort paths (<c>AbortSlewAsync</c>
    /// and <c>AbortSlew</c>) so that any <see cref="RateChangeCommand"/> already in the
    /// channel cannot re-arm tracking after the abort returns.
    /// </summary>
    internal sealed record StopTrackingCommand : ITrackingCommand;

    /// <summary>
    /// Unconditionally re-applies the current tracking rates and restarts the
    /// AltAz timer after a pulse guide completes. Unlike
    /// <see cref="TrackingStateCommand"/>, this bypasses the
    /// <c>ApplyTracking</c> early-exit guard (<c>if (tracking == Tracking) return</c>)
    /// which would suppress the <c>SetTracking()</c> / <c>SkyAxisSlew</c> call
    /// on SkyWatcher hardware when <c>Tracking</c> was never set to false during
    /// the pulse. Mirrors the pre-queue <c>this.SetTracking()</c> direct call.
    /// </summary>
    internal sealed record ResumeTrackingCommand : ITrackingCommand;
}
