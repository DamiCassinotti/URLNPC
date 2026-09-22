using System.Globalization;

// How the LLM selector is pointed at a model (issue #130). Serialized on
// ModeSelectorDriver for the Inspector and overridable per launch, so a batch
// run sweeps models, temperatures or endpoints without a rebuild. Pure logic
// like ModeSelectorChoice; the driver is the adapter that fills it from its own
// fields and hands it to the selector.
public struct LlmSelectorConfig
{
    public const string EndpointArg = "-llmEndpoint";
    public const string ModelArg = "-llmModel";
    public const string TimeoutArg = "-llmTimeout";
    public const string RetriesArg = "-llmRetries";
    public const string TemperatureArg = "-llmTemperature";
    public const string SeedArg = "-llmSeed";
    public const string PromptArg = "-llmPrompt";
    public const string ExemplarsArg = "-llmExemplars";
    public const string ShotsArg = "-llmShots";

    // What names the zero-shot arm on a command line, "" being a value an
    // argument can't carry.
    public const string NoExemplars = "none";

    public string Endpoint;
    public string Model;
    // Which prompt variant is sent (issue #131): the id of a text asset under
    // Resources/Prompts. Sweeping it is how two prompts are compared over the
    // same run.
    public string PromptId;
    // Which exemplar bank fills the prompt's {{EXEMPLARS}} slot (issue #132),
    // empty for the zero-shot arm. The bank is the tuned artifact of this tier,
    // so which one ran is as much a part of a result as the model is.
    public string ExemplarsId;
    // How many of the bank's exemplars are shown; 0 or less is the whole bank.
    // Exemplars cost tokens and tokens cost latency against a 5 s period, which
    // is what the bank-size sweep measures.
    public int Shots;
    // The budget for one decision, retries included — kept under the driver's
    // decision period, which is when the driver cancels an unanswered call and
    // reports a timeout over whatever the ladder was doing.
    public float TimeoutSeconds;
    // Extra attempts after unusable output, not after a transport failure — a
    // dead server is a failure to report, not something to retry inside one
    // decision.
    public int Retries;
    public float Temperature;
    // Fixed by default: a seeded decode plus temp 0 makes a battery run
    // repeatable. The consistency metric is what the temp 0.7 runs are for.
    public int Seed;

    // What Sanitized falls back to for a blank value. Only the text fields
    // can be blank, so this deliberately doesn't restate the numbers — the
    // driver's serialized fields own those, and a second copy here would drift
    // from the Inspector without anything reading it.
    public static LlmSelectorConfig Defaults => new LlmSelectorConfig
    {
        Endpoint = "http://localhost:11434",
        Model = "llama3.1:8b",
        PromptId = ModePrompt.DefaultId,
    };

    // Command line over serialized, field by field: a run that only names a
    // model keeps the Inspector's endpoint and timeout.
    public LlmSelectorConfig WithCommandLine(string[] args)
    {
        LlmSelectorConfig resolved = this;
        if (CommandLineArgs.TryRead(args, EndpointArg, TryReadText, out string endpoint)) resolved.Endpoint = endpoint;
        if (CommandLineArgs.TryRead(args, ModelArg, TryReadText, out string model)) resolved.Model = model;
        if (CommandLineArgs.TryRead(args, TimeoutArg, TryReadFloat, out float timeout)) resolved.TimeoutSeconds = timeout;
        if (CommandLineArgs.TryRead(args, RetriesArg, TryReadInt, out int retries)) resolved.Retries = retries;
        if (CommandLineArgs.TryRead(args, TemperatureArg, TryReadFloat, out float temperature)) resolved.Temperature = temperature;
        if (CommandLineArgs.TryRead(args, SeedArg, TryReadInt, out int seed)) resolved.Seed = seed;
        if (CommandLineArgs.TryRead(args, PromptArg, TryReadText, out string prompt)) resolved.PromptId = prompt;
        if (CommandLineArgs.TryRead(args, ExemplarsArg, TryReadText, out string bank)) resolved.ExemplarsId = bank;
        if (CommandLineArgs.TryRead(args, ShotsArg, TryReadInt, out int shots)) resolved.Shots = shots;
        return resolved.Sanitized();
    }

    // A nonsense value would otherwise show up as a selector that never answers
    // or never stops retrying.
    public LlmSelectorConfig Sanitized()
    {
        LlmSelectorConfig clean = this;
        if (string.IsNullOrWhiteSpace(clean.Endpoint)) clean.Endpoint = Defaults.Endpoint;
        else clean.Endpoint = clean.Endpoint.Trim().TrimEnd('/');
        if (string.IsNullOrWhiteSpace(clean.Model)) clean.Model = Defaults.Model;
        else clean.Model = clean.Model.Trim();
        if (string.IsNullOrWhiteSpace(clean.PromptId)) clean.PromptId = Defaults.PromptId;
        else clean.PromptId = clean.PromptId.Trim();
        clean.ExemplarsId = string.IsNullOrWhiteSpace(clean.ExemplarsId) ? "" : clean.ExemplarsId.Trim();
        if (string.Equals(clean.ExemplarsId, NoExemplars, System.StringComparison.OrdinalIgnoreCase))
        {
            clean.ExemplarsId = "";
        }
        if (clean.Shots < 0) clean.Shots = 0;
        if (clean.TimeoutSeconds < 0.1f) clean.TimeoutSeconds = 0.1f;
        if (clean.Retries < 0) clean.Retries = 0;
        if (clean.Retries > 3) clean.Retries = 3;
        if (clean.Temperature < 0f) clean.Temperature = 0f;
        return clean;
    }

    // Ollama's generate endpoint; the base URL is what the Inspector and the
    // launch argument name.
    public string GenerateUrl => Endpoint + "/api/generate";

    static bool TryReadText(string value, out string parsed)
    {
        parsed = value;
        return !string.IsNullOrWhiteSpace(value);
    }

    static bool TryReadFloat(string value, out float parsed) =>
        float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed);

    static bool TryReadInt(string value, out int parsed) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed);
}
