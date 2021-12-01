using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public abstract class Weapon : MonoBehaviour
{

    [SerializeField] float range = 100f;
    [SerializeField] float damage = 50f;
    [SerializeField] ParticleSystem muzzleFlash;
    [SerializeField] GameObject hitEffect;

    protected abstract Vector3 GetPosition();
    protected abstract Vector3 GetForward();

    public void Shoot()
    {
        muzzleFlash.Play();
        ProcessRaycast();
    }

    void ProcessRaycast()
    {
        RaycastHit hit;
        if (Physics.Raycast(GetPosition(), GetForward(), out hit, range))
        {
            CreateHitImpact(hit);
            ProcessHitEnemy(hit);
        }
    }

    void ProcessHitEnemy(RaycastHit hit)
    {
        Health target = hit.transform.GetComponent<Health>();
        if (target != null)
        {
            target.DecreaseHealth(damage);
        }
    }

    void CreateHitImpact(RaycastHit hit)
    {
        GameObject impact = Instantiate(hitEffect, hit.point, Quaternion.LookRotation(hit.normal));
        Destroy(impact, .1f);
    }
}
