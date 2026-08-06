using System.Text.Json;
using Clici.Core.MarginNormalization;

namespace Clici.Core.Tests;

public sealed class ProofFixtureTests
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    [Fact]
    public void CanonicalProofFixturesMatchTheNormalizer()
    {
        var fixturePath = Path.Combine(
            AppContext.BaseDirectory,
            "proof-fixtures.json");
        var fixtures = JsonSerializer.Deserialize<ProofFixture[]>(
            File.ReadAllText(fixturePath),
            SerializerOptions);

        Assert.NotNull(fixtures);
        Assert.Equal(9, fixtures.Length);

        var normalizer = new MarginNormalizer();

        foreach (var fixture in fixtures)
        {
            var result = normalizer.Normalize(fixture.Input);

            Assert.Equal(
                Enum.Parse<MarginNormalizationStatus>(fixture.ExpectedStatus),
                result.Status);
            Assert.Equal(fixture.ExpectedOutput, result.Text);
        }
    }

    private sealed record ProofFixture(
        string Id,
        string Input,
        string ExpectedStatus,
        string ExpectedOutput);
}
