using Clici.App;

namespace Clici.App.Tests;

public sealed class BuildInformationTests
{
    [Fact]
    public void TheCommitIsShortenedForDisplay()
    {
        Assert.Equal(
            "0.1.0+00860cdd708d",
            BuildInformation.Shorten("0.1.0+00860cdd708dc10ae0b0195b1db3cecc1e5f4b6e"));
    }

    [Fact]
    public void ADirtyMarkerSurvivesShortening()
    {
        // The marker is the whole point of the suffix: a stamp that keeps the
        // commit but drops ".dirty" would claim the binary matches that commit.
        Assert.Equal(
            "0.1.0+00860cdd708d.dirty",
            BuildInformation.Shorten(
                "0.1.0+00860cdd708dc10ae0b0195b1db3cecc1e5f4b6e.dirty"));
    }

    [Fact]
    public void AVersionWithoutACommitIsUnchanged()
    {
        Assert.Equal("0.1.0", BuildInformation.Shorten("0.1.0"));
        Assert.Equal("unknown", BuildInformation.Shorten("unknown"));
    }

    [Fact]
    public void ACommitShorterThanTheDisplayLengthIsNotPadded()
    {
        Assert.Equal("0.1.0+00860cd", BuildInformation.Shorten("0.1.0+00860cd"));
        Assert.Equal(
            "0.1.0+00860cd.dirty",
            BuildInformation.Shorten("0.1.0+00860cd.dirty"));
    }

    [Fact]
    public void TheBuildReportsAStampedVersion()
    {
        // Guards the build plumbing, not the value: if SourceLink stops
        // stamping the commit, this is the test that says so.
        Assert.NotEqual("unknown", BuildInformation.FullVersion);
        Assert.Contains('+', BuildInformation.FullVersion);
    }
}
