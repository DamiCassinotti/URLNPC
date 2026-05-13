using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerWeapon : Weapon
{
    [SerializeField] Camera FPCamera;

    void Update()
    {
        var mouse = Mouse.current;
        if (mouse != null && mouse.leftButton.wasPressedThisFrame)
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
