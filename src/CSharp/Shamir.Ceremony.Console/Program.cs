using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;

namespace ShamirSecretSharing
{
    class Program
    {
        private static IConfiguration _configuration;
        private static List<AuditLogEntry> _auditLog = new List<AuditLogEntry>();
        private static string _auditLogFile;
        private static SessionInfo _sessionInfo;
        private static string _sessionId;
        private static byte[] _adminSessionKey;
        private static List<DefaultKeeperInfo> _defaultKeepers;
        private static string _sessionOutputFolder;

        // Configuration-based settings with static defaults
        private static class ConfigSettings
        {
            // Security Settings
            public static bool ConfirmationRequired => _configuration?.GetValue<bool>("SecuritySettings:ConfirmationRequired") ?? true;
            public static int MinPasswordLength => _configuration?.GetValue<int>("SecuritySettings:MinPasswordLength") ?? 12;
            public static bool RequireUppercase => _configuration?.GetValue<bool>("SecuritySettings:RequireUppercase") ?? true;
            public static bool RequireLowercase => _configuration?.GetValue<bool>("SecuritySettings:RequireLowercase") ?? true;
            public static bool RequireDigit => _configuration?.GetValue<bool>("SecuritySettings:RequireDigit") ?? true;
            public static bool RequireSpecialCharacter => _configuration?.GetValue<bool>("SecuritySettings:RequireSpecialCharacter") ?? true;
            public static int KdfIterations => _configuration?.GetValue<int>("SecuritySettings:KdfIterations") ?? 100000;
            public static int SecureDeletePasses => _configuration?.GetValue<int>("SecuritySettings:SecureDeletePasses") ?? 3;
            public static bool AuditLogEnabled => _configuration?.GetValue<bool>("SecuritySettings:AuditLogEnabled") ?? true;
            public static int AuditLogRetentionDays => _configuration?.GetValue<int>("SecuritySettings:AuditLogRetentionDays") ?? 90;

            // File System Settings
            public static string OutputFolder => _configuration?.GetValue<string>("FileSystem:OutputFolder") ??
                Path.Combine(Path.GetPathRoot(Directory.GetCurrentDirectory()), "ShamirsSecret");

            // Organization Settings
            public static string OrganizationName => _configuration?.GetValue<string>("Organization:Name");
            public static string OrganizationContactPhone => _configuration?.GetValue<string>("Organization:ContactPhone");
        }

        // Validation constants (still hardcoded as they're not user-configurable)
        private const int MAX_RETRY_ATTEMPTS = 3;
        private const int MIN_THRESHOLD = 2;
        private const int MAX_SHARES = 100;
        private const int MAX_INPUT_LENGTH = 500;

