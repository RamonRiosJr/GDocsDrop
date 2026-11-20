# Contributing to GDocsDrop

Thank you for contributing to **GDocsDrop**!

## Development Requirements
- Windows 10 / 11 with `.NET Framework 4.x` (built into Windows; uses `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe`).
- Google Drive for Desktop (`G:\My Drive`) for end-to-end sync testing.

## Coding Standards (C# 5.0 Target)
Because GDocsDrop compiles directly with the built-in Windows `csc.exe` without requiring Visual Studio or external SDKs, all files under `src/` **must remain strictly C# 5.0 compatible**:
- Use `string.Format(...)` instead of `$""` interpolated strings.
- Use explicit null checks instead of `?.` or `??=`.
- Avoid expression-bodied members (`=>` on properties/methods), `out var`, `using var`, `nameof(...)`, and file-scoped namespaces.
- Keep Win32 P/Invoke signatures isolated in `src/NativeMethods.cs`.

## Local Build & Verification
```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\build.ps1
```
To test the sync pipeline without launching the browser UI:
```powershell
Start-Process -FilePath .\dist\GDocsDrop-Windows.exe -ArgumentList @("--dry-run", "--test-out", "`"$env:TEMP\verify.log`"", "`"C:\path\to\sample.docx`"") -Wait
```

## Pull Request Process
1. Enable the repository pre-commit secret guard hook:
   ```bash
   git config core.hooksPath .githooks
   ```
2. Keep changes focused and update `CHANGELOG.md` under `[Unreleased]` if applicable.
3. Verify `.\scripts\build.ps1` compiles with zero warnings or errors before opening a PR.
