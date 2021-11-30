using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class Health : MonoBehaviour
{
    [SerializeField] float health = 100f;
    Counter counter;

    void Start()
    {
        counter = FindObjectOfType<Counter>();
    }

    void Update()
    {

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
            ProcessDeath();
        }
    }

    void ProcessDeath()
    {
        counter.UserWins();
        //Destroy(gameObject);
        //ChoseNewLevel();
    }

    void ChoseNewLevel()
    {
        StartCoroutine(WaitSomeSeconds());
        SceneManager.LoadScene(0);
    }

    IEnumerator WaitSomeSeconds()
    {
       yield return new WaitForSeconds(3);
    }
}