        static void Main(string[] args)
        {
            AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
            Console.CancelKeyPress += OnConsoleCancel;

            try
            {
                // Ensure we're running in an interactive console
                ValidateConsoleEnvironment();

                InitializeSession();
                RunApplication();
            }
            catch (Exception ex)
            {
                LogAudit("ERROR", $"Application error: {ex.Message}");
                Console.WriteLine($"\nError: {ex.Message}");
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"Inner error: {ex.InnerException.Message}");
                }
            }
            finally
            {
                SaveSessionInfo();

                // Keep console open for user to review results
                Console.WriteLine("\nPress any key to exit...");
                Console.ReadKey();
            }
        }

        static void ValidateConsoleEnvironment()
        {
            // Check for redirected input/output
            if (Console.IsInputRedirected || Console.IsOutputRedirected)
            {
                Console.Error.WriteLine("✗ Error: This application requires an interactive console.");
                Console.Error.WriteLine("  Please run directly in a terminal, not with redirected input/output.");
                Environment.Exit(1);
            }

            // Try to detect if we're in an environment that doesn't support ReadKey
            try
            {
                // Test if ReadKey works
                if (Console.KeyAvailable || !Console.KeyAvailable)
                {
                    // If we can check KeyAvailable, we're good
                }
            }
            catch (InvalidOperationException)
            {
                Console.Error.WriteLine("✗ Error: This application requires a fully interactive console.");
                Console.Error.WriteLine("\nPlease run this application in one of these environments:");
                Console.Error.WriteLine("  • Windows: Command Prompt (cmd.exe) or PowerShell");
                Console.Error.WriteLine("  • Windows Terminal");
                Console.Error.WriteLine("  • Linux/Mac: Terminal application");
                Console.Error.WriteLine("\nDo NOT run in:");
                Console.Error.WriteLine("  • Visual Studio Output Window");
                Console.Error.WriteLine("  • Visual Studio Code Output Panel");
                Console.Error.WriteLine("  • PowerShell ISE");
                Console.Error.WriteLine("\nTip: In Visual Studio, use 'Debug > Start Without Debugging' (Ctrl+F5)");
                Console.Error.WriteLine("     or run the compiled .exe directly from the terminal.");
                Environment.Exit(1);
            }
        }

        static void OnProcessExit(object sender, EventArgs e)
        {
            SaveSessionInfo();
        }

        static void OnConsoleCancel(object sender, ConsoleCancelEventArgs e)
        {
            SaveSessionInfo();
            e.Cancel = false;
        }

        static void InitializeSession()
        {
            // Generate session ID
            _sessionId = Guid.NewGuid().ToString();

            var builder = new ConfigurationBuilder();
            builder.Sources.Clear();

            string configPath = Path.Combine(Directory.GetCurrentDirectory(), "appsettings.json");
            if (File.Exists(configPath))
            {
                builder.AddJsonFile(configPath, optional: true, reloadOnChange: true);
                _configuration = builder.Build();
                Console.WriteLine($"✓ Configuration loaded from: {configPath}");
            }
            else
            {
                Console.WriteLine($"⚠ No configuration file found at: {configPath}");
                Console.WriteLine("  Using default settings.");
                _configuration = builder.Build(); // Empty configuration
            }

            // Display active configuration
            Console.WriteLine("\n=== Active Configuration ===");
            Console.WriteLine($"Min Password Length: {ConfigSettings.MinPasswordLength}");
            Console.WriteLine($"Password Complexity: Upper={ConfigSettings.RequireUppercase}, Lower={ConfigSettings.RequireLowercase}, Digit={ConfigSettings.RequireDigit}, Special={ConfigSettings.RequireSpecialCharacter}");
            Console.WriteLine($"KDF Iterations: {ConfigSettings.KdfIterations:N0}");
            Console.WriteLine($"Secure Delete Passes: {ConfigSettings.SecureDeletePasses}");
            Console.WriteLine($"Confirmation Required: {ConfigSettings.ConfirmationRequired}");
            Console.WriteLine($"Audit Log: Enabled={ConfigSettings.AuditLogEnabled}, Retention={ConfigSettings.AuditLogRetentionDays} days");

            // Setup session output folder
            SetupSessionOutputFolder();

            _auditLogFile = Path.Combine(_sessionOutputFolder, $"audit_{DateTime.Now:yyyyMMdd_HHmmss}.log");

            // Clean up old audit logs if retention is configured
            if (ConfigSettings.AuditLogEnabled && ConfigSettings.AuditLogRetentionDays > 0)
            {
                CleanupOldAuditLogs();
            }

            // Load default keepers from configuration
            LoadDefaultKeepers();

            // Initialize session info
            _sessionInfo = new SessionInfo
            {
                SessionId = _sessionId,
                StartTime = DateTime.UtcNow,
                MachineName = Environment.MachineName,
                UserName = Environment.UserName,
                ApplicationVersion = "1.0",
                Events = new List<SessionEvent>(),
                SharesCreated = new List<ShareCreationRecord>(),
                SharesRecovered = new List<ShareRecoveryRecord>(),
                OutputFolder = _sessionOutputFolder
            };

            Console.WriteLine("\n=== Shamir's Secret Sharing System ===");
            Console.WriteLine($"Session ID: {_sessionId}");
            Console.WriteLine($"Started: {_sessionInfo.StartTime:yyyy-MM-dd HH:mm:ss} UTC");
            Console.WriteLine($"Output folder: {_sessionOutputFolder}\n");

            // Display loaded default keepers if any
            if (_defaultKeepers != null && _defaultKeepers.Count > 0)
            {
                Console.WriteLine($"✓ Loaded {_defaultKeepers.Count} default keeper(s) from configuration:");
                foreach (var keeper in _defaultKeepers)
                {
                    Console.WriteLine($"  - {keeper.Name} ({keeper.Email})");
                }
                Console.WriteLine();
            }

            // Prompt for admin session password for provenance
            Console.WriteLine("=== Administrator Session Provenance ===");
            Console.WriteLine("Enter an administrator session password.");
            Console.WriteLine("This will be used to generate an HMAC signature of the session data");
            Console.WriteLine("to provide non-repudiation and proof of administrator oversight.\n");

            SecureString adminPassword = null;
            bool validPassword = false;
            int attempts = 0;

            while (!validPassword && attempts < MAX_RETRY_ATTEMPTS)
            {
                attempts++;
                Console.Write($"Administrator session password (attempt {attempts}/{MAX_RETRY_ATTEMPTS}): ");
                adminPassword = ReadSecureString();

                if (adminPassword.Length < ConfigSettings.MinPasswordLength)
                {
                    Console.WriteLine($"✗ Password must be at least {ConfigSettings.MinPasswordLength} characters. Please try again.");
                    adminPassword?.Dispose();
                    continue;
                }

                validPassword = true;
            }

            if (!validPassword)
            {
                throw new InvalidOperationException("Failed to set administrator session password after maximum attempts.");
            }

            // Derive session key from admin password
            var salt = Encoding.UTF8.GetBytes($"session-{_sessionId}");
            using var pbkdf2 = new Rfc2898DeriveBytes(
                SecureStringToBytes(adminPassword),
                salt,
                ConfigSettings.KdfIterations,
                HashAlgorithmName.SHA256
            );
            _adminSessionKey = pbkdf2.GetBytes(32);
            adminPassword?.Dispose();

            Console.WriteLine("✓ Session integrity key established\n");

            LogAudit("SESSION_INIT", $"Session initialized with ID: {_sessionId}, Output folder: {_sessionOutputFolder}");
            _sessionInfo.Events.Add(new SessionEvent
            {
                Timestamp = DateTime.UtcNow,
                EventType = "SESSION_INITIALIZED",
                Description = $"Session started with administrator provenance. Output folder: {_sessionOutputFolder}"
            });
        }

        static void CleanupOldAuditLogs()
        {
            try
            {
                string baseFolder = ConfigSettings.OutputFolder;
                if (!Directory.Exists(baseFolder))
                    return;

                var cutoffDate = DateTime.Now.AddDays(-ConfigSettings.AuditLogRetentionDays);
                var directories = Directory.GetDirectories(baseFolder);

                foreach (var dir in directories)
                {
                    var auditFiles = Directory.GetFiles(dir, "audit_*.log")
                        .Concat(Directory.GetFiles(dir, "audit_*.json"));

                    foreach (var file in auditFiles)
                    {
                        var fileInfo = new FileInfo(file);
                        if (fileInfo.CreationTime < cutoffDate)
                        {
                            try
                            {
                                SecureDeleteFile(file);
                                Console.WriteLine($"✓ Deleted old audit log: {Path.GetFileName(file)}");
                            }
                            catch
                            {
                                // Continue with other files if one fails
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogAudit("CLEANUP_ERROR", $"Failed to cleanup old audit logs: {ex.Message}");
            }
        }

        static void SetupSessionOutputFolder()
        {
            try
            {
                // Get the output folder from configuration with default
                string baseOutputFolder = ConfigSettings.OutputFolder;

                // Expand environment variables if present (e.g., %USERPROFILE%)
                baseOutputFolder = Environment.ExpandEnvironmentVariables(baseOutputFolder);

                // Create base folder if it doesn't exist
                if (!Directory.Exists(baseOutputFolder))
                {
                    Directory.CreateDirectory(baseOutputFolder);
                    Console.WriteLine($"✓ Created base output folder: {baseOutputFolder}");
                }

                // Create session-specific subfolder using GUID in "N" format (no hyphens)
                string sessionFolderName = _sessionId.Replace("-", "").ToLower();
                _sessionOutputFolder = Path.Combine(baseOutputFolder, sessionFolderName);

                Directory.CreateDirectory(_sessionOutputFolder);

                // Create a session info file in the folder for easy identification
                string sessionInfoFile = Path.Combine(_sessionOutputFolder, "session_info.txt");
                var sessionInfo = new StringBuilder();
                sessionInfo.AppendLine($"Session ID: {_sessionId}");
                sessionInfo.AppendLine($"Created: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                sessionInfo.AppendLine($"Machine: {Environment.MachineName}");
                sessionInfo.AppendLine($"User: {Environment.UserName}");
                File.WriteAllText(sessionInfoFile, sessionInfo.ToString());

                LogAudit("FOLDER_CREATED", $"Session output folder created: {_sessionOutputFolder}");
            }
            catch (Exception ex)
            {
                // Fall back to current directory if folder creation fails
                Console.WriteLine($"⚠ Warning: Could not create output folder: {ex.Message}");
                Console.WriteLine("  Using current directory instead.");
                _sessionOutputFolder = Directory.GetCurrentDirectory();
                LogAudit("FOLDER_ERROR", $"Failed to create output folder: {ex.Message}");
            }
        }

        static void LoadDefaultKeepers()
        {
            try
            {
                var defaultKeepersSection = _configuration?.GetSection("DefaultKeepers");
                if (defaultKeepersSection != null && defaultKeepersSection.Exists())
                {
                    _defaultKeepers = new List<DefaultKeeperInfo>();
                    var keepers = defaultKeepersSection.GetChildren();

                    foreach (var keeperSection in keepers)
                    {
                        var keeper = new DefaultKeeperInfo
                        {
                            Name = keeperSection["Name"],
                            Phone = keeperSection["Phone"],
                            Email = keeperSection["Email"],
                            Department = keeperSection["Department"],
                            Title = keeperSection["Title"],
                            PreferredOrder = keeperSection.GetValue<int?>("PreferredOrder") ?? 999
                        };

                        // Validate the loaded keeper info
                        if (!string.IsNullOrWhiteSpace(keeper.Name) &&
                            IsValidEmail(keeper.Email) &&
                            IsValidPhoneNumber(keeper.Phone))
                        {
                            _defaultKeepers.Add(keeper);
                        }
                        else
                        {
                            LogAudit("CONFIG_WARNING", $"Skipped invalid default keeper: {keeper.Name}");
                        }
                    }

                    // Sort by preferred order
                    _defaultKeepers = _defaultKeepers.OrderBy(k => k.PreferredOrder).ToList();
                }
                else
                {
                    _defaultKeepers = new List<DefaultKeeperInfo>();
                }
            }
            catch (Exception ex)
            {
                LogAudit("CONFIG_ERROR", $"Failed to load default keepers: {ex.Message}");
                _defaultKeepers = new List<DefaultKeeperInfo>();
            }
        }

        static void RunApplication()
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
                        CreateShares();
                        break;
                    case "2":
                        ReconstructSecret();
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

        static void CreateShares()
        {
            LogAudit("CREATE_START", "Secret share creation initiated");
            _sessionInfo.Events.Add(new SessionEvent
            {
                Timestamp = DateTime.UtcNow,
                EventType = "SHARE_CREATION_START",
                Description = "User initiated share creation process"
            });

            Console.WriteLine("\n=== Create Secret Shares ===\n");

            // Gather organization info with validation
            // Check for default organization info first
            string orgName = ConfigSettings.OrganizationName;
            string orgPhone = ConfigSettings.OrganizationContactPhone;

            if (!string.IsNullOrWhiteSpace(orgName))
            {
                Console.WriteLine($"Organization from settings: {orgName}");
                string useDefault = GetValidatedInput(
                    "Use this organization? [y/n]: ",
                    input => input?.ToLower() == "y" || input?.ToLower() == "n",
                    "Please enter 'y' for yes or 'n' for no.",
                    maxLength: 1
                );

                if (useDefault.ToLower() == "n")
                {
                    orgName = GetValidatedInput(
                        "Enter organization name: ",
                        input => !string.IsNullOrWhiteSpace(input) && input.Length <= 100,
                        "Organization name cannot be empty and must be 100 characters or less.",
                        maxLength: 100
                    );

                    orgPhone = GetValidatedInput(
                        "Enter organization contact phone: ",
                        input => IsValidPhoneNumber(input),
                        "Please enter a valid phone number (digits, spaces, +, -, and parentheses allowed).",
                        maxLength: 20
                    );
                }
            }
            else
            {
                orgName = GetValidatedInput(
                    "Enter organization name: ",
                    input => !string.IsNullOrWhiteSpace(input) && input.Length <= 100,
                    "Organization name cannot be empty and must be 100 characters or less.",
                    maxLength: 100
                );

                orgPhone = GetValidatedInput(
                    "Enter organization contact phone: ",
                    input => IsValidPhoneNumber(input),
                    "Please enter a valid phone number (digits, spaces, +, -, and parentheses allowed).",
                    maxLength: 20
                );
            }

            _sessionInfo.Organization = new OrganizationInfo
            {
                Name = orgName,
                ContactPhone = orgPhone
            };

            // Get threshold parameters with validation
            int minKeepers = GetValidatedInteger(
                "Enter minimum number of secret keepers required to reconstruct (threshold): ",
                MIN_THRESHOLD,
                MAX_SHARES,
                "Threshold must be between {0} and {1}."
            );

            int maxKeepers = GetValidatedInteger(
                "Enter total number of secret keepers: ",
                minKeepers,
                MAX_SHARES,
                $"Total keepers must be between {minKeepers} (threshold) and {MAX_SHARES}."
            );

            // Generate or input the master secret
            string generateChoice = GetValidatedInput(
                "Generate random secret (y) or enter your own (n)? [y/n]: ",
                input => input?.ToLower() == "y" || input?.ToLower() == "n",
                "Please enter 'y' for yes or 'n' for no.",
                maxLength: 1
            );

            byte[] masterSecret;
            string secretSource;

            if (generateChoice.ToLower() == "n")
            {
                Console.Write("Enter your secret: ");
                using var secureSecret = ReadSecureString();

                if (secureSecret.Length == 0)
                {
                    Console.WriteLine("✗ Secret cannot be empty. Generating random secret instead.");
                    masterSecret = GenerateRandomSecret(32);
                    secretSource = "System generated (256-bit) - empty input";
                }
                else
                {
                    masterSecret = SecureStringToBytes(secureSecret);
                    secretSource = "User provided";
                }
            }
            else
            {
                masterSecret = GenerateRandomSecret(32); // 256-bit secret
                Console.WriteLine($"Generated secret (hex): {Convert.ToHexString(masterSecret)}");
                secretSource = "System generated (256-bit)";
            }

            // Calculate master secret hash for validation
            string masterSecretHash;
            using (var sha256 = SHA256.Create())
            {
                masterSecretHash = Convert.ToBase64String(sha256.ComputeHash(masterSecret));
            }

            // Generate shares using Shamir's Secret Sharing
            var shares = ShamirSecretShare.GenerateShares(masterSecret, minKeepers, maxKeepers);

            // Collect keeper information and encrypt shares
            var keepers = new List<SecretKeeperRecord>();
            var keeperNames = new List<string>();
            var usedDefaultKeepers = new List<string>();
            int keeperIndex = 0;

            // First, offer to use default keepers if available
            if (_defaultKeepers != null && _defaultKeepers.Count > 0 && maxKeepers > 0)
            {
                Console.WriteLine("\n=== Default Secret Keepers Available ===");

                foreach (var defaultKeeper in _defaultKeepers)
                {
                    if (keeperIndex >= maxKeepers)
                        break;

                    Console.WriteLine($"\nDefault keeper: {defaultKeeper.Name}");
                    if (!string.IsNullOrWhiteSpace(defaultKeeper.Department))
                        Console.WriteLine($"  Department: {defaultKeeper.Department}");
                    if (!string.IsNullOrWhiteSpace(defaultKeeper.Title))
                        Console.WriteLine($"  Title: {defaultKeeper.Title}");
                    Console.WriteLine($"  Phone: {defaultKeeper.Phone}");
                    Console.WriteLine($"  Email: {defaultKeeper.Email}");

                    string useKeeper = GetValidatedInput(
                        "Use this Secret Keeper in this session? [y/n]: ",
                        input => input?.ToLower() == "y" || input?.ToLower() == "n",
                        "Please enter 'y' for yes or 'n' for no.",
                        maxLength: 1
                    );

                    if (useKeeper.ToLower() == "y")
                    {
                        Console.WriteLine($"\n--- Configuring Secret Keeper {keeperIndex + 1} (from defaults) ---");
                        Console.WriteLine($"Name: {defaultKeeper.Name}");
                        Console.WriteLine($"Phone: {defaultKeeper.Phone}");
                        Console.WriteLine($"Email: {defaultKeeper.Email}");

                        SecureString password = null;
                        SecureString confirmPassword = null;
                        bool passwordsMatch = false;

                        do
                        {
                            Console.Write($"Password (min {ConfigSettings.MinPasswordLength} chars");
                            if (ConfigSettings.RequireUppercase || ConfigSettings.RequireLowercase ||
                                ConfigSettings.RequireDigit || ConfigSettings.RequireSpecialCharacter)
                            {
                                Console.Write(", must include");
                                var requirements = new List<string>();
                                if (ConfigSettings.RequireUppercase) requirements.Add("uppercase");
                                if (ConfigSettings.RequireLowercase) requirements.Add("lowercase");
                                if (ConfigSettings.RequireDigit) requirements.Add("number");
                                if (ConfigSettings.RequireSpecialCharacter) requirements.Add("special");
                                Console.Write($" {string.Join(", ", requirements)}");
                            }
                            Console.Write("): ");

                            password = ReadSecureString();

                            if (!ValidatePasswordComplexity(password))
                            {
                                Console.WriteLine($"✗ Password does not meet complexity requirements:");
                                Console.WriteLine($"  - At least {ConfigSettings.MinPasswordLength} characters");
                                if (ConfigSettings.RequireUppercase)
                                    Console.WriteLine("  - At least one uppercase letter");
                                if (ConfigSettings.RequireLowercase)
                                    Console.WriteLine("  - At least one lowercase letter");
                                if (ConfigSettings.RequireDigit)
                                    Console.WriteLine("  - At least one number");
                                if (ConfigSettings.RequireSpecialCharacter)
                                    Console.WriteLine("  - At least one special character");
                                password.Dispose();
                                continue;
                            }

                            Console.Write("Confirm Password: ");
                            confirmPassword = ReadSecureString();

                            passwordsMatch = SecureStringEqual(password, confirmPassword);

                            if (!passwordsMatch)
                            {
                                Console.WriteLine("✗ Passwords don't match. Please try again.");
                                password.Dispose();
                                confirmPassword.Dispose();
                            }
                        } while (!passwordsMatch);

                        // Encrypt the share with the keeper's password
                        var encryptedShare = EncryptShare(shares[keeperIndex], password, out string salt, out string iv);

                        // Dispose SecureStrings
                        password.Dispose();
                        confirmPassword?.Dispose();

                        keepers.Add(new SecretKeeperRecord
                        {
                            Id = Guid.NewGuid().ToString(),
                            ShareNumber = keeperIndex + 1,
                            Name = defaultKeeper.Name,
                            Phone = defaultKeeper.Phone,
                            Email = defaultKeeper.Email,
                            EncryptedShare = encryptedShare.encryptedData,
                            Hmac = encryptedShare.hmac,
                            Salt = salt,
                            IV = iv,
                            CreatedAt = DateTime.UtcNow,
                            SessionId = _sessionId
                        });

                        keeperNames.Add(defaultKeeper.Name);
                        usedDefaultKeepers.Add(defaultKeeper.Name);
                        keeperIndex++;

                        Console.WriteLine($"✓ Keeper {keeperIndex} configured successfully (from defaults)");
                    }
                }

                if (usedDefaultKeepers.Count > 0)
                {
                    Console.WriteLine($"\n✓ Configured {usedDefaultKeepers.Count} keeper(s) from defaults");
                    LogAudit("DEFAULT_KEEPERS_USED", $"Used {usedDefaultKeepers.Count} default keepers: {string.Join(", ", usedDefaultKeepers)}");
                }
            }

            // Now handle remaining keepers that need to be manually entered
            while (keeperIndex < maxKeepers)
            {
                Console.WriteLine($"\n--- Secret Keeper {keeperIndex + 1} ---");

                string name = GetValidatedInput(
                    "Name: ",
                    input => !string.IsNullOrWhiteSpace(input) && input.Length <= 100 && IsValidName(input),
                    "Please enter a valid name (letters, spaces, hyphens, and apostrophes only, max 100 chars).",
                    maxLength: 100
                );
                keeperNames.Add(name);

                string phone = GetValidatedInput(
                    "Phone: ",
                    input => IsValidPhoneNumber(input),
                    "Please enter a valid phone number.",
                    maxLength: 20
                );

                string email = GetValidatedInput(
                    "Email: ",
                    input => IsValidEmail(input),
                    "Please enter a valid email address.",
                    maxLength: 254
                );

                SecureString password = null;
                SecureString confirmPassword = null;
                bool passwordsMatch = false;

                do
                {
                    Console.Write($"Password (min {ConfigSettings.MinPasswordLength} chars");
                    if (ConfigSettings.RequireUppercase || ConfigSettings.RequireLowercase ||
                        ConfigSettings.RequireDigit || ConfigSettings.RequireSpecialCharacter)
                    {
                        Console.Write(", must include");
                        var requirements = new List<string>();
                        if (ConfigSettings.RequireUppercase) requirements.Add("uppercase");
                        if (ConfigSettings.RequireLowercase) requirements.Add("lowercase");
                        if (ConfigSettings.RequireDigit) requirements.Add("number");
                        if (ConfigSettings.RequireSpecialCharacter) requirements.Add("special");
                        Console.Write($" {string.Join(", ", requirements)}");
                    }
                    Console.Write("): ");

                    password = ReadSecureString();

                    if (!ValidatePasswordComplexity(password))
                    {
                        Console.WriteLine($"✗ Password does not meet complexity requirements:");
                        Console.WriteLine($"  - At least {ConfigSettings.MinPasswordLength} characters");
                        if (ConfigSettings.RequireUppercase)
                            Console.WriteLine("  - At least one uppercase letter");
                        if (ConfigSettings.RequireLowercase)
                            Console.WriteLine("  - At least one lowercase letter");
                        if (ConfigSettings.RequireDigit)
                            Console.WriteLine("  - At least one number");
                        if (ConfigSettings.RequireSpecialCharacter)
                            Console.WriteLine("  - At least one special character");
                        password.Dispose();
                        continue;
                    }

                    Console.Write("Confirm Password: ");
                    confirmPassword = ReadSecureString();

                    passwordsMatch = SecureStringEqual(password, confirmPassword);

                    if (!passwordsMatch)
                    {
                        Console.WriteLine("✗ Passwords don't match. Please try again.");
                        password.Dispose();
                        confirmPassword.Dispose();
                    }
                } while (!passwordsMatch);

                // Encrypt the share with the keeper's password
                var encryptedShare = EncryptShare(shares[keeperIndex], password, out string salt, out string iv);

                // Dispose SecureStrings
                password.Dispose();
                confirmPassword?.Dispose();

                keepers.Add(new SecretKeeperRecord
                {
                    Id = Guid.NewGuid().ToString(),
                    ShareNumber = keeperIndex + 1,
                    Name = name,
                    Phone = phone,
                    Email = email,
                    EncryptedShare = encryptedShare.encryptedData,
                    Hmac = encryptedShare.hmac,
                    Salt = salt,
                    IV = iv,
                    CreatedAt = DateTime.UtcNow,
                    SessionId = _sessionId
                });

                keeperIndex++;
                Console.WriteLine($"✓ Keeper {keeperIndex} configured successfully");
            }

            // Create the final output structure
            var output = new ShamirSecretOutput
            {
                Version = "1.0",
                SessionId = _sessionId,
                Organization = new OrganizationInfo
                {
                    Name = orgName,
                    ContactPhone = orgPhone
                },
                Configuration = new ShamirConfiguration
                {
                    TotalShares = maxKeepers,
                    ThresholdRequired = minKeepers,
                    Algorithm = "Shamir-GF256",
                    EncryptionAlgorithm = "AES-256-GCM",
                    KdfAlgorithm = "PBKDF2-SHA256",
                    KdfIterations = ConfigSettings.KdfIterations
                },
                MasterSecretHash = masterSecretHash,
                CreatedAt = DateTime.UtcNow,
                Keepers = keepers
            };

            // Check if confirmation required
            if (ConfigSettings.ConfirmationRequired)
            {
                Console.WriteLine("\n=== Confirmation Test Required ===");
                Console.WriteLine("You must successfully reconstruct the secret to confirm the shares work.");
                Console.WriteLine("This ensures all shares were created correctly.\n");

                bool confirmed = TestReconstruction(output, masterSecret);

                if (!confirmed)
                {
                    Console.WriteLine("\n✗ Reconstruction test failed. Shares were not saved.");
                    LogAudit("CREATE_FAILED", "Reconstruction test failed");
                    _sessionInfo.Events.Add(new SessionEvent
                    {
                        Timestamp = DateTime.UtcNow,
                        EventType = "SHARE_CREATION_FAILED",
                        Description = "Reconstruction test failed - shares not saved"
                    });
                    SecureDelete(masterSecret);
                    return;
                }

                Console.WriteLine("\n✓ Reconstruction test passed!");
            }

            // Save to JSON file
            string json = JsonSerializer.Serialize(output, new JsonSerializerOptions
            {
                WriteIndented = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            });

            string filename = $"secret_shares_{DateTime.Now:yyyyMMdd_HHmmss}.json";
            string fullPath = Path.Combine(_sessionOutputFolder, filename);
            File.WriteAllText(fullPath, json);

            Console.WriteLine($"\n✓ Secret shares have been saved to:");
            Console.WriteLine($"  {fullPath}");
            Console.WriteLine($"\nIMPORTANT: Distribute each keeper's information securely.");
            Console.WriteLine($"Minimum {minKeepers} keepers required to reconstruct the secret.");

            if (usedDefaultKeepers.Count > 0)
            {
                Console.WriteLine($"\nNote: {usedDefaultKeepers.Count} keeper(s) were configured from defaults:");
                foreach (var keeper in usedDefaultKeepers)
                {
                    Console.WriteLine($"  - {keeper}");
                }
            }

            // Record share creation in session
            _sessionInfo.SharesCreated.Add(new ShareCreationRecord
            {
                Timestamp = DateTime.UtcNow,
                OutputFile = fullPath,
                TotalShares = maxKeepers,
                ThresholdRequired = minKeepers,
                SecretSource = secretSource,
                MasterSecretHash = masterSecretHash,
                KeeperNames = keeperNames,
                ConfirmationTestPassed = ConfigSettings.ConfirmationRequired,
                DefaultKeepersUsed = usedDefaultKeepers
            });

            _sessionInfo.Events.Add(new SessionEvent
            {
                Timestamp = DateTime.UtcNow,
                EventType = "SHARE_CREATION_SUCCESS",
                Description = $"Created {maxKeepers} shares with threshold {minKeepers}, saved to {fullPath}. Default keepers used: {usedDefaultKeepers.Count}"
            });

            LogAudit("CREATE_SUCCESS", $"Created {maxKeepers} shares with threshold {minKeepers}, file: {fullPath}");

            // Securely clear sensitive data from memory
            SecureDelete(masterSecret);
        }

        static void ReconstructSecret()
        {
            LogAudit("RECONSTRUCT_START", "Secret reconstruction initiated");
            _sessionInfo.Events.Add(new SessionEvent
            {
                Timestamp = DateTime.UtcNow,
                EventType = "RECONSTRUCTION_START",
                Description = "User initiated secret reconstruction"
            });

            Console.WriteLine("\n=== Reconstruct Secret ===\n");

            // Load the shares file with validation
            string filePath = GetValidatedFilePath(
                "Enter the path to the shares JSON file: ",
                ".json",
                "Please enter a valid path to a JSON file."
            );

            string json = File.ReadAllText(filePath);
            ShamirSecretOutput sharesData = null;

            try
            {
                sharesData = JsonSerializer.Deserialize<ShamirSecretOutput>(json);
                if (sharesData == null)
                {
                    throw new InvalidOperationException("Invalid JSON structure");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"✗ Failed to parse JSON file: {ex.Message}");
                LogAudit("RECONSTRUCT_FAILED", $"Invalid JSON file: {ex.Message}");
                return;
            }

            Console.WriteLine($"\nOrganization: {sharesData.Organization.Name}");
            Console.WriteLine($"Session ID: {sharesData.SessionId ?? "N/A"}");
            Console.WriteLine($"Total shares: {sharesData.Configuration.TotalShares}");
            Console.WriteLine($"Threshold required: {sharesData.Configuration.ThresholdRequired}");
            Console.WriteLine($"Created: {sharesData.CreatedAt:yyyy-MM-dd HH:mm:ss} UTC\n");

            // Record reconstruction attempt
            var recoveryRecord = new ShareRecoveryRecord
            {
                Timestamp = DateTime.UtcNow,
                SourceFile = filePath,
                OriginalSessionId = sharesData.SessionId,
                TotalShares = sharesData.Configuration.TotalShares,
                ThresholdRequired = sharesData.Configuration.ThresholdRequired,
                KeepersUsed = new List<string>(),
                Success = false
            };

            // Collect shares from keepers
            var decryptedShares = new List<Share>();
            var usedKeepers = new HashSet<int>();
            int failedAttempts = 0;
            const int maxFailedAttempts = 10;

            while (decryptedShares.Count < sharesData.Configuration.ThresholdRequired)
            {
                if (failedAttempts >= maxFailedAttempts)
                {
                    Console.WriteLine($"\n✗ Maximum failed attempts ({maxFailedAttempts}) reached. Aborting reconstruction.");
                    LogAudit("RECONSTRUCT_FAILED", "Maximum failed attempts reached");
                    return;
                }

                Console.WriteLine($"\n--- Share {decryptedShares.Count + 1} of {sharesData.Configuration.ThresholdRequired} ---");

                // Show available keepers
                Console.WriteLine("Available keepers:");
                for (int i = 0; i < sharesData.Keepers.Count; i++)
                {
                    if (!usedKeepers.Contains(i))
                    {
                        Console.WriteLine($"  {i + 1}. {sharesData.Keepers[i].Name}");
                    }
                }

                int keeperNum = GetValidatedInteger(
                    "Enter keeper number (or 0 to cancel): ",
                    0,
                    sharesData.Keepers.Count,
                    $"Please enter a number between 0 and {sharesData.Keepers.Count}."
                );

                if (keeperNum == 0)
                {
                    Console.WriteLine("Reconstruction cancelled by user.");
                    return;
                }

                if (usedKeepers.Contains(keeperNum - 1))
                {
                    Console.WriteLine("✗ This keeper has already been used. Please select another.");
                    continue;
                }

                var keeper = sharesData.Keepers[keeperNum - 1];
                Console.WriteLine($"Selected: {keeper.Name}");
                recoveryRecord.KeepersUsed.Add(keeper.Name);

                Console.Write("Enter password for this keeper: ");
                using var password = ReadSecureString();

                try
                {
                    var share = DecryptShare(keeper, password, sharesData.Configuration.KdfIterations);
                    decryptedShares.Add(share);
                    usedKeepers.Add(keeperNum - 1);
                    Console.WriteLine("✓ Share decrypted successfully");
                }
                catch (Exception ex)
                {
                    failedAttempts++;
                    Console.WriteLine($"✗ Failed to decrypt share: {ex.Message}");
                    Console.WriteLine($"Failed attempts: {failedAttempts}/{maxFailedAttempts}");
                    LogAudit("DECRYPT_FAILED", $"Failed to decrypt share for keeper {keeper.Name}");
                    recoveryRecord.KeepersUsed.Remove(keeper.Name);
                }
            }

            // Reconstruct the secret
            Console.WriteLine("\nReconstructing secret...");

            try
            {
                byte[] reconstructedSecret = ShamirSecretShare.ReconstructSecret(
                    decryptedShares,
                    sharesData.Configuration.ThresholdRequired
                );

                // Verify the hash
                using var sha256 = SHA256.Create();
                string reconstructedHash = Convert.ToBase64String(sha256.ComputeHash(reconstructedSecret));

                if (reconstructedHash == sharesData.MasterSecretHash)
                {
                    Console.WriteLine("\n✓ Secret successfully reconstructed and verified!");
                    Console.WriteLine($"Secret (hex): {Convert.ToHexString(reconstructedSecret)}");

                    // Only show UTF-8 if it's valid text
                    try
                    {
                        string utf8String = Encoding.UTF8.GetString(reconstructedSecret);
                        if (IsValidUtf8(reconstructedSecret))
                        {
                            Console.WriteLine($"Secret (UTF-8): {utf8String}");
                        }
                    }
                    catch
                    {
                        // Not valid UTF-8, skip displaying as text
                    }

                    recoveryRecord.Success = true;
                    recoveryRecord.RecoveredSecretHash = reconstructedHash;

                    LogAudit("RECONSTRUCT_SUCCESS", "Secret successfully reconstructed");
                    _sessionInfo.Events.Add(new SessionEvent
                    {
                        Timestamp = DateTime.UtcNow,
                        EventType = "RECONSTRUCTION_SUCCESS",
                        Description = $"Successfully reconstructed secret from {sharesData.Configuration.ThresholdRequired} shares"
                    });
                }
                else
                {
                    Console.WriteLine("\n✗ Warning: Reconstructed secret hash doesn't match original!");
                    LogAudit("RECONSTRUCT_FAILED", "Hash mismatch");
                    _sessionInfo.Events.Add(new SessionEvent
                    {
                        Timestamp = DateTime.UtcNow,
                        EventType = "RECONSTRUCTION_FAILED",
                        Description = "Hash mismatch - possible data corruption"
                    });
                }

                // Securely clear the reconstructed secret
                SecureDelete(reconstructedSecret);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n✗ Failed to reconstruct secret: {ex.Message}");
                LogAudit("RECONSTRUCT_FAILED", ex.Message);
                _sessionInfo.Events.Add(new SessionEvent
                {
                    Timestamp = DateTime.UtcNow,
                    EventType = "RECONSTRUCTION_FAILED",
                    Description = ex.Message
                });
            }

            _sessionInfo.SharesRecovered.Add(recoveryRecord);
        }

        // Input validation helper methods
        static string GetValidatedInput(string prompt, Func<string, bool> validator, string errorMessage, int maxLength = MAX_INPUT_LENGTH)
        {
            int attempts = 0;
            while (attempts < MAX_RETRY_ATTEMPTS)
            {
                attempts++;
                Console.Write(prompt);
                string input = Console.ReadLine()?.Trim();

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

        static int GetValidatedInteger(string prompt, int min, int max, string errorMessage)
        {
            int attempts = 0;
            while (attempts < MAX_RETRY_ATTEMPTS)
            {
                attempts++;
                Console.Write(prompt);
                string input = Console.ReadLine()?.Trim();

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
                string path = Console.ReadLine()?.Trim();

                if (!string.IsNullOrWhiteSpace(path))
                {
                    // Remove quotes if present
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

        static bool IsValidEmail(string email)
        {
            if (string.IsNullOrWhiteSpace(email) || email.Length > 254)
                return false;

            try
            {
                // Basic email validation pattern
                var pattern = @"^[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}$";
                return Regex.IsMatch(email, pattern);
            }
            catch
            {
                return false;
            }
        }

        static bool IsValidPhoneNumber(string phone)
        {
            if (string.IsNullOrWhiteSpace(phone) || phone.Length > 20)
                return false;

            // Allow digits, spaces, +, -, parentheses
            var pattern = @"^[\d\s\+\-\(\)]+$";
            return Regex.IsMatch(phone, pattern) && Regex.IsMatch(phone, @"\d{3,}");
        }

        static bool IsValidName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return false;

            // Allow letters, spaces, hyphens, apostrophes
            var pattern = @"^[a-zA-Z\s\-']+$";
            return Regex.IsMatch(name, pattern);
        }

        static bool IsValidUtf8(byte[] bytes)
        {
            try
            {
                Encoding.UTF8.GetString(bytes);
                return true;
            }
            catch
            {
                return false;
            }
        }

        static bool TestReconstruction(ShamirSecretOutput sharesData, byte[] originalSecret)
        {
            Console.WriteLine("Please enter passwords for threshold number of shares to test reconstruction.");

            var testShares = new List<Share>();
            int maxTestAttempts = 3;

            for (int i = 0; i < sharesData.Configuration.ThresholdRequired; i++)
            {
                int attempts = 0;
                bool shareDecrypted = false;

                while (!shareDecrypted && attempts < maxTestAttempts)
                {
                    attempts++;
                    Console.WriteLine($"\nTest share {i + 1} of {sharesData.Configuration.ThresholdRequired}");
                    Console.WriteLine($"Keeper: {sharesData.Keepers[i].Name}");
                    Console.Write($"Enter password (attempt {attempts}/{maxTestAttempts}): ");

                    using var password = ReadSecureString();

                    try
                    {
                        var share = DecryptShare(sharesData.Keepers[i], password, sharesData.Configuration.KdfIterations);
                        testShares.Add(share);
                        Console.WriteLine("✓ Share verified");
                        shareDecrypted = true;
                    }
                    catch
                    {
                        if (attempts < maxTestAttempts)
                        {
                            Console.WriteLine($"✗ Failed to decrypt share. Please try again.");
                        }
                        else
                        {
                            Console.WriteLine($"✗ Failed to decrypt share after {maxTestAttempts} attempts.");
                            return false;
                        }
                    }
                }
            }

            try
            {
                byte[] reconstructed = ShamirSecretShare.ReconstructSecret(
                    testShares,
                    sharesData.Configuration.ThresholdRequired
                );

                bool matches = reconstructed.SequenceEqual(originalSecret);
                SecureDelete(reconstructed);
                return matches;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"✗ Reconstruction test failed: {ex.Message}");
                return false;
            }
        }

        static void SaveSessionInfo()
        {
            if (_sessionInfo == null) return;

            _sessionInfo.EndTime = DateTime.UtcNow;
            _sessionInfo.Duration = _sessionInfo.EndTime - _sessionInfo.StartTime;

            // Create session summary
            _sessionInfo.Summary = new SessionSummary
            {
                TotalSharesCreated = _sessionInfo.SharesCreated.Sum(s => s.TotalShares),
                TotalShareSets = _sessionInfo.SharesCreated.Count,
                TotalRecoveryAttempts = _sessionInfo.SharesRecovered.Count,
                SuccessfulRecoveries = _sessionInfo.SharesRecovered.Count(r => r.Success),
                FailedRecoveries = _sessionInfo.SharesRecovered.Count(r => !r.Success),
                TotalEvents = _sessionInfo.Events.Count
            };

            // Add final event
            _sessionInfo.Events.Add(new SessionEvent
            {
                Timestamp = DateTime.UtcNow,
                EventType = "SESSION_END",
                Description = $"Session completed after {_sessionInfo.Duration.TotalMinutes:F2} minutes"
            });

            // Serialize session info
            var sessionJson = JsonSerializer.Serialize(_sessionInfo, new JsonSerializerOptions
            {
                WriteIndented = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            });

            // Calculate HMAC of session data using admin key
            string sessionHmac = "";
            if (_adminSessionKey != null)
            {
                using var hmac = new HMACSHA256(_adminSessionKey);
                var sessionBytes = Encoding.UTF8.GetBytes(sessionJson);
                var hmacBytes = hmac.ComputeHash(sessionBytes);
                sessionHmac = Convert.ToBase64String(hmacBytes);
            }

            // Create final session output with HMAC
            var sessionOutput = new SessionOutput
            {
                SessionData = _sessionInfo,
                SessionDataHash = CalculateSha256Hash(sessionJson),
                AdminSessionHmac = sessionHmac,
                HmacAlgorithm = "HMAC-SHA256",
                SignatureTimestamp = DateTime.UtcNow,
                SignatureNote = "This HMAC signature provides non-repudiation and proves administrator oversight of this session. Verify with the administrator's session password."
            };

            // Save to file
            var finalJson = JsonSerializer.Serialize(sessionOutput, new JsonSerializerOptions
            {
                WriteIndented = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            });

            string sessionFilename = $"session_complete_{DateTime.Now:yyyyMMdd_HHmmss}.json";
            string fullSessionPath = Path.Combine(_sessionOutputFolder, sessionFilename);

            try
            {
                File.WriteAllText(fullSessionPath, finalJson);
                Console.WriteLine($"\n=== Session Complete ===");
                Console.WriteLine($"Session ID: {_sessionId}");
                Console.WriteLine($"Duration: {_sessionInfo.Duration.TotalMinutes:F2} minutes");
                Console.WriteLine($"Session folder: {_sessionOutputFolder}");
                Console.WriteLine($"Session file: {sessionFilename}");

                if (!string.IsNullOrEmpty(sessionHmac))
                {
                    Console.WriteLine($"Session HMAC: {sessionHmac.Substring(0, Math.Min(20, sessionHmac.Length))}...");
                    Console.WriteLine("\nThe session file contains a cryptographic signature that proves");
                    Console.WriteLine("administrator oversight and ensures the session data has not been altered.");
                }

                // Create a summary README file in the session folder
                CreateSessionReadme();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"✗ Failed to save session file: {ex.Message}");
                LogAudit("SESSION_SAVE_FAILED", ex.Message);
            }

            // Clear sensitive data
            if (_adminSessionKey != null)
            {
                SecureDelete(_adminSessionKey);
            }

            // Save audit log as well
            SaveAuditLog();
        }

        static void CreateSessionReadme()
        {
            try
            {
                var readme = new StringBuilder();
                readme.AppendLine("SHAMIR'S SECRET SHARING SESSION");
                readme.AppendLine("================================");
                readme.AppendLine();
                readme.AppendLine($"Session ID: {_sessionId}");
                readme.AppendLine($"Date: {_sessionInfo.StartTime:yyyy-MM-dd}");
                readme.AppendLine($"Duration: {_sessionInfo.Duration.TotalMinutes:F2} minutes");
                readme.AppendLine($"Machine: {_sessionInfo.MachineName}");
                readme.AppendLine($"User: {_sessionInfo.UserName}");
                readme.AppendLine();

                if (_sessionInfo.Organization != null)
                {
                    readme.AppendLine("ORGANIZATION");
                    readme.AppendLine("------------");
                    readme.AppendLine($"Name: {_sessionInfo.Organization.Name}");
                    readme.AppendLine($"Contact: {_sessionInfo.Organization.ContactPhone}");
                    readme.AppendLine();
                }

                readme.AppendLine("SESSION SUMMARY");
                readme.AppendLine("---------------");
                readme.AppendLine($"Share Sets Created: {_sessionInfo.Summary.TotalShareSets}");
                readme.AppendLine($"Total Shares: {_sessionInfo.Summary.TotalSharesCreated}");
                readme.AppendLine($"Recovery Attempts: {_sessionInfo.Summary.TotalRecoveryAttempts}");
                readme.AppendLine($"Successful Recoveries: {_sessionInfo.Summary.SuccessfulRecoveries}");
                readme.AppendLine();

                readme.AppendLine("FILES IN THIS SESSION");
                readme.AppendLine("---------------------");

                var files = Directory.GetFiles(_sessionOutputFolder, "*.*");
                foreach (var file in files.OrderBy(f => f))
                {
                    var fileInfo = new FileInfo(file);
                    readme.AppendLine($"- {fileInfo.Name} ({fileInfo.Length:N0} bytes)");
                }

                readme.AppendLine();
                readme.AppendLine("IMPORTANT NOTES");
                readme.AppendLine("---------------");
                readme.AppendLine("• Keep all session files together for audit purposes");
                readme.AppendLine("• The session_complete_*.json file contains cryptographic proof of the session");
                readme.AppendLine("• Distribute secret_shares_*.json files to appropriate keepers");
                readme.AppendLine("• Store audit logs securely for compliance purposes");

                string readmePath = Path.Combine(_sessionOutputFolder, "README.txt");
                File.WriteAllText(readmePath, readme.ToString());
            }
            catch (Exception ex)
            {
                LogAudit("README_ERROR", $"Failed to create README: {ex.Message}");
            }
        }

        static string CalculateSha256Hash(string input)
        {
            using var sha256 = SHA256.Create();
            var bytes = Encoding.UTF8.GetBytes(input);
            var hash = sha256.ComputeHash(bytes);
            return Convert.ToBase64String(hash);
        }

        static SecureString ReadSecureString()
        {
            var secureString = new SecureString();

            try
            {
                // Always use interactive mode with masking
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
                            // Move cursor back, write space to erase *, move back again
                            Console.Write("\b \b");
                        }
                    }
                    else if (key.Key == ConsoleKey.Escape)
                    {
                        // Allow user to cancel password entry with ESC
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
                // This can happen in some environments like VS output window
                throw new InvalidOperationException(
                    "This application requires an interactive console. " +
                    "Please run it directly in a terminal (cmd, PowerShell, Terminal, etc.) " +
                    "rather than in an IDE output window or with redirected input.", ex);
            }

            secureString.MakeReadOnly();
            return secureString;
        }

        static bool ValidatePasswordComplexity(SecureString password)
        {
            if (password.Length < ConfigSettings.MinPasswordLength) return false;

            string tempPassword = SecureStringToString(password);

            bool hasUpper = !ConfigSettings.RequireUppercase || Regex.IsMatch(tempPassword, @"[A-Z]");
            bool hasLower = !ConfigSettings.RequireLowercase || Regex.IsMatch(tempPassword, @"[a-z]");
            bool hasNumber = !ConfigSettings.RequireDigit || Regex.IsMatch(tempPassword, @"[0-9]");
            bool hasSpecial = !ConfigSettings.RequireSpecialCharacter ||
                              Regex.IsMatch(tempPassword, @"[!@#$%^&*()_+=\[{\]};:<>|./?,-]");

            // Securely clear temporary string
            SecureDelete(Encoding.UTF8.GetBytes(tempPassword));

            return hasUpper && hasLower && hasNumber && hasSpecial;
        }

        static bool SecureStringEqual(SecureString ss1, SecureString ss2)
        {
            if (ss1.Length != ss2.Length) return false;

            string s1 = SecureStringToString(ss1);
            string s2 = SecureStringToString(ss2);
            bool equal = s1 == s2;

            // Securely clear temporary strings
            SecureDelete(Encoding.UTF8.GetBytes(s1));
            SecureDelete(Encoding.UTF8.GetBytes(s2));

            return equal;
        }

        static string SecureStringToString(SecureString secureString)
        {
            IntPtr ptr = IntPtr.Zero;
            try
            {
                ptr = Marshal.SecureStringToGlobalAllocUnicode(secureString);
                return Marshal.PtrToStringUni(ptr);
            }
            finally
            {
                Marshal.ZeroFreeGlobalAllocUnicode(ptr);
            }
        }

        static byte[] SecureStringToBytes(SecureString secureString)
        {
            string str = SecureStringToString(secureString);
            byte[] bytes = Encoding.UTF8.GetBytes(str);
            SecureDelete(Encoding.UTF8.GetBytes(str));
            return bytes;
        }

        static void SecureDelete(byte[] data)
        {
            if (data == null) return;

            // Overwrite with random data multiple times based on configuration
            using var rng = RandomNumberGenerator.Create();
            for (int i = 0; i < ConfigSettings.SecureDeletePasses; i++)
            {
                rng.GetBytes(data);
            }
            Array.Clear(data, 0, data.Length);
        }

        static void SecureDeleteFile(string filepath)
        {
            if (!File.Exists(filepath)) return;

            try
            {
                var fileInfo = new FileInfo(filepath);
                long fileSize = fileInfo.Length;

                using (var stream = new FileStream(filepath, FileMode.Open, FileAccess.Write, FileShare.None))
                {
                    var buffer = new byte[4096];
                    using var rng = RandomNumberGenerator.Create();

                    // Overwrite with random data based on configuration
                    for (int pass = 0; pass < ConfigSettings.SecureDeletePasses; pass++)
                    {
                        stream.Seek(0, SeekOrigin.Begin);
                        long written = 0;

                        while (written < fileSize)
                        {
                            rng.GetBytes(buffer);
                            int toWrite = (int)Math.Min(buffer.Length, fileSize - written);
                            stream.Write(buffer, 0, toWrite);
                            written += toWrite;
                        }
                        stream.Flush();
                    }
                }

                File.Delete(filepath);
                LogAudit("FILE_DELETED", $"Securely deleted file: {filepath}");
            }
            catch (Exception ex)
            {
                LogAudit("FILE_DELETE_FAILED", $"Failed to securely delete {filepath}: {ex.Message}");
            }
        }

        static byte[] GenerateRandomSecret(int length)
        {
            using var rng = RandomNumberGenerator.Create();
            var bytes = new byte[length];
            rng.GetBytes(bytes);
            return bytes;
        }

        static (string encryptedData, string hmac) EncryptShare(Share share, SecureString password, out string salt, out string iv)
        {
            // Generate salt for key derivation
            var saltBytes = new byte[32];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(saltBytes);
            }
            salt = Convert.ToBase64String(saltBytes);

            // Convert SecureString to bytes
            byte[] passwordBytes = SecureStringToBytes(password);

            // Derive key from password using PBKDF2 with configured iterations
            using var pbkdf2 = new Rfc2898DeriveBytes(passwordBytes, saltBytes, ConfigSettings.KdfIterations, HashAlgorithmName.SHA256);
            var key = pbkdf2.GetBytes(32); // 256-bit key
            var hmacKey = pbkdf2.GetBytes(32); // Separate key for HMAC

            // Generate IV
            var ivBytes = new byte[16];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(ivBytes);
            }
            iv = Convert.ToBase64String(ivBytes);

            // Serialize share
            var shareJson = JsonSerializer.Serialize(share);
            var plaintext = Encoding.UTF8.GetBytes(shareJson);

            // Encrypt using AES-GCM
            using var aesGcm = new AesGcm(key);
            var ciphertext = new byte[plaintext.Length];
            var tag = new byte[16];
            aesGcm.Encrypt(ivBytes, plaintext, ciphertext, tag);

            // Combine ciphertext and tag
            var combined = new byte[ciphertext.Length + tag.Length];
            Buffer.BlockCopy(ciphertext, 0, combined, 0, ciphertext.Length);
            Buffer.BlockCopy(tag, 0, combined, ciphertext.Length, tag.Length);

            // Calculate HMAC
            using var hmac = new HMACSHA256(hmacKey);
            var hmacValue = hmac.ComputeHash(combined);

            // Securely clear sensitive data
            SecureDelete(passwordBytes);
            SecureDelete(key);
            SecureDelete(hmacKey);
            SecureDelete(plaintext);

            return (Convert.ToBase64String(combined), Convert.ToBase64String(hmacValue));
        }

        static Share DecryptShare(SecretKeeperRecord keeper, SecureString password, int kdfIterations)
        {
            var saltBytes = Convert.FromBase64String(keeper.Salt);
            var ivBytes = Convert.FromBase64String(keeper.IV);
            var combined = Convert.FromBase64String(keeper.EncryptedShare);
            var storedHmac = Convert.FromBase64String(keeper.Hmac);

            // Convert SecureString to bytes
            byte[] passwordBytes = SecureStringToBytes(password);

            // Derive keys using the KDF iterations from the file (or default)
            using var pbkdf2 = new Rfc2898DeriveBytes(passwordBytes, saltBytes, kdfIterations, HashAlgorithmName.SHA256);
            var key = pbkdf2.GetBytes(32);
            var hmacKey = pbkdf2.GetBytes(32);

            // Verify HMAC
            using var hmac = new HMACSHA256(hmacKey);
            var computedHmac = hmac.ComputeHash(combined);

            if (!computedHmac.SequenceEqual(storedHmac))
            {
                SecureDelete(passwordBytes);
                SecureDelete(key);
                SecureDelete(hmacKey);
                throw new CryptographicException("HMAC verification failed");
            }

            // Extract ciphertext and tag
            var ciphertext = new byte[combined.Length - 16];
            var tag = new byte[16];
            Buffer.BlockCopy(combined, 0, ciphertext, 0, ciphertext.Length);
            Buffer.BlockCopy(combined, ciphertext.Length, tag, 0, 16);

            // Decrypt
            using var aesGcm = new AesGcm(key);
            var plaintext = new byte[ciphertext.Length];
            aesGcm.Decrypt(ivBytes, ciphertext, tag, plaintext);

            // Deserialize share
            var shareJson = Encoding.UTF8.GetString(plaintext);
            var share = JsonSerializer.Deserialize<Share>(shareJson);

            // Securely clear sensitive data
            SecureDelete(passwordBytes);
            SecureDelete(key);
            SecureDelete(hmacKey);
            SecureDelete(plaintext);

            return share;
        }

        static void LogAudit(string eventType, string message)
        {
            if (!ConfigSettings.AuditLogEnabled)
                return;

            var entry = new AuditLogEntry
            {
                Timestamp = DateTime.UtcNow,
                SessionId = _sessionId,
                EventType = eventType,
                Message = message,
                User = Environment.UserName,
                Machine = Environment.MachineName
            };

            _auditLog.Add(entry);

            // Also write immediately to file
            string logLine = $"{entry.Timestamp:yyyy-MM-dd HH:mm:ss.fff} | {entry.SessionId} | {entry.EventType} | {entry.User}@{entry.Machine} | {entry.Message}";
            try
            {
                File.AppendAllText(_auditLogFile, logLine + Environment.NewLine);
            }
            catch
            {
                // Fail silently if can't write to audit log
            }
        }

        static void SaveAuditLog()
        {
            if (!ConfigSettings.AuditLogEnabled || _auditLog.Count == 0)
                return;

            try
            {
                var json = JsonSerializer.Serialize(_auditLog, new JsonSerializerOptions { WriteIndented = true });
                string filename = $"audit_detail_{DateTime.Now:yyyyMMdd_HHmmss}.json";
                string fullPath = Path.Combine(_sessionOutputFolder, filename);
                File.WriteAllText(fullPath, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"✗ Failed to save audit log: {ex.Message}");
            }
        }
    }

    // Shamir's Secret Sharing implementation
    public static class ShamirSecretShare
    {
        private const int FieldSize = 256; // GF(2^8)

        public static List<Share> GenerateShares(byte[] secret, int threshold, int numShares)
        {
            var shares = new List<Share>();
            var coefficients = new byte[threshold][];

            // Initialize coefficients with secret as constant term
            coefficients[0] = secret;

            // Generate random coefficients for polynomial
            using var rng = RandomNumberGenerator.Create();
            for (int i = 1; i < threshold; i++)
            {
                coefficients[i] = new byte[secret.Length];
                rng.GetBytes(coefficients[i]);
            }

            // Generate shares by evaluating polynomial at x = 1, 2, ..., numShares
            for (int x = 1; x <= numShares; x++)
            {
                var y = new byte[secret.Length];

                for (int byteIndex = 0; byteIndex < secret.Length; byteIndex++)
                {
                    byte result = coefficients[0][byteIndex];
                    byte xPower = (byte)x;

                    for (int degree = 1; degree < threshold; degree++)
                    {
                        result = GF256Add(result, GF256Multiply(coefficients[degree][byteIndex], xPower));
                        xPower = GF256Multiply(xPower, (byte)x);
                    }

                    y[byteIndex] = result;
                }

                shares.Add(new Share { X = x, Y = Convert.ToBase64String(y) });
            }

            return shares;
        }

        public static byte[] ReconstructSecret(List<Share> shares, int threshold)
        {
            if (shares.Count < threshold)
                throw new ArgumentException($"Need at least {threshold} shares to reconstruct");

            // Get the length of the secret from the first share
            var firstY = Convert.FromBase64String(shares[0].Y);
            var result = new byte[firstY.Length];

            // Reconstruct each byte of the secret
            for (int byteIndex = 0; byteIndex < firstY.Length; byteIndex++)
            {
                // Use Lagrange interpolation
                byte reconstructedByte = 0;

                for (int i = 0; i < threshold; i++)
                {
                    byte yi = Convert.FromBase64String(shares[i].Y)[byteIndex];
                    byte numerator = 1;
                    byte denominator = 1;

                    for (int j = 0; j < threshold; j++)
                    {
                        if (i != j)
                        {
                            numerator = GF256Multiply(numerator, (byte)shares[j].X);
                            byte diff = GF256Add((byte)shares[i].X, (byte)shares[j].X);
                            denominator = GF256Multiply(denominator, diff);
                        }
                    }

                    byte lagrangeCoeff = GF256Divide(numerator, denominator);
                    reconstructedByte = GF256Add(reconstructedByte, GF256Multiply(yi, lagrangeCoeff));
                }

                result[byteIndex] = reconstructedByte;
            }

            return result;
        }

        private static byte GF256Add(byte a, byte b) => (byte)(a ^ b);

        private static byte GF256Multiply(byte a, byte b)
        {
            if (a == 0 || b == 0) return 0;

            int result = 0;
            int temp = a;

            while (b != 0)
            {
                if ((b & 1) != 0)
                    result ^= temp;

                temp <<= 1;
                if (temp >= 256)
                    temp ^= 0x11b; // Reduction polynomial for GF(2^8)

                b >>= 1;
            }

            return (byte)result;
        }

        private static byte GF256Divide(byte a, byte b)
        {
            if (b == 0) throw new DivideByZeroException();
            if (a == 0) return 0;

            return GF256Multiply(a, GF256Inverse(b));
        }

        private static byte GF256Inverse(byte a)
        {
            if (a == 0) throw new ArgumentException("Zero has no inverse");

            // Extended Euclidean algorithm for GF(2^8)
            for (byte b = 1; b < 255; b++)
            {
                if (GF256Multiply(a, b) == 1)
                    return b;
            }

            throw new ArgumentException("No inverse found");
        }
    }

    // Data models
    public class DefaultKeeperInfo
    {
        public string Name { get; set; }
        public string Phone { get; set; }
        public string Email { get; set; }
        public string Department { get; set; }
        public string Title { get; set; }
        public int PreferredOrder { get; set; }
    }

    public class Share
    {
        public int X { get; set; }
        public string Y { get; set; }
    }

    public class SecretKeeperRecord
    {
        public string Id { get; set; }
        public int ShareNumber { get; set; }
        public string Name { get; set; }
        public string Phone { get; set; }
        public string Email { get; set; }
        public string EncryptedShare { get; set; }
        public string Hmac { get; set; }
        public string Salt { get; set; }
        public string IV { get; set; }
        public DateTime CreatedAt { get; set; }
        public string SessionId { get; set; }
    }

    public class OrganizationInfo
    {
        public string Name { get; set; }
        public string ContactPhone { get; set; }
    }

    public class ShamirConfiguration
    {
        public int TotalShares { get; set; }
        public int ThresholdRequired { get; set; }
        public string Algorithm { get; set; }
        public string EncryptionAlgorithm { get; set; }
        public string KdfAlgorithm { get; set; }
        public int KdfIterations { get; set; }
    }

    public class ShamirSecretOutput
    {
        public string Version { get; set; }
        public string SessionId { get; set; }
        public OrganizationInfo Organization { get; set; }
        public ShamirConfiguration Configuration { get; set; }
        public string MasterSecretHash { get; set; }
        public DateTime CreatedAt { get; set; }
        public List<SecretKeeperRecord> Keepers { get; set; }
    }

    public class AuditLogEntry
    {
        public DateTime Timestamp { get; set; }
        public string SessionId { get; set; }
        public string EventType { get; set; }
        public string Message { get; set; }
        public string User { get; set; }
        public string Machine { get; set; }
    }

    public class SessionEvent
    {
        public DateTime Timestamp { get; set; }
        public string EventType { get; set; }
        public string Description { get; set; }
    }

    public class ShareCreationRecord
    {
        public DateTime Timestamp { get; set; }
        public string OutputFile { get; set; }
        public int TotalShares { get; set; }
        public int ThresholdRequired { get; set; }
        public string SecretSource { get; set; }
        public string MasterSecretHash { get; set; }
        public List<string> KeeperNames { get; set; }
        public bool ConfirmationTestPassed { get; set; }
        public List<string> DefaultKeepersUsed { get; set; }
    }

    public class ShareRecoveryRecord
    {
        public DateTime Timestamp { get; set; }
        public string SourceFile { get; set; }
        public string OriginalSessionId { get; set; }
        public int TotalShares { get; set; }
        public int ThresholdRequired { get; set; }
        public List<string> KeepersUsed { get; set; }
        public bool Success { get; set; }
        public string RecoveredSecretHash { get; set; }
    }

    public class SessionSummary
    {
        public int TotalSharesCreated { get; set; }
        public int TotalShareSets { get; set; }
        public int TotalRecoveryAttempts { get; set; }
        public int SuccessfulRecoveries { get; set; }
        public int FailedRecoveries { get; set; }
        public int TotalEvents { get; set; }
    }

    public class SessionInfo
    {
        public string SessionId { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public TimeSpan Duration { get; set; }
        public string MachineName { get; set; }
        public string UserName { get; set; }
        public string ApplicationVersion { get; set; }
        public string OutputFolder { get; set; }
        public OrganizationInfo Organization { get; set; }
        public SessionSummary Summary { get; set; }
        public List<SessionEvent> Events { get; set; }
        public List<ShareCreationRecord> SharesCreated { get; set; }
        public List<ShareRecoveryRecord> SharesRecovered { get; set; }
    }

    public class SessionOutput
    {
        public SessionInfo SessionData { get; set; }
        public string SessionDataHash { get; set; }
        public string AdminSessionHmac { get; set; }
        public string HmacAlgorithm { get; set; }
        public DateTime SignatureTimestamp { get; set; }
        public string SignatureNote { get; set; }
    }
}