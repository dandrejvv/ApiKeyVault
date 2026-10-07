# Security Policy

ApiKeyVault is built from the ground up to protect confidential developer API keys and cloud credentials. We take security and cryptographic integrity very seriously.

---

## 🛡️ Supported Versions

| Version | Supported |
|---|---|
| Latest `main` | :white_check_mark: |
| Releases (< 1 year old) | :white_check_mark: |

---

## 🚨 Reporting a Vulnerability

If you discover a security vulnerability or cryptographic flaw in ApiKeyVault, please report it responsibly:

1. **Do NOT open a public GitHub issue.**
2. Send an email detailing the vulnerability to `security@apikeyvault.local` (or submit a confidential advisory via GitHub's **Private Vulnerability Reporting** tab under Security).
3. Include in your report:
   - A description of the vulnerability and attack vector.
   - Steps to reproduce or proof-of-concept code.
   - Any potential impact on confidentiality, integrity, or hardware enrollment.

We will acknowledge your report within 48 hours and work with you to analyze, reproduce, and resolve the issue before public disclosure.

---

## 🔒 Threat Model Scope

Please consult [docs/design/crypto-and-persistence.md](docs/design/crypto-and-persistence.md) for the formal threat model.

### In Scope
- Offline attacks on the `.akv` vault file.
- Unauthorized payload modification, truncation, or tampering (AEAD integrity).
- Replay or downgrade rollback attacks.
- Cross-device enrollment bypasses.
- Clipboard and memory leakage of secrets.

### Out of Scope
- Malware or root compromises already running inside the user's logged-in OS session with full privileges to the OS Credential Manager (DPAPI).
- Physical access to an unlocked, active workstation without full-disk encryption.
