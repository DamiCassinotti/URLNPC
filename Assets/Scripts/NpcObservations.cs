using UnityEngine;

// Everything the policy is allowed to know this step, gathered by EnemyAgent and
// handed to NpcObservations.Fill. Target info comes off PerceptionMemory only
// (sensory contract, issue #9) — there is deliberately no field for the target's
// true position or HP.
public struct NpcObservationInput
{
    public bool canAttack;
    public float cooldownRemaining01;

    public bool targetVisible;
    public bool hasEverSeen;
    public Vector3 lastSeenPosition;
    public float timeSinceSeen;

    public Vector3 selfPosition;
    public Vector3 selfForward;
    public float sightRange;

    public float normalizedHealth;
    public float normalizedSpeed;

    public bool recentlyDamaged;
    public HitDirection hitDirection;
    public NpcMode mode;
}

// The frozen observation layout (#43), engine-free so the whole 18-float table
// is testable without an Academy. Index order is the contract: reordering or
// re-meaning any slot invalidates every trained model.
//
//  0     canAttack
//  1     cooldown remaining, normalized
//  2     target visible
//  3     distance to the perceived position / sightRange   (1 when never seen)
//  4,5   bearing sin, cos vs own forward                   (0,0 when never seen)
//  6     time since seen / SeenHorizonSeconds              (1 when never seen)
//  7     own HP, normalized
//  8     recently damaged
//  9-12  hit direction one-hot: front, back, left, right
//  13-16 commanded mode one-hot, in NpcModes.All order
//  17    own speed, normalized
public static class NpcObservations
{
    // Past this the "how stale is my sighting" input saturates; a memory older
    // than a few seconds is equally useless whatever its exact age.
    public const float SeenHorizonSeconds = 10f;

    // The mode one-hot occupies exactly these slots; a fifth NpcMode would need
    // a wider observation vector, not a quiet overflow into index 17.
    public const int ModeOneHotStart = 13;
    public const int ModeOneHotSlots = 4;

    public static void Fill(float[] into, in NpcObservationInput input)
    {
        if (into == null || into.Length != NpcBrainSpec.ObservationSize)
        {
            throw new System.ArgumentException(
                $"observation buffer must be exactly {NpcBrainSpec.ObservationSize} long", nameof(into));
        }

        into[0] = input.canAttack ? 1f : 0f;
        into[1] = Clamp01(input.cooldownRemaining01);
        into[2] = input.targetVisible ? 1f : 0f;

        // Never seen: maximum distance and no bearing, so the untrained-on
        // "I have no idea where they are" state is a single fixed point rather
        // than whatever the zeroed LastSeenPosition happens to imply.
        float distance01 = 1f;
        float bearingSin = 0f;
        float bearingCos = 0f;
        if (input.hasEverSeen)
        {
            Vector3 toTarget = Flat(input.lastSeenPosition - input.selfPosition);
            float range = input.sightRange > 0f ? input.sightRange : 1f;
            distance01 = Clamp01(toTarget.magnitude / range);

            Vector3 forward = Flat(input.selfForward);
            if (toTarget.sqrMagnitude > 1e-6f && forward.sqrMagnitude > 1e-6f)
            {
                toTarget.Normalize();
                forward.Normalize();
                bearingCos = Vector3.Dot(forward, toTarget);
                bearingSin = Vector3.Dot(Vector3.Cross(Vector3.up, forward), toTarget);
            }
        }
        into[3] = distance01;
        into[4] = bearingSin;
        into[5] = bearingCos;
        // Infinity (never seen) clamps to 1, same as a long-lapsed sighting.
        into[6] = Clamp01(input.timeSinceSeen / SeenHorizonSeconds);

        into[7] = Clamp01(input.normalizedHealth);

        into[8] = input.recentlyDamaged ? 1f : 0f;
        into[9] = input.hitDirection == HitDirection.Front ? 1f : 0f;
        into[10] = input.hitDirection == HitDirection.Back ? 1f : 0f;
        into[11] = input.hitDirection == HitDirection.Left ? 1f : 0f;
        into[12] = input.hitDirection == HitDirection.Right ? 1f : 0f;

        for (int i = 0; i < ModeOneHotSlots; i++)
        {
            into[ModeOneHotStart + i] = (int)input.mode == i ? 1f : 0f;
        }

        into[17] = Clamp01(input.normalizedSpeed);
    }

    static Vector3 Flat(Vector3 v)
    {
        v.y = 0f;
        return v;
    }

    // Mathf.Clamp01 passes NaN through, and a NaN observation trips ML-Agents'
    // own check deep inside the sensor where the source is no longer visible.
    static float Clamp01(float value)
    {
        if (float.IsNaN(value)) return 0f;
        return Mathf.Clamp01(value);
    }
}
