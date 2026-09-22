using NUnit.Framework;

// The LLM selector's configuration (issue #130): serialized defaults with the
// launch arguments on top, field by field, so a batch run can sweep one knob
// without a rebuild — and nonsense values can't produce a selector that never
// answers.
public class LlmSelectorConfigTests
{
    static LlmSelectorConfig Serialized()
    {
        return new LlmSelectorConfig
        {
            Endpoint = "http://box:1234",
            Model = "qwen2.5:3b",
            TimeoutSeconds = 3f,
            Retries = 1,
            Temperature = 0f,
            Seed = 7,
            PromptId = "v1",
        };
    }

    [Test]
    public void WithNoArguments_TheSerializedValuesStand()
    {
        LlmSelectorConfig config = Serialized().WithCommandLine(new string[0]);

        Assert.That(config.Endpoint, Is.EqualTo("http://box:1234"));
        Assert.That(config.Model, Is.EqualTo("qwen2.5:3b"));
        Assert.That(config.TimeoutSeconds, Is.EqualTo(3f));
        Assert.That(config.Seed, Is.EqualTo(7));
    }

    [Test]
    public void EachArgument_OverridesOnlyItsOwnField()
    {
        LlmSelectorConfig config = Serialized().WithCommandLine(new[]
        {
            "player", LlmSelectorConfig.ModelArg, "llama3.1:8b",
            LlmSelectorConfig.TemperatureArg, "0.7",
        });

        Assert.That(config.Model, Is.EqualTo("llama3.1:8b"));
        Assert.That(config.Temperature, Is.EqualTo(0.7f).Within(1e-4f));
        Assert.That(config.Endpoint, Is.EqualTo("http://box:1234"), "untouched fields keep the Inspector value");
        Assert.That(config.TimeoutSeconds, Is.EqualTo(3f));
    }

    [Test]
    public void EveryKnob_IsReachableFromTheCommandLine()
    {
        LlmSelectorConfig config = Serialized().WithCommandLine(new[]
        {
            LlmSelectorConfig.EndpointArg, "http://other:11434",
            LlmSelectorConfig.ModelArg, "mistral",
            LlmSelectorConfig.TimeoutArg, "2.5",
            LlmSelectorConfig.RetriesArg, "0",
            LlmSelectorConfig.TemperatureArg, "0.7",
            LlmSelectorConfig.SeedArg, "99",
            LlmSelectorConfig.PromptArg, "v2",
        });

        Assert.That(config.Endpoint, Is.EqualTo("http://other:11434"));
        Assert.That(config.Model, Is.EqualTo("mistral"));
        Assert.That(config.TimeoutSeconds, Is.EqualTo(2.5f).Within(1e-4f));
        Assert.That(config.Retries, Is.EqualTo(0));
        Assert.That(config.Temperature, Is.EqualTo(0.7f).Within(1e-4f));
        Assert.That(config.Seed, Is.EqualTo(99));
        Assert.That(config.PromptId, Is.EqualTo("v2"));
    }

    [Test]
    public void AnUnparseableValue_LeavesTheSerializedOneAlone()
    {
        LlmSelectorConfig config = Serialized().WithCommandLine(new[]
        {
            LlmSelectorConfig.TimeoutArg, "soon",
            LlmSelectorConfig.SeedArg, "lucky",
        });

        Assert.That(config.TimeoutSeconds, Is.EqualTo(3f));
        Assert.That(config.Seed, Is.EqualTo(7));
    }

    [Test]
    public void ATrailingSlashOnTheEndpoint_DoesNotDoubleUpInTheUrl()
    {
        LlmSelectorConfig config = Serialized().WithCommandLine(new[]
        {
            LlmSelectorConfig.EndpointArg, " http://box:11434/ ",
        });

        Assert.That(config.GenerateUrl, Is.EqualTo("http://box:11434/api/generate"));
    }

    [Test]
    public void NonsenseValues_AreClampedRatherThanUsed()
    {
        LlmSelectorConfig config = new LlmSelectorConfig
        {
            Endpoint = "  ",
            Model = "",
            TimeoutSeconds = -5f,
            Retries = -2,
            Temperature = -1f,
            PromptId = " ",
        }.Sanitized();

        Assert.That(config.Endpoint, Is.EqualTo(LlmSelectorConfig.Defaults.Endpoint));
        Assert.That(config.Model, Is.EqualTo(LlmSelectorConfig.Defaults.Model));
        Assert.That(config.TimeoutSeconds, Is.GreaterThan(0f));
        Assert.That(config.Retries, Is.EqualTo(0));
        Assert.That(config.Temperature, Is.EqualTo(0f));
        Assert.That(config.PromptId, Is.EqualTo(ModePrompt.DefaultId));
    }
}
