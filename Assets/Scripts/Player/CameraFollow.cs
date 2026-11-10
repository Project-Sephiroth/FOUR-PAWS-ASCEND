using UnityEngine;

public class CameraFollow : MonoBehaviour
{
    public static CameraFollow Instance { get; private set; }

    [Header("카메라 설정")]
    [SerializeField] private Vector3 offset = new Vector3(0, 0, -10);
    [SerializeField] private float smoothSpeed = 5f;

    [Header("카메라 이동 범위")]
    [SerializeField] private float minX;
    [SerializeField] private float maxX;
    [SerializeField] private float minY;
    [SerializeField] private float maxY;

    private Transform target;

    private void Awake()
    {
        Instance = this;
    }

    private void LateUpdate()
    {
        if (target == null)
            return;

        Vector3 targetPos = target.position + offset;

        Vector3 newPos = Vector3.Lerp(
            transform.position,
            targetPos,
            smoothSpeed * Time.deltaTime
        );

        transform.position = ClampPosition(newPos);
    }

    private Vector3 ClampPosition(Vector3 pos)
    {
        pos.x = Mathf.Clamp(pos.x, minX, maxX);
        pos.y = Mathf.Clamp(pos.y, minY, maxY);

        return pos;
    }

    public void SetTarget(Transform newTarget)
    {
        target = newTarget;

        if (target != null)
            transform.position = ClampPosition(target.position + offset);
    }

    public void ClearTarget()
    {
        target = null;
    }
}