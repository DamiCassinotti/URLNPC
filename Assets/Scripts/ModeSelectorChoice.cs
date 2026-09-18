// Which selector commands the ModeChannel outside training, as pure logic —
// mirrors DriverSelector so the priority chain is testable without play mode
// or a real command line. ModeSelectorDriver is the adapter half.

public enum ModeSelectorKind
{
    // Nothing commands modes; the channel keeps whatever it holds.
    None = 0,
    Fixed = 1,
    Random = 2,
    Fsm = 3,
    Llm = 4,
}

public static class ModeSelectorChoice
{
    public const string CommandLineArg = "-modeSelector";

    // Highest priority first: command line, code-level override, Inspector
    // default. The scan is CommandLineArgs.TryRead: lenient, so an occurrence
    // naming something that isn't a selector is skipped and the first one that
    // does wins.
    public static ModeSelectorKind Resolve(
        string[] args,
        ModeSelectorKind? codeOverride,
        ModeSelectorKind inspectorDefault)
    {
        if (CommandLineArgs.TryRead(args, CommandLineArg, TryParseKind, out ModeSelectorKind fromArgs))
        {
            return fromArgs;
        }

        if (codeOverride.HasValue) return codeOverride.Value;
        return inspectorDefault;
    }

    static bool TryParseKind(string value, out ModeSelectorKind kind)
    {
        kind = ModeSelectorKind.None;
        switch (value.ToLowerInvariant())
        {
            case "none": kind = ModeSelectorKind.None; return true;
            case "fixed": kind = ModeSelectorKind.Fixed; return true;
            case "random": kind = ModeSelectorKind.Random; return true;
            case "fsm": kind = ModeSelectorKind.Fsm; return true;
            case "llm": kind = ModeSelectorKind.Llm; return true;
            default: return false;
        }
    }
}
