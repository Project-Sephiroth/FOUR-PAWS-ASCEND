
using UnityEngine;

public class TitleCameraMovement : MonoBehaviour
{
    [SerializeField] float duration = 2.0f;
    [SerializeField] float moveDistance = 3.0f;

    float timer = 0f;
    bool isBack = true;

    Vector3 fwdDest;
    Vector3 backDest;

    private void Start()
    {
        fwdDest = transform.position;
        backDest = fwdDest + Vector3.back * moveDistance;
    }

    private void Update()
    {
        timer += Time.deltaTime;

        float t = Mathf.Clamp01(timer / duration);

        if (isBack)
        {
            float smooth = 1f - Mathf.Pow(1f - t, 3f);

            transform.position = Vector3.Lerp(fwdDest, backDest, smooth);
        }
        else
        {
            float smooth = Mathf.Pow(t, 3f);

            transform.position = Vector3.Lerp(backDest, fwdDest, smooth);
        }

        if (timer >= duration)
        {
            timer = 0f;
            isBack = !isBack;
        }
    }
}

