// Which driver takes the player body, as pure logic — engine-free so the
// priority chain is testable without play mode or a real command line.
// CombatantRig is the adapter half.
public static class DriverSelector
{
    public const string CommandLineArg = "-playerDriver";

    // Highest priority first: command line, code-level override, Inspector
    // default.
    //
    // The command line scan is deliberately lenient — the editor and player are
    // launched with plenty of arguments this code knows nothing about, so an
    // unparseable occurrence is skipped rather than treated as an error. The
    // first occurrence naming a real driver wins.
    public static CombatantRig.DriverKind Resolve(
        string[] args,
        CombatantRig.DriverKind? codeOverride,
        CombatantRig.DriverKind inspectorDefault)
    {
        if (args != null)
        {
            // Stop one short of the end: the flag needs a value after it.
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] != CommandLineArg) continue;
                string value = args[i + 1];
                if (value == null) continue;
                switch (value.ToLowerInvariant())
                {
                    case "agent": return CombatantRig.DriverKind.Agent;
                    case "human": return CombatantRig.DriverKind.Human;
                }
            }
        }

        if (codeOverride.HasValue) return codeOverride.Value;
        return inspectorDefault;
    }
}
