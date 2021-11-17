using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Health : MonoBehaviour
{
    [SerializeField] float health = 100f;

    void Update()
    {
        if (this.health <= 0)
        {
            Destroy(gameObject);
        }
    }

    public void DecreaseHealth(float damage)
    {
        this.health -= damage;
    }
}
