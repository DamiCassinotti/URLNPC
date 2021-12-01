using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerWeapon : Weapon
{
    [SerializeField] Camera FPCamera;

    void Update()
    {
        if (Input.GetButtonDown("Fire1"))
        {
            Shoot();
        }
    }

    override protected Vector3 GetPosition()
    {
        return FPCamera.transform.position;
    }

    override protected Vector3 GetForward()
    {
        return FPCamera.transform.forward;
    }
}
