# Task: Convert VideoMergeTool to Portable Folder Deployment

## Objective

Convert the current VideoMergeTool deployment model into a self-contained portable Windows folder.

The final application must be distributed as a ZIP file containing:

* `VideoMergeTool.exe`
* all required .NET runtime files
* all application DLLs
* `Tools/ffmpeg.exe`
* `Tools/ffprobe.exe`
* bundled companion videos
* license files
* a user-facing README

The recipient must only need to:

1. Extract the ZIP.
2. Open the extracted folder.
3. Double-click `VideoMergeTool.exe`.

The recipient must not need to install:

* .NET Runtime
* .NET SDK
* FFmpeg
* Visual Studio
* an MSI installer
* any additional dependency

## Target environment

* Windows 10 or Windows 11
* Architecture: `win-x64`
* WPF
* Self-contained deployment
* Folder-based deployment
* Do not publish as a single executable

## Required published folder structure

The published folder must have this structure:

```text
VideoMergeTool-<version>-win-x64/
├── VideoMergeTool.exe
├── VideoMergeTool.dll
├── VideoMergeTool.deps.json
├── VideoMergeTool.runtimeconfig.json
├── *.dll
├── *.json
│
├── Tools/
│   ├── ffmpeg.exe
│   └── ffprobe.exe
│
├── Assets/
│   └── CompanionVideos/
│       ├── companion-01.mp4
│       ├── companion-02.mp4
│       └── ...
│
├── Licenses/
│   ├── FFmpeg-LICENSE.txt
│   └── THIRD-PARTY-NOTICES.txt
│
└── README.txt
```

Do not move the .NET runtime DLLs into custom subdirectories. Preserve the folder structure produced by `dotnet publish`.

## Source project structure

The WPF project must contain:

```text
src/VideoMergeTool.App/
├── Tools/
│   ├── ffmpeg.exe
│   └── ffprobe.exe
│
├── Assets/
│   └── CompanionVideos/
│       └── *.mp4
│
├── Licenses/
│   ├── FFmpeg-LICENSE.txt
│   └── THIRD-PARTY-NOTICES.txt
│
└── README.txt
```

## Remove old single-file deployment logic

Remove all deployment logic related to embedded payload extraction, including:

* `PayloadExtractor`
* `payload.zip`
* embedded payload resources
* payload hash calculation
* LocalApplicationData extraction
* payload cache folders
* temporary payload extraction
* `PublishSingleFile=true`
* `IncludeAllContentForSelfExtract`
* companion-video extraction at application startup

Do not leave unused classes, registrations, configuration properties, or tests related to the old payload approach.

## Project publish configuration

Update `VideoMergeTool.App.csproj`.

For Release publishing, configure:

```xml
<PropertyGroup Condition="'$(Configuration)' == 'Release'">
  <RuntimeIdentifier>win-x64</RuntimeIdentifier>
  <SelfContained>true</SelfContained>
  <PublishSingleFile>false</PublishSingleFile>
  <PublishTrimmed>false</PublishTrimmed>
  <PublishReadyToRun>false</PublishReadyToRun>
  <DebugType>none</DebugType>
  <DebugSymbols>false</DebugSymbols>
</PropertyGroup>
```

Do not enable trimming.

Do not enable Native AOT.

Do not enable single-file publishing.

Add content rules so the following files are copied to both build output and publish output:

```xml
<ItemGroup>
  <Content Include="Tools\ffmpeg.exe">
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    <CopyToPublishDirectory>PreserveNewest</CopyToPublishDirectory>
  </Content>

  <Content Include="Tools\ffprobe.exe">
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    <CopyToPublishDirectory>PreserveNewest</CopyToPublishDirectory>
  </Content>

  <Content Include="Assets\CompanionVideos\**\*">
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    <CopyToPublishDirectory>PreserveNewest</CopyToPublishDirectory>
  </Content>

  <Content Include="Licenses\**\*">
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    <CopyToPublishDirectory>PreserveNewest</CopyToPublishDirectory>
  </Content>

  <Content Include="README.txt">
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    <CopyToPublishDirectory>PreserveNewest</CopyToPublishDirectory>
  </Content>
</ItemGroup>
```

Avoid duplicate `Content` entries if the SDK already includes files implicitly. Use `Update` instead of `Include` where needed to avoid duplicate-item build errors.

## Runtime path resolution

All bundled application files must be resolved relative to:

```csharp
AppContext.BaseDirectory
```

