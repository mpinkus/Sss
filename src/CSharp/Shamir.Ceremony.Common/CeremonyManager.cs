using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Shamir.Ceremony.Common.Configuration;
using Shamir.Ceremony.Common.Cryptography;
using Shamir.Ceremony.Common.Events;
using Shamir.Ceremony.Common.Models;
using Shamir.Ceremony.Common.Services;

namespace Shamir.Ceremony.Common;

public class CeremonyManager
{
    private readonly CeremonyConfiguration _configuration;
    private readonly CryptographyService _cryptographyService;
    private SessionManager? _sessionManager;
    private AuditLogger? _auditLogger;
    private string _sessionId = string.Empty;
    private string _sessionOutputFolder = string.Empty;

    public event EventHandler<ProgressEventArgs>? ProgressUpdated;
    public event EventHandler<InputRequestEventArgs>? InputRequested;
    public event EventHandler<ValidationEventArgs>? ValidationResult;
    public event EventHandler<CompletionEventArgs>? OperationCompleted;

    public CeremonyManager(CeremonyConfiguration configuration)
    {
        _configuration = configuration;
        _cryptographyService = new CryptographyService(configuration.Security);
    }

    public async Task<CeremonyResult> CreateSharesAsync()
    {
        try
        {
            await InitializeSessionAsync();
            
            _auditLogger?.LogAudit("CREATE_START", "Secret share creation initiated");
            _sessionManager?.AddEvent("SHARE_CREATION_START", "User initiated share creation process");

            OnProgressUpdated("Starting share creation ceremony...", 0, "CREATE_START");

            var orgInfo = await GetOrganizationInfoAsync();
            _sessionManager!.SessionInfo.Organization = orgInfo;

            var (threshold, totalShares) = await GetThresholdParametersAsync();
            var (masterSecret, secretSource) = await GetMasterSecretAsync();

            string masterSecretHash;
            using (var sha256 = SHA256.Create())
            {
                masterSecretHash = Convert.ToBase64String(sha256.ComputeHash(masterSecret));
            }

            OnProgressUpdated("Generating shares using Shamir's Secret Sharing...", 30, "GENERATING_SHARES");
            var shares = ShamirSecretShare.GenerateShares(masterSecret, threshold, totalShares);

            OnProgressUpdated("Collecting keeper information and encrypting shares...", 50, "COLLECTING_KEEPERS");
            var keepers = await CollectKeeperInformationAsync(shares, totalShares);

            var output = new ShamirSecretOutput
            {
                Version = "1.0.0",
                SessionId = _sessionId,
                Organization = orgInfo,
                Configuration = new ShamirConfiguration
                {
                    TotalShares = totalShares,
                    ThresholdRequired = threshold,
                    Algorithm = "Shamir-GF256",
                    EncryptionAlgorithm = "AES-256-GCM",
                    KdfAlgorithm = "PBKDF2-SHA256",
                    KdfIterations = _configuration.Security.KdfIterations
                },
                MasterSecretHash = masterSecretHash,
                CreatedAt = DateTime.UtcNow,
                Keepers = keepers.keepers
            };

            if (_configuration.Security.ConfirmationRequired)
            {
                OnProgressUpdated("Running confirmation test...", 80, "CONFIRMATION_TEST");
                bool confirmed = await TestReconstructionAsync(output, masterSecret);
                if (!confirmed)
                {
                    _cryptographyService.SecureDelete(masterSecret);
                    return new CeremonyResult
                    {
                        Success = false,
                        Message = "Reconstruction test failed. Shares were not saved."
                    };
                }
            }

            OnProgressUpdated("Saving shares to file...", 90, "SAVING_SHARES");
            string outputPath = await SaveSharesAsync(output);

            var creationRecord = new ShareCreationRecord
            {
                Timestamp = DateTime.UtcNow,
                OutputFile = outputPath,
                TotalShares = totalShares,
                ThresholdRequired = threshold,
                SecretSource = secretSource,
                MasterSecretHash = masterSecretHash,
                KeeperNames = keepers.keeperNames,
                ConfirmationTestPassed = _configuration.Security.ConfirmationRequired,
                DefaultKeepersUsed = keepers.usedDefaultKeepers
            };

            _sessionManager.AddShareCreationRecord(creationRecord);
            _sessionManager.AddEvent("SHARE_CREATION_SUCCESS", 
                $"Created {totalShares} shares with threshold {threshold}, saved to {outputPath}. Default keepers used: {keepers.usedDefaultKeepers.Count}");

            _auditLogger?.LogAudit("CREATE_SUCCESS", $"Created {totalShares} shares with threshold {threshold}, file: {outputPath}");

            _cryptographyService.SecureDelete(masterSecret);

            OnProgressUpdated("Share creation completed successfully!", 100, "CREATE_SUCCESS");
            OnOperationCompleted(true, "Share creation completed successfully", "CREATE_SHARES", output);

            return new CeremonyResult
            {
                Success = true,
                Message = $"Successfully created {totalShares} shares with threshold {threshold}",
                OutputFilePath = outputPath,
                SharesData = output
            };
        }
        catch (Exception ex)
        {
            _auditLogger?.LogAudit("CREATE_FAILED", $"Share creation failed: {ex.Message}");
            OnOperationCompleted(false, $"Share creation failed: {ex.Message}", "CREATE_SHARES", null);
            return new CeremonyResult
            {
                Success = false,
                Message = $"Share creation failed: {ex.Message}"
            };
        }
    }

