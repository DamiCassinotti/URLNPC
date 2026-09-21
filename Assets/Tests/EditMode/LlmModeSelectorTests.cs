using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

// The LLM selector's rules with the transport faked (issue #130): what the
// request carries, the retry-then-give-up ladder over output that names no
// mode, what a transport failure does, and the request body that goes on the
// wire. Every answer here is already complete, so the whole call finishes
// synchronously — the timeout, whose continuation needs real frames, is
// LlmSelectorRuntimeTests (PlayMode).
public class LlmModeSelectorTests
{
    class FakeEndpoint : ILlmEndpoint
    {
        public readonly List<LlmRequest> Requests = new List<LlmRequest>();
        public readonly List<CancellationToken> Tokens = new List<CancellationToken>();
        public readonly Queue<LlmCompletion> Answers = new Queue<LlmCompletion>();

        public FakeEndpoint Answering(params string[] texts)
        {
            foreach (string text in texts) Answers.Enqueue(LlmCompletion.Answer(text));
            return this;
        }

        public FakeEndpoint Failing(string error)
        {
            Answers.Enqueue(LlmCompletion.Failure(error));
            return this;
        }

        // Burns wall clock inside the call — the budget is a stopwatch, and a
        // fake that answers instantly can never spend it.
        public int DelayMs;

        public Task<LlmCompletion> CompleteAsync(LlmRequest request, CancellationToken cancellation)
        {
            Requests.Add(request);
            Tokens.Add(cancellation);
            if (DelayMs > 0) Thread.Sleep(DelayMs);
            return Task.FromResult(Answers.Count > 0 ? Answers.Dequeue() : LlmCompletion.Answer(""));
        }
    }

    static LlmSelectorConfig Config(int retries = 1)
    {
        return new LlmSelectorConfig
        {
            Endpoint = "http://localhost:11434",
            Model = "test-model",
            TimeoutSeconds = 2f,
            Retries = retries,
            Temperature = 0f,
            Seed = 5,
        };
    }

    static GameStateSnapshot Snapshot()
    {
        return new GameStateSnapshot
        {
            hpPercent = 40,
            targetVisible = true,
            targetDistance = DistanceBucket.Mid,
            secondsSinceSeen = 0,
            arenaName = "Courtyard",
            mode = NpcMode.Patrol,
        };
    }

    // Every case's answer is already complete, so the task is too by the time
    // SelectModeAsync returns — the assertion protects the Result read below.
    static NpcMode Select(LlmModeSelector selector, ModeDecisionReport report, GameStateSnapshot snapshot = null)
    {
        Task<NpcMode> task = selector.SelectModeAsync(snapshot ?? Snapshot(), report, CancellationToken.None);
        Assert.That(task.IsCompleted, Is.True, "a completed answer must not leave the call pending");
        return task.Result;
    }

    [Test]
    public void AValidAnswer_IsTheModeAndFillsTheReport()
    {
        var endpoint = new FakeEndpoint().Answering("{\"mode\":\"Retreat\",\"reason\":\"badly hurt\"}");
        var report = new ModeDecisionReport();

        NpcMode mode = Select(new LlmModeSelector(endpoint, Config()), report);

        Assert.That(mode, Is.EqualTo(NpcMode.Retreat));
        Assert.That(report.Parsed, Is.True);
        Assert.That(report.RetryUsed, Is.False);
        Assert.That(report.Reason, Is.EqualTo("badly hurt"));
        Assert.That(report.ModelName, Is.EqualTo("test-model"));
        Assert.That(report.RawResponse, Does.Contain("Retreat"));
        Assert.That(report.Prompt, Does.Contain("hpPercent"), "the snapshot has to reach the prompt");
    }

    [Test]
    public void TheRequest_CarriesTheModelTemperatureSeedAndSchema()
    {
        var endpoint = new FakeEndpoint().Answering("{\"mode\":\"Hunt\",\"reason\":\"\"}");
        var cancellation = new CancellationTokenSource();

        new LlmModeSelector(endpoint, Config()).SelectModeAsync(Snapshot(), cancellation.Token).Wait();

        LlmRequest request = endpoint.Requests[0];
        Assert.That(request.Model, Is.EqualTo("test-model"));
        Assert.That(request.Temperature, Is.EqualTo(0f));
        Assert.That(request.Seed, Is.EqualTo(5));
        Assert.That(request.JsonSchema, Is.EqualTo(LlmModeResponse.Schema()),
            "the schema is the first line of defence against an unusable answer");
        Assert.That(request.TimeoutSeconds, Is.GreaterThan(0));
        Assert.That(endpoint.Tokens[0].CanBeCanceled, Is.True, "the driver's cancellation has to reach the transport");
    }

    [Test]
    public void UnusableOutput_IsRetriedOnce_AndTheSecondAnswerStands()
    {
        var endpoint = new FakeEndpoint().Answering("I'd rather not.", "{\"mode\":\"Patrol\",\"reason\":\"no contact\"}");
        var report = new ModeDecisionReport();

        NpcMode mode = Select(new LlmModeSelector(endpoint, Config(retries: 1)), report);

        Assert.That(mode, Is.EqualTo(NpcMode.Patrol));
        Assert.That(endpoint.Requests.Count, Is.EqualTo(2));
        Assert.That(report.RetryUsed, Is.True);
        Assert.That(report.Parsed, Is.True);
        Assert.That(report.RawResponse, Does.Contain("rather not").And.Contain("Patrol"),
            "both attempts belong in the decisions file");
    }

