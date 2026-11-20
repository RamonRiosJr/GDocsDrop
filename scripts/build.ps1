param(
    [switch]$Install
)

$ErrorActionPreference = "Stop"

$RepoRoot = Split-Path -Parent $PSScriptRoot
$SrcDir = Join-Path $RepoRoot "src"
$AssetsDir = Join-Path $RepoRoot "assets"
$DistDir = Join-Path $RepoRoot "dist"
$CscPath = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"

if (-not (Test-Path $CscPath)) {
    throw "csc.exe not found at $CscPath"
}

New-Item -ItemType Directory -Path $DistDir -Force | Out-Null

$OutExe = Join-Path $DistDir "GDocsDrop-Windows.exe"
$Win32Icon = Join-Path $AssetsDir "gdocsdrop.ico"

$SourceFiles = Get-ChildItem -Path $SrcDir -Filter "*.cs" | Sort-Object Name | ForEach-Object { $_.FullName }
if (-not $SourceFiles -or $SourceFiles.Count -eq 0) {
    throw "No .cs files found in $SrcDir"
}

$CscArgs = @(
    "/nologo",
    "/target:winexe",
    "/platform:anycpu",
    "/optimize+",
    "/out:$OutExe",
    "/reference:System.dll,System.Core.dll,System.Drawing.dll,System.Windows.Forms.dll,System.Web.Extensions.dll"
)
if (Test-Path $Win32Icon) {
    $CscArgs += "/win32icon:$Win32Icon"
}
$CscArgs += $SourceFiles

Write-Host "Compiling GDocsDrop-Windows.exe..."
& $CscPath @CscArgs
if ($LASTEXITCODE -ne 0) {
    throw "csc.exe failed with exit code $LASTEXITCODE"
}

$hash = (Get-FileHash -Path $OutExe -Algorithm SHA256).Hash.ToLowerInvariant()
$SumFile = Join-Path $DistDir "SHA256SUMS.txt"
"$hash  GDocsDrop-Windows.exe" | Set-Content -Path $SumFile -Encoding ASCII
Write-Host "Built: $OutExe ($((Get-Item $OutExe).Length) bytes, sha256=$hash)"

if ($Install) {
    $InstallDir = Join-Path $env:LOCALAPPDATA "Programs\GDocsDrop"
    $InstallIconsDir = Join-Path $InstallDir "icons"
    $DriveIconsSource = "C:\Program Files\Google\Drive File Stream"

    New-Item -ItemType Directory -Path $InstallDir -Force | Out-Null
    New-Item -ItemType Directory -Path $InstallIconsDir -Force | Out-Null

    if (Test-Path $Win32Icon) {
        Copy-Item -Path $Win32Icon -Destination (Join-Path $InstallIconsDir "gdocsdrop.ico") -Force
    }

    foreach ($icon in @("docs.ico", "sheets.ico", "slides.ico", "drive_fs.ico")) {
        $srcIcon = Join-Path $DriveIconsSource $icon
        if (Test-Path $srcIcon) {
            Copy-Item -Path $srcIcon -Destination (Join-Path $InstallIconsDir $icon) -Force
        }
    }

    $InstalledExe = Join-Path $InstallDir "GDocsDrop.exe"
    Copy-Item -Path $OutExe -Destination $InstalledExe -Force
    Write-Host "Installed to $InstalledExe"

    Write-Host "Registering per-user file associations..."
    $proc = Start-Process -FilePath $InstalledExe -ArgumentList "--register" -Wait -PassThru
    if ($proc.ExitCode -ne 0) {
        throw "Registration failed with exit code $($proc.ExitCode)"
    }

    $WshShell = New-Object -ComObject WScript.Shell
    $ShortcutIcon = Join-Path $InstallIconsDir "docs.ico"
    if (-not (Test-Path $ShortcutIcon)) {
        $ShortcutIcon = Join-Path $InstallIconsDir "gdocsdrop.ico"
    }
    if (-not (Test-Path $ShortcutIcon)) {
        $ShortcutIcon = $InstalledExe
    }

    $Shortcuts = @(
        @{
            Path = (Join-Path ([Environment]::GetFolderPath("Desktop")) "GDocsDrop.lnk")
            Description = "Drop Office/document files to open in Google Docs"
        },
        @{
            Path = (Join-Path ([Environment]::GetFolderPath("Programs")) "GDocsDrop.lnk")
            Description = "Open documents in Google Docs via Google Drive"
        },
        @{
            Path = (Join-Path ([Environment]::GetFolderPath("SendTo")) "Google Docs (GDocsDrop).lnk")
            Description = "Send document directly to Google Docs"
        }
    )

    foreach ($sc in $Shortcuts) {
        $s = $WshShell.CreateShortcut($sc.Path)
        $s.TargetPath = $InstalledExe
        $s.WorkingDirectory = $InstallDir
        $s.Description = $sc.Description
        $s.IconLocation = "$ShortcutIcon,0"
        $s.Save()
        Write-Host "Created shortcut: $($sc.Path)"
    }
}
