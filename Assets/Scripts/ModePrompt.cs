using System.Collections.Generic;
using System.Text;

// The prompt the LLM selector sends, as data rather than a string literal
// (issue #131): one text per variant, identified by id, so a battery run can
// compare two of them and any logged decision says which one produced it. The
// text itself is a Resources asset (ModePromptLibrary loads it, scripts/battery.py
// reads the same file for its offline twin); this is the rendering half — the
// static part comes from the template, the two moving parts are substituted in.
//
// A variant that drops {{HISTORY}} is the memoryless ablation: the history is
// simply not shown. {{STATE}} is mandatory — a prompt without the current
// snapshot is not asking about anything.
public class ModePrompt
{
    // The variant shipped as Resources/Prompts/v1.txt, and what every LLM run
    // uses unless -llmPrompt names another.
    public const string DefaultId = "v1";

    public const string HistoryToken = "{{HISTORY}}";
    public const string StateToken = "{{STATE}}";

    // What renders in place of the history before the first decision of a
    // round, so the model never sees an empty section it has to interpret.
    public const string NoHistory = "(none — this is the first decision of the round)";

    public readonly string Id;
    public readonly string Template;

    public ModePrompt(string id, string template)
    {
        Id = id ?? "";
        Template = template ?? "";
    }

    // A template is unusable without the state token; the rest of the text is
    // the variant's business.
    public bool IsUsable => Template.Contains(StateToken);

    public string Render(GameStateSnapshot current, IList<ModePromptTurn> history)
    {
        string rendered = Template.Replace(StateToken, ModeDecisionRecord.SnapshotObject(current));
        if (rendered.Contains(HistoryToken))
        {
            rendered = rendered.Replace(HistoryToken, RenderHistory(history));
        }
        return rendered;
    }

    // Deliberately not the full snapshot JSON per turn: three of those would
    // cost more tokens than the state being decided on. The fields kept are the
    // ones a mode decision turns on, which is also what makes the trend
    // readable ("still not visible, still losing HP").
    internal static string RenderHistory(IList<ModePromptTurn> history)
    {
        if (history == null || history.Count == 0) return NoHistory;
        var sb = new StringBuilder(256);
        for (int i = 0; i < history.Count; i++)
        {
            GameStateSnapshot s = history[i].Snapshot;
            if (i > 0) sb.Append('\n');
            // Counted back from now rather than timestamped: the decision
            // period is the unit the model is reasoning in, and a wall clock
            // would need the round clock to be running to mean anything.
            int ago = history.Count - i;
            sb.Append('-').Append(ago).Append(ago == 1 ? " decision:  " : " decisions: ");
            if (s == null)
            {
                sb.Append("(no state)");
            }
            else
            {
                sb.Append("hp ").Append(s.hpPercent).Append('%')
                  .Append(" | visible ").Append(s.targetVisible ? "yes" : "no")
                  .Append(" | dist ").Append(s.targetDistance)
                  .Append(" | seen ").Append(s.secondsSinceSeen).Append("s ago")
                  .Append(" | damaged ").Append(s.recentlyDamaged ? s.damagedFrom.ToString() : "no");
            }
            sb.Append(" -> ").Append(history[i].Mode);
        }
        return sb.ToString();
    }
}

// One past decision: the state it was made on and the mode it chose.
public struct ModePromptTurn
{
    public GameStateSnapshot Snapshot;
    public NpcMode Mode;

    public ModePromptTurn(GameStateSnapshot snapshot, NpcMode mode)
    {
        Snapshot = snapshot;
        Mode = mode;
    }
}

// The last few decisions, oldest first. Owned by LlmModeSelector and cleared
// per episode: a round's history has nothing to say about the next one.
public class ModePromptHistory
{
    public const int Capacity = 3;

    readonly List<ModePromptTurn> turns = new List<ModePromptTurn>(Capacity);

    public IList<ModePromptTurn> Turns => turns;

    public void Add(GameStateSnapshot snapshot, NpcMode mode)
    {
        turns.Add(new ModePromptTurn(snapshot, mode));
        while (turns.Count > Capacity) turns.RemoveAt(0);
    }

    public void Clear() => turns.Clear();
}