Create or update `ApplicationPaths` so it exposes:

```csharp
public sealed class ApplicationPaths
{
    public ApplicationPaths(string applicationDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationDirectory);

        ApplicationDirectory =
            Path.GetFullPath(applicationDirectory);

        FFmpegPath = Path.Combine(
            ApplicationDirectory,
            "Tools",
            "ffmpeg.exe");

        FFprobePath = Path.Combine(
            ApplicationDirectory,
            "Tools",
            "ffprobe.exe");

        CompanionVideoDirectory = Path.Combine(
            ApplicationDirectory,
            "Assets",
            "CompanionVideos");

        LicenseDirectory = Path.Combine(
            ApplicationDirectory,
            "Licenses");

        ReadmePath = Path.Combine(
            ApplicationDirectory,
            "README.txt");
    }

    public string ApplicationDirectory { get; }

    public string FFmpegPath { get; }

    public string FFprobePath { get; }

    public string CompanionVideoDirectory { get; }

    public string LicenseDirectory { get; }

    public string ReadmePath { get; }
}
```

Register it through dependency injection using:

```csharp
services.AddSingleton(
    new ApplicationPaths(
        AppContext.BaseDirectory));
```

Do not use the current working directory.

Do not assume the application is launched from its own folder.

## Startup validation

Create or update `ApplicationPathValidator`.

It must validate:

1. `Tools/ffmpeg.exe` exists.
2. `Tools/ffprobe.exe` exists.
3. `Assets/CompanionVideos` exists.
4. At least one `.mp4` file exists in `Assets/CompanionVideos`.
5. The application can read the companion-video directory.

The MP4 extension check must be case-insensitive.

Example behavior:

```csharp
var companionVideos = Directory
    .EnumerateFiles(
        paths.CompanionVideoDirectory,
        "*",
        SearchOption.TopDirectoryOnly)
    .Where(file =>
        string.Equals(
            Path.GetExtension(file),
            ".mp4",
            StringComparison.OrdinalIgnoreCase))
    .ToList();
```

Run validation before showing `MainWindow`.

If validation fails:

* show a user-friendly WPF error dialog;
* include the missing path;
* do not show the main application window;
* shut down cleanly;
* do not display an unhandled stack trace to the user.

Detailed exception information may be logged.

## Companion video behavior

The application must read companion videos directly from:

```text
<AppContext.BaseDirectory>/Assets/CompanionVideos
```

The application must never:

* delete companion videos;
* move companion videos;
* rename companion videos;
* overwrite companion videos;
* write output files into the application folder.

The existing video-processing logic must remain unchanged.

The user still selects one input folder.

The application still creates:

```text
<selected-input-folder>/output
```

The application must continue excluding `output` and `processed` when scanning input videos.

## README file

Create a Vietnamese `README.txt` with the following information:

```text
VIDEO MERGE TOOL

Cách sử dụng:

1. Giải nén toàn bộ file ZIP vào một thư mục.
2. Không chạy ứng dụng trực tiếp bên trong file ZIP.
3. Mở VideoMergeTool.exe.
4. Chọn thư mục chứa video cần xử lý.
5. Nhấn Quét video.
6. Nhấn Bắt đầu.
7. Video kết quả sẽ nằm trong thư mục output.

Lưu ý:

- Không xóa thư mục Tools.
- Không xóa thư mục Assets.
- Không di chuyển riêng VideoMergeTool.exe ra khỏi thư mục ứng dụng.
- Phải giữ nguyên toàn bộ file và thư mục đi kèm ứng dụng.
- Ứng dụng không yêu cầu cài .NET hoặc FFmpeg.
```

## License files

Ensure these files exist in the source project and are copied to publish output:

```text
Licenses/FFmpeg-LICENSE.txt
Licenses/THIRD-PARTY-NOTICES.txt
```

Do not invent the FFmpeg license text.

If the repository does not currently contain the correct license text, create placeholder files clearly marked:

```text
TODO: Replace this file with the license text belonging to the exact FFmpeg build distributed with this application.
```

Report this as an unresolved release requirement.

## Portable publish script

Create:

```text
scripts/publish-portable.ps1
```

The script must accept:

```powershell
-Version "1.0.0"
-Runtime "win-x64"
```

Defaults:

```powershell
Version = "1.0.0"
Runtime = "win-x64"
```

The script must:

