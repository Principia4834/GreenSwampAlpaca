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

// Single-consumer command processor for AltAz tracking.
// See AltAz-Tracking-Race-Condition-Analysis-2026-04-26.md, Sections 7 and 9
// for the design decisions (D1-D8) that govern this implementation.

using ASCOM.Common.DeviceInterfaces;
using GreenSwamp.Alpaca.Principles;
using System.Threading.Channels;

namespace GreenSwamp.Alpaca.MountControl
{
    /// <summary>
    /// Immutable read model published after every command cycle.
    /// Blazor UI and ASCOM read-back properties consume this via the volatile
    /// reference on <see cref="TrackingCommandProcessor.LastSnapshot"/> with
    /// no locks and no torn reads (D5).
    /// </summary>
    internal sealed record TrackingSnapshot(
        bool Tracking,
        TrackingMode Mode,
        double RateRa,
        double RateDec,
        DateTimeOffset PublishedAt);

    /// <summary>
    /// Single-consumer, multi-writer queue processor for all AltAz tracking
    /// state changes. Owns the <see cref="MediaTimer"/> lifecycle (previously
    /// shared across threads) and ensures <see cref="SkyPredictor"/> writes
    /// only occur on the consumer task thread, eliminating RC-1 through RC-9.
    /// </summary>
    internal sealed class TrackingCommandProcessor
    {
        private const double SiderealRate = 15.0410671786691;

        private readonly Mount _mount;
        private readonly Channel<ITrackingCommand> _channel;

        // 0 = no tick pending; 1 = tick already queued — prevents tick storms (RC-7/D5).
        private int _tickPending;

        private Task? _consumerTask;

        // Immutable snapshot; written only from consumer task, read from any thread.
        private volatile TrackingSnapshot? _lastSnapshot;

        /// <summary>Latest published snapshot, or <c>null</c> before first command.</summary>
        internal TrackingSnapshot? LastSnapshot => _lastSnapshot;

