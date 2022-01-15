using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Health : MonoBehaviour
{
    [SerializeField] public float health = 100f ;
    GameManager gameManager;

    void Start()
    {
        gameManager = FindObjectOfType<GameManager>();
    }

    public void DecreaseHealth(float damage)
    {
        this.health -= damage;
        this.CheckDeath();
    }

    void CheckDeath()
    {
        if (this.health <= 0)
        {
            gameManager.ProcessDeath(gameObject.tag);
        }
    }
}
