# Shamir's Secret Sharing Console Application

A secure, enterprise-grade implementation of Shamir's Secret Sharing algorithm with comprehensive session management, audit logging, and cryptographic provenance.

## Overview

This application implements Shamir's Secret Sharing scheme, allowing you to split a secret into multiple encrypted shares that can be distributed to different secret keepers. The original secret can only be reconstructed when a minimum threshold of keepers combine their shares.

### Key Features

- **Shamir's Secret Sharing**: Cryptographically secure secret splitting using GF(256) field arithmetic
- **Strong Encryption**: AES-256-GCM encryption with PBKDF2 key derivation for each share
- **Session Management**: Unique session IDs with complete activity tracking
- **Administrator Provenance**: HMAC signatures provide non-repudiation and integrity verification
- **Comprehensive Audit Trail**: Every action is logged with timestamps and context
- **Memory Protection**: SecureString usage and multi-pass secure deletion
- **Password Complexity**: Enforced strong passwords (12+ chars, mixed case, numbers, special)
- **Reconstruction Testing**: Mandatory verification before saving shares

## Installation

### Prerequisites

- .NET 9.0 SDK or later
- Windows, Linux, or macOS

### Setup

1. Clone or download the project files
2. Navigate to the project directory
3. Install dependencies:

```bash
dotnet restore
```

4. Build the application:

```bash
dotnet build
```

## Usage

### Running the Application

```bash
dotnet run
```

### Initial Setup

1. **Session Initialization**
   - A unique session ID is generated automatically
   - You'll be prompted for an administrator session password
   - This password creates an HMAC signature for session integrity
   - It does NOT authenticate users - it provides cryptographic proof of oversight

2. **Mode Selection**
   - Option 1: Create new secret shares
   - Option 2: Reconstruct secret from existing shares

### Creating Secret Shares

1. **Organization Information**
   - Enter organization name
   - Enter contact phone number

2. **Share Configuration**
   - Set minimum threshold (number of shares needed to reconstruct)
   - Set total number of shares to create
   - Example: 3-of-5 means any 3 shares can reconstruct the secret

3. **Secret Generation**
   - Generate a random 256-bit secret (recommended)
   - Or enter your own secret

4. **Secret Keeper Setup**
   - For each keeper, enter:
     - Name
     - Phone number
     - Email address
     - Strong password (12+ characters with complexity requirements)
     - Password confirmation

5. **Confirmation Test**
   - If enabled (default), you must successfully reconstruct the secret
   - Ensures all shares work correctly before distribution
   - Prevents irrecoverable secrets due to errors

6. **Output Files**
   - `secret_shares_[sessionId]_[timestamp].json` - Contains all encrypted shares
   - Distribute each keeper's information securely
   - Keep the threshold requirement documented

### Reconstructing a Secret

1. **Load Shares File**
   - Provide path to the JSON shares file
   - System displays organization and configuration details

2. **Gather Threshold Shares**
   - Select keepers from the available list
   - Enter each keeper's password
   - Need minimum threshold number of valid shares

3. **Secret Recovery**
   - System reconstructs and verifies the secret
   - Displays recovered secret in hex and UTF-8 formats
   - Verifies integrity using stored hash

## Configuration (appsettings.json)

```json
{
  "SecuritySettings": {
    "ConfirmationRequired": true,      // Require test reconstruction
    "MinPasswordLength": 12,           // Minimum password length
    "RequireUppercase": true,          // Require uppercase letters
    "RequireLowercase": true,          // Require lowercase letters
    "RequireDigit": true,              // Require numbers
    "RequireSpecialCharacter": true,  // Require special characters
    "KdfIterations": 100000,          // PBKDF2 iterations
    "SecureDeletePasses": 3,          // Overwrite passes for secure deletion
    "AuditLogEnabled": true,          // Enable audit logging
    "AuditLogRetentionDays": 90       // Audit log retention period
  }
}
```

## Output Files

