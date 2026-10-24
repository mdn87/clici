using Clici.PromptNavigation.Probe;

namespace Clici.App.Tests;

public sealed class AnchorRowCandidatesTests
{
    [Theory]
    [InlineData("reply\r\n      \r\n", 2)]
    [InlineData("reply\n      \n", 2)]
    [InlineData("reply\r      \r", 2)]
    [InlineData("reply\r\n      ", 2)]
    public void BlankRowsMapOnlyAfterPhysicalAndTextualCountsAgree(string text, int physicalRows)
    {
        var (aligned, rows) = AnchorRowCandidates.Analyze(text, physicalRows);
        Assert.True(aligned);
        Assert.Equal(new[] { 1 }, rows);
    }

    [Theory]
    [InlineData("long wrapped reply\r\n      \r\n", 4)]
    [InlineData("      \r\n", 2)]
    [InlineData("reply\r\n      \r\n", 1)]
    public void OmittedWrapBoundariesOrChangedProviderRowsRequireSubdivision(string text, int physicalRows)
    {
        var (aligned, rows) = AnchorRowCandidates.Analyze(text, physicalRows);
        Assert.False(aligned);
        Assert.NotEmpty(rows);
    }

    [Fact]
    public void IndentedOutputAndShortBlanksAreNotAnchorCandidates()
    {
        var (aligned, rows) = AnchorRowCandidates.Analyze(
            "        code\r\n     \r\n\r\n\t      \r\n      日本語 🙂\r\n      \r\n        \r\n", 7);
        Assert.True(aligned);
        Assert.Equal(new[] { 5, 6 }, rows);
    }
}
