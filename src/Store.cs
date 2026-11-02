using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

namespace RetryLab;

public record Operation(string Id, string Reference, long AmountCents, string CreatedAt);
public record Creation(Operation Operation, bool Replayed);
public record Event(string Id, string OperationId, long AmountCents, string Status, int Attempts,
    long DueAt, long LeaseUntil, string? LeaseToken, string? LastError);
public record Attempt(string EventId, int Number, string Outcome, long At);
public sealed class KeyConflictException : Exception;

/// <summary>SQLite transactions protect invariants across threads and processes on one host.</summary>
public sealed class Store
{
    private readonly string connectionString;
    private readonly TimeProvider clock;
    public Store(string path, TimeProvider? clock = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        connectionString = new SqliteConnectionStringBuilder { DataSource = path, DefaultTimeout = 15 }.ToString();
        this.clock = clock ?? TimeProvider.System;
        using var db = Open();
        Execute(db, null, """
            PRAGMA journal_mode=WAL;
            CREATE TABLE IF NOT EXISTS operations (
              id TEXT PRIMARY KEY, idem_key TEXT NOT NULL UNIQUE, fingerprint TEXT NOT NULL,
              reference TEXT NOT NULL, amount_cents INTEGER NOT NULL CHECK(amount_cents > 0), created_at TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS outbox (
              id TEXT PRIMARY KEY, operation_id TEXT NOT NULL UNIQUE REFERENCES operations(id),
              amount_cents INTEGER NOT NULL, status TEXT NOT NULL DEFAULT 'pending', attempts INTEGER NOT NULL DEFAULT 0,
              due_at INTEGER NOT NULL, lease_until INTEGER NOT NULL DEFAULT 0, lease_token TEXT, last_error TEXT);
            CREATE TABLE IF NOT EXISTS attempts (
              id INTEGER PRIMARY KEY, event_id TEXT NOT NULL, number INTEGER NOT NULL, outcome TEXT NOT NULL, at INTEGER NOT NULL);
            """);
    }
    private SqliteConnection Open()
    {
        var db = new SqliteConnection(connectionString); db.Open();
        Execute(db, null, "PRAGMA foreign_keys=ON;"); return db;
    }
    internal static int Execute(SqliteConnection db, SqliteTransaction? tx, string sql, params (string, object?)[] values)
    {
        using var cmd = db.CreateCommand(); cmd.Transaction = tx; cmd.CommandText = sql;
        foreach (var (key, value) in values) cmd.Parameters.AddWithValue(key, value ?? DBNull.Value);
        return cmd.ExecuteNonQuery();
    }
    internal static SqliteCommand Query(SqliteConnection db, SqliteTransaction? tx, string sql, params (string, object?)[] values)
    {
        var cmd = db.CreateCommand(); cmd.Transaction = tx; cmd.CommandText = sql;
        foreach (var (key, value) in values) cmd.Parameters.AddWithValue(key, value ?? DBNull.Value);
        return cmd;
    }
    public Creation Create(string key, string reference, long amountCents)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > 120) throw new ArgumentException("Informe uma chave entre 1 e 120 caracteres.");
        if (string.IsNullOrWhiteSpace(reference) || reference.Length > 120) throw new ArgumentException("Referência inválida (1 a 120 caracteres).");
        if (amountCents is <= 0 or > 100_000_000) throw new ArgumentException("Valor deve ser de 1 a 100.000.000 centavos.");
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{reference.Length}:{reference}|{amountCents}")));
        using var db = Open(); using var tx = db.BeginTransaction(deferred: false);
        using (var cmd = Query(db, tx, "SELECT id,reference,amount_cents,created_at,fingerprint FROM operations WHERE idem_key=$key", ("$key", key)))
        using (var row = cmd.ExecuteReader())
        {
            if (row.Read())
            {
                if (row.GetString(4) != fingerprint) throw new KeyConflictException();
                return new(new(row.GetString(0), row.GetString(1), row.GetInt64(2), row.GetString(3)), true);
            }
        }
        var operation = new Operation(Guid.NewGuid().ToString("N"), reference, amountCents, clock.GetUtcNow().ToString("O"));
        Execute(db, tx, "INSERT INTO operations VALUES($id,$key,$hash,$reference,$amount,$at)",
            ("$id", operation.Id), ("$key", key), ("$hash", fingerprint), ("$reference", reference), ("$amount", amountCents), ("$at", operation.CreatedAt));
        Execute(db, tx, "INSERT INTO outbox(id,operation_id,amount_cents,due_at) VALUES($id,$operation,$amount,$due)",
            ("$id", Guid.NewGuid().ToString("N")), ("$operation", operation.Id), ("$amount", amountCents), ("$due", Now));
        tx.Commit(); return new(operation, false);
    }
    public long Now => clock.GetUtcNow().ToUnixTimeMilliseconds();
    public Event? Claim()
    {
        using var db = Open(); using var tx = db.BeginTransaction(deferred: false);
        Event? item;
        using (var cmd = Query(db, tx, "SELECT * FROM outbox WHERE (status='pending' AND due_at <= $now) OR (status='processing' AND lease_until <= $now) ORDER BY due_at,id LIMIT 1", ("$now", Now)))
        using (var rows = cmd.ExecuteReader()) item = rows.Read() ? ReadEvent(rows) : null;
        if (item is null) return null;
        var token = Guid.NewGuid().ToString("N"); var until = Now + 30_000;
        Execute(db, tx, "UPDATE outbox SET status='processing', attempts=attempts+1, lease_until=$until, lease_token=$token WHERE id=$id",
            ("$until", until), ("$token", token), ("$id", item.Id));
        tx.Commit(); return item with { Status = "processing", Attempts = item.Attempts + 1, LeaseUntil = until, LeaseToken = token };
    }
    public bool Finish(Event item, bool delivered, string? error = null)
    {
        using var db = Open(); using var tx = db.BeginTransaction(deferred: false);
        var status = delivered ? "delivered" : item.Attempts >= 3 ? "dead" : "pending";
        var delay = Math.Min(30_000L, 1000L << Math.Min(item.Attempts, 10));
        var changed = Execute(db, tx, """
            UPDATE outbox SET status=$status,due_at=$due,lease_until=0,lease_token=NULL,last_error=$error
            WHERE id=$id AND status='processing' AND lease_token=$token
            """, ("$status", status), ("$due", Now + delay), ("$error", error), ("$id", item.Id), ("$token", item.LeaseToken));
        if (changed == 1) Execute(db, tx, "INSERT INTO attempts(event_id,number,outcome,at) VALUES($id,$number,$outcome,$at)",
            ("$id", item.Id), ("$number", item.Attempts), ("$outcome", delivered ? "delivered" : error), ("$at", Now));
        tx.Commit(); return changed == 1;
    }
    public bool Requeue(string id)
    {
        using var db = Open(); using var tx = db.BeginTransaction(deferred: false);
        var n = Execute(db, tx, "UPDATE outbox SET status='pending',attempts=0,due_at=$now,last_error=NULL WHERE id=$id AND status='dead'", ("$id", id), ("$now", Now));
        if (n == 1) Execute(db, tx, "INSERT INTO attempts(event_id,number,outcome,at) VALUES($id,0,'manual-requeue',$at)", ("$id", id), ("$at", Now));
        tx.Commit(); return n == 1;
    }
    private static Event ReadEvent(SqliteDataReader r) => new(r.GetString(0), r.GetString(1), r.GetInt64(2), r.GetString(3), r.GetInt32(4), r.GetInt64(5), r.GetInt64(6), r.IsDBNull(7) ? null : r.GetString(7), r.IsDBNull(8) ? null : r.GetString(8));
    public List<Event> Events()
    {
        using var db = Open(); using var cmd = Query(db, null, "SELECT * FROM outbox ORDER BY due_at,id"); using var r = cmd.ExecuteReader();
        var result = new List<Event>(); while (r.Read()) result.Add(ReadEvent(r)); return result;
    }
    public List<Operation> Operations()
    {
        using var db = Open(); using var cmd = Query(db, null, "SELECT id,reference,amount_cents,created_at FROM operations ORDER BY created_at DESC"); using var r = cmd.ExecuteReader();
        var result = new List<Operation>(); while (r.Read()) result.Add(new(r.GetString(0), r.GetString(1), r.GetInt64(2), r.GetString(3))); return result;
    }
    public List<Attempt> History()
    {
        using var db = Open(); using var cmd = Query(db, null, "SELECT event_id,number,outcome,at FROM attempts ORDER BY id DESC LIMIT 100"); using var r = cmd.ExecuteReader();
        var result = new List<Attempt>(); while (r.Read()) result.Add(new(r.GetString(0), r.GetInt32(1), r.GetString(2), r.GetInt64(3))); return result;
    }
}
