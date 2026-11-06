using Deckloom.Accounts.Testing;

namespace Deckloom.Accounts.Application.Tests;

// T-025 (REQ-029): sequencia previsivel tok-0001, tok-0002, ... e distinta.
public class SequentialTokenGeneratorTests
{
    [Fact]
    public void Generates_a_predictable_sequence()
    {
        var generator = new SequentialTokenGenerator();

        var tokens = new[] { generator.Generate(), generator.Generate(), generator.Generate() };

        Assert.Equal(["tok-0001", "tok-0002", "tok-0003"], tokens);
    }

    [Fact]
    public void Never_repeats_a_token()
    {
        var generator = new SequentialTokenGenerator();

        var tokens = Enumerable.Range(0, 1000).Select(_ => generator.Generate()).ToList();

        Assert.Equal(tokens.Count, tokens.Distinct().Count());
    }

    [Fact]
    public void Each_generator_starts_its_own_sequence()
    {
        Assert.Equal(new SequentialTokenGenerator().Generate(), new SequentialTokenGenerator().Generate());
    }
}
