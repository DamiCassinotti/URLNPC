using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerMovement : MonoBehaviour
{
    [SerializeField] float rotationThrust = 200f;
    [SerializeField] float advanceThrust = 1f;
    private Rigidbody rb;
    
    void Start()
    {
        rb = GetComponent<Rigidbody>();
    }

    void Update()
    {
        ProcessAdvancing();
        ProcessRotation();
    }

    void ProcessRotation()
    {
        if (IsPressed(KeyCode.D))
        {
            Rotate(rotationThrust);
        }
        else if (IsPressed(KeyCode.A))
        {
            Rotate(-rotationThrust);
        }
    }

    void Rotate(float rotationThisFrame)
    {
        transform.Rotate(Vector3.up * rotationThisFrame * Time.deltaTime);
    }

    void ProcessAdvancing()
    {
        if (IsPressed(KeyCode.W))
        {
            Advance(advanceThrust);
        }
        else if (IsPressed(KeyCode.S))
        {
            Advance(-advanceThrust);
        }
    }

    void Advance(float advancingThisFrame)
    {
        transform.Translate(Vector3.forward * advancingThisFrame * Time.deltaTime);
    }

    bool IsPressed(KeyCode key)
    {
        return Input.GetKey(key);
    }
}