    public async Task<CeremonyResult> ReconstructSecretAsync(string? sharesFilePath = null)
    {
        try
        {
            await InitializeSessionAsync();
            
            _auditLogger?.LogAudit("RECONSTRUCT_START", "Secret reconstruction initiated");
            _sessionManager?.AddEvent("RECONSTRUCTION_START", "User initiated secret reconstruction");

            OnProgressUpdated("Starting secret reconstruction...", 0, "RECONSTRUCT_START");

            if (string.IsNullOrEmpty(sharesFilePath))
            {
                sharesFilePath = await RequestFilePathAsync("Enter the path to the shares JSON file: ", ".json", "Please enter a valid path to a JSON file.");
            }

            OnProgressUpdated("Loading shares file...", 20, "LOADING_SHARES");
            string json = File.ReadAllText(sharesFilePath);
            ShamirSecretOutput sharesData;

            try
            {
                sharesData = JsonSerializer.Deserialize<ShamirSecretOutput>(json) ?? 
                    throw new InvalidOperationException("Invalid JSON structure");
            }
            catch (Exception ex)
            {
                _auditLogger?.LogAudit("RECONSTRUCT_FAILED", $"Invalid JSON file: {ex.Message}");
                return new CeremonyResult
                {
                    Success = false,
                    Message = $"Failed to parse JSON file: {ex.Message}"
                };
            }

            var recoveryRecord = new ShareRecoveryRecord
            {
                Timestamp = DateTime.UtcNow,
                SourceFile = sharesFilePath,
                OriginalSessionId = sharesData.SessionId,
                TotalShares = sharesData.Configuration.TotalShares,
                ThresholdRequired = sharesData.Configuration.ThresholdRequired,
                KeepersUsed = new List<string>(),
                Success = false
            };

            OnProgressUpdated("Collecting shares from keepers...", 40, "COLLECTING_SHARES");
            var decryptedShares = await CollectSharesFromKeepersAsync(sharesData, recoveryRecord);

            OnProgressUpdated("Reconstructing secret...", 80, "RECONSTRUCTING");
            byte[] reconstructedSecret = ShamirSecretShare.ReconstructSecret(decryptedShares, sharesData.Configuration.ThresholdRequired);

            using var sha256 = SHA256.Create();
            string reconstructedHash = Convert.ToBase64String(sha256.ComputeHash(reconstructedSecret));

            if (reconstructedHash == sharesData.MasterSecretHash)
            {
                recoveryRecord.Success = true;
                recoveryRecord.RecoveredSecretHash = reconstructedHash;

                _auditLogger?.LogAudit("RECONSTRUCT_SUCCESS", "Secret successfully reconstructed");
                _sessionManager?.AddEvent("RECONSTRUCTION_SUCCESS", 
                    $"Successfully reconstructed secret from {sharesData.Configuration.ThresholdRequired} shares");

                OnProgressUpdated("Secret reconstruction completed successfully!", 100, "RECONSTRUCT_SUCCESS");
                OnOperationCompleted(true, "Secret successfully reconstructed and verified!", "RECONSTRUCT_SECRET", reconstructedSecret);

                var result = new CeremonyResult
                {
                    Success = true,
                    Message = "Secret successfully reconstructed and verified!",
                    ReconstructedSecret = reconstructedSecret
                };

                _sessionManager?.AddShareRecoveryRecord(recoveryRecord);
                return result;
            }
            else
            {
                _auditLogger?.LogAudit("RECONSTRUCT_FAILED", "Hash mismatch");
                _sessionManager?.AddEvent("RECONSTRUCTION_FAILED", "Hash mismatch - possible data corruption");
                
                _cryptographyService.SecureDelete(reconstructedSecret);
                _sessionManager?.AddShareRecoveryRecord(recoveryRecord);

                return new CeremonyResult
                {
                    Success = false,
                    Message = "Warning: Reconstructed secret hash doesn't match original!"
                };
            }
        }
        catch (Exception ex)
        {
            _auditLogger?.LogAudit("RECONSTRUCT_FAILED", ex.Message);
            _sessionManager?.AddEvent("RECONSTRUCTION_FAILED", ex.Message);
            OnOperationCompleted(false, $"Secret reconstruction failed: {ex.Message}", "RECONSTRUCT_SECRET", null);
            
            return new CeremonyResult
            {
                Success = false,
                Message = $"Failed to reconstruct secret: {ex.Message}"
            };
        }
    }

