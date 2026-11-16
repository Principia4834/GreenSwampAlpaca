using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GreenSwamp.Alpaca.Server.Diagnostics
{
    /// <summary>
    /// Detects thread-pool starvation and debugger pauses: it asks for a 100 ms delay and warns
    /// when the continuation runs much later than that. Also logs a 10 s thread-pool summary.
    /// </summary>
    public sealed class ThreadStallMonitor(ILoggerFactory loggerFactory) : BackgroundService
    {
        private const int ExpectedMs = 100;
        private const int StallWarnMs = 500;
        private static readonly TimeSpan SummaryInterval = TimeSpan.FromSeconds(10);

        private readonly ILogger _log = loggerFactory.CreateLogger("GS.Diag");

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _log.LogInformation("[GS.Diag] ThreadStallMonitor started");
            var sinceSummary = System.Diagnostics.Stopwatch.StartNew();
            long worstMs = 0;

            try
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    var t0 = System.Diagnostics.Stopwatch.GetTimestamp();
                    await Task.Delay(ExpectedMs, stoppingToken);
                    var lateMs = (long)System.Diagnostics.Stopwatch.GetElapsedTime(t0).TotalMilliseconds - ExpectedMs;
                    worstMs = Math.Max(worstMs, lateMs);

                    if (lateMs >= StallWarnMs)
                    {
                        _log.LogWarning(
                            "[GS.Diag] STALL: a {Expected} ms delay completed {Late} ms late. thr={Threads} queue={Queue} (thread-pool starvation, or the debugger was paused)",
                            ExpectedMs, lateMs, ThreadPool.ThreadCount, ThreadPool.PendingWorkItemCount);
                    }

                    if (sinceSummary.Elapsed >= SummaryInterval)
                    {
                        _log.LogInformation(
                            "[GS.Diag] ThreadPool summary: thr={Threads} queue={Queue} completed={Completed} worstLate={Worst} ms",
                            ThreadPool.ThreadCount, ThreadPool.PendingWorkItemCount, ThreadPool.CompletedWorkItemCount, worstMs);
                        worstMs = 0;
                        sinceSummary.Restart();
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
        }
    }
}
