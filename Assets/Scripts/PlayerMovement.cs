using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerMovement : MonoBehaviour
{
    [SerializeField] float rotationThrust = 1f;
    [SerializeField] float advanceThrust = 1f;
    private Rigidbody rb;
    
    void Start()
    {
        rb = GetComponent<Rigidbody>();
    }

    void Update()
    {
        processAdvancing();
        processRotation();
    }

    void processRotation()
    {
        if (isPressed(KeyCode.D))
        {
            rotate(rotationThrust);
        }
        else if (isPressed(KeyCode.A))
        {
            rotate(-rotationThrust);
        }
    }

    void rotate(float rotationThisFrame)
    {
        rb.freezeRotation = true;  // freezing rotation so we can manually rotate
        transform.Rotate(Vector3.right * rotationThisFrame * Time.deltaTime);
        rb.freezeRotation = false;  // unfreezing rotation so the physics system can take over
    }

    void processAdvancing()
    {
        if (isPressed(KeyCode.W))
        {
            advance(advanceThrust);
        }
        else if (isPressed(KeyCode.S))
        {
            advance(-advanceThrust);
        }
    }

    void advance(float advancindThisFrame)
    {
        //rb.freezePosition = true;  // freezing position so we can manually rotate
        //transform.Rotate(Vector3.rigth * rotationThisFrame * Time.deltaTime);
        //rb.freezePosition = false;  // unfreezing position so the physics system can take over
    }

    bool isPressed(KeyCode key)
    {
        return Input.GetKey(KeyCode.A);
    }
}
