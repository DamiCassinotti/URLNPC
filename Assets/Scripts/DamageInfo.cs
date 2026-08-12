using UnityEngine;

// What Health.OnDamaged carries. The shooter's position is threaded through
// from Weapon so a combatant can react to being shot from a direction it can't
// see; damage with no shooter (scripted, environment, tests) leaves HasSource
// false rather than pointing at the origin.
public readonly struct DamageInfo
{
    public readonly float Amount;
    public readonly Vector3 SourcePosition;
    public readonly bool HasSource;

    public DamageInfo(float amount)
    {
        Amount = amount;
        SourcePosition = Vector3.zero;
        HasSource = false;
    }

    public DamageInfo(float amount, Vector3 sourcePosition)
    {
        Amount = amount;
        SourcePosition = sourcePosition;
        HasSource = true;
    }
}