        internal TrackingCommandProcessor(Mount mount)
        {
            _mount = mount;
            _channel = Channel.CreateUnbounded<ITrackingCommand>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false
            });
        }

        // ----------------------------------------------------------------
        // Lifecycle
        // ----------------------------------------------------------------

        /// <summary>
        /// Start the consumer task. Call from <c>MountConnect()</c>.
        /// </summary>
        internal void Start(CancellationToken ct)
        {
            _consumerTask = Task.Factory.StartNew(
                () => ConsumeAsync(ct),
                ct,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default).Unwrap();
        }

        /// <summary>
        /// Signal end-of-stream and await the consumer task. Call from
        /// <c>MountDisconnect()</c>. Safe to call more than once.
        /// </summary>
        internal async Task StopAsync()
        {
            _channel.Writer.TryComplete();
            if (_consumerTask != null)
            {
                try { await _consumerTask.ConfigureAwait(false); }
                catch (OperationCanceledException) { }
            }
        }

        // ----------------------------------------------------------------
        // Writers (called from any thread — timer callback, ASCOM setters, UI)
        // ----------------------------------------------------------------

        /// <summary>
        /// Post a tracking command without blocking. Returns <c>false</c>
        /// only if the channel has been completed (disconnect in progress).
        /// </summary>
        internal bool Post(ITrackingCommand command) =>
            _channel.Writer.TryWrite(command);

        /// <summary>
        /// Post a <see cref="TimerTickCommand"/> only if no tick is already
        /// pending. The multimedia timer callback returns immediately (D5);
        /// the consumer processes the tick on its own thread.
        /// </summary>
        internal void PostTick()
        {
            if (Interlocked.CompareExchange(ref _tickPending, 1, 0) == 0)
                _channel.Writer.TryWrite(new TimerTickCommand());
        }

        // ----------------------------------------------------------------
        // Consumer loop
        // ----------------------------------------------------------------

        private async Task ConsumeAsync(CancellationToken ct)
        {
            await foreach (var cmd in _channel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            {
                try
                {
                    Process(cmd);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex) { _mount.LogTrackingError(ex); }
            }
        }

        // ----------------------------------------------------------------
        // Command dispatch — runs exclusively on the consumer task thread
        // ----------------------------------------------------------------

        private void Process(ITrackingCommand cmd)
        {
            switch (cmd)
            {
                case TimerTickCommand:
                    // Reset pending flag before processing so a new tick can be queued
                    // the moment the timer fires again, maintaining D5 latency.
                    Interlocked.Exchange(ref _tickPending, 0);
                    if (_mount.IsMountRunning && _mount.Tracking)
                        _mount.SetTracking();
                    break;

                case RateChangeCommand rc:
                    // Both axes applied atomically by the consumer (D2).
                    // SetTracking posts the hardware command immediately (D1).
                    _mount.RateRa = rc.RateRa;
                    _mount.RateDec = rc.RateDec;
                    // Seed the predictor with the new rates before SetTracking so that
                    // SetAltAzTrackingRates -> GetRaDecAtTime projects the correct future
                    // position on both axes (mirrors ActionRateRaDec for the AltAz path).
                    if (_mount.Settings.AlignmentMode == ASCOM.Common.DeviceInterfaces.AlignmentMode.AltAz)
                    {
                        if (_mount.Tracking)
                        {
                            var raDec = _mount.SkyPredictor.GetRaDecAtTime(
                                GreenSwamp.Alpaca.Principles.HiResDateTime.UtcNow);
                            _mount.SkyPredictor.Set(raDec[0], raDec[1], rc.RateRa, rc.RateDec);
                        }
                        else
                        {
                            _mount.SkyPredictor.Set(
                                _mount.RightAscensionXForm, _mount.DeclinationXForm,
                                rc.RateRa, rc.RateDec);
                        }
                    }
                    _mount.SetTracking();
                    PublishSnapshot();
                    break;

                case TrackingStateCommand ts:
                    _mount.ApplyTracking(ts.Tracking);
                    PublishSnapshot();
                    break;

                case PulseGuideCommand pg:
                    ApplyPulseGuide(pg);
                    break;

                case SeedAndEnableCommand se:
                    // Sync paths S7/S8 (Option A / D6): seed then re-enable.
                    _mount.SkyPredictor.Set(se.Ra, se.Dec, se.RateRa, se.RateDec);
                    _mount.ApplyTracking(true);
                    PublishSnapshot();
                    break;

                case SlewBoundaryCommand sb:
                    // Stop the timer so SlewController can write SkyPredictor
                    // directly after the ACK (Option A / D6). IsStart=false is
                    // an advisory; post-slew tracking restore is handled by the
                    // TrackingStateCommand emitted by ApplyTracking.
                    if (sb.IsStart)
                        _mount.StopAltAzTrackingTimerInternal();
                    sb.Ack.TrySetResult();
                    break;

                case StopTrackingCommand:
                    // Abort / stop-axes path (S9 / D6).
                    _mount.StopAltAzTrackingTimerInternal();
                    _mount.SkyPredictor.Reset();
                    _mount.Tracking = false;
                    _mount.TrackingMode = TrackingMode.Off;
                    _mount.SetTracking();
                    PublishSnapshot();
                    break;

                case ResumeTrackingCommand:
                    // Post-pulse tracking restore. Bypasses the ApplyTracking early-exit
                    // guard that fires because Tracking was never set to false during the
                    // pulse, which on SkyWatcher hardware would leave axes stopped with no
                    // SkyAxisSlew re-issued. Mirrors the pre-queue direct SetTracking() call.
                    // SetTracking() already conditionally starts the AltAz timer internally
                    // (only if not already running), so no explicit timer call is needed here.
                    if (_mount.IsMountRunning && _mount.Tracking)
                        _mount.SetTracking();
                    PublishSnapshot();
                    break;
            }
        }

        // ----------------------------------------------------------------
        // Helpers
        // ----------------------------------------------------------------

        /// <summary>
        /// Applies the predictor adjustment for one pulse-guide axis (D4/D8).
        /// Only the two SkyPredictor.Set lines are routed through the queue;
        /// the hardware pulse action remains on Task.Run in PulseGuideAltAz.
        /// </summary>
        private void ApplyPulseGuide(PulseGuideCommand pg)
        {
            switch (pg.Axis)
            {
                case 0: // RA
                    _mount.SkyPredictor.Set(
                        _mount.SkyPredictor.Ra - pg.DurationMs * 0.001 * pg.GuideRate / SiderealRate,
                        _mount.SkyPredictor.Dec);
                    break;
                case 1: // Dec
                    _mount.SkyPredictor.Set(
                        _mount.SkyPredictor.Ra,
                        _mount.SkyPredictor.Dec + pg.DurationMs * pg.GuideRate * 0.001);
                    break;
            }
        }

        private void PublishSnapshot()
        {
            _lastSnapshot = new TrackingSnapshot(
                _mount.Tracking,
                _mount.TrackingMode,
                _mount.RateRa,
                _mount.RateDec,
                DateTimeOffset.UtcNow);
        }
    }
}
