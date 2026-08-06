using UnityEngine;

public class EnemyWeapon : Weapon
{
    [Header("Enemy Aim")]
    [Tooltip("Max angular error applied to each shot, in degrees. 0 = perfect aim.")]
    [SerializeField] float aimSpreadDegrees = 5f;

    override protected Vector3 GetPosition()
    {
        return gameObject.transform.position;
    }

    override protected Vector3 GetForward()
    {
        Vector3 forward = gameObject.transform.forward;
        if (aimSpreadDegrees <= 0f) return forward;
        // Deliberately UnityEngine.Random, not RunRng: this is drawn a
        // policy-dependent number of times, which would desync the
        // reproducible streams between runs.
        Vector3 perp = Vector3.Cross(forward, Random.onUnitSphere).normalized;
        if (perp.sqrMagnitude < 1e-4f) perp = Vector3.up;
        float angle = Random.Range(-aimSpreadDegrees, aimSpreadDegrees);
        return Quaternion.AngleAxis(angle, perp) * forward;
    }
}
