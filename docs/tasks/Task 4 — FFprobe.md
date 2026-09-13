Implement FFprobeService using ProcessStartInfo.ArgumentList.

Use JSON output.
Parse:
- duration;
- video stream presence;
- audio stream presence;
- width;
- height.

Separate JSON parsing from process execution so parsing can be unit tested.
Handle cancellation by killing the process tree.