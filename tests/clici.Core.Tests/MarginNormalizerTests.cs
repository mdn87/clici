using Clici.Core.MarginNormalization;

namespace Clici.Core.Tests;

public sealed class MarginNormalizerTests
{
    private readonly MarginNormalizer _normalizer = new();

    [Fact]
    public void RemovesStandardTwoSpaceMargin()
    {
        const string input = "  First line\n  Second line\n    Nested line";

        var result = _normalizer.Normalize(input);

        Assert.Equal(MarginNormalizationStatus.Normalized, result.Status);
        Assert.Equal("First line\nSecond line\n  Nested line", result.Text);
    }

    [Fact]
    public void FourSpaceBaseMarginIsDetectedAndRemoved()
    {
        // Every content line shares a four-space base margin, so four spaces are
        // detected and removed while the deeper nested line keeps its relative
        // indentation.
        const string input = "    First\n    Second\n      Nested";

        var result = _normalizer.Normalize(input);

        Assert.Equal(MarginNormalizationStatus.Normalized, result.Status);
        Assert.Equal("First\nSecond\n  Nested", result.Text);
    }

    [Fact]
    public void MixedTwoAndFourSpaceLinesPreserveRelativeIndentation()
    {
        var result = _normalizer.Normalize("  First\n    Nested\n  Last");

        Assert.Equal("First\n  Nested\nLast", result.Text);
    }

    [Fact]
    public void FewerThanThreeNonblankLinesAreLeftUntouched()
    {
        // The classifier requires at least three nonblank lines of evidence.
        const string input = "  First\n  Second";

        var result = _normalizer.Normalize(input);

        Assert.Equal(MarginNormalizationStatus.NotEligible, result.Status);
        Assert.Same(input, result.Text);
    }

