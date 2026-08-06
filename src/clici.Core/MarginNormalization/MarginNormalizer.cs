using System.Text;

namespace Clici.Core.MarginNormalization;

public sealed class MarginNormalizer
{
    public MarginNormalizationResult Normalize(
        string? text,
        MarginNormalizationOptions? options = null)
    {
        options ??= MarginNormalizationOptions.Default;

        try
        {
            if (string.IsNullOrEmpty(text))
            {
                return MarginNormalizationResult.NotEligible(text ?? string.Empty);
            }

            var lines = ParseLines(text);
            var nonblankLines = lines.Where(line => !IsBlank(text, line)).ToArray();

            // A trailing newline does not turn one content line into multiline content.
            if (nonblankLines.Length < 2)
            {
                return MarginNormalizationResult.NotEligible(text, nonblankLines.Length);
            }

            var marginLineCount = nonblankLines.Count(
                line => StartsWithAsciiSpaces(text, line, options.MarginSpaces));
            var columnZeroLineCount = nonblankLines.Count(
                line => StartsAtColumnZero(text, line));

            var marginRatio = (double)marginLineCount / nonblankLines.Length;
            var columnZeroRatio = (double)columnZeroLineCount / nonblankLines.Length;

            if (marginRatio < options.MinimumMarginLineRatio ||
                columnZeroRatio >= options.MaximumColumnZeroLineRatio)
            {
                return MarginNormalizationResult.NotEligible(
                    text,
                    nonblankLines.Length,
                    marginLineCount,
                    columnZeroLineCount);
            }

            var builder = new StringBuilder(text.Length);
            var changedLineCount = 0;

            foreach (var line in lines)
            {
                var spacesToRemove = StartsWithAsciiSpaces(text, line, options.MarginSpaces)
                    ? options.MarginSpaces
                    : 0;

                if (spacesToRemove > 0)
                {
                    changedLineCount++;
                }

                builder.Append(
                    text,
                    line.ContentStart + spacesToRemove,
                    line.ContentLength - spacesToRemove);
                builder.Append(text, line.TerminatorStart, line.TerminatorLength);
            }

            if (changedLineCount == 0)
            {
                return MarginNormalizationResult.EligibleUnchanged(
                    text,
                    nonblankLines.Length,
                    marginLineCount,
                    columnZeroLineCount);
            }

            return MarginNormalizationResult.Normalized(
                builder.ToString(),
                nonblankLines.Length,
                marginLineCount,
                columnZeroLineCount,
                changedLineCount);
        }
        catch (Exception exception)
        {
            return MarginNormalizationResult.FailedSafely(
                text ?? string.Empty,
                exception.GetType().Name);
        }
    }

    private static List<LineSegment> ParseLines(string text)
    {
        var lines = new List<LineSegment>();
        var contentStart = 0;
        var index = 0;

        while (index < text.Length)
        {
            if (text[index] is not ('\r' or '\n'))
            {
                index++;
                continue;
            }

            var terminatorLength = text[index] == '\r' &&
                                   index + 1 < text.Length &&
                                   text[index + 1] == '\n'
                ? 2
                : 1;

            lines.Add(new LineSegment(
                contentStart,
                index - contentStart,
                index,
                terminatorLength));

            index += terminatorLength;
            contentStart = index;
        }

        lines.Add(new LineSegment(contentStart, text.Length - contentStart, text.Length, 0));
        return lines;
    }

    private static bool IsBlank(string text, LineSegment line)
    {
        for (var index = line.ContentStart;
             index < line.ContentStart + line.ContentLength;
             index++)
        {
            if (!char.IsWhiteSpace(text[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool StartsWithAsciiSpaces(
        string text,
        LineSegment line,
        int requiredSpaces)
    {
        if (requiredSpaces < 1 || line.ContentLength < requiredSpaces)
        {
            return false;
        }

        for (var offset = 0; offset < requiredSpaces; offset++)
        {
            if (text[line.ContentStart + offset] != ' ')
            {
                return false;
            }
        }

        return true;
    }

    private static bool StartsAtColumnZero(string text, LineSegment line)
    {
        if (line.ContentLength == 0)
        {
            return false;
        }

        var firstCharacter = text[line.ContentStart];
        return firstCharacter is not (' ' or '\t');
    }

    private readonly record struct LineSegment(
        int ContentStart,
        int ContentLength,
        int TerminatorStart,
        int TerminatorLength);
}
