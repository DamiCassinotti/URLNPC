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
        ProcessNpcDeath();
        ProcessPlayerDeath();
        ChoseNewLevel();
    }

    void ProcessNpcDeath()
    {
        if (gameObject.tag == "NPC")
        {
            counter.UserWins();
            Destroy(gameObject);
        }
    }

    void ProcessPlayerDeath()
    {
        if (gameObject.tag == "Player")
        {
            counter.NpcWins();
        }
    }

    void ChoseNewLevel()
    {
        StartCoroutine(WaitSomeSeconds());
        int currentSceneIndex = SceneManager.GetActiveScene().buildIndex;
        SceneManager.LoadScene(currentSceneIndex);
    }

    IEnumerator WaitSomeSeconds()
    {
       yield return new WaitForSeconds(3);
    }
}
