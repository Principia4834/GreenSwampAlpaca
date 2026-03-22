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
    /// Enabled once SkyQueueImplementation exposes StepsUpdated event (Q1).
    /// </summary>
    [Fact(Skip = "Enable after Q1: SkyQueueImplementation.StepsUpdated event added and made internal/public")]
    public void WhenStepsUpdatedThenInstanceEventFires()
    {
        // SkyQueueImplementation will need to be made internal (with InternalsVisibleTo)
        // or a test-seam added before this test can be filled in.
        // Placeholder body — kept for documentation purposes.
        Assert.True(true, "placeholder — enable and fill in after Q1");
    }

    // -------------------------------------------------------------------------
    // Q2 — Queue instances owned by MountInstance
    // -------------------------------------------------------------------------

    /// <summary>
    /// After Q2: each MountInstance must own a non-null queue reference
    /// after it has been started (simulated path only).
    /// Enabled once MountInstance exposes MountQueue / SkyQueue fields (Q2).
    /// </summary>
    [Fact(Skip = "Enable after Q2: requires testable MountInstance + Simulator queue startup")]
    public void WhenSimulatorMountStartedThenMountInstanceOwnsQueue()
    {
        // Arrange — requires a MountInstance constructor that is testable without
        // a live serial port; use the simulator mount type
        // var settings = new SkySettingsInstance { Mount = MountType.Simulator };
        // var instance = new MountInstance("test-0", settings);

        // Act
        // instance.MountStart();

        // Assert
        // Assert.NotNull(instance.MountQueueInstance);
        // Assert.True(instance.MountQueueInstance.IsRunning);

        // Cleanup
        // instance.MountStop();
        Assert.True(true, "placeholder — enable and fill in after Q2 test infrastructure is ready");
    }

    /// <summary>
    /// After Q2: two distinct MountInstance objects must hold independent
    /// queue references (reference inequality).
    /// </summary>
    [Fact(Skip = "Enable after Q2: requires testable MountInstance + per-instance queues")]
    public void WhenTwoDevicesRegisteredThenQueuesAreIndependent()
    {
        // var settings = new SkySettingsInstance { Mount = MountType.Simulator };
        // var device0 = new MountInstance("test-0", settings);
        // var device1 = new MountInstance("test-1", settings);
        // device0.MountStart();
        // device1.MountStart();
        // Assert.NotSame(device0.MountQueueInstance, device1.MountQueueInstance);
        // device0.MountStop();
        // device1.MountStop();
        Assert.True(true, "placeholder — enable after per-instance queues are wired (post-Q4)");
    }

    // -------------------------------------------------------------------------
    // Q3 + Q4 — Command dispatch through instance queue
    // -------------------------------------------------------------------------

    /// <summary>
    /// After Q3+Q4: a command sent to device 0's queue must not appear
    /// in device 1's queue. Verifies per-device isolation end-to-end.
    /// </summary>
    [Fact(Skip = "Enable after Q3+Q4: command base classes accept ICommandQueue")]
    public void WhenCommandSentToDevice0ThenDevice1QueueUnaffected()
    {
        Assert.True(true, "placeholder — enable and fill in after Q3+Q4");
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
