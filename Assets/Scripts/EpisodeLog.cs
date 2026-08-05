using System.Collections.Generic;
using System.Text;

// Per-episode telemetry bookkeeping as plain state, engine-free so it is
// unit-testable: which episode events belong to, when it started, and what
// each side did. TelemetryLogger owns one and feeds it Time.time.
public class EpisodeLog
{
    class SideStats
    {
        public float damageDealt;
        public float damageTaken;
        public int shots;
        public int hits;

        public float Accuracy => shots > 0 ? (float)hits / shots : 0f;
    }

    readonly Dictionary<string, SideStats> sides = new Dictionary<string, SideStats>();
    bool openedByRoundEnd;

    public int Index { get; private set; }
    public float StartTime { get; private set; }

    public void Begin(float now)
    {
        Index++;
        StartTime = now;
        openedByRoundEnd = false;
        sides.Clear();
    }

    public float Elapsed(float now) => now - StartTime;

    public void RecordShot(string shooter, bool hit, float damage)
    {
        SideStats stats = Side(shooter);
        stats.shots++;
        if (!hit) return;
        stats.hits++;
        stats.damageDealt += damage;
    }

    public void RecordDamage(string victim, float amount) => Side(victim).damageTaken += amount;

    // Opens the next episode immediately: during training the round restarts
    // in place, with no scene reload to do it.
    public void EndRound(float now)
    {
        Begin(now);
        openedByRoundEnd = true;
    }

    // The reload a decided round triggers belongs to the episode EndRound
    // already opened — restamp it rather than burn a second index and leave a
    // phantom episode with no summary.
    public void SceneLoaded(float now)
    {
        if (!openedByRoundEnd)
        {
            Begin(now);
            return;
        }
        openedByRoundEnd = false;
        StartTime = now;
    }

    public string SidesJson()
    {
        var sb = new StringBuilder(64);
        sb.Append("\"sides\":{");
        bool first = true;
        foreach (KeyValuePair<string, SideStats> kv in sides)
        {
            if (!first) sb.Append(',');
            first = false;
            SideStats s = kv.Value;
            sb.Append('"').Append(JsonLine.Escape(kv.Key)).Append("\":{")
              .Append(JsonLine.Field("damageDealt", s.damageDealt)).Append(',')
              .Append(JsonLine.Field("damageTaken", s.damageTaken)).Append(',')
              .Append(JsonLine.Field("shots", s.shots)).Append(',')
              .Append(JsonLine.Field("hits", s.hits)).Append(',')
              .Append(JsonLine.Field("accuracy", s.Accuracy)).Append('}');
        }
        return sb.Append('}').ToString();
    }

    SideStats Side(string tag)
    {
        if (!sides.TryGetValue(tag, out SideStats stats))
        {
            stats = new SideStats();
            sides[tag] = stats;
        }
        return stats;
    }
}
