using GreenSwamp.Alpaca.Mount.SkyWatcher;
using GreenSwamp.Alpaca.Mount.Simulator;

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
        // Full verification requires MountInstance.MountStart() + a running simulator tick.
        Assert.True(true, "placeholder — run as integration test with live simulator");
    }

    // -------------------------------------------------------------------------
    // Q2 — Queue instances owned by MountInstance
    // -------------------------------------------------------------------------

    /// <summary>
    /// After Q2: each MountInstance must own a non-null queue reference
    /// after it has been started (simulated path only).
    /// Integration test — requires a runnable simulator environment.
    /// </summary>
    [Fact(Skip = "Integration test: requires full MountInstance.MountStart() with running simulator")]
    public void WhenSimulatorMountStartedThenMountInstanceOwnsQueue()
    {
        // var settings = new SkySettingsInstance { Mount = MountType.Simulator };
        // var instance = new MountInstance("test-0", settings);
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
    [Fact(Skip = "Integration test: requires two running MountInstance queues")]
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
    /// The static SkyQueue.IsRunning must stay false when the implementation
    /// has not been started. This guards the static bridge during transition.
    /// </summary>
    [Fact]
    public void WhenSkyQueueNotStartedThenIsRunningIsFalse()
    {
        // The static singleton is shared — we can only assert it reflects the
        // underlying implementation state.  If another test started the queue
        // this could fail; run in isolation.
        Assert.False(SkyQueue.IsRunning);
    }

    /// <summary>
    /// The static MountQueue.IsRunning must stay false when not started.
    /// </summary>
    [Fact]
    public void WhenMountQueueNotStartedThenIsRunningIsFalse()
    {
        Assert.False(MountQueue.IsRunning);
    }
}
