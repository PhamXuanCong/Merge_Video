Implement VideoProcessingService.

Process tasks sequentially for the MVP.

For every input:
- select a companion video;
- read metadata;
- create output and temporary paths;
- run FFmpeg;
- validate;
- commit temporary output;
- handle source action only after commit.

Return a processing summary.