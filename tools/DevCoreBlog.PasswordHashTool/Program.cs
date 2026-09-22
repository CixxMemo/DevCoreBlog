using System.Text;
using DevCoreBlog.Services.Security;

return PasswordHashTool.Run(args);

internal static class PasswordHashTool
{
    private static readonly Pbkdf2PasswordHasher Hasher = new();

    public static int Run(string[] args)
    {
        try
        {
            return args switch
            {
                [] => GenerateInteractively(),
                ["--stdin"] => GenerateFromStandardInput(),
                ["--self-test"] => PasswordHashDiagnostics.RunSelfTest(Hasher),
                ["--benchmark"] => PasswordHashDiagnostics.RunBenchmark(Hasher),
                ["--migrate-env", var path] => EnvironmentFileMigrator.Migrate(path, Hasher),
                _ => ShowUsageError()
            };
        }
        catch (Exception exception) when (
            exception is ArgumentException or IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Password hash operation failed: {exception.Message}");
            return 1;
        }
    }

    private static int GenerateInteractively()
    {
        if (Console.IsInputRedirected)
        {
            Console.Error.WriteLine("Interactive input is unavailable. Use --stdin for controlled automation.");
            return 2;
        }

        Console.Error.Write("Admin password: ");
        var password = ReadSecret();
        Console.Error.Write("Confirm password: ");
        var confirmation = ReadSecret();
        if (!string.Equals(password, confirmation, StringComparison.Ordinal))
        {
            Console.Error.WriteLine("Passwords do not match.");
            return 1;
        }

        Console.Out.WriteLine(Hasher.HashPassword(password));
        return 0;
    }

    private static int GenerateFromStandardInput()
    {
        var password = Console.In.ReadLine();
        if (password is null)
        {
            Console.Error.WriteLine("No password was received on standard input.");
            return 1;
        }

        Console.Out.WriteLine(Hasher.HashPassword(password));
        return 0;
    }

    private static string ReadSecret()
    {
        var value = new StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                Console.Error.WriteLine();
                return value.ToString();
            }

            if (key.Key == ConsoleKey.Backspace && value.Length > 0)
            {
                value.Length--;
                continue;
            }

            if (!char.IsControl(key.KeyChar))
            {
                value.Append(key.KeyChar);
            }
        }
    }

    private static int ShowUsageError()
    {
        Console.Error.WriteLine(
            "Usage: PasswordHashTool [--stdin | --self-test | --benchmark | --migrate-env <path>]");
        return 2;
    }
}
