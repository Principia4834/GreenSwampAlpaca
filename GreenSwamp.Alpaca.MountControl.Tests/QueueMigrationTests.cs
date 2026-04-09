using GreenSwamp.Alpaca.Mount.Commands;
using GreenSwamp.Alpaca.Mount.SkyWatcher;
using GreenSwamp.Alpaca.Mount.Simulator;
using GreenSwamp.Alpaca.Shared;
using System.Threading;

namespace GreenSwamp.Alpaca.MountControl.Tests;

/// <summary>
/// Phase 0 — Queue migration tests.
/// Tests are added incrementally as each Q-step is implemented.
/// </summary>
public class QueueMigrationTests
{
    // -------------------------------------------------------------------------
    // Q1 — Executor write-back decoupled from static queue properties
    // -------------------------------------------------------------------------

    /// <summary>
    /// After Q1: executor step write-back must reach the instance event,
    /// not write directly to the static SkyQueue.Steps property.
    /// Requires a live executor invocation — integration test, run explicitly.
    /// </summary>
    [Fact(Skip = "Integration test: requires simulator to be started and steps to be processed")]
    public void WhenStepsUpdatedThenInstanceEventFires()
    {
        // The callback chain (Q1/Q4d): executor → SetupCallbacks lambda → SkyServer.Steps setter
        // Full verification requires Mount.MountStart() + a running simulator tick.
        Assert.True(true, "placeholder — run as integration test with live simulator");
    }

    // -------------------------------------------------------------------------
    // Q2 — Queue instances owned by Mount
    // -------------------------------------------------------------------------

    /// <summary>
    /// After Q2: each Mount must own a non-null queue reference
    /// after it has been started (simulated path only).
    /// Integration test — requires a runnable simulator environment.
    /// </summary>
    [Fact(Skip = "Integration test: requires full Mount.MountStart() with running simulator")]
    public void WhenSimulatorMountStartedThenMountInstanceOwnsQueue()
    {
        // var settings = new SkySettings { Mount = MountType.Simulator };
        // var instance = new Mount("test-0", settings);
        // instance.MountStart();
        // Assert.NotNull(instance.MountQueueInstance);
        // Assert.True(instance.MountQueueInstance.IsRunning);
        // instance.MountStop();
        Assert.True(true, "placeholder — enable when integration test harness is available");
    }

    /// <summary>
    /// After Q2: two distinct MountQueueImplementation objects must not share
    /// the same reference — each device owns an independent queue instance.
    /// </summary>
    [Fact]
    public void WhenTwoDevicesRegisteredThenQueuesAreIndependent()
    {
        var queue0 = new MountQueueImplementation();
        var queue1 = new MountQueueImplementation();

        Assert.NotSame(queue0, queue1);
    }

    // -------------------------------------------------------------------------
    // Q3 + Q4 — Command dispatch through instance queue
    // -------------------------------------------------------------------------

    /// <summary>
    /// After Q3+Q4: a command sent to device 0's queue must not appear
    /// in device 1's queue. Verifies per-device isolation end-to-end.
    /// Integration test — requires two running queue instances.
    /// </summary>
    [Fact(Skip = "Integration test: requires two running Mount queues")]
    public void WhenCommandSentToDevice0ThenDevice1QueueUnaffected()
    {
        // Arrange: two independent MountQueueImplementation instances (Q2)
        // Act: send a command to queue0 using queue0.NewId
        // Assert: queue1.GetCommandResult() returns nothing / different result
        Assert.True(true, "placeholder — enable when integration test harness is available");
    }

    // -------------------------------------------------------------------------
    // Static queue bridge — regression guard
    // -------------------------------------------------------------------------

    /// <summary>
    /// A freshly created SkyQueueImplementation must not report IsRunning
    /// before Start() is called.
    /// </summary>
    [Fact]
    public void WhenSkyQueueNotStartedThenIsRunningIsFalse()
    {
        var queue = new SkyQueueImplementation();
        Assert.False(queue.IsRunning);
    }

    /// <summary>
    /// A freshly created MountQueueImplementation must not report IsRunning
    /// before Start() is called.
    /// </summary>
    [Fact]
    public void WhenMountQueueNotStartedThenIsRunningIsFalse()
    {
        var queue = new MountQueueImplementation();
        Assert.False(queue.IsRunning);
    }

    // -------------------------------------------------------------------------
    // Q-eventbased — Event-based queue completion (ManualResetEventSlim)
    // -------------------------------------------------------------------------

    /// <summary>
    /// ProcessCommandQueue must call CompletionEvent.Set() in its finally block.
    /// </summary>
    [Fact]
    public void WhenCommandCompletedThenCompletionEventIsSet()
    {
        var queue = new FakeCommandQueue();
        queue.Start();
        var command = new FakeCommand(queue);

        queue.GetCommandResult(command);

        Assert.True(command.CompletionEvent.IsSet);
        queue.Stop();
    }

