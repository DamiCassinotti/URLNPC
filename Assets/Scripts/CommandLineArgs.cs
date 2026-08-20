// The one "find -flag and read the value after it" scan, shared by everything
// that takes a launch argument (-runSeed, -playerDriver, -trainModes). Pure
// logic, so each caller's parsing is testable without a real command line.
public static class CommandLineArgs
{
    public delegate bool TryParse<T>(string value, out T parsed);

    // Deliberately lenient: the editor and player are launched with plenty of
    // arguments this code knows nothing about, so an occurrence whose value
    // doesn't parse is skipped and the scan continues — the first one that
    // parses wins.
    public static bool TryRead<T>(string[] args, string flag, TryParse<T> parse, out T parsed)
    {
        parsed = default;
        if (args == null) return false;

        // Stop one short of the end: the flag needs a value after it.
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] != flag) continue;
            string value = args[i + 1];
            if (value != null && parse(value, out parsed)) return true;
        }
        parsed = default; // a failed parse may have written to it
        return false;
    }

    public static bool Contains(string[] args, string flag)
    {
        return args != null && System.Array.IndexOf(args, flag) >= 0;
    }
}
