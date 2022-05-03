using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CamCtrl : MonoBehaviour
{
    public float speed = 300;
    private float engineMultiple = 0;
    // Start is called before the first frame update
    void Start()
    {
    }

    // Update is called once per frame
    void Update()
    {
        float tempV = Input.GetAxis("Vertical");
        float rotateInputX = Input.GetAxis("Mouse X");
        float rotateInputY = Input.GetAxis("Mouse Y");
        float tempH = Input.GetAxis("Horizontal");
        float tempDH = Input.GetAxis("rotationForward");

        if (Input.GetKeyDown(KeyCode.Alpha1))
            engineMultiple = 1;
        else if (Input.GetKeyDown(KeyCode.Alpha2))
            engineMultiple = 10;
        else if (Input.GetKeyDown(KeyCode.Alpha3))
            engineMultiple = 100;
        else if (Input.GetKeyDown(KeyCode.Alpha4))
            engineMultiple = 1000;
        else if (Input.GetKeyDown(KeyCode.Alpha5))
            engineMultiple = 10000;

        transform.Rotate(-rotateInputY, rotateInputX, -tempDH, Space.Self);

        transform.position = transform.position + transform.forward * tempV * speed * engineMultiple * Time.deltaTime;
        transform.position = transform.position + transform.right * tempH * speed * engineMultiple * Time.deltaTime;
    }
}
