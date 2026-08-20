// Which driver takes the player body, as pure logic — engine-free so the
// priority chain is testable without play mode or a real command line.
// CombatantRig is the adapter half.
public static class DriverSelector
{
    public const string CommandLineArg = "-playerDriver";

    // Highest priority first: command line, code-level override, Inspector
    // default.
    //
    // The scan is CommandLineArgs.TryRead: lenient, so an occurrence naming
    // something that isn't a driver is skipped and the first one that does
    // wins.
    public static CombatantRig.DriverKind Resolve(
        string[] args,
        CombatantRig.DriverKind? codeOverride,
        CombatantRig.DriverKind inspectorDefault)
    {
        if (CommandLineArgs.TryRead(args, CommandLineArg, TryParseDriver, out CombatantRig.DriverKind fromArgs))
        {
            return fromArgs;
        }

        if (codeOverride.HasValue) return codeOverride.Value;
        return inspectorDefault;
    }

    static bool TryParseDriver(string value, out CombatantRig.DriverKind driver)
    {
        driver = CombatantRig.DriverKind.Human;
        switch (value.ToLowerInvariant())
        {
            case "agent": driver = CombatantRig.DriverKind.Agent; return true;
            case "human": driver = CombatantRig.DriverKind.Human; return true;
            default: return false;
        }
    }
}
