using NUnit.Framework;

// The Anthropic Messages request body (issue #156) — what goes on the wire when
// the cloud arm is chosen, and the missing-key path that stops a run rather
// than throwing at it. The HTTP roundtrip and the response parsing depend on a
// real UnityWebRequest and are not covered here.
public class AnthropicEndpointTests
{
    [Test]
    public void TheBody_CarriesTheModelTemperatureAndMessages_ButNoSeedOrSchema()
    {
        string body = AnthropicEndpoint.BuildBody(new LlmRequest
        {
            Model = "claude-haiku-4-5-20251001",
            Prompt = "pick a mode\nnow",
            Temperature = 0.7f,
            Seed = 5,
            JsonSchema = LlmModeResponse.Schema(),
        });

        Assert.That(body, Does.Contain("\"model\":\"claude-haiku-4-5-20251001\""));
        Assert.That(body, Does.Contain("\"max_tokens\":"));
        Assert.That(body, Does.Contain("\"temperature\":0.7"));
        Assert.That(body, Does.Contain("\"messages\":[{\"role\":\"user\""));
        Assert.That(body, Does.Contain("pick a mode\\nnow"),
            "the prompt's newline must not split the body");
        Assert.That(body, Does.Not.Contain("\"seed\""),
            "the Messages API has no seed parameter");
        Assert.That(body, Does.Not.Contain("\"format\"").And.Not.Contain("\"schema\""),
            "there is no schema-constrained decode on the plain Messages endpoint");
        Assert.That(body.Split('\n').Length, Is.EqualTo(1));
    }

    [Test]
    public void WithNoApiKey_TheCallFails_RatherThanThrowing()
    {
        var endpoint = new AnthropicEndpoint(AnthropicEndpoint.DefaultBaseUrl, apiKey: "");

        var task = endpoint.CompleteAsync(new LlmRequest
        {
            Model = "claude-haiku-4-5-20251001",
            Prompt = "anything",
            TimeoutSeconds = 5,
        }, System.Threading.CancellationToken.None);

        Assert.That(task.IsCompleted, Is.True, "no API key must not spend a task on a request");
        Assert.That(task.Result.Ok, Is.False);
        Assert.That(task.Result.Error, Does.Contain(AnthropicEndpoint.ApiKeyEnvVar));
    }
}
