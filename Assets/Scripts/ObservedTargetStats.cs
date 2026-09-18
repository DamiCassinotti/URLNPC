using UnityEngine;

// What the NPC has learned about the player by watching them — the observed
// half of the game-state snapshot (issue #123). Sensory contract (issue #9):
// every figure accumulates only while the target is visible and freezes the
// moment sight is lost, so nothing here reflects state the NPC never saw.
//
// The engine adapter (GameStateSnapshotBuilder) feeds one sample per frame;
// the means are time-weighted so a variable frame rate doesn't skew them.
public class ObservedTargetStats
{
    // Whether the last sample saw the target — the gate RecordShot checks,
    // since shot events land between samples.
    public bool TargetVisible { get; private set; }

    public float VisibleSeconds { get; private set; }

    public int ShotsHeard { get; private set; }

    // Doubled like ModeMeanTally's sums: an episode is thousands of samples of
    // a few centimetres or metre-seconds each.
    double distanceSeconds;
    double movedMetres;
    double movedSeconds;
    Vector3 lastPosition;
    // The previous sample was visible, so a position delta is a real move and
    // not the jump across an unseen interval.
    bool chained;

    public void Sample(bool visible, Vector3 targetPosition, float distanceToTarget, float deltaSeconds)
    {
        if (!visible)
        {
            TargetVisible = false;
            chained = false;
            return;
        }
        if (deltaSeconds > 0f)
        {
            VisibleSeconds += deltaSeconds;
            distanceSeconds += (double)distanceToTarget * deltaSeconds;
            if (chained)
            {
                Vector3 moved = targetPosition - lastPosition;
                // Flat: a hop up a stair isn't walking speed, and the two body
                // origins sit at different heights (#103).
                moved.y = 0f;
                movedMetres += moved.magnitude;
                movedSeconds += deltaSeconds;
            }
        }
        TargetVisible = true;
        chained = true;
        lastPosition = targetPosition;
    }

    // A shot attributed to the target. The gate lives here rather than in the
    // adapter: "heard" is scoped the way every observed stat is — only while
    // the target is visible — so the adapter can forward every target shot.
    public void RecordShot()
    {
        if (TargetVisible) ShotsHeard++;
    }

    // Metres, time-weighted over the seconds the target was visible.
    public float MeanEngagementDistance =>
        VisibleSeconds > 0f ? (float)(distanceSeconds / VisibleSeconds) : 0f;

    // Metres per second over the watched moves; 0 until at least one sample
    // pair chained. The first sample of every visible interval contributes no
    // move — there is no previous position it is a real step from.
    public float MeanSpeed =>
        movedSeconds > 0.0 ? (float)(movedMetres / movedSeconds) : 0f;

    public float ShotsPer10Seconds =>
        VisibleSeconds > 0f ? ShotsHeard / VisibleSeconds * 10f : 0f;

    // Episode resets: last episode's scouting must not leak into the next.
    public void Reset()
    {
        TargetVisible = false;
        VisibleSeconds = 0f;
        ShotsHeard = 0;
        distanceSeconds = 0.0;
        movedMetres = 0.0;
        movedSeconds = 0.0;
        chained = false;
    }
}
