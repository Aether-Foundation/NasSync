namespace NasSync.Core.Tests;

/// <summary>
/// Placeholder test class for sync engine unit tests.
/// Tests will cover change detection, conflict resolution, queue management, and state transitions.
/// </summary>
public class SyncEngineTests
{
    [Fact]
    public void SyncState_InitialValue_ShouldBeIdle()
    {
        // Arrange & Act & Assert — placeholder for first test
        Assert.Equal(SyncState.Idle, default(SyncState));
    }

    [Fact]
    public void SyncQueueStatus_DefaultValues_ShouldBeZero()
    {
        // Arrange & Act
        var status = new SyncQueueStatus(0, 0, 0, 0);

        // Assert
        Assert.Equal(0, status.PendingCount);
        Assert.Equal(0, status.InProgressCount);
        Assert.Equal(0, status.CompletedCount);
        Assert.Equal(0, status.FailedCount);
    }
}
