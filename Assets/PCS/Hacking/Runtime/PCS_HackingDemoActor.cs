using UnityEngine;

namespace FourPaws.PCS.Hacking
{
    // 접근 실습용 임시 조작체다. 본편 플레이어 입력이나 이동 컴포넌트를 참조하지 않는다.
    public sealed class PCS_HackingDemoActor : MonoBehaviour
    {
        [Tooltip("데모 조작체의 초당 이동 속도입니다.")] [SerializeField] private float moveSpeed = 3f;
        [Tooltip("이동 영역 최소 좌표입니다.")] [SerializeField] private Vector2 minimum = new Vector2(-3.6f, -1.4f);
        [Tooltip("이동 영역 최대 좌표입니다.")] [SerializeField] private Vector2 maximum = new Vector2(3.6f, 1.4f);
        public bool Locked { get; private set; }
        public void SetLocked(bool value) { Locked = value; }

        public void Move(float deltaTime)
        {
            if (Locked) return;
            float x = (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow) ? 1 : 0) -
                      (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow) ? 1 : 0);
            float y = (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow) ? 1 : 0) -
                      (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow) ? 1 : 0);
            Vector2 move = Vector2.ClampMagnitude(new Vector2(x, y), 1f) * Mathf.Max(0f, moveSpeed) * deltaTime;
            Vector3 position = transform.position;
            position.x = Mathf.Clamp(position.x + move.x, minimum.x, maximum.x);
            position.y = Mathf.Clamp(position.y + move.y, minimum.y, maximum.y);
            transform.position = position;
        }

        public static bool AnyDemoKeyHeld()
        {
            return Input.GetKey(KeyCode.Space) || Input.GetKey(KeyCode.E) || Input.GetKey(KeyCode.Escape) ||
                Input.GetKey(KeyCode.R) || Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.A) ||
                Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.UpArrow) ||
                Input.GetKey(KeyCode.DownArrow) || Input.GetKey(KeyCode.LeftArrow) || Input.GetKey(KeyCode.RightArrow);
        }
    }
}
