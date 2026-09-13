Implement FFmpegCommandBuilder.

Output:
- 1080x1920.
- Input video on the left.
- Bundled companion video on the right.
- Each region is 540x1920.
- Preserve aspect ratio and crop.
- Loop the companion video.
- Use input audio.
- Support h264_nvenc and libx264.
- Add -progress pipe:1 and -nostats.

Return an argument list, not a quoted command string.
Add unit tests.