    public void FinalizeSession()
    {
        _sessionManager?.SaveSessionInfo();
        _auditLogger?.SaveAuditLog(_sessionOutputFolder);
    }

    private async Task InitializeSessionAsync()
    {
        _sessionId = Guid.NewGuid().ToString("N")[..16];
        
        _sessionOutputFolder = Path.Combine(_configuration.FileSystem.OutputFolder, $"session_{_sessionId}");
        Directory.CreateDirectory(_sessionOutputFolder);

        var adminPassword = await RequestSecureStringAsync("Administrator session password: ");
        var adminSessionKey = DeriveAdminSessionKey(adminPassword);

        _sessionManager = new SessionManager(_sessionId, _sessionOutputFolder, adminSessionKey, _cryptographyService);
        _auditLogger = new AuditLogger(_configuration.Security, _sessionId, _sessionOutputFolder);

        OnProgressUpdated($"Session initialized: {_sessionId}", null, "SESSION_INIT");
    }

    private byte[] DeriveAdminSessionKey(SecureString adminPassword)
    {
        var passwordBytes = SecureStringToBytes(adminPassword);
        var salt = Encoding.UTF8.GetBytes("ShamirCeremonyAdminSession");
        
        using var pbkdf2 = new Rfc2898DeriveBytes(passwordBytes, salt, _configuration.Security.KdfIterations, HashAlgorithmName.SHA256);
        var key = pbkdf2.GetBytes(32);
        
        _cryptographyService.SecureDelete(passwordBytes);
        return key;
    }

