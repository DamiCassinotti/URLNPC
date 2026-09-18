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

// A resolved pick: the kind, plus the pinned mode when the command line spelt
// it as "fixed:<Mode>". Null means the driver's serialized default applies.
public struct ModeSelectorSelection
{
    public ModeSelectorKind Kind;
    public NpcMode? FixedMode;
}

public static class ModeSelectorChoice
{
    public const string CommandLineArg = "-modeSelector";

    // Highest priority first: command line, code-level override, Inspector
    // default. The scan is CommandLineArgs.TryRead: lenient, so an occurrence
    // naming something that isn't a selector is skipped and the first one that
    // does wins.
    public static ModeSelectorSelection ResolveSelection(
        string[] args,
        ModeSelectorKind? codeOverride,
        ModeSelectorKind inspectorDefault)
    {
        if (CommandLineArgs.TryRead(args, CommandLineArg, TryParseSelection, out ModeSelectorSelection fromArgs))
        {
            return fromArgs;
        }

        return new ModeSelectorSelection { Kind = codeOverride ?? inspectorDefault };
    }

    public static ModeSelectorKind Resolve(
        string[] args,
        ModeSelectorKind? codeOverride,
        ModeSelectorKind inspectorDefault)
    {
        return ResolveSelection(args, codeOverride, inspectorDefault).Kind;
    }

    // "fixed:<Mode>" pins the mode along with the kind; only Fixed takes the
    // suffix, and a suffix naming no mode fails the whole value rather than
    // dropping to the driver's default — a typo must not pin the wrong mode.
    public static bool TryParseSelection(string value, out ModeSelectorSelection selection)
    {
        selection = default;
        string kindPart = value;
        string suffix = null;
        int colon = value.IndexOf(':');
        if (colon >= 0)
        {
            kindPart = value.Substring(0, colon);
            suffix = value.Substring(colon + 1);
        }

        if (!TryParseKind(kindPart, out ModeSelectorKind kind)) return false;
        if (suffix != null)
        {
            if (kind != ModeSelectorKind.Fixed) return false;
            // TryParse alone accepts any integer; IsDefined keeps "fixed:7" out.
            if (!System.Enum.TryParse(suffix, true, out NpcMode mode)
                || !System.Enum.IsDefined(typeof(NpcMode), mode)) return false;
            selection.FixedMode = mode;
        }
        selection.Kind = kind;
        return true;
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
