[CmdletBinding()]
param(
    [string]$Version = "1.0.0",
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"

function Invoke-DotNet {
    param([string[]]$Arguments)

    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
}

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$solutionPath = Join-Path $repositoryRoot "VideoMergeTool.sln"
$projectPath = Join-Path $repositoryRoot "src\VideoMergeTool.App\VideoMergeTool.App.csproj"
$publishRoot = Join-Path $repositoryRoot "artifacts\publish"
$packageName = "VideoMergeTool-$Version-$Runtime"
$publishDirectory = Join-Path $publishRoot $packageName
$zipPath = Join-Path $publishRoot "$packageName.zip"

New-Item -ItemType Directory -Path $publishRoot -Force | Out-Null

if (Test-Path -LiteralPath $publishDirectory) {
    Remove-Item -LiteralPath $publishDirectory -Recurse -Force
}

if (Test-Path -LiteralPath $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}

Invoke-DotNet @("restore", $solutionPath)
Invoke-DotNet @("build", $solutionPath, "--configuration", "Release")

$testProjects = Get-ChildItem -LiteralPath (Join-Path $repositoryRoot "tests") -Filter "*.csproj" -File -Recurse
foreach ($testProject in $testProjects) {
    Invoke-DotNet @(
        "test",
        $testProject.FullName,
        "--configuration", "Release",
        "--no-build",
        "--no-restore"
    )
}
Invoke-DotNet @(
    "publish",
    $projectPath,
    "--configuration", "Release",
    "--runtime", $Runtime,
    "--self-contained", "true",
    "--output", $publishDirectory,
    "--no-restore",
    "-p:PublishSingleFile=false",
    "-p:PublishTrimmed=false",
    "-p:PublishReadyToRun=false",
    "-p:DebugType=None",
    "-p:DebugSymbols=false",
    "-p:Version=$Version"
)

$requiredPaths = @(
    "VideoMergeTool.exe",
    "Tools\ffmpeg.exe",
    "Tools\ffprobe.exe",
    "Assets\CompanionVideos",
    "Licenses",
    "README.txt"
)

foreach ($relativePath in $requiredPaths) {
    $path = Join-Path $publishDirectory $relativePath
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Published package is missing required path: $path"
    }
}

$companionVideos = Get-ChildItem -LiteralPath (Join-Path $publishDirectory "Assets\CompanionVideos") -File -Recurse |
    Where-Object { $_.Extension -ieq ".mp4" }
if ($companionVideos.Count -eq 0) {
    throw "Published package does not contain a companion MP4 video."
}

Push-Location $publishRoot
try {
    Compress-Archive -LiteralPath $packageName -DestinationPath $zipPath -Force
}
finally {
    Pop-Location
}

if (-not (Test-Path -LiteralPath $zipPath)) {
    throw "ZIP creation failed: $zipPath"
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [System.IO.Compression.ZipFile]::OpenRead($zipPath)
try {
    $packagePrefix = "$packageName/"
    foreach ($entry in $archive.Entries) {
        $normalizedEntryPath = $entry.FullName.Replace("\", "/")
        if (-not $normalizedEntryPath.StartsWith($packagePrefix, [StringComparison]::Ordinal)) {
            throw "ZIP entry is not inside the required top-level package folder: $($entry.FullName)"
        }
    }
}
finally {
    $archive.Dispose()
}

$folderSize = (Get-ChildItem -LiteralPath $publishDirectory -File -Recurse |
    Measure-Object -Property Length -Sum).Sum
$zipSize = (Get-Item -LiteralPath $zipPath).Length

Write-Host "Portable folder: $publishDirectory"
Write-Host "Portable ZIP:    $zipPath"
Write-Host "Folder size:     $([Math]::Round($folderSize / 1MB, 2)) MB"
Write-Host "ZIP size:        $([Math]::Round($zipSize / 1MB, 2)) MB"
