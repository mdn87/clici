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
    public void FourSpaceNestedIndentationBecomesTwoSpaces()
    {
        var result = _normalizer.Normalize("    First\n    Second");

        Assert.Equal("  First\n  Second", result.Text);
    }

    [Fact]
    public void MixedTwoAndFourSpaceLinesPreserveRelativeIndentation()
    {
        var result = _normalizer.Normalize("  First\n    Nested\n  Last");

        Assert.Equal("First\n  Nested\nLast", result.Text);
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
    public void BlankLinesDoNotAffectPercentages()
    {
        const string input = "  First\n\n \t\n  Second";

        var result = _normalizer.Normalize(input);

        Assert.Equal(MarginNormalizationStatus.Normalized, result.Status);
        Assert.Equal("First\n\n \t\nSecond", result.Text);
        Assert.Equal(2, result.NonblankLineCount);
    }

    [Fact]
    public void TooManyColumnZeroLinesPreventNormalization()
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
    public void ExactlyTwentyPercentColumnZeroLinesPreventNormalization()
    {
        const string input = "  one\n  two\n  three\n  four\nzero";

        var result = _normalizer.Normalize(input);

        Assert.Equal(MarginNormalizationStatus.NotEligible, result.Status);
        Assert.Equal(input, result.Text);
    }

    [Fact]
    public void TooFewMarginLinesPreventNormalization()
    {
        var input = string.Join(
            "\n",
            Enumerable.Repeat("  margin", 6)
                .Concat(Enumerable.Repeat(" one-space", 4)));

        var result = _normalizer.Normalize(input);

        Assert.Equal(MarginNormalizationStatus.NotEligible, result.Status);
        Assert.Equal(input, result.Text);
    }

    [Fact]
    public void TabsAreNotInterpretedAsSpaces()
    {
        const string input = "\tFirst\n\tSecond";

        var result = _normalizer.Normalize(input);

        Assert.Equal(MarginNormalizationStatus.NotEligible, result.Status);
        Assert.Equal(input, result.Text);
    }

    [Fact]
    public void LinesWithOneLeadingSpaceRemainUnchanged()
    {
        var input = string.Join(
            "\n",
            Enumerable.Repeat("  margin", 7)
                .Concat(Enumerable.Repeat(" one-space", 3)));
        var expected = string.Join(
            "\n",
            Enumerable.Repeat("margin", 7)
                .Concat(Enumerable.Repeat(" one-space", 3)));

        var result = _normalizer.Normalize(input);

        Assert.Equal(MarginNormalizationStatus.Normalized, result.Status);
        Assert.Equal(expected, result.Text);
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
    public void TrailingNewlineIsPreserved()
    {
        const string input = "  First\r\n  Second\r\n";

        var result = _normalizer.Normalize(input);

        Assert.Equal("First\r\nSecond\r\n", result.Text);
    }

    [Fact]
    public void UnicodeContentIsPreserved()
    {
        const string input = "  naïve café ☕\n  日本語 🚀";

        var result = _normalizer.Normalize(input);

        Assert.Equal("naïve café ☕\n日本語 🚀", result.Text);
    }

    [Fact]
    public void ConfigurableThresholdChangesEligibility()
    {
        const string input = "  First\n  Second\n one-space";

        var defaultResult = _normalizer.Normalize(input);
        var configuredResult = _normalizer.Normalize(
            input,
            new MarginNormalizationOptions(0.66, 0.20, 2));

        Assert.Equal(MarginNormalizationStatus.NotEligible, defaultResult.Status);
        Assert.Equal(MarginNormalizationStatus.Normalized, configuredResult.Status);
        Assert.Equal("First\nSecond\n one-space", configuredResult.Text);
    }

    [Fact]
    public void ConfigurableMarginWidthRemovesExactlyThatWidth()
    {
        const string input = "    First\n      Nested";

        var result = _normalizer.Normalize(
            input,
            new MarginNormalizationOptions(0.70, 0.20, 4));

        Assert.Equal("First\n  Nested", result.Text);
    }

    [Fact]
    public void AlreadyNormalizedContentIsUnchanged()
    {
        const string input = "First\nSecond\n  Nested";

        var result = _normalizer.Normalize(input);

        Assert.Equal(MarginNormalizationStatus.NotEligible, result.Status);
        Assert.Equal(input, result.Text);
    }

    [Fact]
    public void EligibleContentWithNoMatchingMarginReportsEligibleUnchanged()
    {
        const string input = " one\n one";

        var result = _normalizer.Normalize(
            input,
            new MarginNormalizationOptions(0, 1, 2));

        Assert.Equal(MarginNormalizationStatus.EligibleUnchanged, result.Status);
        Assert.Equal(input, result.Text);
    }

    [Fact]
    public void ZeroMaximumColumnZeroRatioDisablesNormalization()
    {
        const string input = "  one\n  two";

        var result = _normalizer.Normalize(
            input,
            new MarginNormalizationOptions(0.70, 0, 2));

        Assert.Equal(MarginNormalizationStatus.NotEligible, result.Status);
        Assert.Equal(input, result.Text);
    }
}
