using UnityEngine;
using UnityEngine.AI;

public class EnemyWeapon : Weapon
{
    [Header("Enemy Aim")]
    [Tooltip("Max angular error applied to each shot, in degrees. 0 = perfect aim. Ignored at runtime — CombatBalance.AimSpreadDegrees is forced on in Awake.")]
    [SerializeField] float aimSpreadDegrees = CombatBalance.AimSpreadDegrees;

    NavMeshAgent navMeshAgent;

    protected override void Awake()
    {
        base.Awake();
        aimSpreadDegrees = CombatBalance.AimSpreadDegrees;
        navMeshAgent = GetComponent<NavMeshAgent>();
    }

    override protected Vector3 GetPosition()
    {
        // Not the bare transform (#103): the Enemy prefab's origin is its
        // capsule centre lifted 1 m onto the mesh, so it happened to read as
        // chest height, while CombatantRig's player body has its origin at the
        // feet and fired from the ankles — its rays clipped the cover the
        // enemy's cleared. No agent means no lifted origin to correct for.
        if (navMeshAgent == null) return transform.position;
        return transform.position
            + Vector3.up * BodyMetrics.MuzzleOffset(navMeshAgent.height, navMeshAgent.baseOffset);
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
