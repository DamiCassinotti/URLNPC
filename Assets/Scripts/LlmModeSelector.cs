using System.Threading;
using System.Threading.Tasks;

// The LLM arm of the selector comparison (issue #130): the snapshot goes to a
// model behind ILlmEndpoint — a local Ollama by default — and the mode it names
// is what gets commanded. Nothing here knows which model that is; swapping 3B
// for 8B, or the endpoint for a cloud one, is LlmSelectorConfig.
//
// The failure ladder, from the issue: schema-constrained decode, then defensive
// parsing, then Retries more attempts on output that names no mode, then NoMode
// so the driver keeps the channel's mode and counts a failure toward its
// fallback selector. A transport failure or a timeout throws instead — the
// driver records it the same way and never blocks on any of it.
public class LlmModeSelector : IReportingModeSelector
{
    // An answer that is no mode. The driver's Enum.IsDefined check turns this
    // into an Invalid mode_decision line: the mode is kept, the failure counts.
    internal const NpcMode NoMode = (NpcMode)(-1);

    // Too little of the budget left for a retry to be worth issuing.
    internal const float MinimumAttemptSeconds = 0.25f;

    readonly ILlmEndpoint endpoint;

    public LlmSelectorConfig Config;

    public LlmModeSelector(ILlmEndpoint endpoint, LlmSelectorConfig config)
    {
        this.endpoint = endpoint;
        Config = config.Sanitized();
    }

    public Task<NpcMode> SelectModeAsync(GameStateSnapshot snapshot, CancellationToken cancellation)
    {
        return SelectModeAsync(snapshot, new ModeDecisionReport(), cancellation);
    }

    public async Task<NpcMode> SelectModeAsync(
        GameStateSnapshot snapshot, ModeDecisionReport report, CancellationToken cancellation)
    {
        report.ModelName = Config.Model;
        if (snapshot == null)
        {
            throw new System.InvalidOperationException(
                "no game-state snapshot to send — the body carries no GameStateSnapshotBuilder");
        }

        string prompt = BuildPrompt(snapshot);
        report.Prompt = prompt;

        // TimeoutSeconds is the budget for the whole call, retries included —
        // per attempt it would let (Retries + 1) attempts outlive the decision
        // period, and the driver cancelling a retry mid-flight reports a
        // timeout where the ladder had actually reached a verdict.
        var budget = System.Diagnostics.Stopwatch.StartNew();
        int attempts = Config.Retries + 1;
        for (int attempt = 0; attempt < attempts; attempt++)
        {
            float remaining = Config.TimeoutSeconds - (float)budget.Elapsed.TotalSeconds;
            // Nothing useful left to ask in: the answer would land after the
            // driver had given up on it anyway.
            if (attempt > 0 && remaining < MinimumAttemptSeconds) break;
            if (attempt > 0) report.RetryUsed = true;
            string text = await Complete(prompt, remaining, cancellation);
            report.RawResponse = attempt == 0 ? text : report.RawResponse + "\n--- retry ---\n" + text;

            if (LlmModeResponse.TryParse(text, out LlmModeResponse parsed))
            {
                report.Parsed = true;
                report.Reason = parsed.Reason;
                return parsed.Mode;
            }
        }

        report.Parsed = false;
        return NoMode;
    }

    // One attempt. Throws rather than answering: a dead server or a model too
    // slow to be useful is not a mode.
    async Task<string> Complete(string prompt, float timeoutSeconds, CancellationToken cancellation)
    {
        var request = new LlmRequest
        {
            Model = Config.Model,
            Prompt = prompt,
            Temperature = Config.Temperature,
            Seed = Config.Seed,
            JsonSchema = LlmModeResponse.Schema(),
            TimeoutSeconds = (int)System.Math.Ceiling(timeoutSeconds),
        };

        using (var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
        {
            Task<LlmCompletion> call = endpoint.CompleteAsync(request, linked.Token);
            int timeoutMs = (int)(timeoutSeconds * 1000f);
            // The timeout is enforced here as well as signalled through the
            // token: an endpoint that ignores cancellation must not be able to
            // hold the decision open past its budget.
            Task finished = await Task.WhenAny(call, Task.Delay(timeoutMs));
            if (finished != call)
            {
                linked.Cancel();
                Observe(call);
                throw new System.TimeoutException(
                    $"{Config.Model} did not answer within {timeoutSeconds:0.##} s " +
                    $"of the call's {Config.TimeoutSeconds:0.##} s budget");
            }

            LlmCompletion completion = await call;
            if (!completion.Ok)
            {
                throw new System.InvalidOperationException(
                    $"{Config.Model} call failed: {completion.Error}");
            }
            return completion.Text ?? "";
        }
    }

    // The abandoned call may still fault; swallow it there rather than letting
    // it surface as unobserved-task noise.
    static void Observe(Task task)
    {
        task.ContinueWith(t => { _ = t.Exception; },
            CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    // Prompt v0: enough to get a real answer out of a real model, deliberately
    // minimal. The system prompt, the mode catalog and the snapshot history are
    // #131's, and the snapshot JSON here is telemetry's serialization rather
    // than a second formatter to keep in step with it.
    internal static string BuildPrompt(GameStateSnapshot snapshot)
    {
        var sb = new System.Text.StringBuilder(512);
        sb.Append("You are the tactical commander of an NPC in a first-person shooter duel. ")
          .Append("Pick the mode it should be in right now.\n")
          .Append("Modes: ");
        for (int i = 0; i < NpcModes.All.Length; i++)
        {
            if (i > 0) sb.Append(", ");
            sb.Append(NpcModes.All[i]);
        }
        sb.Append(".\n")
          .Append("Hunt closes on the target and fights. HoldCover stays behind cover out of the ")
          .Append("target's line of sight. Retreat breaks contact and opens distance. Patrol ")
          .Append("searches unexplored ground for a target it cannot find.\n")
          .Append("State (the NPC knows nothing about the target beyond what is here):\n")
          .Append(ModeDecisionRecord.SnapshotObject(snapshot))
          .Append('\n')
          .Append("Answer with JSON only: {\"mode\": \"<one of the modes>\", \"reason\": \"<short>\"}");
        return sb.ToString();
    }
}
