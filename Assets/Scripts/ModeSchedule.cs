// The mode-sampling rules behind ModeDirector, engine-free so they are testable
// without play mode: "now" comes in as a value and the draw comes in as a
// function, so a test can pin both.
public class ModeSchedule
{
    // Shortest time a drawn mode is held before the next draw. Zero or less
    // draws every tick.
    public float MinDwellSeconds = 5f;

    // Modes this run may command. Empty means never draw.
    public NpcMode[] Pool = System.Array.Empty<NpcMode>();

    // Only meaningful once HasDrawn is true.
    public NpcMode Current { get; private set; }

    public bool HasDrawn { get; private set; }

    float lastDrawTime;

    // pickIndex(count) must return [0, count). Returns true when it drew — the
    // draw may repeat the current mode, which simply extends the dwell. Letting
    // repeats through keeps the distribution uniform; forbidding them would
    // make a two-mode pool alternate deterministically.
    public bool TryAdvance(float now, System.Func<int, int> pickIndex)
    {
        if (Pool.Length == 0) return false;
        if (HasDrawn && now - lastDrawTime < MinDwellSeconds) return false;

        Current = Pool[pickIndex(Pool.Length)];
        lastDrawTime = now;
        HasDrawn = true;
        return true;
    }

    // Episode resets: the next tick draws a fresh mode with a full dwell.
    public void Reset()
    {
        HasDrawn = false;
        lastDrawTime = 0f;
    }
}
