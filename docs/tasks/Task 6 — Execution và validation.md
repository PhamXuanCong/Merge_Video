Implement FFmpegService and OutputValidationService.

Requirements:
- Read stdout progress and stderr concurrently.
- Parse out_time_us.
- Kill FFmpeg on cancellation.
- Validate the temporary output using FFprobe.
- Never delete or move the input on failure.
- Delete temporary output on failure or cancellation.