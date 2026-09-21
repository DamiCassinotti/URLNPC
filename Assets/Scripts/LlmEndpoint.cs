using System.Threading;
using System.Threading.Tasks;

// The HTTP layer behind an interface (issue #130), so LlmModeSelector's prompt
// building, parsing and retry rules are testable without a server and the
// transport can be swapped — a local Ollama today, a cloud endpoint later — as
// one construction change.
public interface ILlmEndpoint
{
    // Must not throw for a server- or network-level failure: report it as
    // Ok == false with a message. Cancellation is the caller's timeout as much
    // as its teardown, so abort the request when the token trips.
    Task<LlmCompletion> CompleteAsync(LlmRequest request, CancellationToken cancellation);
}

public struct LlmRequest
{
    public string Model;
    public string Prompt;
    public float Temperature;
    public int Seed;
    // A JSON schema the server constrains decoding to, or null for free text.
    public string JsonSchema;
    public int TimeoutSeconds;
}

public struct LlmCompletion
{
    public bool Ok;
    // The model's text; the schema makes it a JSON object, but nothing here
    // assumes so — parsing is LlmModeResponse's job and stays defensive.
    public string Text;
    // Why the call failed, for the mode_decision line. Empty when Ok.
    public string Error;

    public static LlmCompletion Answer(string text) => new LlmCompletion { Ok = true, Text = text ?? "" };

    public static LlmCompletion Failure(string error) =>
        new LlmCompletion { Ok = false, Text = "", Error = error ?? "" };
}
