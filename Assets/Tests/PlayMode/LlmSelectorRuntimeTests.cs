using System.Collections;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

// The parts of the LLM selector (issue #130) that need real frames: the timeout
// on a server that never answers — its continuation resumes on Unity's
// synchronization context, which only runs between frames — and the manual run
// against a live Ollama the issue asks for. The rules themselves are
// LlmModeSelectorTests (EditMode).
public class LlmSelectorRuntimeTests : PlayModeTestBase
{
    // Environment variable rather than a fixture the suite always runs: CI has
    // no model server, and a test that reaches one is a manual check.
    //   URLNPC_OLLAMA=http://localhost:11434 URLNPC_OLLAMA_MODEL=llama3.1:8b
    const string EndpointVar = "URLNPC_OLLAMA";
    const string ModelVar = "URLNPC_OLLAMA_MODEL";

    class HungEndpoint : ILlmEndpoint
    {
        public CancellationToken Token;

        public Task<LlmCompletion> CompleteAsync(LlmRequest request, CancellationToken cancellation)
        {
            Token = cancellation;
            // Never answers, and deliberately ignores the token: the selector's
            // own timeout is what has to end the call.
            return new TaskCompletionSource<LlmCompletion>().Task;
        }
    }

    static GameStateSnapshot Snapshot()
    {
        return GameStateSnapshot.Build(new GameStateInput
        {
            health = 55f,
            maxHealth = 100f,
            targetVisible = true,
            hasEverSeen = true,
            distanceToLastSeen = 18f,
            seenHorizonSeconds = 10f,
            roundTimeRemaining = 74f,
            arenaIndex = 2,
            arenaName = "Courtyard",
            mode = NpcMode.Patrol,
            timeInMode = 6f,
        });
    }

    static IEnumerator WaitFor(Task task, float seconds)
    {
        float deadline = Time.realtimeSinceStartup + seconds;
        while (!task.IsCompleted && Time.realtimeSinceStartup < deadline) yield return null;
    }

    [UnityTest]
    public IEnumerator AServerThatNeverAnswers_TimesOutAndIsNotLeftPending()
    {
        var endpoint = new HungEndpoint();
        LlmSelectorConfig config = LlmSelectorConfig.Defaults;
        config.TimeoutSeconds = 0.2f;
        var selector = new LlmModeSelector(endpoint, config);

        Task<NpcMode> task = selector.SelectModeAsync(Snapshot(), new ModeDecisionReport(), CancellationToken.None);
        Assert.That(task.IsCompleted, Is.False, "the call must not resolve before its timeout");

        yield return WaitFor(task, 5f);

        Assert.That(task.IsCompleted, Is.True, "the timeout never fired");
        Assert.That(task.Exception.GetBaseException(), Is.TypeOf<System.TimeoutException>());
        Assert.That(endpoint.Token.IsCancellationRequested, Is.True,
            "the transport has to be told to abort, not just abandoned");
    }

    [UnityTest]
    public IEnumerator ALiveOllama_AnswersARealSnapshotWithAMode()
    {
        string endpointUrl = System.Environment.GetEnvironmentVariable(EndpointVar);
        if (string.IsNullOrEmpty(endpointUrl))
        {
            Assert.Ignore($"set {EndpointVar} (and optionally {ModelVar}) to run this against a live Ollama");
        }

        LlmSelectorConfig config = LlmSelectorConfig.Defaults;
        config.Endpoint = endpointUrl;
        string model = System.Environment.GetEnvironmentVariable(ModelVar);
        if (!string.IsNullOrEmpty(model)) config.Model = model;
        // A cold model load is slow in a way the in-game budget is not.
        config.TimeoutSeconds = 60f;
        config = config.Sanitized();

        var report = new ModeDecisionReport();
        var selector = new LlmModeSelector(new OllamaEndpoint(config.GenerateUrl), config);
        Task<NpcMode> task = selector.SelectModeAsync(Snapshot(), report, CancellationToken.None);

        yield return WaitFor(task, 90f);

        Assert.That(task.IsCompleted, Is.True, "no answer from the model");
        Assert.That(task.Exception, Is.Null, $"the call failed: {task.Exception?.GetBaseException().Message}");
        Assert.That(System.Enum.IsDefined(typeof(NpcMode), task.Result), Is.True,
            $"the model named no mode — raw output was: {report.RawResponse}");
        Debug.Log($"[LlmSelector] {config.Model} chose {task.Result} — \"{report.Reason}\"");
    }
}
