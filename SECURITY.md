# Security Policy

## Architecture & Credential Posture
- **Zero-Secret Design**: GDocsDrop stores no OAuth client secrets, passwords, or API tokens. Sync and account resolution rely solely on the user's already-authenticated **Google Drive for Desktop** local mount and NTFS Alternate Data Streams (`:user.drive.id`, `:user.drive.email`).
- **Least Privilege**: File associations are registered strictly per-user under `HKCU\Software\Classes` without requiring administrator elevation.
- **No Telemetry**: GDocsDrop makes zero background network requests; it only asks Windows to open the resolved `docs.google.com` or `drive.google.com` URL in your default browser.

## Supported Versions

| Version | Supported |
| --- | --- |
| `1.0.x` | Yes |

## Reporting a Vulnerability

If you discover a security issue, please report it privately rather than opening a public issue:
- Direct contact form: [https://ramonrios.net/contactus](https://ramonrios.net/contactus)

Please include steps to reproduce, affected Windows version, and any relevant dry-run trace logs (making sure to redact personal paths or email addresses).
