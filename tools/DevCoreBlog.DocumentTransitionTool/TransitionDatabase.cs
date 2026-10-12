using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DevCoreBlog.Services.Documents;
using Npgsql;

/// <summary>One bounded transaction reads or applies a finite manifest; all unrelated fields remain unchanged.</summary>
internal sealed class TransitionDatabase(string connectionString)
{
    internal static NpgsqlConnectionStringBuilder Settings(string value) => new(value) {
        Timeout = 5, CommandTimeout = 10, CancellationTimeout = 1000, Pooling = false, IncludeErrorDetail = false,
        ApplicationName = "DevCoreBlog.DocumentTransition"
    };
    internal static string Fingerprint(NpgsqlConnectionStringBuilder s) => TransitionHash.Of(string.Join('|', s.Host, s.Port, s.Database, s.Username));
    internal async Task<TransitionSnapshot> ExportAsync(CancellationToken token)
    {
        await using var c = new NpgsqlConnection(Settings(connectionString).ConnectionString); await c.OpenAsync(token);
        await using var tx = await c.BeginTransactionAsync(IsolationLevel.RepeatableRead, token);
        await Command(c, tx, "SET TRANSACTION READ ONLY; SET LOCAL statement_timeout=10000; SET LOCAL lock_timeout=1000; SET LOCAL TIME ZONE 'UTC'", token);
        return await Snapshot(c, tx, token);
    }
    internal async Task<object> RunAsync(TransitionPlan plan, bool apply, CancellationToken token)
    {
        var prepared = TransitionPlanValidation.Prepare(plan);
        var settings = Settings(connectionString);
        if (Fingerprint(settings) != plan.TargetFingerprint) throw new InvalidOperationException();
        await using var c = new NpgsqlConnection(settings.ConnectionString); await c.OpenAsync(token);
        await using var tx = await c.BeginTransactionAsync(IsolationLevel.RepeatableRead, token);
        await Command(c, tx, "SET LOCAL statement_timeout=10000; SET LOCAL lock_timeout=1000; SET LOCAL idle_in_transaction_session_timeout=15000; SET LOCAL TIME ZONE 'UTC'", token);
        if (apply) await Command(c, tx, "LOCK TABLE public.\"Posts\", public.\"Categories\", public.\"WebhookReceipts\" IN EXCLUSIVE MODE", token);
        else await Command(c, tx, "SET TRANSACTION READ ONLY", token);
        await using (var schema = new NpgsqlCommand("SELECT count(*)=5 FROM information_schema.columns WHERE table_schema='public' AND table_name='Posts' AND column_name IN ('DocumentVersion','DocumentJson','DocumentPlainText','DocumentWordCount','DocumentReadingMinutes')", c, tx))
            if (await schema.ExecuteScalarAsync(token) is not true) throw new InvalidOperationException();
        var before = await Snapshot(c, tx, token);
        using var json = JsonDocument.Parse(before.Posts); var rows = json.RootElement.EnumerateArray().ToDictionary(r => r.GetProperty("Id").GetInt32());
        var ids = prepared.Select(r => r.Input.Id).ToHashSet();
        if (before.PreservedDigest(ids) != plan.PreservedDigest) throw new InvalidOperationException();
        ValidateActive(rows.Values);
        var already = prepared.All(p => rows.TryGetValue(p.Input.Id, out var row) &&
            row.GetProperty("EditVersion").GetInt64() == p.Input.EditVersion + 1 && Matches(row, p));
        if (already && !rows.Values.Any(r => r.GetProperty("IsActive").GetBoolean() && Legacy(r)) && (prepared.Length > 0 || before.Digest == plan.SourceDigest))
            return new { status = "already_applied", changed = 0, activeKnownDocuments = CountKnown(rows.Values), noWrites = true };
        if (before.Digest != plan.SourceDigest) throw new InvalidOperationException();
        var legacy = rows.Values.Where(r => r.GetProperty("IsActive").GetBoolean() && Legacy(r)).Select(r => r.GetProperty("Id").GetInt32()).ToHashSet();
        if (!legacy.SetEquals(ids)) throw new InvalidOperationException();
        foreach (var p in prepared)
        {
            var row = rows[p.Input.Id];
            if (!Legacy(row) || row.GetProperty("EditVersion").GetInt64() != p.Input.EditVersion ||
                TransitionHash.Of(row.GetRawText()) != p.Input.SourceHash || TransitionHash.Preserved(row) != p.Input.PreservedHash)
                throw new InvalidOperationException();
        }
        if (!apply) return new { status = "ready", changed = 0, candidates = prepared.Length, noWrites = true };
        var now = DateTime.UtcNow;
        foreach (var p in prepared)
        {
            await using var command = new NpgsqlCommand("""
                UPDATE public."Posts" SET "DocumentVersion"=@version,"DocumentJson"=@json,"DocumentPlainText"=@plain,
                  "DocumentWordCount"=@words,"DocumentReadingMinutes"=@minutes,"EditVersion"="EditVersion"+1,"UpdatedDate"=@now
                WHERE "Id"=@id AND "EditVersion"=@revision
                """, c, tx);
            command.Parameters.AddWithValue("version", p.Document.Version); command.Parameters.AddWithValue("json", p.Document.Json);
            command.Parameters.AddWithValue("plain", p.Document.PlainText); command.Parameters.AddWithValue("words", p.Document.WordCount);
            command.Parameters.AddWithValue("minutes", p.Document.ReadingMinutes); command.Parameters.AddWithValue("now", now);
            command.Parameters.AddWithValue("id", p.Input.Id); command.Parameters.AddWithValue("revision", p.Input.EditVersion);
            if (await command.ExecuteNonQueryAsync(token) != 1) throw new InvalidOperationException();
        }
        var after = await Snapshot(c, tx, token);
        if (after.PreservedDigest(ids) != plan.PreservedDigest) throw new InvalidOperationException();
        using var final = JsonDocument.Parse(after.Posts); ValidateActive(final.RootElement.EnumerateArray());
        if (final.RootElement.EnumerateArray().Any(r => r.GetProperty("IsActive").GetBoolean() && Legacy(r))) throw new InvalidOperationException();
        await tx.CommitAsync(token);
        return new { status = "applied", changed = prepared.Length, activeKnownDocuments = CountKnown(final.RootElement.EnumerateArray()), noWrites = false, afterDigest = after.Digest };
    }
    private static readonly string[] Facts = ["DocumentVersion", "DocumentJson", "DocumentPlainText", "DocumentWordCount", "DocumentReadingMinutes"];
    internal static bool Legacy(JsonElement row) => Facts.All(f => row.TryGetProperty(f, out var value) && value.ValueKind == JsonValueKind.Null);
    private static bool Matches(JsonElement row, PreparedRow p) => row.GetProperty("DocumentVersion").GetInt32() == p.Document.Version &&
        row.GetProperty("DocumentJson").GetString() == p.Document.Json && row.GetProperty("DocumentPlainText").GetString() == p.Document.PlainText &&
        row.GetProperty("DocumentWordCount").GetInt32() == p.Document.WordCount && row.GetProperty("DocumentReadingMinutes").GetInt32() == p.Document.ReadingMinutes;
    private static int CountKnown(IEnumerable<JsonElement> rows) => rows.Count(r => r.GetProperty("IsActive").GetBoolean() && !Legacy(r));
    private static void ValidateActive(IEnumerable<JsonElement> rows)
    {
        var validator = new ContentDocumentValidator(); var producer = new DocumentTextProducer();
        foreach (var row in rows.Where(r => r.GetProperty("IsActive").GetBoolean()))
        {
            if (Legacy(row)) continue;
            if (!Facts.All(f => row.TryGetProperty(f, out var value) && value.ValueKind != JsonValueKind.Null) || row.GetProperty("DocumentVersion").GetInt32() != 1)
                throw new InvalidOperationException();
            var source = row.GetProperty("DocumentJson").GetString() ?? throw new InvalidOperationException();
            var validated = validator.Validate(source).Document ?? throw new InvalidOperationException(); var reading = producer.Produce(validated);
            if (row.GetProperty("DocumentPlainText").GetString() != reading.PlainText || row.GetProperty("DocumentWordCount").GetInt32() != reading.WordCount || row.GetProperty("DocumentReadingMinutes").GetInt32() != reading.ReadingMinutes)
                throw new InvalidOperationException();
        }
    }
    private static async Task Command(NpgsqlConnection c, NpgsqlTransaction tx, string sql, CancellationToken token)
    { await using var command = new NpgsqlCommand(sql, c, tx); await command.ExecuteNonQueryAsync(token); }
    private async Task<TransitionSnapshot> Snapshot(NpgsqlConnection c, NpgsqlTransaction tx, CancellationToken token)
    {
        async Task<string> Read(string sql)
        {
            await using var command = new NpgsqlCommand(sql, c, tx); var result = await command.ExecuteScalarAsync(token) as string;
            if (result is null || result.Length > 16_777_216) throw new InvalidOperationException(); return result;
        }
        await using (var limit = new NpgsqlCommand("SELECT count(*)<=100 AND coalesce(sum(octet_length(\"Content\")),0)<=8388608 FROM public.\"Posts\"", c, tx))
            if (await limit.ExecuteScalarAsync(token) is not true) throw new InvalidOperationException();
        return new(Fingerprint(Settings(connectionString)),
            await Read("SELECT coalesce(jsonb_agg(to_jsonb(p) ORDER BY \"Id\"),'[]')::text FROM public.\"Posts\" p"),
            await Read("SELECT coalesce(jsonb_agg(to_jsonb(c) ORDER BY \"Id\"),'[]')::text FROM public.\"Categories\" c"),
            await Read("SELECT coalesce(jsonb_agg(to_jsonb(r) ORDER BY to_jsonb(r)->>'ContractVersion',\"Key\"),'[]')::text FROM public.\"WebhookReceipts\" r"),
            await Read("SELECT coalesce(jsonb_agg(to_jsonb(m) ORDER BY \"MigrationId\"),'[]')::text FROM public.\"__EFMigrationsHistory\" m"),
            await Read("SELECT coalesce(jsonb_agg(to_jsonb(s)-'sequenceowner' ORDER BY sequencename),'[]')::text FROM pg_sequences s WHERE schemaname='public'"));
    }
}
