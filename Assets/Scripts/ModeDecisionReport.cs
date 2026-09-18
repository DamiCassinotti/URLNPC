using System.Threading;
using System.Threading.Tasks;

// What one selector call says about itself, for the mode_decision telemetry
// line (issue #126). The driver hands a fresh report to every call, so the
// answer and its diagnostics travel together — an abandoned call's report is
// simply never read, with no race against the next one. The baselines fill
// nothing; the LLM selector is what the fields exist for.
public class ModeDecisionReport
{
    // e.g. "llama3.1:8b"; empty for a selector with no model.
    public string ModelName = "";

    // The model's stated reason for its choice; empty when there is none.
    public string Reason = "";

    // False when the raw output named no mode. The driver's own checks come on
    // top: an undefined enum answer is recorded as unparsed either way.
    public bool Parsed = true;

    // Whether the answer needed a second attempt at the model.
    public bool RetryUsed;

    // The full prompt and raw response are large, so they go to the sibling
    // decisions file keyed by decision id, never inline on the JSONL line.
    public string Prompt = "";
    public string RawResponse = "";
}

// Optional extension of IModeSelector: a selector that has diagnostics to
// report implements this and fills the call's report as it works. The driver
// prefers it over the plain call when present.
public interface IReportingModeSelector : IModeSelector
{
    Task<NpcMode> SelectModeAsync(GameStateSnapshot snapshot, ModeDecisionReport report, CancellationToken cancellation);
}
