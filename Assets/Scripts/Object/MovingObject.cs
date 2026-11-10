using Fusion;
using System.Collections;
using UnityEngine;
using static IMoveable;

[RequireComponent(typeof(Rigidbody2D))]
public class MovingObject : NetworkBehaviour, IInteractable, IMoveable
{
    [SerializeField] bool canInteract = true;
    [SerializeField] private float moveSpeed = 3f;
    [SerializeField] private float arcHeight = 2f;

    private Rigidbody2D rb;
    private Collider2D col;

    private Vector2 startPosition;
    private Vector2 targetPosition;

    private MoveType curMoveType;

    private float moveDuration;
    private float elapsedTime;

    private float currentArcHeight;
    private float currentMoveSpeed;

    [Networked] public NetworkBool IsMoving { get; private set; }

    [Networked] public Interactor Holder { get; private set; }

    public bool IsHeld => Holder != null;

    [SerializeField] InteractableOutline outline;


    private void OnEnable()
    {
        rb = GetComponent<Rigidbody2D>();
        col = GetComponent<Collider2D>();
    }

    public override void FixedUpdateNetwork()
    {
        if (!HasStateAuthority)
            return;

        if (IsMoving)
            Move();
    }

    public void OnInteract(Interactor interactor)
    {
        Debug.Log($"MovingObject: holder 를 {interactor.gameObject.name} 으로 설정");
        Holder = interactor;

        if (interactor is Thrower thrower)
            thrower.TryThrow(this);
        if (interactor is Tongue tongue)
            tongue.TryTongue(this);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_RequestHold(Interactor interactor)
    {
        rb.simulated = false;
        col.enabled = false;
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_RequestRelease()
    {
        if (IsHeld)
            ReleaseHold();

        Holder = null;

        rb.simulated = true;
        rb.linearVelocity = Vector2.zero;
        rb.angularVelocity = 0f;

        col.enabled = true;
    }

    private void ReleaseHold()
    {
        Holder = null;
        StartCoroutine(ReleaseHoldTime());
    }

    /// <summary>
    /// 던져지는 동안 기다렸다가 rb 활성화
    /// </summary>
    /// <returns></returns>
    IEnumerator ReleaseHoldTime()
    {
        yield return new WaitForSeconds(moveDuration);

        rb.simulated = true;
        rb.linearVelocity = Vector2.zero;
        col.enabled = true;
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_RequestMove(MoveType type, Vector2 target, float speed = -1f, float height = -1f, RpcInfo info = default)
    {
        currentMoveSpeed = speed >= 0f ? speed : moveSpeed;
        currentArcHeight = height >= 0f ? height : arcHeight;

        TryMove(type, target);
    }

    public void TryMove(MoveType type, Vector2 targetPosition)
    {
        if (!HasStateAuthority || IsMoving)
            return;

        //손에 들려 있다면 던지기 전에 해제
        if (IsHeld)
            ReleaseHold();

        startPosition = rb.position;
        this.targetPosition = targetPosition;
        curMoveType = type;

        float distance = Vector2.Distance(startPosition, targetPosition);

        if (distance <= 0.001f || currentMoveSpeed <= 0f)
            return;

        moveDuration = distance / currentMoveSpeed;
        elapsedTime = 0f;

        IsMoving = true;
    }

    public void Move()
    {
        if (!HasStateAuthority || !IsMoving)
            return;

        elapsedTime += Runner.DeltaTime;

        float t = Mathf.Clamp01(elapsedTime / moveDuration);

        Vector2 nextPosition = Vector2.Lerp(startPosition, targetPosition, t);

        if (curMoveType == MoveType.Parabola)
            nextPosition.y += 4f * currentArcHeight * t * (1f - t);

        rb.MovePosition(nextPosition);

        if (t >= 1f)
            IsMoving = false;
    }

    public void SetOutlineMat(bool isOn)
    {
        if (outline != null)
            outline.SetOutline(isOn);
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (!canInteract)
            return;

        IInteractable interactable = collision.gameObject.GetComponent<IInteractable>();

        if (interactable != null)
            interactable.OnInteract(null);
    }
}
