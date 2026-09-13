using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using VideoMergeTool.Core.Features.RenameVideo.Enums;
using VideoMergeTool.Core.Features.RenameVideo.Models;

namespace VideoMergeTool.Infrastructure.Features.RenameVideo;

/// <summary>
/// Reads and appends the <c>rename-log.csv</c> kept inside each renamed folder. It is written as
/// UTF-8 with a BOM so Excel shows Vietnamese names and emoji correctly.
/// </summary>
internal static class RenameLogCsv
{
    public const string FileName = "rename-log.csv";

    private const string Header = "Time,Batch,Action,OldName,NewName,Status,Note";
    private const string TimeFormat = "yyyy-MM-dd HH:mm:ss";

    /// <summary>Leading characters that make Excel evaluate a cell as a formula.</summary>
    private const string FormulaTriggers = "=+-@\t'";

    private static readonly UTF8Encoding Utf8WithBom = new(encoderShouldEmitUTF8Identifier: true);

    public static string GetPath(string folder) => Path.Combine(folder, FileName);

    /// <summary>Opens the log for appending, creating it with a header row if it is new.</summary>
    public static StreamWriter OpenForAppend(string folder)
    {
        var stream = new FileStream(GetPath(folder), FileMode.Append, FileAccess.Write, FileShare.Read);
        try
        {
            // Flushing every row keeps the log truthful even if the app is killed mid-batch.
            var writer = new StreamWriter(stream, Utf8WithBom) { AutoFlush = true };
            if (stream.Length == 0)
            {
                writer.WriteLine(Header);
            }

            return writer;
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    public static string FormatLine(RenameLogEntry entry) => string.Join(
        ',',
        Encode(entry.Time.ToString(TimeFormat, CultureInfo.InvariantCulture)),
        Encode(entry.BatchId),
        Encode(entry.Action.ToString()),
        Encode(entry.OldName),
        Encode(entry.NewName),
        Encode(entry.Status.ToString()),
        Encode(entry.Note));

    public static IReadOnlyList<RenameLogEntry> Read(string folder)
    {
        var path = GetPath(folder);
        if (!File.Exists(path))
        {
            return [];
        }

        var entries = new List<RenameLogEntry>();

        // ReadWrite sharing so a log that Excel or this app has open can still be read.
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);

        while (reader.ReadLine() is { } line)
        {
            if (TryParseLine(line, out var entry))
            {
                entries.Add(entry);
            }
        }

        return entries;
    }

    /// <summary>Returns <c>false</c> for the header and for any row that was hand-edited beyond recognition.</summary>
    public static bool TryParseLine(string line, [NotNullWhen(true)] out RenameLogEntry? entry)
    {
        entry = null;

        var fields = SplitFields(line);
        if (fields.Count < 7 ||
            !DateTime.TryParseExact(fields[0], TimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time) ||
            !Enum.TryParse<RenameLogAction>(fields[2], out var action) ||
            !Enum.TryParse<RenameItemStatus>(fields[5], out var status))
        {
            return false;
        }

        entry = new RenameLogEntry(time, fields[1], action, fields[3], fields[4], status, fields[6]);
        return true;
    }

    /// <summary>
    /// Video titles come from the internet, so a name such as "=HYPERLINK(…)" must not turn into
    /// a live formula when the log is opened in Excel. A leading apostrophe neutralises it and
    /// <see cref="Decode"/> removes it again, so undo still sees the exact name.
    /// </summary>
    private static string Encode(string value)
    {
        value = value.ReplaceLineEndings(" ");

        if (value.Length > 0 && FormulaTriggers.Contains(value[0]))
        {
            value = "'" + value;
        }

        return value.AsSpan().IndexOfAny(',', '"') >= 0
            ? $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\""
            : value;
    }

    private static string Decode(string value) => value.StartsWith('\'') ? value[1..] : value;

    private static List<string> SplitFields(string line)
    {
        var fields = new List<string>(7);
        var current = new StringBuilder();
        var inQuotes = false;

        for (var index = 0; index < line.Length; index++)
        {
            var character = line[index];

            if (inQuotes)
            {
                if (character != '"')
                {
                    current.Append(character);
                }
                else if (index + 1 < line.Length && line[index + 1] == '"')
                {
                    current.Append('"');
                    index++;
                }
                else
                {
                    inQuotes = false;
                }
            }
            else if (character == '"')
            {
                inQuotes = true;
            }
            else if (character == ',')
            {
                fields.Add(Decode(current.ToString()));
                current.Clear();
            }
            else
            {
                current.Append(character);
            }
        }

        fields.Add(Decode(current.ToString()));
        return fields;
    }
}
