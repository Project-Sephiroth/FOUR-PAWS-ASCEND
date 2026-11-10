using Fusion;
using UnityEngine;

public class RockMaker : NetworkBehaviour
{
    [SerializeField] LayerMask groundLayer;
    [SerializeField] MovingObject rockPrefab;
    [SerializeField] Thrower thrower;

    Vector2 mousePos;
    GameObject curClick;
    MovingObject curRock;

    private void Update()
    {
        if (!Input.GetMouseButtonDown(0))
            return;

        mousePos = Camera.main.ScreenToWorldPoint(Input.mousePosition);

        Collider2D hit = Physics2D.OverlapPoint(mousePos, groundLayer);

        if (hit == null)
            return;

        Debug.Log($"RockMaker: ground {hit.gameObject.name} 을 클릭");

        curClick = hit.gameObject;

        if (curClick != null && curRock == null)
            MakeRock();
    }

    void MakeRock()
    {
        if (!HasStateAuthority)
            return;

        if (curRock != null)
            return;

        NetworkObject obj = Runner.Spawn(rockPrefab.GetComponent<NetworkObject>(), transform.position, transform.rotation);
        curRock = obj.GetComponent<MovingObject>();

        //오브젝트 풀에서 rock 받아와 활성화 
        thrower.TryThrow(curRock);
    }
}