    private async Task<OrganizationInfo> GetOrganizationInfoAsync()
    {
        string orgName = _configuration.Organization.Name;
        string orgPhone = _configuration.Organization.ContactPhone;

        if (!string.IsNullOrWhiteSpace(orgName))
        {
            bool useDefault = await RequestYesNoAsync($"Organization from settings: {orgName}\nUse this organization?");
            if (!useDefault)
            {
                orgName = await RequestTextAsync("Enter organization name: ", 
                    input => !string.IsNullOrWhiteSpace(input) && input.Length <= 100,
                    "Organization name cannot be empty and must be 100 characters or less.", 100);

                orgPhone = await RequestTextAsync("Enter organization contact phone: ",
                    IsValidPhoneNumber,
                    "Please enter a valid phone number (digits, spaces, +, -, and parentheses allowed).", 20);
            }
        }
        else
        {
            orgName = await RequestTextAsync("Enter organization name: ",
                input => !string.IsNullOrWhiteSpace(input) && input.Length <= 100,
                "Organization name cannot be empty and must be 100 characters or less.", 100);

            orgPhone = await RequestTextAsync("Enter organization contact phone: ",
                IsValidPhoneNumber,
                "Please enter a valid phone number (digits, spaces, +, -, and parentheses allowed).", 20);
        }

        return new OrganizationInfo { Name = orgName, ContactPhone = orgPhone };
    }

    private async Task<(int threshold, int totalShares)> GetThresholdParametersAsync()
    {
        int threshold = await RequestIntegerAsync(
            "Enter minimum number of secret keepers required to reconstruct (threshold): ",
            2, 100, "Threshold must be between {0} and {1}.");

        int totalShares = await RequestIntegerAsync(
            "Enter total number of secret keepers: ",
            threshold, 100, $"Total keepers must be between {threshold} (threshold) and 100.");

        return (threshold, totalShares);
    }

    private async Task<(byte[] masterSecret, string secretSource)> GetMasterSecretAsync()
    {
        bool generateRandom = await RequestYesNoAsync("Generate random secret (y) or enter your own (n)?");

        if (!generateRandom)
        {
            var secureSecret = await RequestSecureStringAsync("Enter your secret: ");
            if (secureSecret.Length == 0)
            {
                OnValidationResult(false, "Secret cannot be empty. Generating random secret instead.", "SECRET_INPUT");
                return (_cryptographyService.GenerateRandomSecret(32), "System generated (256-bit) - empty input");
            }
            else
            {
                return (SecureStringToBytes(secureSecret), "User provided");
            }
        }
        else
        {
            var secret = _cryptographyService.GenerateRandomSecret(32);
            OnProgressUpdated($"Generated secret (hex): {Convert.ToHexString(secret)}", null, "SECRET_GENERATED");
            return (secret, "System generated (256-bit)");
        }
    }

