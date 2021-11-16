using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class GameLogic : MonoBehaviour
{
    [SerializeField] float health = 100f;
    [SerializeField] private float bulletDamage = 10;
    void OnParticleCollision(GameObject other)
    {
        Debug.Log($"{name}I'm hit! by {other.gameObject.name}");
        TakeDamage();
    }

    void TakeDamage()
    {
        health -= bulletDamage;
        if (health <= 0)
        {
            Destroy(gameObject);
        }
    }
}
