using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace GreenSwamp.Alpaca.Server.Diagnostics
{
    /// <summary>
    /// Step-by-step lifecycle tracer for a page or service. Every line is written under the "GS.Diag"
    /// category as: [GS.Diag] tag +elapsedMs thr/queue message.
    /// <see cref="StepAsync{T}"/> brackets an await with START / DONE lines and raises a
    /// "STILL WAITING" warning every few seconds, so the last START without a DONE is the call that is hanging.
    /// </summary>
    public sealed class GsDiag
    {
        private const string Category = "GS.Diag";
        private static readonly TimeSpan WaitWarnInterval = TimeSpan.FromSeconds(3);

        // Only the first few calls of each step are logged in full; slow, failed and hung calls are always logged.
        private const int VerboseCount = 3;
        private const long SlowMs = 1000;

        private readonly ILogger _log;
        private readonly string _tag;
        private readonly Stopwatch _sw = Stopwatch.StartNew();
        private readonly ConcurrentDictionary<string, int> _counts = new();

        private GsDiag(ILogger log, string tag)
        {
            _log = log;
            _tag = tag;
        }

        public static GsDiag Create(ILoggerFactory factory, string component, int deviceNumber, string instanceId)
            => new(factory.CreateLogger(Category),
                $"{component}[dev={deviceNumber} id={(instanceId.Length > 6 ? instanceId[..6] : instanceId)}]");

        public void Mark(string message) => Write(LogLevel.Information, message);

        public void Warn(string message) => Write(LogLevel.Warning, message);

        public async Task StepAsync(string step, Func<Task> work)
        {
            await StepAsync<bool>(step, async () =>
            {
                await work();
                return true;
            });
        }

        public async Task<T> StepAsync<T>(string step, Func<Task<T>> work)
        {
            var call = _counts.AddOrUpdate(step, 1, (_, c) => c + 1);
            var verbose = call <= VerboseCount;
            var started = _sw.ElapsedMilliseconds;

            if (verbose) Write(LogLevel.Information, $"{step} START");

            using var cts = new CancellationTokenSource();
            try
            {
                var task = work();
                while (true)
                {
                    var finished = await Task.WhenAny(task, Task.Delay(WaitWarnInterval, cts.Token));
                    if (finished == task) break;
                    Write(LogLevel.Warning, $"{step} STILL WAITING after {_sw.ElapsedMilliseconds - started} ms");
                }

                var result = await task;
                var took = _sw.ElapsedMilliseconds - started;
                if (verbose || took >= SlowMs)
                    Write(took >= SlowMs ? LogLevel.Warning : LogLevel.Information, $"{step} DONE in {took} ms");
                return result;
            }
            catch (Exception ex)
            {
                Write(LogLevel.Warning, $"{step} FAILED after {_sw.ElapsedMilliseconds - started} ms: {ex.GetType().Name}: {ex.Message}");
                throw;
            }
            finally
            {
                cts.Cancel();
            }
        }

        private void Write(LogLevel level, string message)
            => _log.Log(level, "[GS.Diag] {Tag} +{ElapsedMs}ms thr={Threads} queue={Queue} {Message}",
                _tag, _sw.ElapsedMilliseconds, ThreadPool.ThreadCount, ThreadPool.PendingWorkItemCount, message);
    }
}