    private async Task<(List<SecretKeeperRecord> keepers, List<string> keeperNames, List<string> usedDefaultKeepers)> CollectKeeperInformationAsync(List<Share> shares, int totalShares)
    {
        var keepers = new List<SecretKeeperRecord>();
        var keeperNames = new List<string>();
        var usedDefaultKeepers = new List<string>();
        int keeperIndex = 0;

        if (_configuration.DefaultKeepers.Count > 0 && totalShares > 0)
        {
            OnProgressUpdated($"Found {_configuration.DefaultKeepers.Count} default keeper(s) from configuration", null, "DEFAULT_KEEPERS_FOUND");

            foreach (var defaultKeeper in _configuration.DefaultKeepers.OrderBy(k => k.PreferredOrder))
            {
                if (keeperIndex >= totalShares) break;

                OnProgressUpdated($"Default keeper: {defaultKeeper.Name} ({defaultKeeper.Email})", null, "DEFAULT_KEEPER_OPTION");
                
                bool useKeeper = await RequestYesNoAsync($"Use {defaultKeeper.Name} as Secret Keeper in this session?");

                if (useKeeper)
                {
                    var password = await RequestSecureStringAsync($"Enter password for {defaultKeeper.Name}: ");
                    
                    var (encryptedData, hmac) = _cryptographyService.EncryptShare(shares[keeperIndex], password, out string salt, out string iv);

                    var keeper = new SecretKeeperRecord
                    {
                        Id = Guid.NewGuid().ToString(),
                        ShareNumber = shares[keeperIndex].X,
                        Name = defaultKeeper.Name,
                        Phone = defaultKeeper.Phone,
                        Email = defaultKeeper.Email,
                        EncryptedShare = encryptedData,
                        Hmac = hmac,
                        Salt = salt,
                        IV = iv,
                        CreatedAt = DateTime.UtcNow,
                        SessionId = _sessionId
                    };

                    keepers.Add(keeper);
                    keeperNames.Add(defaultKeeper.Name);
                    usedDefaultKeepers.Add(defaultKeeper.Name);
                    keeperIndex++;

                    OnValidationResult(true, $"Keeper {defaultKeeper.Name} configured successfully", "KEEPER_CONFIG");
                }
            }
        }

        while (keeperIndex < totalShares)
        {
            OnProgressUpdated($"Configuring Secret Keeper {keeperIndex + 1} of {totalShares}", 
                (int)((double)keeperIndex / totalShares * 100), "CONFIGURING_KEEPER");

            var name = await RequestTextAsync($"Enter name for keeper {keeperIndex + 1}: ",
                IsValidName, "Please enter a valid name (letters, spaces, hyphens, apostrophes only).", 100);

            var phone = await RequestTextAsync($"Enter phone for {name}: ",
                IsValidPhoneNumber, "Please enter a valid phone number.", 20);

            var email = await RequestTextAsync($"Enter email for {name}: ",
                IsValidEmail, "Please enter a valid email address.", 254);

            var password = await RequestSecureStringAsync($"Enter password for {name}: ");

            var (encryptedData, hmac) = _cryptographyService.EncryptShare(shares[keeperIndex], password, out string salt, out string iv);

            var keeper = new SecretKeeperRecord
            {
                Id = Guid.NewGuid().ToString(),
                ShareNumber = shares[keeperIndex].X,
                Name = name,
                Phone = phone,
                Email = email,
                EncryptedShare = encryptedData,
                Hmac = hmac,
                Salt = salt,
                IV = iv,
                CreatedAt = DateTime.UtcNow,
                SessionId = _sessionId
            };

            keepers.Add(keeper);
            keeperNames.Add(name);
            keeperIndex++;

            OnValidationResult(true, $"Keeper {name} configured successfully", "KEEPER_CONFIG");
        }

        return (keepers, keeperNames, usedDefaultKeepers);
    }

    private async Task<bool> TestReconstructionAsync(ShamirSecretOutput sharesData, byte[] originalSecret)
    {
        OnProgressUpdated("Please enter passwords for threshold number of shares to test reconstruction.", null, "TEST_RECONSTRUCTION");

        var testShares = new List<Share>();
        const int maxTestAttempts = 3;

        for (int i = 0; i < sharesData.Configuration.ThresholdRequired; i++)
        {
            int attempts = 0;
            bool shareDecrypted = false;

            while (!shareDecrypted && attempts < maxTestAttempts)
            {
                attempts++;
                OnProgressUpdated($"Test share {i + 1} of {sharesData.Configuration.ThresholdRequired} - Keeper: {sharesData.Keepers[i].Name}", 
                    null, "TEST_SHARE_REQUEST");

                var password = await RequestSecureStringAsync($"Enter password (attempt {attempts}/{maxTestAttempts}): ");

                try
                {
                    var share = _cryptographyService.DecryptShare(sharesData.Keepers[i], password, sharesData.Configuration.KdfIterations);
                    testShares.Add(share);
                    OnValidationResult(true, "Share verified", "SHARE_VERIFICATION");
                    shareDecrypted = true;
                }
                catch
                {
                    if (attempts < maxTestAttempts)
                    {
                        OnValidationResult(false, "Failed to decrypt share. Please try again.", "SHARE_VERIFICATION");
                    }
                    else
                    {
                        OnValidationResult(false, $"Failed to decrypt share after {maxTestAttempts} attempts.", "SHARE_VERIFICATION");
                        return false;
                    }
                }
            }
        }

        try
        {
            byte[] reconstructed = ShamirSecretShare.ReconstructSecret(testShares, sharesData.Configuration.ThresholdRequired);
            bool matches = reconstructed.SequenceEqual(originalSecret);
            _cryptographyService.SecureDelete(reconstructed);
            return matches;
        }
        catch (Exception ex)
        {
            OnValidationResult(false, $"Reconstruction test failed: {ex.Message}", "RECONSTRUCTION_TEST");
            return false;
        }
    }

