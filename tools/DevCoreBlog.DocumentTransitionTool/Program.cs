using System.Security.Cryptography;
using System.Text.Json;

// Operator-only process: neither MVC nor automatic migrations or Markdown conversion start here.
try
{
    using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(45)); var token = deadline.Token;
    var connection = Environment.GetEnvironmentVariable("TRANSITION_CONNECTION");
    if (string.IsNullOrWhiteSpace(connection) || args.Length < 2) throw new InvalidOperationException();
    var database = new TransitionDatabase(connection); object report;
    if (args.Length == 2 && args[0] == "export")
    {
        var snapshot = await database.ExportAsync(token); await PrivateFiles.WriteAsync(args[1], snapshot, token);
        report = new { status = "exported", digest = snapshot.Digest, privateFile = true, noWrites = true };
    }
    else if (args.Length == 4 && args[0] == "plan")
    {
        var snapshot = await PrivateFiles.ReadAsync<TransitionSnapshot>(args[1], token);
        var documents = await PrivateFiles.ReadAsync<TransitionDocuments>(args[2], token);
        var plan = ManifestAuthoring.Build(snapshot, documents);
        await PrivateFiles.WriteAsync(args[3], plan, token);
        report = new { status = "manifest_prepared", candidates = plan.Rows.Length, noWrites = true };
    }
    else if (args.Length == 2 && args[0] == "backup")
        report = await TransitionArchive.BackupAsync(connection, args[1], token);
    else if ((args.Length == 2 && args[0] == "dry-run") || (args.Length == 4 && args[0] == "apply"))
    {
        var plan = await PrivateFiles.ReadAsync<TransitionPlan>(args[1], token);
        if (args[0] == "apply")
        {
            var proof = await PrivateFiles.ReadAsync<TransitionBackupProof>(args[3], token);
            await using var archive = File.OpenRead(args[2]);
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(archive, token));
            if (proof.Version != 1 || proof.TargetFingerprint != plan.TargetFingerprint || proof.ArchiveSha256 != hash ||
                proof.SourceDigest != proof.RestoredDigest || proof.SourceDigest != plan.SourceDigest || proof.SourceDigest.Length != 64)
                throw new InvalidOperationException();
        }
        report = await database.RunAsync(plan, args[0] == "apply", token);
    }
    else throw new InvalidOperationException();
    Console.WriteLine(JsonSerializer.Serialize(report, PrivateFiles.Options)); return 0;
}
catch (Exception)
{
    // Input, provider and file exceptions may contain data/credentials; never export their messages.
    Console.WriteLine("{\"status\":\"refused_or_failed\",\"verified\":false}"); return 1;
}
internal sealed record TransitionBackupProof(int Version, string TargetFingerprint, string ArchiveSha256, string SourceDigest, string RestoredDigest);