    [Fact]
    public void SingleLineTextIsUnchanged()
    {
        const string input = "  One line";

        var result = _normalizer.Normalize(input);

        Assert.Equal(MarginNormalizationStatus.NotEligible, result.Status);
        Assert.Same(input, result.Text);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\r\n \t\r\n")]
    public void EmptyOrWhitespaceOnlyTextIsUnchanged(string input)
    {
        var result = _normalizer.Normalize(input);

        Assert.Equal(MarginNormalizationStatus.NotEligible, result.Status);
        Assert.Equal(input, result.Text);
    }

    [Fact]
    public void BlankLinesDoNotAffectDetection()
    {
        const string input = "  First\n\n \t\n  Second\n  Third";

        var result = _normalizer.Normalize(input);

        Assert.Equal(MarginNormalizationStatus.Normalized, result.Status);
        Assert.Equal("First\n\n \t\nSecond\nThird", result.Text);
        Assert.Equal(3, result.NonblankLineCount);
    }

    [Fact]
    public void ColumnZeroLinesAreConflictsThatBlockNormalization()
    {
        var input = string.Join(
            "\n",
            Enumerable.Repeat("  indented", 7)
                .Concat(Enumerable.Repeat("column zero", 3)));

        var result = _normalizer.Normalize(input);

        Assert.Equal(MarginNormalizationStatus.NotEligible, result.Status);
        Assert.Equal(input, result.Text);
    }

    [Fact]
    public void OneSpaceOutliersAreConflictsThatBlockNormalization()
    {
        // The reviewer's dangerous case: seven two-space lines and three
        // one-space lines. Dedenting the two-space lines would reverse the
        // relative indentation of the one-space outliers, so the whole item is
        // left untouched instead.
        var input = string.Join(
            "\n",
            Enumerable.Repeat("  margin", 7)
                .Concat(Enumerable.Repeat(" one-space", 3)));

        var result = _normalizer.Normalize(input);

        Assert.Equal(MarginNormalizationStatus.NotEligible, result.Status);
        Assert.Equal(input, result.Text);
    }

    [Fact]
    public void TabIndentedLinesAreConflictsThatBlockNormalization()
    {
        const string input = "  First\n\tSecond\n  Third";

        var result = _normalizer.Normalize(input);

        Assert.Equal(MarginNormalizationStatus.NotEligible, result.Status);
        Assert.Equal(input, result.Text);
    }

    [Fact]
    public void TabOnlyIndentationIsNotInterpretedAsSpaces()
    {
        const string input = "\tFirst\n\tSecond\n\tThird";

        var result = _normalizer.Normalize(input);

        Assert.Equal(MarginNormalizationStatus.NotEligible, result.Status);
        Assert.Equal(input, result.Text);
    }

    [Fact]
    public void ThreeSpaceBaseMarginIsNotACandidateWidth()
    {
        // Only two- and four-space base margins are accepted automatically.
        const string input = "   First\n   Second\n   Third";

        var result = _normalizer.Normalize(input);

        Assert.Equal(MarginNormalizationStatus.NotEligible, result.Status);
        Assert.Equal(input, result.Text);
    }

    [Fact]
    public void CrLfInputRetainsCrLf()
    {
        const string input = "  First\r\n  Second\r\n    Nested";

        var result = _normalizer.Normalize(input);

        Assert.Equal("First\r\nSecond\r\n  Nested", result.Text);
        Assert.DoesNotContain("\n", result.Text.Replace("\r\n", string.Empty));
    }

    [Fact]
    public void LfInputRetainsLf()
    {
        const string input = "  First\n  Second\n    Nested";

        var result = _normalizer.Normalize(input);

        Assert.Equal("First\nSecond\n  Nested", result.Text);
        Assert.DoesNotContain('\r', result.Text);
    }

    [Fact]
    public void StandaloneCrTerminatorsArePreservedWhenNormalized()
    {
        const string input = "  First\r  Second\r    Nested";

        var result = _normalizer.Normalize(input);

        Assert.Equal(MarginNormalizationStatus.Normalized, result.Status);
        Assert.Equal("First\rSecond\r  Nested", result.Text);
    }

    [Fact]
    public void MixedLineTerminatorsArePreservedExactlyWhenNormalized()
    {
        const string input = "  First\r\n  Second\n    Nested\r  Last\r\n";

        var result = _normalizer.Normalize(input);

        Assert.Equal(MarginNormalizationStatus.Normalized, result.Status);
        Assert.Equal("First\r\nSecond\n  Nested\rLast\r\n", result.Text);
    }

    [Fact]
    public void TrailingNewlineIsPreserved()
    {
        const string input = "  First\r\n  Second\r\n  Third\r\n";

        var result = _normalizer.Normalize(input);

        Assert.Equal(MarginNormalizationStatus.Normalized, result.Status);
        Assert.Equal("First\r\nSecond\r\nThird\r\n", result.Text);
    }

    [Fact]
    public void UnicodeContentIsPreserved()
    {
        const string input = "  naïve café ☕\n  日本語 🚀\n  more";

        var result = _normalizer.Normalize(input);

        Assert.Equal("naïve café ☕\n日本語 🚀\nmore", result.Text);
    }

    [Fact]
    public void FixedMarginWidthOverrideRemovesExactlyThatWidth()
    {
        const string input = "    First\n      Nested\n    Last";

        var result = _normalizer.Normalize(
            input,
            new MarginNormalizationOptions(3, [2, 4], FixedMarginWidth: 4));

        Assert.Equal(MarginNormalizationStatus.Normalized, result.Status);
        Assert.Equal("First\n  Nested\nLast", result.Text);
    }

    [Fact]
    public void FixedMarginWidthOverrideRequiresAllLinesShareTheMargin()
    {
        // One line is shallower than the fixed override width, so it is a
        // conflict and normalization is refused.
        const string input = "    First\n  Second\n    Third";

        var result = _normalizer.Normalize(
            input,
            new MarginNormalizationOptions(3, [2, 4], FixedMarginWidth: 4));

        Assert.Equal(MarginNormalizationStatus.NotEligible, result.Status);
        Assert.Equal(input, result.Text);
    }

    [Fact]
    public void AlreadyNormalizedContentIsUnchanged()
    {
        const string input = "First\nSecond\n  Nested";

        var result = _normalizer.Normalize(input);

        Assert.Equal(MarginNormalizationStatus.NotEligible, result.Status);
        Assert.Equal(input, result.Text);
    }
}
