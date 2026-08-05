/// <summary>
/// Which driver takes the player body, as pure logic — engine-free so the
/// priority chain is unit-testable without play mode or a real command line.
/// <see cref="CombatantRig"/> owns the adapter half: it passes
/// <c>System.Environment.GetCommandLineArgs()</c>, the static
/// <see cref="CombatantRig.DriverOverride"/> and its serialized field in.
/// </summary>
public static class DriverSelector
{
    /// <summary>Command line flag: <c>-playerDriver human|agent</c>.</summary>
    public const string CommandLineArg = "-playerDriver";

    /// <summary>
    /// Resolve the driver, highest priority first: command line, then a
    /// code-level override, then the Inspector default.
    ///
    /// The command line scan is deliberately lenient — the editor and player
    /// are launched with plenty of arguments this code knows nothing about, so
    /// an unparseable occurrence (unrecognized value, or the flag appearing as
    /// the very last argument with nothing after it) is skipped rather than
    /// treated as an error. The first occurrence that names a real driver wins.
    /// </summary>
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