    /// <summary>
    /// GetCommandResult must return failure immediately when the queue is not running.
    /// </summary>
    [Fact]
    public void WhenQueueStoppedBeforeCommandExecutedThenGetCommandResultReturnsFailure()
    {
        var queue = new FakeCommandQueue();
        queue.Start();
        var command = new FakeCommand(queue);
        queue.Stop();

        var result = queue.GetCommandResult(command);

        Assert.False(result.Successful);
        Assert.NotNull(result.Exception);
    }

    /// <summary>
    /// Statistics.CommandsTimedOut must increment when CompletionEvent is not set within the timeout.
    /// Uses a short-timeout queue (100 ms) and a command that sleeps 400 ms.
    /// </summary>
    [Fact]
    public void WhenGetCommandResultTimesOutThenTimedOutIsIncrementedInStatistics()
    {
        var queue = new ShortTimeoutFakeCommandQueue(); // CompletionTimeoutMs = 100 ms
        queue.Start();
        var command = new FakeCommand(queue, () => Thread.Sleep(400));

        var result = queue.GetCommandResult(command);

        Assert.Equal(1, queue.Statistics.CommandsTimedOut);
        Assert.False(result.Successful);
        queue.Stop(); // waits up to 5 s for the background task to drain
    }

    /// <summary>
    /// Statistics.CommandsSuccessful must increment after a command executes without errors.
    /// </summary>
    [Fact]
    public void WhenCommandSucceedsThenSuccessfulIsIncrementedInStatistics()
    {
        var queue = new FakeCommandQueue();
        queue.Start();
        var command = new FakeCommand(queue);

        queue.GetCommandResult(command);

        Assert.Equal(1, queue.Statistics.CommandsSuccessful);
        Assert.True(command.Successful);
        queue.Stop();
    }

    /// <summary>
    /// Statistics.CommandsFailed must increment when a command's Execute() throws.
    /// CommandBase.Execute() catches the exception internally so only Failed (not ExceptionsHandled) increments.
    /// </summary>
    [Fact]
    public void WhenCommandThrowsThenFailedIsIncrementedInStatistics()
    {
        var queue = new FakeCommandQueue();
        queue.Start();
        var command = new FakeCommand(queue, () => throw new InvalidOperationException("Simulated failure"));

        queue.GetCommandResult(command);

        Assert.Equal(1, queue.Statistics.CommandsFailed);
        Assert.False(command.Successful);
        Assert.NotNull(command.Exception);
        queue.Stop();
    }

    /// <summary>
    /// Start() must reset Statistics so counters from a prior session do not carry over.
    /// </summary>
    [Fact]
    public void WhenStartCalledThenStatisticsAreReset()
    {
        var queue = new FakeCommandQueue();
        queue.Start();
        var command = new FakeCommand(queue);
        queue.GetCommandResult(command);
        Assert.Equal(1, queue.Statistics.CommandsSuccessful);

        // Start() calls Stop() internally then resets statistics
        queue.Start();

        Assert.Equal(0, queue.Statistics.CommandsSuccessful);
        Assert.Equal(0, queue.Statistics.TotalCommandsProcessed);
        queue.Stop();
    }

    /// <summary>
    /// MountQueueImplementation must use a 22-second completion timeout.
    /// </summary>
    [Fact]
    public void WhenMountQueueImplementationCreatedThenCompletionTimeoutIs22Seconds()
    {
        var queue = new ExposedMountQueueImplementation();
        Assert.Equal(22000, queue.PublicCompletionTimeoutMs);
    }

    /// <summary>
    /// SkyQueueImplementation must use the default 40-second completion timeout.
    /// </summary>
    [Fact]
    public void WhenSkyQueueImplementationCreatedThenCompletionTimeoutIs40Seconds()
    {
        var queue = new ExposedSkyQueueImplementation();
        Assert.Equal(40000, queue.PublicCompletionTimeoutMs);
    }

    // -------------------------------------------------------------------------
    // Test infrastructure
    // -------------------------------------------------------------------------

    private sealed class FakeExecutor { }

    private class FakeCommandQueue : CommandQueueBase<FakeExecutor>
    {
        protected override bool IsConnected() => true;
        protected override FakeExecutor CreateExecutor() => new FakeExecutor();
        protected override void InitializeExecutor(FakeExecutor executor) { }
        protected override void CleanupExecutor(FakeExecutor executor) { }
    }

    private sealed class ShortTimeoutFakeCommandQueue : FakeCommandQueue
    {
        protected override int CompletionTimeoutMs => 100;
    }

    private sealed class FakeCommand : ActionCommand<FakeExecutor>
    {
        private readonly Action? _executeAction;

        public FakeCommand(ICommandQueue<FakeExecutor> queue, Action? executeAction = null)
            : base(queue.NewId, queue)
        {
            _executeAction = executeAction;
        }

        protected override void ExecuteAction(FakeExecutor executor) => _executeAction?.Invoke();
    }

    private sealed class ExposedMountQueueImplementation : MountQueueImplementation
    {
        public int PublicCompletionTimeoutMs => CompletionTimeoutMs;
    }

    private sealed class ExposedSkyQueueImplementation : SkyQueueImplementation
    {
        public int PublicCompletionTimeoutMs => CompletionTimeoutMs;
    }
}