    private async Task<string> SaveSharesAsync(ShamirSecretOutput output)
    {
        string json = JsonSerializer.Serialize(output, new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        });

        string filename = $"secret_shares_{DateTime.Now:yyyyMMdd_HHmmss}.json";
        string fullPath = Path.Combine(_sessionOutputFolder, filename);
        File.WriteAllText(fullPath, json);

        return fullPath;
    }

    private async Task<List<Share>> CollectSharesFromKeepersAsync(ShamirSecretOutput sharesData, ShareRecoveryRecord recoveryRecord)
    {
        var decryptedShares = new List<Share>();
        var usedKeepers = new HashSet<int>();
        int failedAttempts = 0;
        const int maxFailedAttempts = 10;

        while (decryptedShares.Count < sharesData.Configuration.ThresholdRequired)
        {
            if (failedAttempts >= maxFailedAttempts)
            {
                throw new InvalidOperationException($"Maximum failed attempts ({maxFailedAttempts}) reached. Aborting reconstruction.");
            }

            OnProgressUpdated($"Share {decryptedShares.Count + 1} of {sharesData.Configuration.ThresholdRequired}", null, "COLLECTING_SHARE");

            var availableKeepers = new List<string>();
            for (int i = 0; i < sharesData.Keepers.Count; i++)
            {
                if (!usedKeepers.Contains(i))
                {
                    availableKeepers.Add($"{i + 1}. {sharesData.Keepers[i].Name}");
                }
            }

            OnProgressUpdated($"Available keepers:\n{string.Join("\n", availableKeepers)}", null, "AVAILABLE_KEEPERS");

            int keeperNum = await RequestIntegerAsync("Enter keeper number (or 0 to cancel): ", 0, sharesData.Keepers.Count, 
                $"Please enter a number between 0 and {sharesData.Keepers.Count}.");

            if (keeperNum == 0)
            {
                throw new OperationCanceledException("Reconstruction cancelled by user.");
            }

            if (usedKeepers.Contains(keeperNum - 1))
            {
                OnValidationResult(false, "This keeper has already been used. Please select another.", "KEEPER_SELECTION");
                continue;
            }

            var keeper = sharesData.Keepers[keeperNum - 1];
            OnProgressUpdated($"Selected: {keeper.Name}", null, "KEEPER_SELECTED");
            recoveryRecord.KeepersUsed.Add(keeper.Name);

            var password = await RequestSecureStringAsync("Enter password for this keeper: ");

            try
            {
                var share = _cryptographyService.DecryptShare(keeper, password, sharesData.Configuration.KdfIterations);
                decryptedShares.Add(share);
                usedKeepers.Add(keeperNum - 1);
                OnValidationResult(true, "Share decrypted successfully", "SHARE_DECRYPTION");
            }
            catch (Exception ex)
            {
                failedAttempts++;
                OnValidationResult(false, $"Failed to decrypt share: {ex.Message}", "SHARE_DECRYPTION");
                OnProgressUpdated($"Failed attempts: {failedAttempts}/{maxFailedAttempts}", null, "FAILED_ATTEMPTS");
                _auditLogger?.LogAudit("DECRYPT_FAILED", $"Failed to decrypt share for keeper {keeper.Name}");
                recoveryRecord.KeepersUsed.Remove(keeper.Name);
            }
        }

        return decryptedShares;
    }

    private async Task<string> RequestTextAsync(string prompt, Func<string, bool> validator, string errorMessage, int maxLength)
    {
        var request = new InputRequestEventArgs
        {
            RequestType = InputRequestType.Text,
            Prompt = prompt,
            ErrorMessage = errorMessage,
            MaxLength = maxLength,
            Validator = validator
        };

        OnInputRequested(request);
        var result = await request.CompletionSource.Task;
        return (string)result;
    }

    private async Task<SecureString> RequestSecureStringAsync(string prompt)
    {
        var request = new InputRequestEventArgs
        {
            RequestType = InputRequestType.SecureString,
            Prompt = prompt
        };

        OnInputRequested(request);
        var result = await request.CompletionSource.Task;
        return (SecureString)result;
    }

    private async Task<int> RequestIntegerAsync(string prompt, int min, int max, string errorMessage)
    {
        var request = new InputRequestEventArgs
        {
            RequestType = InputRequestType.Integer,
            Prompt = prompt,
            MinValue = min,
            MaxValue = max,
            ErrorMessage = errorMessage
        };

        OnInputRequested(request);
        var result = await request.CompletionSource.Task;
        return (int)result;
    }

    private async Task<string> RequestFilePathAsync(string prompt, string expectedExtension, string errorMessage)
    {
        var request = new InputRequestEventArgs
        {
            RequestType = InputRequestType.FilePath,
            Prompt = prompt,
            ExpectedExtension = expectedExtension,
            ErrorMessage = errorMessage
        };

        OnInputRequested(request);
        var result = await request.CompletionSource.Task;
        return (string)result;
    }

    private async Task<bool> RequestYesNoAsync(string prompt)
    {
        var request = new InputRequestEventArgs
        {
            RequestType = InputRequestType.YesNo,
            Prompt = prompt
        };

        OnInputRequested(request);
        var result = await request.CompletionSource.Task;
        return (bool)result;
    }

    private static bool IsValidPhoneNumber(string phone)
    {
        if (string.IsNullOrWhiteSpace(phone) || phone.Length > 20)
            return false;

        var pattern = @"^[\d\s\+\-\(\)]+$";
        return Regex.IsMatch(phone, pattern) && Regex.IsMatch(phone, @"\d{3,}");
    }

    private static bool IsValidName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;

        var pattern = @"^[a-zA-Z\s\-']+$";
        return Regex.IsMatch(name, pattern);
    }

    private static bool IsValidEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email) || email.Length > 254)
            return false;

        try
        {
            var pattern = @"^[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}$";
            return Regex.IsMatch(email, pattern);
        }
        catch
        {
            return false;
        }
    }

    private static byte[] SecureStringToBytes(SecureString secureString)
    {
        IntPtr ptr = IntPtr.Zero;
        try
        {
            ptr = System.Runtime.InteropServices.Marshal.SecureStringToGlobalAllocUnicode(secureString);
            string str = System.Runtime.InteropServices.Marshal.PtrToStringUni(ptr) ?? string.Empty;
            return Encoding.UTF8.GetBytes(str);
        }
        finally
        {
            System.Runtime.InteropServices.Marshal.ZeroFreeGlobalAllocUnicode(ptr);
        }
    }

    protected virtual void OnProgressUpdated(string message, int? percentComplete, string eventType)
    {
        ProgressUpdated?.Invoke(this, new ProgressEventArgs 
        { 
            Message = message, 
            PercentComplete = percentComplete, 
            EventType = eventType 
        });
    }

    protected virtual void OnInputRequested(InputRequestEventArgs e)
    {
        InputRequested?.Invoke(this, e);
    }

    protected virtual void OnValidationResult(bool isValid, string message, string validationTarget)
    {
        ValidationResult?.Invoke(this, new ValidationEventArgs 
        { 
            IsValid = isValid, 
            Message = message, 
            ValidationTarget = validationTarget 
        });
    }

    protected virtual void OnOperationCompleted(bool success, string message, string operationType, object? result)
    {
        OperationCompleted?.Invoke(this, new CompletionEventArgs 
        { 
            Success = success, 
            Message = message, 
            OperationType = operationType, 
            Result = result 
        });
    }
}
