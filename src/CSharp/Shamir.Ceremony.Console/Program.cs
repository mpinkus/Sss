using System.Security;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Shamir.Ceremony.Common;
using Shamir.Ceremony.Common.Configuration;
using Shamir.Ceremony.Common.Events;

namespace ShamirSecretSharing;

class Program
{
    private static CeremonyManager? _ceremonyManager;
    private static readonly Dictionary<string, TaskCompletionSource<object>> _pendingInputRequests = new();
    private const int MAX_RETRY_ATTEMPTS = 3;

    static async Task Main(string[] args)
    {
        AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
        Console.CancelKeyPress += OnConsoleCancel;

        try
        {
            ValidateConsoleEnvironment();
            await InitializeApplicationAsync();
            await RunApplicationAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\nError: {ex.Message}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"Inner error: {ex.InnerException.Message}");
            }
        }
        finally
        {
            _ceremonyManager?.FinalizeSession();
            Console.WriteLine("\nPress any key to exit...");
            Console.ReadKey();
        }
    }

    static void ValidateConsoleEnvironment()
    {
        if (Console.IsInputRedirected || Console.IsOutputRedirected)
        {
            Console.Error.WriteLine("✗ Error: This application requires an interactive console.");
            Console.Error.WriteLine("  Please run directly in a terminal, not with redirected input/output.");
            Environment.Exit(1);
        }
    }

    static async Task InitializeApplicationAsync()
    {
        var builder = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);

        var configuration = builder.Build();
        var ceremonyConfig = CeremonyConfiguration.FromConfiguration(configuration);

        string configPath = Path.Combine(Directory.GetCurrentDirectory(), "appsettings.json");
        if (File.Exists(configPath))
        {
            Console.WriteLine($"✓ Configuration loaded from: {configPath}");
        }
        else
        {
            Console.WriteLine($"⚠ No configuration file found at: {configPath}");
            Console.WriteLine("  Using default settings.");
        }

        Console.WriteLine("\n=== Active Configuration ===");
        Console.WriteLine($"Min Password Length: {ceremonyConfig.Security.MinPasswordLength}");
        Console.WriteLine($"Password Complexity: Upper={ceremonyConfig.Security.RequireUppercase}, Lower={ceremonyConfig.Security.RequireLowercase}, Digit={ceremonyConfig.Security.RequireDigit}, Special={ceremonyConfig.Security.RequireSpecialCharacter}");
        Console.WriteLine($"KDF Iterations: {ceremonyConfig.Security.KdfIterations:N0}");
        Console.WriteLine($"Secure Delete Passes: {ceremonyConfig.Security.SecureDeletePasses}");
        Console.WriteLine($"Confirmation Required: {ceremonyConfig.Security.ConfirmationRequired}");
        Console.WriteLine($"Audit Log: Enabled={ceremonyConfig.Security.AuditLogEnabled}, Retention={ceremonyConfig.Security.AuditLogRetentionDays} days");

        _ceremonyManager = new CeremonyManager(ceremonyConfig);
        _ceremonyManager.ProgressUpdated += OnProgressUpdated;
        _ceremonyManager.InputRequested += OnInputRequested;
        _ceremonyManager.ValidationResult += OnValidationResult;
        _ceremonyManager.OperationCompleted += OnOperationCompleted;

        Console.WriteLine("\n=== Shamir's Secret Sharing System ===");
        Console.WriteLine("Ceremony Manager Initialized");

        if (ceremonyConfig.DefaultKeepers.Count > 0)
        {
            Console.WriteLine($"✓ Loaded {ceremonyConfig.DefaultKeepers.Count} default keeper(s) from configuration:");
            foreach (var keeper in ceremonyConfig.DefaultKeepers)
            {
                Console.WriteLine($"  - {keeper.Name} ({keeper.Email})");
            }
            Console.WriteLine();
        }
    }

    static async Task RunApplicationAsync()
    {
        bool continueRunning = true;

        while (continueRunning)
        {
            Console.WriteLine("\n=== Main Menu ===");
            Console.WriteLine("Select mode:");
            Console.WriteLine("1. Create new secret shares");
            Console.WriteLine("2. Reconstruct secret from shares");
            Console.WriteLine("3. Exit");

            string choice = GetValidatedInput(
                "\nEnter choice (1, 2, or 3): ",
                input => input == "1" || input == "2" || input == "3",
                "Invalid choice. Please enter 1, 2, or 3.",
                maxLength: 1
            );

            switch (choice)
            {
                case "1":
                    await CreateSharesAsync();
                    break;
                case "2":
                    await ReconstructSecretAsync();
                    break;
                case "3":
                    continueRunning = false;
                    Console.WriteLine("\nExiting application...");
                    break;
            }

            if (continueRunning && choice != "3")
            {
                Console.WriteLine("\nPress any key to return to main menu...");
                Console.ReadKey();
            }
        }
    }

    static async Task CreateSharesAsync()
    {
        Console.WriteLine("\n=== Create Secret Shares ===\n");
        
        if (_ceremonyManager == null)
        {
            Console.WriteLine("✗ Ceremony manager not initialized");
            return;
        }

        var result = await _ceremonyManager.CreateSharesAsync();
        
        if (result.Success)
        {
            Console.WriteLine($"\n✓ Secret shares have been saved to:");
            Console.WriteLine($"  {result.OutputFilePath}");
            Console.WriteLine($"\nIMPORTANT: Distribute each keeper's information securely.");
            Console.WriteLine($"Minimum {result.SharesData?.Configuration.ThresholdRequired} keepers required to reconstruct the secret.");
        }
        else
        {
            Console.WriteLine($"\n✗ {result.Message}");
        }
    }

    static async Task ReconstructSecretAsync()
    {
        Console.WriteLine("\n=== Reconstruct Secret ===\n");
        
        if (_ceremonyManager == null)
        {
            Console.WriteLine("✗ Ceremony manager not initialized");
            return;
        }

        var result = await _ceremonyManager.ReconstructSecretAsync();
        
        if (result.Success && result.ReconstructedSecret != null)
        {
            Console.WriteLine("\n✓ Secret successfully reconstructed and verified!");
            Console.WriteLine($"Secret (hex): {Convert.ToHexString(result.ReconstructedSecret)}");

            try
            {
                string utf8String = System.Text.Encoding.UTF8.GetString(result.ReconstructedSecret);
                if (IsValidUtf8(result.ReconstructedSecret))
                {
                    Console.WriteLine($"Secret (UTF-8): {utf8String}");
                }
            }
            catch
            {
            }
        }
        else
        {
            Console.WriteLine($"\n✗ {result.Message}");
        }
    }

    static void OnProgressUpdated(object? sender, ProgressEventArgs e)
    {
        if (e.PercentComplete.HasValue)
        {
            Console.WriteLine($"[{e.PercentComplete}%] {e.Message}");
        }
        else
        {
            Console.WriteLine($"• {e.Message}");
        }
    }

    static void OnInputRequested(object? sender, InputRequestEventArgs e)
    {
        _pendingInputRequests[e.RequestId] = e.CompletionSource;
        
        try
        {
            object result = e.RequestType switch
            {
                InputRequestType.Text => GetValidatedInput(e.Prompt, e.Validator!, e.ErrorMessage, e.MaxLength),
                InputRequestType.SecureString => ReadSecureString(e.Prompt),
                InputRequestType.Integer => GetValidatedInteger(e.Prompt, e.MinValue, e.MaxValue, e.ErrorMessage),
                InputRequestType.FilePath => GetValidatedFilePath(e.Prompt, e.ExpectedExtension, e.ErrorMessage),
                InputRequestType.YesNo => GetYesNoInput(e.Prompt),
                _ => throw new ArgumentException($"Unsupported input request type: {e.RequestType}")
            };

            e.CompletionSource.SetResult(result);
        }
        catch (Exception ex)
        {
            e.CompletionSource.SetException(ex);
        }
        finally
        {
            _pendingInputRequests.Remove(e.RequestId);
        }
    }

    static void OnValidationResult(object? sender, ValidationEventArgs e)
    {
        string icon = e.IsValid ? "✓" : "✗";
        Console.WriteLine($"{icon} {e.Message}");
    }

    static void OnOperationCompleted(object? sender, CompletionEventArgs e)
    {
        string icon = e.Success ? "✓" : "✗";
        Console.WriteLine($"\n{icon} {e.Message}");
    }

    static string GetValidatedInput(string prompt, Func<string, bool> validator, string errorMessage, int maxLength = 500)
    {
        int attempts = 0;
        while (attempts < MAX_RETRY_ATTEMPTS)
        {
            attempts++;
            Console.Write(prompt);
            string? input = Console.ReadLine()?.Trim();

            if (input != null && input.Length <= maxLength && validator(input))
            {
                return input;
            }

            if (attempts < MAX_RETRY_ATTEMPTS)
            {
                Console.WriteLine($"✗ {errorMessage} (Attempt {attempts}/{MAX_RETRY_ATTEMPTS})");
            }
        }

        throw new InvalidOperationException($"Failed to get valid input after {MAX_RETRY_ATTEMPTS} attempts.");
    }

    static SecureString ReadSecureString(string prompt)
    {
        Console.Write(prompt);
        var secureString = new SecureString();

        try
        {
            ConsoleKeyInfo key;
            do
            {
                key = Console.ReadKey(intercept: true);

                if (key.Key == ConsoleKey.Enter)
                {
                    Console.WriteLine();
                    break;
                }
                else if (key.Key == ConsoleKey.Backspace)
                {
                    if (secureString.Length > 0)
                    {
                        secureString.RemoveAt(secureString.Length - 1);
                        Console.Write("\b \b");
                    }
                }
                else if (key.Key == ConsoleKey.Escape)
                {
                    Console.WriteLine("\n✗ Password entry cancelled.");
                    secureString.Clear();
                    break;
                }
                else if (!char.IsControl(key.KeyChar))
                {
                    secureString.AppendChar(key.KeyChar);
                    Console.Write("*");
                }
            } while (key.Key != ConsoleKey.Enter);
        }
        catch (InvalidOperationException ex)
        {
            throw new InvalidOperationException(
                "This application requires an interactive console. " +
                "Please run it directly in a terminal (cmd, PowerShell, Terminal, etc.) " +
                "rather than in an IDE output window or with redirected input.", ex);
        }

        secureString.MakeReadOnly();
        return secureString;
    }

    static int GetValidatedInteger(string prompt, int min, int max, string errorMessage)
    {
        int attempts = 0;
        while (attempts < MAX_RETRY_ATTEMPTS)
        {
            attempts++;
            Console.Write(prompt);
            string? input = Console.ReadLine()?.Trim();

            if (int.TryParse(input, out int value) && value >= min && value <= max)
            {
                return value;
            }

            if (attempts < MAX_RETRY_ATTEMPTS)
            {
                Console.WriteLine($"✗ {string.Format(errorMessage, min, max)} (Attempt {attempts}/{MAX_RETRY_ATTEMPTS})");
            }
        }

        throw new InvalidOperationException($"Failed to get valid integer after {MAX_RETRY_ATTEMPTS} attempts.");
    }

    static string GetValidatedFilePath(string prompt, string expectedExtension, string errorMessage)
    {
        int attempts = 0;
        while (attempts < MAX_RETRY_ATTEMPTS)
        {
            attempts++;
            Console.Write(prompt);
            string? path = Console.ReadLine()?.Trim();

            if (!string.IsNullOrWhiteSpace(path))
            {
                path = path.Trim('"', '\'');

                if (File.Exists(path) && path.EndsWith(expectedExtension, StringComparison.OrdinalIgnoreCase))
                {
                    return path;
                }

                if (!File.Exists(path))
                {
                    Console.WriteLine($"✗ File not found: {path}");
                }
                else if (!path.EndsWith(expectedExtension, StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine($"✗ File must have {expectedExtension} extension");
                }
            }
            else
            {
                Console.WriteLine("✗ Path cannot be empty");
            }

            if (attempts < MAX_RETRY_ATTEMPTS)
            {
                Console.WriteLine($"{errorMessage} (Attempt {attempts}/{MAX_RETRY_ATTEMPTS})");
            }
        }

        throw new InvalidOperationException($"Failed to get valid file path after {MAX_RETRY_ATTEMPTS} attempts.");
    }

    static bool GetYesNoInput(string prompt)
    {
        return GetValidatedInput(
            $"{prompt} [y/n]: ",
            input => input?.ToLower() == "y" || input?.ToLower() == "n",
            "Please enter 'y' for yes or 'n' for no.",
            maxLength: 1
        ).ToLower() == "y";
    }

    static bool IsValidUtf8(byte[] bytes)
    {
        try
        {
            System.Text.Encoding.UTF8.GetString(bytes);
            return true;
        }
        catch
        {
            return false;
        }
    }

    static void OnProcessExit(object? sender, EventArgs e)
    {
        _ceremonyManager?.FinalizeSession();
    }

    static void OnConsoleCancel(object? sender, ConsoleCancelEventArgs e)
    {
        Console.WriteLine("\n\nReceived cancellation signal. Cleaning up...");
        _ceremonyManager?.FinalizeSession();
        e.Cancel = false;
    }
}
