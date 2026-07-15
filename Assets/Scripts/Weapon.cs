using UnityEngine;

public abstract class Weapon : MonoBehaviour
{

    [SerializeField] float range = 100f;
    [SerializeField] float damage = 50f;
    [SerializeField] ParticleSystem muzzleFlash;
    [SerializeField] GameObject hitEffect;

    [Header("Tracer")]
    [SerializeField] Color tracerColor = new Color(1f, 0.85f, 0.3f, 1f);
    [SerializeField] float tracerWidth = 0.04f;
    [SerializeField] float tracerDuration = 0.06f;
    [SerializeField] Transform muzzleOrigin;

    static Material s_tracerMaterial;

    protected abstract Vector3 GetPosition();
    protected abstract Vector3 GetForward();

    /// <summary>
    /// Telemetry hook (issue #12): fired once per shot with the weapon, the
    /// Health that was hit (null on a miss) and the damage applied.
    /// </summary>
    public static event System.Action<Weapon, Health, float> ShotFired;

    public void Shoot()
    {
        if (muzzleFlash != null) muzzleFlash.Play();
        ProcessRaycast();
    }

    void ProcessRaycast()
    {
        Vector3 origin = GetPosition();
        Vector3 direction = GetForward();
        Vector3 endPoint = origin + direction * range;
        Health victim = null;

        if (Physics.Raycast(origin, direction, out RaycastHit hit, range))
        {
            endPoint = hit.point;
            CreateHitImpact(hit);
            victim = ProcessHitEnemy(hit);
        }

        ShotFired?.Invoke(this, victim, victim != null ? damage : 0f);
        SpawnTracer(GetTracerStart(origin), endPoint);
    }

    Vector3 GetTracerStart(Vector3 fallback)
    {
        if (muzzleOrigin != null) return muzzleOrigin.position;
        if (muzzleFlash != null) return muzzleFlash.transform.position;
        return fallback;
    }

    void SpawnTracer(Vector3 start, Vector3 end)
    {
        GameObject go = new GameObject("Tracer");
        var lr = go.AddComponent<LineRenderer>();
        lr.material = GetTracerMaterial();
        lr.startColor = tracerColor;
        lr.endColor = tracerColor;
        lr.startWidth = tracerWidth;
        lr.endWidth = tracerWidth;
        lr.positionCount = 2;
        lr.SetPosition(0, start);
        lr.SetPosition(1, end);
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
        lr.useWorldSpace = true;
        Destroy(go, tracerDuration);
    }

    static Material GetTracerMaterial()
    {
        if (s_tracerMaterial != null) return s_tracerMaterial;
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Sprites/Default");
        s_tracerMaterial = new Material(shader);
        return s_tracerMaterial;
    }

    // Returns the Health that took the hit, or null if the surface wasn't a combatant.
    Health ProcessHitEnemy(RaycastHit hit)
    {
        Health target = hit.transform.GetComponentInParent<Health>();
        if (target != null)
        {
            target.DecreaseHealth(damage);
        }
        return target;
    }

    void CreateHitImpact(RaycastHit hit)
    {
        if (hitEffect == null) return;
        GameObject impact = Instantiate(hitEffect, hit.point, Quaternion.LookRotation(hit.normal));
        Destroy(impact, .1f);
    }
}
