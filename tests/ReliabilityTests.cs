using RetryLab;
using Xunit;

namespace RetryLabTests;

public sealed class ManualClock : TimeProvider
{
    private DateTimeOffset now = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => now;
    public void Advance(int milliseconds) => now = now.AddMilliseconds(milliseconds);
}
public sealed class ReliabilityTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "retrylab-test-" + Guid.NewGuid().ToString("N"));
    private readonly ManualClock clock = new();
    private Store CreateStore() => new(Path.Combine(folder, "operations.db"), clock);
    private SimulatedReceiver Receiver() => new(Path.Combine(folder, "receiver.db"));
    public void Dispose() { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); Directory.Delete(folder, true); }

    [Fact] public void ReplayReturnsSameOperationAndSingleOutboxEvent()
    {
        var store = CreateStore(); var first = store.Create("order-1", "pedido", 12990);
        var replay = store.Create("order-1", "pedido", 12990);
        Assert.False(first.Replayed); Assert.True(replay.Replayed); Assert.Equal(first.Operation, replay.Operation);
        Assert.Single(store.Operations()); Assert.Single(store.Events());
    }
    [Fact] public void ReusedKeyWithChangedPayloadIsRejectedWithoutWrites()
    {
        var store = CreateStore(); store.Create("key", "original", 100);
        Assert.Throws<KeyConflictException>(() => store.Create("key", "changed", 100));
        Assert.Throws<KeyConflictException>(() => store.Create("key", "original", 200));
        Assert.Single(store.Operations()); Assert.Single(store.Events());
    }
    [Theory] [InlineData(0)] [InlineData(-1)] [InlineData(100_000_001)]
    public void InvalidAmountNeverCreatesBusinessOrOutboxRows(long amount)
    {
        var store = CreateStore(); Assert.Throws<ArgumentException>(() => store.Create("key", "order", amount));
        Assert.Empty(store.Operations()); Assert.Empty(store.Events());
    }
    [Fact] public async Task ParallelRequestsAcrossConnectionsCreateOneOperation()
    {
        var store = CreateStore();
        var results = await Task.WhenAll(Enumerable.Range(0, 24).Select(_ => Task.Run(() => CreateStore().Create("same", "order", 100))));
        Assert.Single(results.Select(x => x.Operation.Id).Distinct()); Assert.Single(results, x => !x.Replayed);
        Assert.Single(store.Events());
    }
    [Fact] public void OperationAndOutboxRollbackTogetherIfSecondWriteFails()
    {
        var store = CreateStore();
        using (var db = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={Path.Combine(folder, "operations.db")}"))
        {
            db.Open(); using var cmd = db.CreateCommand();
            cmd.CommandText = "CREATE TRIGGER fail_outbox BEFORE INSERT ON outbox BEGIN SELECT RAISE(ABORT,'injected failure'); END;";
            cmd.ExecuteNonQuery();
        }
        Assert.Throws<Microsoft.Data.Sqlite.SqliteException>(() => store.Create("key", "order", 100));
        Assert.Empty(store.Operations()); Assert.Empty(store.Events());
    }
    [Fact] public void BackoffPreventsImmediateRetryAndFailureGoesToDeadLetter()
    {
        var store = CreateStore(); var receiver = Receiver(); var worker = new Dispatcher(store, receiver);
        store.Create("key", "order", 100); worker.Tick(FailureMode.Unavailable);
        Assert.Null(worker.Tick(FailureMode.Healthy));
        clock.Advance(2000); worker.Tick(FailureMode.Unavailable);
        clock.Advance(4000); worker.Tick(FailureMode.Unavailable);
        Assert.Equal("dead", Assert.Single(store.Events()).Status); Assert.Empty(receiver.Receipts());
        Assert.Equal(3, store.History().Count); Assert.Null(worker.Tick(FailureMode.Healthy));
        Assert.True(store.Requeue(store.Events()[0].Id)); worker.Tick(FailureMode.Healthy);
        Assert.Equal("delivered", store.Events()[0].Status); Assert.Single(receiver.Receipts());
        Assert.Contains(store.History(), x => x.Outcome == "manual-requeue");
    }
    [Fact] public void LostAcknowledgementDoesNotDuplicateReceiverEffectAfterRestart()
    {
        var store = CreateStore(); store.Create("key", "order", 100);
        new Dispatcher(store, Receiver()).Tick(FailureMode.AcknowledgementLost);
        Assert.Single(Receiver().Receipts()); Assert.Equal("pending", store.Events()[0].Status);
        clock.Advance(2000);
        var reopenedStore = CreateStore(); new Dispatcher(reopenedStore, Receiver()).Tick(FailureMode.Healthy);
        Assert.Equal("delivered", reopenedStore.Events()[0].Status); Assert.Single(Receiver().Receipts());
        Assert.Equal(2, reopenedStore.History().Count);
    }
    [Fact] public void ExpiredLeaseCanBeRecoveredAndStaleWorkerCannotAcknowledge()
    {
        var store = CreateStore(); store.Create("key", "order", 100);
        var stale = store.Claim()!; Assert.Null(CreateStore().Claim());
        clock.Advance(30001); var current = CreateStore().Claim()!;
        Assert.Equal(stale.Id, current.Id); Assert.NotEqual(stale.LeaseToken, current.LeaseToken);
        Assert.False(store.Finish(stale, true)); Assert.True(store.Finish(current, true));
        Assert.Equal("delivered", store.Events()[0].Status);
    }
    [Fact] public async Task ConcurrentDispatchersCannotClaimTheSameLiveLease()
    {
        var store = CreateStore(); store.Create("key", "order", 100);
        var claims = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => Task.Run(() => CreateStore().Claim())));
        Assert.Single(claims, x => x is not null);
    }
    [Fact] public void OnlyDeadLettersCanBeManuallyRequeued()
    {
        var store = CreateStore(); store.Create("key", "order", 100);
        Assert.False(store.Requeue(store.Events()[0].Id)); Assert.False(store.Requeue("missing"));
    }
}
