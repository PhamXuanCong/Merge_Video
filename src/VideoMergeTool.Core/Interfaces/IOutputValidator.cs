using VideoMergeTool.Core.Models;

namespace VideoMergeTool.Core.Interfaces;

/// <summary>
/// Validates a generated output before it is committed to its final filename.
/// </summary>
public interface IOutputValidator
{
    Task<OutputValidationResult> ValidateAsync(
        string outputFile,
        TimeSpan expectedDuration,
        TimeSpan durationTolerance,
        CancellationToken cancellationToken);
}