### 1. Secret Shares File
`secret_shares_[sessionId]_[timestamp].json`
- Contains encrypted shares for all keepers
- Each share includes: encrypted data, HMAC, salt, IV
- Session ID for traceability
- Organization information and configuration

### 2. Session Report
`session_[sessionId]_[timestamp].json`
- Complete session record with all events
- Summary statistics (shares created, recoveries attempted)
- SHA-256 hash of session data
- HMAC signature using administrator session password
- Provides non-repudiation and integrity verification

### 3. Audit Logs
- `audit_[sessionId]_[timestamp].log` - Real-time text log
- `audit_session_[sessionId]_[timestamp].json` - Structured JSON audit data

## Security Features

### Encryption
- **AES-256-GCM**: Authenticated encryption for shares
- **PBKDF2-SHA256**: Key derivation with 100,000 iterations
- **Unique salts and IVs**: Per-share cryptographic parameters
- **HMAC-SHA256**: Additional integrity verification

### Memory Protection
- **SecureString**: Passwords never stored as plain text in memory
- **Secure deletion**: 3-pass random overwrite of sensitive data
- **Automatic cleanup**: Sensitive data cleared after use

### Session Integrity
- **Administrator provenance**: HMAC signature proves oversight
- **Non-repudiation**: Administrator cannot deny session supervision
- **Tamper detection**: Any modification invalidates signature
- **Complete audit trail**: Every action logged with context

## Security Considerations

### Best Practices
1. **Distribute shares securely**: Use separate secure channels for each keeper
2. **Document the threshold**: Ensure organization knows minimum shares needed
3. **Test recovery process**: Regularly verify shares can reconstruct secret
4. **Secure the session file**: Contains proof of administrative oversight
5. **Protect keeper passwords**: Each keeper must safeguard their password

### Threat Model
- **Protects against**: Single point of failure, unauthorized access, insider threats
- **Requires**: Threshold number of keepers to collude for reconstruction
- **Assumes**: Secure distribution channels, trustworthy keepers

## Troubleshooting

### Common Issues

1. **"File not found" during reconstruction**
   - Ensure the shares JSON file path is correct
   - Check file permissions

2. **"HMAC verification failed"**
   - Incorrect password for keeper
   - Share file may be corrupted

3. **"Password does not meet complexity requirements"**
   - Ensure 12+ characters
   - Include uppercase, lowercase, number, and special character

4. **Reconstruction test fails**
   - Verify all keeper passwords are correct
   - Ensure you're using the right shares file

## Session Verification

To verify session integrity:

1. Obtain the session JSON file
2. Extract the `SessionData` object
3. Recalculate SHA-256 hash of SessionData
4. Using administrator session password, calculate HMAC of SessionData
5. Compare with stored `AdminSessionHmac` value
6. If they match, session data is authentic and unmodified

## Compliance and Auditing

This application provides:
- **Complete audit trail**: All operations logged with timestamps
- **Non-repudiation**: Cryptographic proof of administrator oversight
- **Data integrity**: Tamper-evident session records
- **Compliance support**: Suitable for regulated environments requiring key ceremonies

## Technical Details

### Shamir's Algorithm
- Implements polynomial interpolation in GF(256)
- Secret is the constant term of a random polynomial
- Shares are points on the polynomial curve
- Lagrange interpolation reconstructs the secret

### Cryptographic Stack
- **Field**: GF(2^8) with reduction polynomial x^8 + x^4 + x^3 + x + 1
- **KDF**: PBKDF2-HMAC-SHA256 (100,000 iterations)
- **Encryption**: AES-256-GCM (authenticated encryption)
- **Integrity**: HMAC-SHA256 for shares and session data
- **Random**: Cryptographically secure RNG for all random values

## License

[Specify your license here]

## Support

For issues, questions, or contributions, please [specify contact method or repository URL].

## Version

Current Version: 1.0
.NET Framework: 9.0
Last Updated: [Date]