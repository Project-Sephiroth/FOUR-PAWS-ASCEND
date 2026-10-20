using UnityEngine;

public class TitleCameraMovement : MonoBehaviour
{
    [SerializeField] float moveAmount = 2.0f;
    [SerializeField] float moveSpeed = 1.5f;
    bool isBack = false;

    private void Update()
    {
        if (isBack)
        {
            transform.Translate(Vector3.forward * moveSpeed);

            if (transform.position.z > -10 + moveAmount)
                isBack = false;
        }
        else
        {
            transform.Translate(Vector3.back * moveSpeed);

            if (transform.position.z < -10 - moveAmount)
                isBack = true;
        }
        
    }
}
