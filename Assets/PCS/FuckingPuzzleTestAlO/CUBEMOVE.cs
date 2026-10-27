
using UnityEngine;

public class CUBEMOVE : MonoBehaviour
{
    [Header("이동 설정")]
    [SerializeField] private float moveDistance = 3f;
    [SerializeField] private float moveSpeed = 2f;

    private Vector3 targetPosition;
    private bool isMoving = false;

    // 다른 스크립트에서 호출할 함수
    public void MoveCube()
    {
        if (isMoving) return;

        targetPosition = transform.position + Vector3.right * moveDistance;
        isMoving = true;
    }

    void Update()
    {
        if (!isMoving) return;

        transform.position = Vector3.MoveTowards(
            transform.position,
            targetPosition,
            moveSpeed * Time.deltaTime
        );

        if (Vector3.Distance(transform.position, targetPosition) < 0.001f)
        {
            transform.position = targetPosition;
            isMoving = false;
        }
    }
}
