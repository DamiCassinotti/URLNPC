using System;
using UnityEngine;

public class Health : MonoBehaviour
{
    [SerializeField] public float maxHealth = 100f;
    [SerializeField] public float health = 100f;
    GameManager gameManager;

    public event Action<DamageInfo> OnDamaged;
    public event Action OnDied;

    bool isDead;

    void Start()
    {
        gameManager = FindAnyObjectByType<GameManager>();
    }

    // Damage with no known shooter.
    public void DecreaseHealth(float damage) => DecreaseHealth(new DamageInfo(damage));

    public void DecreaseHealth(DamageInfo info)
    {
        if (isDead) return;
        this.health -= info.Amount;
        OnDamaged?.Invoke(info);
        this.CheckDeath();
    }

    public void ResetHealth()
    {
        this.health = maxHealth;
        isDead = false;
    }

    void CheckDeath()
    {
        if (this.health <= 0)
        {
            isDead = true;
            OnDied?.Invoke();
            if (gameManager != null) gameManager.ProcessDeath(gameObject.tag);
        }
    }
}