    [Test]
    public void UnusableOutputTwice_AnswersNoMode_SoTheDriverKeepsTheChannel()
    {
        var endpoint = new FakeEndpoint().Answering("no idea", "still no idea");
        var report = new ModeDecisionReport();

        NpcMode mode = Select(new LlmModeSelector(endpoint, Config(retries: 1)), report);

        Assert.That(System.Enum.IsDefined(typeof(NpcMode), mode), Is.False,
            "the driver reads an undefined mode as an invalid decision");
        Assert.That(mode, Is.EqualTo(LlmModeSelector.NoMode));
        Assert.That(endpoint.Requests.Count, Is.EqualTo(2), "one retry, not a loop");
        Assert.That(report.Parsed, Is.False);
        Assert.That(report.RetryUsed, Is.True);
    }

    [Test]
    public void AnOutOfVocabularyMode_CountsAsUnusable()
    {
        var endpoint = new FakeEndpoint().Answering("{\"mode\":\"Flank\",\"reason\":\"\"}",
            "{\"mode\":\"HoldCover\",\"reason\":\"\"}");

        NpcMode mode = Select(new LlmModeSelector(endpoint, Config(retries: 1)), new ModeDecisionReport());

        Assert.That(mode, Is.EqualTo(NpcMode.HoldCover));
        Assert.That(endpoint.Requests.Count, Is.EqualTo(2));
    }

    [Test]
    public void WithRetriesOff_UnusableOutput_IsNotAskedTwice()
    {
        var endpoint = new FakeEndpoint().Answering("no idea");

        NpcMode mode = Select(new LlmModeSelector(endpoint, Config(retries: 0)), new ModeDecisionReport());

        Assert.That(mode, Is.EqualTo(LlmModeSelector.NoMode));
        Assert.That(endpoint.Requests.Count, Is.EqualTo(1));
    }

    // The retry shares the decision's budget rather than getting a fresh one:
    // two full timeouts would outlive the driver's decision period, which
    // cancels the call and reports a timeout over the verdict it was reaching.
    [Test]
    public void AnUnusableAnswerThatSpentTheBudget_IsNotRetried()
    {
        // Slow enough to spend the whole budget, and answers synchronously, so
        // the attempt itself never trips the timeout.
        var endpoint = new FakeEndpoint { DelayMs = 120 }
            .Answering("no idea", "{\"mode\":\"Hunt\",\"reason\":\"\"}");
        LlmSelectorConfig config = Config(retries: 1);
        config.TimeoutSeconds = 0.1f;

        NpcMode mode = Select(new LlmModeSelector(endpoint, config), new ModeDecisionReport());

        Assert.That(endpoint.Requests.Count, Is.EqualTo(1),
            "a retry the driver would cancel mid-flight is not worth issuing");
        Assert.That(mode, Is.EqualTo(LlmModeSelector.NoMode));
    }

    [Test]
    public void ATransportFailure_Throws_AndIsNotRetried()
    {
        var endpoint = new FakeEndpoint().Failing("connection refused");
        var report = new ModeDecisionReport();

        Task<NpcMode> task = new LlmModeSelector(endpoint, Config(retries: 1))
            .SelectModeAsync(Snapshot(), report, CancellationToken.None);

        Assert.That(task.IsFaulted, Is.True, "a dead server is a failure to report, not a mode");
        Assert.That(task.Exception.GetBaseException().Message, Does.Contain("connection refused"));
        Assert.That(endpoint.Requests.Count, Is.EqualTo(1),
            "the retry is for unusable output, not for a broken transport");
    }

    [Test]
    public void WithNoSnapshot_ItFailsInsteadOfPromptingWithNothing()
    {
        var endpoint = new FakeEndpoint().Answering("{\"mode\":\"Hunt\",\"reason\":\"\"}");

        Task<NpcMode> task = new LlmModeSelector(endpoint, Config())
            .SelectModeAsync(null, new ModeDecisionReport(), CancellationToken.None);

        Assert.That(task.IsFaulted, Is.True);
        Assert.That(endpoint.Requests, Is.Empty);
    }

    [Test]
    public void ThePrompt_NamesEveryModeAndTheStateItIsDecidingOn()
    {
        string prompt = LlmModeSelector.BuildPrompt(Snapshot());

        foreach (NpcMode mode in NpcModes.All)
        {
            Assert.That(prompt, Does.Contain(mode.ToString()), $"{mode} is not offered");
        }
        Assert.That(prompt, Does.Contain("\"hpPercent\":40"));
        Assert.That(prompt, Does.Not.Contain("\"snapshot\""),
            "the state goes in as the flat object the prompt describes");
        Assert.That(prompt, Does.Contain("Courtyard"));
    }

    [Test]
    public void TheRequestBody_EmbedsTheSchemaAsJson_NotAsAString()
    {
        string body = OllamaEndpoint.BuildBody(new LlmRequest
        {
            Model = "test-model",
            Prompt = "pick a mode\nnow",
            Temperature = 0.7f,
            Seed = 5,
            JsonSchema = LlmModeResponse.Schema(),
        });

        Assert.That(body, Does.Contain("\"format\":{\"type\":\"object\""));
        Assert.That(body, Does.Contain("\"stream\":false"));
        Assert.That(body, Does.Contain("\"keep_alive\""), "a model unloaded between episodes costs a decision");
        Assert.That(body, Does.Contain("\"temperature\":0.7"));
        Assert.That(body, Does.Contain("\"seed\":5"));
        Assert.That(body, Does.Contain("pick a mode\\nnow"), "the prompt's newline must not split the body");
        Assert.That(body.Split('\n').Length, Is.EqualTo(1));
    }
}
