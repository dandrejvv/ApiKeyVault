<#
.SYNOPSIS
    Builds ready-to-run copies of ApiKeyVault (desktop app + akv CLI) into dist/.

.DESCRIPTION
    Default: a PORTABLE build (dist/portable) that runs on Windows, macOS and Linux
    (x64 and ARM) with the .NET 10 runtime installed. One folder for every platform.

        Windows:        ApiKeyVault.exe            akv.exe  (or akv.cmd)
        macOS / Linux:  ./ApiKeyVault               ./akv
        Anywhere:       dotnet ApiKeyVault.dll     dotnet akv.dll

    Optional: -Runtime <rid> builds a self-contained single-file executable for one
    platform instead (dist/<rid>), which doesn't need .NET installed.

.EXAMPLE
    ./scripts/publish.ps1                        # portable, all platforms (needs .NET 10 runtime)
    ./scripts/publish.ps1 -Zip                   # ... plus dist/ApiKeyVault-portable.zip
    ./scripts/publish.ps1 -Runtime win-x64       # self-contained Windows x64 executables
    ./scripts/publish.ps1 -Runtime osx-arm64     # self-contained Apple Silicon executables
#>
param(
    [ValidateSet('portable', 'win-x64', 'win-arm64', 'linux-x64', 'linux-arm64', 'osx-x64', 'osx-arm64')]
    [string]$Runtime = 'portable',

    [string]$Configuration = 'Release',

    # Also produce dist/ApiKeyVault-<runtime>.zip
    [switch]$Zip
)

$ErrorActionPreference = 'Stop'

$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$distRoot = Join-Path $root 'dist'
$out = Join-Path $distRoot $Runtime
$portable = $Runtime -eq 'portable'

if (Test-Path $out) {
    Remove-Item -Recurse -Force $out
}

if ($portable) {
    # Framework-dependent, no runtime identifier: managed .dlls plus every platform's native
    # libraries (Avalonia's Skia/HarfBuzz) under runtimes/, picked at startup by .NET.
    $publishArgs = @('-c', $Configuration, '-p:DebugType=none', '-p:SatelliteResourceLanguages=en', '-o', $out)
}
else {
    $publishArgs = @(
        '-c', $Configuration,
        '-r', $Runtime,
        '--self-contained', 'true',
        '-p:PublishSingleFile=true',
        # Avalonia ships native Skia/HarfBuzz/ANGLE libraries; embed them so the app stays one file.
        '-p:IncludeNativeLibrariesForSelfExtract=true',
        '-p:EnableCompressionInSingleFile=true',
        '-p:DebugType=none',
        '-o', $out
    )
}

$projects = @(
    @{ Name = 'Desktop app'; Path = 'src/ApiKeyVault.UI/ApiKeyVault.UI.csproj' },
    @{ Name = 'akv CLI'; Path = 'src/ApiKeyVault.Cli/ApiKeyVault.Cli.csproj' }
)

foreach ($project in $projects) {
    Write-Host "Publishing $($project.Name) ($Runtime)..." -ForegroundColor Cyan
    dotnet publish (Join-Path $root $project.Path) @publishArgs
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed for $($project.Name) (exit code $LASTEXITCODE)."
    }
}

if ($portable) {
    # NuGet packages ship natives for mobile/exotic targets a desktop app can't run on; drop them.
    # Kept: Windows (x64/arm64/x86), macOS (x64/arm64) and Linux glibc/musl (x64/arm64/arm).
    $unsupported = '^(android|ios|tvos|maccatalyst|browser)|loongarch64$|riscv64$|^linux-x86$'
    Get-ChildItem (Join-Path $out 'runtimes') -Directory |
        Where-Object { $_.Name -match $unsupported } |
        Remove-Item -Recurse -Force

    # Launchers for platforms without the .exe app host. LF line endings for the shell scripts.
    $sh = "#!/bin/sh`nexec dotnet `"`$(dirname `"`$0`")/{0}.dll`" `"`$@`"`n"
    [IO.File]::WriteAllText((Join-Path $out 'akv'), ($sh -f 'akv'))
    [IO.File]::WriteAllText((Join-Path $out 'ApiKeyVault'), ($sh -f 'ApiKeyVault'))
    [IO.File]::WriteAllText((Join-Path $out 'akv.cmd'), "@dotnet `"%~dp0akv.dll`" %*`r`n")
    if (-not $IsWindows) {
        chmod +x (Join-Path $out 'akv') (Join-Path $out 'ApiKeyVault')
    }
}

Write-Host ''
Write-Host "Done: $out" -ForegroundColor Green
$size = (Get-ChildItem $out -Recurse -File | Measure-Object Length -Sum).Sum / 1MB
'  {0} files, {1:N1} MB' -f (Get-ChildItem $out -Recurse -File).Count, $size
if ($portable) {
    '  Requires the .NET 10 runtime: https://dotnet.microsoft.com/download/dotnet/10.0'
}

if ($Zip) {
    $zipPath = Join-Path $distRoot "ApiKeyVault-$Runtime.zip"
    if (Test-Path $zipPath) {
        Remove-Item -Force $zipPath
    }
    Compress-Archive -Path (Join-Path $out '*') -DestinationPath $zipPath
    Write-Host "Zipped: $zipPath" -ForegroundColor Green
}
