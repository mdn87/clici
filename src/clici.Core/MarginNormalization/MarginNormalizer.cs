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

            // A trailing newline does not turn one content line into multiline
            // content. The classifier needs a minimum number of lines as evidence.
            if (nonblankLines.Length < options.MinimumNonblankLines)
            {
                return MarginNormalizationResult.NotEligible(text, nonblankLines.Length);
            }

            var columnZeroLineCount = 0;
            var minimumLeadingSpaces = int.MaxValue;

            foreach (var line in nonblankLines)
            {
                var indent = MeasureLeadingIndent(text, line);

                // A tab anywhere in the leading indentation makes the margin
                // ambiguous. Tab-indented lines are conflicts, not neutral
                // outliers, so the whole item is left untouched.
                if (indent.ContainsTab)
                {
                    return MarginNormalizationResult.NotEligible(
                        text,
                        nonblankLines.Length);
                }

                if (indent.LeadingSpaces == 0)
                {
                    columnZeroLineCount++;
                }

                minimumLeadingSpaces = Math.Min(minimumLeadingSpaces, indent.LeadingSpaces);
            }

            var marginWidth = ResolveMarginWidth(options, minimumLeadingSpaces);
            if (marginWidth is null)
            {
                return MarginNormalizationResult.NotEligible(
                    text,
                    nonblankLines.Length,
                    marginLineCount: 0,
                    columnZeroLineCount);
            }

            var builder = new StringBuilder(text.Length);
            var changedLineCount = 0;

            foreach (var line in lines)
            {
                var spacesToRemove = StartsWithAsciiSpaces(text, line, marginWidth.Value)
                    ? marginWidth.Value
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
                    nonblankLines.Length,
                    columnZeroLineCount);
            }

            return MarginNormalizationResult.Normalized(
                builder.ToString(),
                nonblankLines.Length,
                nonblankLines.Length,
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

    /// <summary>
    /// Chooses the margin width to strip. In fixed-override mode the configured
    /// width is used and every content line must share at least that margin. In
    /// automatic mode the shared base margin must be exactly one of the
    /// candidate widths (two or four spaces); anything else — a one-space or
    /// three-space base, or a column-zero conflict — is rejected.
    /// </summary>
    private static int? ResolveMarginWidth(
        MarginNormalizationOptions options,
        int minimumLeadingSpaces)
    {
        if (options.FixedMarginWidth is int fixedWidth)
        {
            return fixedWidth >= 1 && minimumLeadingSpaces >= fixedWidth
                ? fixedWidth
                : null;
        }

        return options.CandidateMarginWidths.Contains(minimumLeadingSpaces)
            ? minimumLeadingSpaces
            : null;
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

    private static LeadingIndent MeasureLeadingIndent(string text, LineSegment line)
    {
        var leadingSpaces = 0;
        var index = line.ContentStart;
        var end = line.ContentStart + line.ContentLength;

        while (index < end && text[index] == ' ')
        {
            leadingSpaces++;
            index++;
        }

        var containsTab = index < end && text[index] == '\t';
        return new LeadingIndent(leadingSpaces, containsTab);
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

    private readonly record struct LeadingIndent(int LeadingSpaces, bool ContainsTab);

    private readonly record struct LineSegment(
        int ContentStart,
        int ContentLength,
        int TerminatorStart,
        int TerminatorLength);
}
