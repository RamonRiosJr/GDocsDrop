# Changelog

All notable changes to **GDocsDrop** are documented in this file.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.0.0] - 2026-11-20
### Added
- Native `.NET Framework 4.x` WinForms application (`GDocsDrop-Windows.exe`) with zero external runtime dependencies.
- Dark-mode interactive Drop Zone window (`DropWindow`) with drag-and-drop, file picker, `Pin on Top`, Drive connection status pill, and clickable recent-files history.
- Minimal borderless bottom-right progress HUD (`QuickToastForm`) for double-click, Desktop shortcut drop, and `Send to` launches.
- Zero-secret Google Drive for Desktop integration via Win32 `CreateFileW` NTFS Alternate Data Stream reads (`:user.drive.id`, `:user.drive.email`, `:user.drive.md5`).
- SHA-256 content deduplication cache (`%LOCALAPPDATA%\GDocsDrop\history.json`) and non-destructive timestamp rename on name collisions.
- Per-user Windows file associations (`HKCU\Software\Classes`) with `--register` / `--unregister` CLI flags and `SHChangeNotify` icon refresh.
- Automated GitHub Actions Windows build, release artifact publishing, Dependabot, and Secret Guard CI workflows.
