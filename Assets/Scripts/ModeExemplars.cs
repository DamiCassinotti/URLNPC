using System.Collections.Generic;
using System.Text;

// The few-shot exemplar bank (issue #132): curated state -> answer pairs shown
// to the model ahead of the state it has to decide on. No weights move in this
// tier, so the bank is the only tuned artifact it has — versioned like the
// prompt, and kept disjoint from battery/snapshots.json, since an exemplar that
// is also a battery item is an answer the model was handed rather than one it
// found.
//
// Stored as the text it renders into rather than as a snapshot structure: the
// state line is the same JSON ModeDecisionRecord.SnapshotObject emits, so an
// exemplar reads exactly like the state the prompt ends with, and
// scripts/battery.py renders the same file the same way the game does.
public class ModeExemplars
{
    // The bank shipped as Resources/Exemplars/bank-v1.txt.
    public const string DefaultId = "bank-v1";

    // The block carries its own heading, so a prompt whose bank is empty has no
    // dangling section left behind (ModePrompt drops the slot entirely).
    public const string Heading =
        "Worked examples — states of the kind below and the answer each should get:";

    public readonly string Id;
    public readonly IList<ModeExemplar> Entries;

    ModeExemplars(string id, List<ModeExemplar> entries)
    {
        Id = id;
        Entries = entries;
    }

    public int Count => Entries.Count;

    // One entry per three non-blank lines: the id, the state JSON, the answer
    // JSON. '#' starts a comment anywhere. A malformed entry fails the whole
    // bank rather than being skipped — a bank quietly one exemplar short would
    // be a shot count the run's label disagrees with.
    public static bool TryParse(string id, string text, out ModeExemplars bank, out string error)
    {
        bank = null;
        error = "";
        var entries = new List<ModeExemplar>();
        var ids = new HashSet<string>();

        string pendingId = null;
        string pendingState = null;
        foreach (string raw in (text ?? "").Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line[0] == '#') continue;

            if (line[0] != '{')
            {
                if (pendingId != null)
                {
                    error = $"exemplar '{pendingId}' is missing its state or answer line";
                    return false;
                }
                pendingId = line;
                continue;
            }

            if (pendingId == null)
            {
                error = $"a JSON line with no exemplar id above it: {Excerpt(line)}";
                return false;
            }
            if (pendingState == null)
            {
                pendingState = line;
                continue;
            }

            if (!LlmModeResponse.TryParse(line, out LlmModeResponse answer))
            {
                // The production parser, deliberately: an exemplar answer the
                // selector could not read is one the model is being taught to
                // imitate.
                error = $"exemplar '{pendingId}' has an answer that names no mode: {Excerpt(line)}";
                return false;
            }
            if (!ids.Add(pendingId))
            {
                error = $"duplicate exemplar id '{pendingId}'";
                return false;
            }
            entries.Add(new ModeExemplar(pendingId, pendingState, line, answer.Mode));
            pendingId = null;
            pendingState = null;
        }

        if (pendingId != null)
        {
            error = $"exemplar '{pendingId}' is missing its state or answer line";
            return false;
        }
        if (entries.Count == 0)
        {
            error = "the bank holds no exemplars";
            return false;
        }

        bank = new ModeExemplars(id ?? "", entries);
        return true;
    }

    // Round-robin over the modes in NpcModes order, taking each mode's entries
    // in curated order: the bank-size sweep then compares balanced subsets, and
    // a four-shot prompt shows one of each rather than four Hunts.
    public IList<ModeExemplar> Take(int shots)
    {
        var byMode = new List<List<ModeExemplar>>();
        foreach (NpcMode mode in NpcModes.All)
        {
            var row = new List<ModeExemplar>();
            foreach (ModeExemplar entry in Entries)
            {
                if (entry.Mode == mode) row.Add(entry);
            }
            byMode.Add(row);
        }

        int wanted = shots <= 0 || shots > Entries.Count ? Entries.Count : shots;
        var picked = new List<ModeExemplar>(wanted);
        for (int round = 0; picked.Count < wanted; round++)
        {
            bool any = false;
            foreach (List<ModeExemplar> row in byMode)
            {
                if (round >= row.Count) continue;
                any = true;
                picked.Add(row[round]);
                if (picked.Count == wanted) break;
            }
            if (!any) break;
        }
        return picked;
    }

    // What goes into the prompt's slot. Empty for no shots, which is what
    // collapses the slot and leaves the zero-shot text.
    public string Render(int shots)
    {
        IList<ModeExemplar> picked = Take(shots);
        if (picked.Count == 0) return "";

        var sb = new StringBuilder(1024);
        sb.Append(Heading);
        foreach (ModeExemplar entry in picked)
        {
            sb.Append("\n\nState: ").Append(entry.State)
              .Append("\nAnswer: ").Append(entry.Answer);
        }
        return sb.ToString();
    }

    static string Excerpt(string line) =>
        line.Length > 60 ? line.Substring(0, 60) + "..." : line;
}

// One curated pair. State and Answer are the lines as the bank stores them —
// rendered verbatim, so the file is what the model sees.
public struct ModeExemplar
{
    public readonly string Id;
    public readonly string State;
    public readonly string Answer;
    public readonly NpcMode Mode;

    public ModeExemplar(string id, string state, string answer, NpcMode mode)
    {
        Id = id;
        State = state;
        Answer = answer;
        Mode = mode;
    }
}