1. Resolve the repository root from the script location.
2. Clean the previous versioned publish folder.
3. Clean the previous versioned ZIP.
4. Run `dotnet restore`.
5. Run a Release build.
6. Run all tests.
7. Publish the WPF project as self-contained.
8. Use folder-based publishing.
9. Verify all required application files.
10. Verify at least one companion MP4 exists.
11. Create a versioned ZIP.
12. Print the final folder path.
13. Print the final ZIP path.
14. Print the unpacked folder size.
15. Print the ZIP size.
16. Return a non-zero exit code on any failure.

Expected output folder:

```text
artifacts/publish/VideoMergeTool-<version>-<runtime>/
```

Expected ZIP:

```text
artifacts/publish/VideoMergeTool-<version>-<runtime>.zip
```

Use this publish command or its MSBuild-equivalent:

```powershell
dotnet publish `
    src/VideoMergeTool.App/VideoMergeTool.App.csproj `
    --configuration Release `
    --runtime $Runtime `
    --self-contained true `
    --output $publishDirectory `
    -p:PublishSingleFile=false `
    -p:PublishTrimmed=false `
    -p:PublishReadyToRun=false `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -p:Version=$Version
```

## ZIP structure requirement

The ZIP must contain one top-level application folder.

Correct:

```text
VideoMergeTool-1.0.0-win-x64.zip
└── VideoMergeTool-1.0.0-win-x64/
    ├── VideoMergeTool.exe
    ├── Tools/
    ├── Assets/
    └── ...
```

Incorrect:

```text
VideoMergeTool-1.0.0-win-x64.zip
├── VideoMergeTool.exe
├── Tools/
├── Assets/
└── ...
```

The user must be able to extract the ZIP and receive one clean application folder.

## Publish validation

The script must fail if any of these are missing:

```text
VideoMergeTool.exe
Tools/ffmpeg.exe
Tools/ffprobe.exe
Assets/CompanionVideos
Licenses
README.txt
```

The script must also fail if:

* no companion MP4 exists;
* build fails;
* tests fail;
* publish fails;
* ZIP creation fails.

Optionally validate that no `.pdb` file is present in the final package.

## Tests

Add or update tests for:

1. `ApplicationPaths` creates correct paths from a supplied root.
2. `ApplicationPathValidator` reports missing FFmpeg.
3. `ApplicationPathValidator` reports missing FFprobe.
4. `ApplicationPathValidator` reports missing companion directory.
5. `ApplicationPathValidator` reports an empty companion directory.
6. `.MP4` files are accepted case-insensitively.
7. Runtime paths do not depend on `Environment.CurrentDirectory`.

Tests must use temporary directories.

Tests must not require the real FFmpeg binaries.

Remove obsolete tests related to embedded payload extraction.

## Do not change

Do not change:

* FFmpeg filter behavior;
* video dimensions;
* encoder selection;
* progress parsing;
* input scanning rules;
* source-file deletion safety;
* output validation rules;
* companion-video selection logic;
* WPF layout, except where needed for startup-error handling;
* task processing order.

This task is only about portable-folder deployment and runtime resource paths.

## Required verification commands

Run:

```powershell
dotnet format VideoMergeTool.sln
```

Then:

```powershell
dotnet build VideoMergeTool.sln `
    --configuration Release
```

Then:

```powershell
dotnet test VideoMergeTool.sln `
    --configuration Release
```

Then:

```powershell
powershell -ExecutionPolicy Bypass `
    -File scripts/publish-portable.ps1 `
    -Version 1.0.0 `
    -Runtime win-x64
```

## Manual verification

After publishing, verify:

1. The application starts by double-clicking `VideoMergeTool.exe`.
2. It does not require a separately installed .NET runtime.
3. It finds `ffmpeg.exe`.
4. It finds `ffprobe.exe`.
5. It finds companion videos.
6. Moving the complete portable folder to another location does not break paths.
7. Launching the application from a shortcut with a different working directory does not break paths.
8. Removing `ffmpeg.exe` causes a clear startup error.
9. Removing all companion videos causes a clear startup error.
10. The application still creates output inside the user-selected input folder.
11. No output video is written into the application folder.
12. Input videos remain safe when processing fails.

## Completion report

When finished, report:

1. All files added.
2. All files modified.
3. All files removed.
4. Build result.
5. Test result.
6. Number of passing tests.
7. Publish result.
8. Exact portable folder path.
9. Exact ZIP path.
10. Folder size.
11. ZIP size.
12. Any unresolved license or packaging issue.
13. Confirmation that the video-processing workflow was not changed.
