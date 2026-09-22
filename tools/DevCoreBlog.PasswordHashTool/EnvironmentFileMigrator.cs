using System.Text;
using System.Text.RegularExpressions;
using DevCoreBlog.Services.Security;
using DotNetEnv;

internal static partial class EnvironmentFileMigrator
{
    public static int Migrate(string path, Pbkdf2PasswordHasher hasher)
    {
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            Console.Error.WriteLine("The requested environment file does not exist.");
            return 1;
        }

        var originalContent = File.ReadAllText(fullPath);
        var lines = File.ReadAllLines(fullPath).ToList();
        var passwordIndexes = FindAssignmentIndexes(lines, PlaintextPasswordPattern());
        var hashIndexes = FindAssignmentIndexes(lines, PasswordHashPattern());
        if (passwordIndexes.Count > 1 || hashIndexes.Count > 1)
        {
            Console.Error.WriteLine("Duplicate admin password assignments must be resolved before migration.");
            return 1;
        }

        Environment.SetEnvironmentVariable("ADMIN_PASSWORD", null);
        Environment.SetEnvironmentVariable("ADMIN_PASSWORD_HASH", null);
        Env.Load(fullPath);

        var plaintextPassword = Environment.GetEnvironmentVariable("ADMIN_PASSWORD");
        var existingHash = Environment.GetEnvironmentVariable("ADMIN_PASSWORD_HASH");
        if (passwordIndexes.Count == 0)
        {
            if (hashIndexes.Count == 1 &&
                Pbkdf2PasswordHasher.IsValidEncodedHash(existingHash))
            {
                Console.Out.WriteLine("ADMIN_PASSWORD_HASH is already configured; no file change was needed.");
                return 0;
            }

            Console.Error.WriteLine("ADMIN_PASSWORD was not found; no migration was performed.");
            return 1;
        }

        if (string.IsNullOrWhiteSpace(plaintextPassword))
        {
            Console.Error.WriteLine("ADMIN_PASSWORD is empty or whitespace; no migration was performed.");
            return 1;
        }

        string migratedHash;
        if (!string.IsNullOrWhiteSpace(existingHash))
        {
            if (!Pbkdf2PasswordHasher.IsValidEncodedHash(existingHash) ||
                !hasher.VerifyPassword(plaintextPassword, existingHash))
            {
                Console.Error.WriteLine("Existing ADMIN_PASSWORD_HASH does not match the plaintext password.");
                return 1;
            }

            migratedHash = existingHash;
        }
        else
        {
            migratedHash = hasher.HashPassword(plaintextPassword);
        }

        if (!hasher.VerifyPassword(plaintextPassword, migratedHash))
        {
            Console.Error.WriteLine("Generated hash verification failed; no file change was made.");
            return 1;
        }

        var insertionIndex = hashIndexes.Count == 1
            ? hashIndexes[0]
            : passwordIndexes[0];
        var migratedLines = new List<string>(lines.Count);
        for (var index = 0; index < lines.Count; index++)
        {
            if (index == insertionIndex)
            {
                migratedLines.Add($"ADMIN_PASSWORD_HASH={migratedHash}");
            }

            if (passwordIndexes.Contains(index) || hashIndexes.Contains(index))
            {
                continue;
            }

            migratedLines.Add(lines[index]);
        }

        var newline = originalContent.Contains("\r\n", StringComparison.Ordinal)
            ? "\r\n"
            : "\n";
        var endsWithNewline = originalContent.EndsWith('\n');
        var migratedContent = string.Join(newline, migratedLines) +
            (endsWithNewline ? newline : string.Empty);
        WriteAtomically(fullPath, migratedContent);

        Console.Out.WriteLine("Migrated ADMIN_PASSWORD to ADMIN_PASSWORD_HASH without printing either value.");
        return 0;
    }

    private static List<int> FindAssignmentIndexes(
        IReadOnlyList<string> lines,
        Regex pattern)
    {
        var indexes = new List<int>();
        for (var index = 0; index < lines.Count; index++)
        {
            if (pattern.IsMatch(lines[index]))
            {
                indexes.Add(index);
            }
        }

        return indexes;
    }

    private static void WriteAtomically(string path, string content)
    {
        var directory = Path.GetDirectoryName(path) ?? Directory.GetCurrentDirectory();
        var temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(path)}.f07-{Guid.NewGuid():N}.tmp");

        try
        {
            File.WriteAllText(temporaryPath, content, new UTF8Encoding(false));
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(temporaryPath, File.GetUnixFileMode(path));
            }

            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    [GeneratedRegex(@"^\s*(?:export\s+)?ADMIN_PASSWORD\s*=", RegexOptions.CultureInvariant)]
    private static partial Regex PlaintextPasswordPattern();

    [GeneratedRegex(@"^\s*(?:export\s+)?ADMIN_PASSWORD_HASH\s*=", RegexOptions.CultureInvariant)]
    private static partial Regex PasswordHashPattern();
}
