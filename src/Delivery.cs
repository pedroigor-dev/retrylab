using Microsoft.Data.Sqlite;
namespace RetryLab;

public enum FailureMode { Healthy, Unavailable, AcknowledgementLost }
public record Receipt(string EventId, long AmountCents);

/// <summary>A separate durable destination. No network or real money is involved.</summary>
public sealed class SimulatedReceiver
{
    private readonly string connectionString;
    public SimulatedReceiver(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        connectionString = new SqliteConnectionStringBuilder { DataSource = path, DefaultTimeout = 15 }.ToString();
        using var db = Open(); Store.Execute(db, null, "PRAGMA journal_mode=WAL; CREATE TABLE IF NOT EXISTS receipts(event_id TEXT PRIMARY KEY,amount_cents INTEGER NOT NULL);");
    }
    private SqliteConnection Open() { var db = new SqliteConnection(connectionString); db.Open(); return db; }
    public void Accept(Event item, FailureMode mode)
    {
        if (mode == FailureMode.Unavailable) throw new IOException("destination-unavailable");
        using (var db = Open())
        {
            // A unique event ID deduplicates the business effect even after a lost acknowledgement.
            Store.Execute(db, null, "INSERT INTO receipts VALUES($id,$amount) ON CONFLICT(event_id) DO NOTHING", ("$id", item.Id), ("$amount", item.AmountCents));
        }
        if (mode == FailureMode.AcknowledgementLost) throw new IOException("acknowledgement-lost-after-commit");
    }
    public List<Receipt> Receipts()
    {
        using var db = Open(); using var cmd = Store.Query(db, null, "SELECT * FROM receipts ORDER BY event_id"); using var r = cmd.ExecuteReader();
        var result = new List<Receipt>(); while (r.Read()) result.Add(new(r.GetString(0), r.GetInt64(1))); return result;
    }
}
public sealed class Dispatcher(Store store, SimulatedReceiver receiver)
{
    public Event? Tick(FailureMode mode)
    {
        var item = store.Claim(); if (item is null) return null;
        try { receiver.Accept(item, mode); store.Finish(item, true); }
        catch (IOException ex) { store.Finish(item, false, ex.Message); }
        return item;
    }
}
