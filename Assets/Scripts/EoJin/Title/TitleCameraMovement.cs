using UnityEngine;

public class TitleCameraMovement : MonoBehaviour
{
    [SerializeField] float moveAmount = 2.0f;
    [SerializeField] float moveSpeed = 1.5f;
    bool isBack = true;
    [SerializeField] float lerpTime = 10f;
    float timer = 0f;

    private void Update()
    {
        if (!isBack)
        {
            timer -= Time.deltaTime * lerpTime;
            transform.Translate(Vector3.forward * (moveSpeed / timer));

            if (transform.position.z > -10 + moveAmount)
            {
                isBack = true;
                timer = lerpTime;
            }
        }
        else
        {
            timer -= Time.deltaTime * lerpTime;
            transform.Translate(Vector3.back * (moveSpeed / timer));

            if (transform.position.z < -10 - moveAmount)
            {
                isBack = false;
                timer = lerpTime;
            }
        }
        
    }
}
