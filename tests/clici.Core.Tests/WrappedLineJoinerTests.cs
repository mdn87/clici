using Clici.Core.LineJoining;

namespace Clici.Core.Tests;

public sealed class WrappedLineJoinerTests
{
    private readonly WrappedLineJoiner _joiner = new();

    [Fact]
    public void WrappedBangCommandIsRejoinedWithASingleSpace()
    {
        // The reported case: a Claude Code bang command wrapped by the
        // terminal, its continuation starting at column zero.
        const string input =
            "! wsl.exe -u root -- bash -c \"install -o root -g root -m 0644 /tmp/managed-settings-noallow.json\n" +
            "/etc/claude-code/managed-settings.json && echo installed\"";

        var result = _joiner.JoinIfWrapSignature(input);

        Assert.Equal(LineJoinStatus.Joined, result.Status);
        Assert.Equal(
            "! wsl.exe -u root -- bash -c \"install -o root -g root -m 0644 /tmp/managed-settings-noallow.json " +
            "/etc/claude-code/managed-settings.json && echo installed\"",
            result.Text);
    }

    [Fact]
    public void WrappedCommandWithMarginedContinuationIsRejoined()
    {
        const string input =
            "python3 scripts/orca.py begin --envelope .agents/read-elicitation.envelope.json\r\n" +
            "  --json";

        var result = _joiner.JoinIfWrapSignature(input);

        Assert.Equal(LineJoinStatus.Joined, result.Status);
        Assert.Equal(
            "python3 scripts/orca.py begin --envelope .agents/read-elicitation.envelope.json --json",
            result.Text);
    }

    [Fact]
    public void ThreeLineWordWrapWithRaggedEdgeIsRejoined()
    {
        // Word wrapping pushes a whole word down when it does not fit, so the
        // right edge is ragged and every seam sits on a dropped space.
        const string first =
            "dotnet publish src/clici.App/clici.App.csproj --configuration Release";
        const string second =
            "--runtime win-x64 --self-contained true -p:PublishSingleFile=true";
        const string third = "--output artifacts/publish";

        var result = _joiner.JoinIfWrapSignature($"{first}\n{second}\n{third}");

        Assert.Equal(LineJoinStatus.Joined, result.Status);
        Assert.Equal($"{first} {second} {third}", result.Text);
        Assert.Equal(3, result.SourceLineCount);
    }

    [Fact]
    public void WrappedUrlWithNoWordBoundaryRefusesJoin()
    {
        // A URL too long for the row is split by column, not at a space. There
        // is no word boundary anywhere in the copy, so the single space a join
        // would insert lands inside the URL and breaks it.
        const string input =
            "https://github.com/mdn87/clici/releases/download/v0.1.0/clici-0.1.0-wi\n" +
            "n-x64-setup.exe";

        var result = _joiner.JoinIfWrapSignature(input);

        Assert.Equal(LineJoinStatus.NotEligible, result.Status);
        Assert.Equal(input, result.Text);
    }

    [Fact]
    public void FlushRightEdgeInsideSpacedTextRefusesJoin()
    {
        // The copy has word boundaries, but its non-final lines are flush to
        // one column — the shape mid-token wrapping produces. The seam falls
        // inside the artifact name, so the copy is left untouched.
        const string input =
            "curl -fsSL https://example.com/downloads/very-long-artifact-name-here-\n" +
            "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef.tar.g\n" +
            "z";

        var result = _joiner.JoinIfWrapSignature(input);

        Assert.Equal(LineJoinStatus.NotEligible, result.Status);
        Assert.Equal(input, result.Text);
    }

    [Fact]
    public void TrailingNewlineDoesNotBlockTheSignature()
    {
        const string first =
            "git log --oneline --graph --decorate --all --max-count=40 --date=short";

        var result = _joiner.JoinIfWrapSignature($"{first}\nremainder\n");

        Assert.Equal(LineJoinStatus.Joined, result.Status);
        Assert.Equal($"{first} remainder", result.Text);
    }

    [Fact]
    public void BlankLineBetweenContentRefusesJoin()
    {
        var first = new string('a', 80);
        var second = new string('b', 80);

        var result = _joiner.JoinIfWrapSignature($"{first}\n\n{second}");

        Assert.Equal(LineJoinStatus.NotEligible, result.Status);
    }

    [Fact]
    public void ShortAndVariedLinesRefuseJoin()
    {
        // Ordinary multiline content: short, ragged lines. This is margin
        // normalization's territory, never joining's.
        const string input = "  First line\n  Second\n    Nested detail\n  Last";

        var result = _joiner.JoinIfWrapSignature(input);

        Assert.Equal(LineJoinStatus.NotEligible, result.Status);
    }

    [Fact]
    public void InteriorShortLineRefusesJoin()
    {
        var wide = new string('a', 90);

        var result = _joiner.JoinIfWrapSignature($"{wide}\nshort\n{wide}");

        Assert.Equal(LineJoinStatus.NotEligible, result.Status);
    }

    [Fact]
    public void FinalLineLongerThanTheWrapWidthRefusesJoin()
    {
        var first = new string('a', 60);
        var final = new string('b', 90);

        var result = _joiner.JoinIfWrapSignature($"{first}\n{final}");

        Assert.Equal(LineJoinStatus.NotEligible, result.Status);
    }

    [Theory]
    [InlineData('|')]
    [InlineData('+')]
    [InlineData('│')]
    [InlineData('┌')]
    public void TableAndBoxDrawingRowsRefuseJoin(char framing)
    {
        // Framed table rows are uniform full-width lines and would otherwise
        // satisfy the wrap signature.
        var row = framing + new string('-', 78) + framing;

        var result = _joiner.JoinIfWrapSignature($"{row}\n{row}\n{row}");

        Assert.Equal(LineJoinStatus.NotEligible, result.Status);
    }

    [Fact]
    public void SingleLineRefusesJoin()
    {
        var result = _joiner.JoinIfWrapSignature(new string('a', 90));

        Assert.Equal(LineJoinStatus.NotEligible, result.Status);
    }

    [Fact]
    public void JoinedOutputIsANoOpOnASecondPass()
    {
        const string first =
            "git log --oneline --graph --decorate --all --max-count=40 --date=short";
        var firstPass = _joiner.JoinIfWrapSignature($"{first}\nremainder");

        var secondPass = _joiner.JoinIfWrapSignature(firstPass.Text);

        Assert.Equal(LineJoinStatus.Joined, firstPass.Status);
        Assert.Equal(LineJoinStatus.NotEligible, secondPass.Status);
    }

    [Fact]
    public void JoinAllLinesIgnoresTheSignatureAndBlankLines()
    {
        const string input = "  first fragment\n\nsecond\n  third  ";

        var result = _joiner.JoinAllLines(input);

        Assert.Equal(LineJoinStatus.Joined, result.Status);
        Assert.Equal("first fragment second third", result.Text);
        Assert.Equal(3, result.SourceLineCount);
    }

    [Fact]
    public void JoinAllLinesRefusesSingleNonblankLine()
    {
        var result = _joiner.JoinAllLines("only line\n");

        Assert.Equal(LineJoinStatus.NotEligible, result.Status);
    }
}
