# VideoMergeTool instructions

## Product

VideoMergeTool is a Windows WPF application.

The user selects one input folder containing MP4 videos.

The application:
1. Scans MP4 files recursively.
2. Excludes output and processed folders.
3. Selects a bundled companion video from Assets/CompanionVideos.
4. Combines the input and companion videos horizontally.
5. Writes results into <input-folder>/output.
6. Preserves the input folder's relative structure.

## Architecture

- App contains WPF UI and ViewModels.
- Core contains models, enums, interfaces and business rules.
- Infrastructure contains filesystem, FFmpeg and FFprobe implementations.

Core must not reference App or Infrastructure.
Infrastructure must not reference App.

Do not put FFmpeg commands or filesystem code in ViewModels.

## Safety

Never delete or move an input video unless:
- FFmpeg exited successfully.
- The temporary output exists.
- FFprobe validates the output.
- The output contains a video stream.
- The output duration is within one second of the input duration.
- The temporary output was committed to its final filename.

On cancellation or failure:
- Preserve the input video.
- Delete the temporary .processing.mp4 file.

Never modify files under Assets/CompanionVideos.

## Runtime paths

Resolve bundled files from AppContext.BaseDirectory.

Expected paths:
- Tools/ffmpeg.exe
- Tools/ffprobe.exe
- Assets/CompanionVideos

## Code conventions

- Use constructor injection.
- Use CancellationToken for async workflows.
- Use ProcessStartInfo.ArgumentList.
- Do not concatenate quoted process arguments.
- Use invariant culture for FFmpeg numeric arguments.
- Keep parsing logic unit-testable.
- Run build and relevant tests before completing a task.