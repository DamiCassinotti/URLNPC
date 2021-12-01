using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class EnemyWeapon : Weapon
{

    override protected Vector3 GetPosition()
    {
        return gameObject.transform.position;
    }

    override protected Vector3 GetForward()
    {
        return gameObject.transform.forward;
    }

}
