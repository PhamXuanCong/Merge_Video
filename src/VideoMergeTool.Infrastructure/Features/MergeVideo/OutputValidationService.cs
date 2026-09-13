using VideoMergeTool.Core.Features.MergeVideo.Interfaces;
using VideoMergeTool.Core.Features.MergeVideo.Models;

namespace VideoMergeTool.Infrastructure.Features.MergeVideo;

public sealed class OutputValidationService : IOutputValidator
{
    private readonly IFFprobeService _ffprobeService;

    public OutputValidationService(IFFprobeService ffprobeService)
    {
        _ffprobeService = ffprobeService;
    }

    public async Task<OutputValidationResult> ValidateAsync(
        string outputFile,
        TimeSpan expectedDuration,
        TimeSpan durationTolerance,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(outputFile))
        {
            return new OutputValidationResult(false, null, "The temporary output file was not created.");
        }

        try
        {
            var metadata = await _ffprobeService.GetMetadataAsync(outputFile, cancellationToken);
            if (!metadata.HasVideo)
            {
                return new OutputValidationResult(false, metadata, "The output does not contain a video stream.");
            }

            if (Math.Abs((metadata.Duration - expectedDuration).TotalSeconds) > durationTolerance.TotalSeconds)
            {
                return new OutputValidationResult(
                    false,
                    metadata,
                    "The output duration differs from the input duration beyond the allowed tolerance.");
            }

            return new OutputValidationResult(true, metadata, null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return new OutputValidationResult(false, null, exception.Message);
        }
    }
}
