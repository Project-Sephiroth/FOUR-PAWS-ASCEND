using Fusion;
using UnityEngine;

public class Passenger : NetworkBehaviour, ICarriable, IInteractable
{
    [SerializeField] private InteractableOutline outlineMat;

    [Networked] private Carrier CurrentCarrier { get; set; }
    [SerializeField] private bool puppetKey;
    [Networked] public Thrower CurrentLifter { get; private set; }
    public Carrier RabbitCarrier => CurrentCarrier;
    public bool CanBeCarried => CanRide || puppetKey;
    public bool IsCarried => CurrentCarrier != null || CurrentLifter != null;
    [Networked] public NetworkBool CanRide { get; private set; }

    private PlayerInput input;
    private Rigidbody2D rb;
    private bool physicsSuppressed;
    private bool wasSimulated;
    private bool attached;
    private Transform originalParent;
    [Networked] private TickTimer Flight { get; set; }
    public bool IsFlying => Flight.IsRunning;
    private float originalGravity;

    private void Awake()
    {
        input = GetComponent<PlayerInput>();
        rb = GetComponent<Rigidbody2D>();
        originalParent = transform.parent;
    }

    public MyEnum.CharacterType CharacterType { get; private set; }

    public void SetCharacterType(MyEnum.CharacterType type)
    {
        CharacterType = type;
        CanRide = type == MyEnum.CharacterType.Mouse || type == MyEnum.CharacterType.Frog;
    }

    public override void FixedUpdateNetwork()
    {
        if (!HasStateAuthority)
            return;

        if (Flight.Expired(Runner))
        {
            Flight = default;
            rb.gravityScale = originalGravity;
        }

        if (IsFlying)
            return;

        if (!IsCarried)
        {
            if (attached)
                Detach();
            return;
        }

        if (input != null && input.JumpInput)
        {
            input.JumpInput = false;
            OnDrop();
            return;
        }


    }

    public override void Render()
    {
        ApplyPhysics();
    }

    public void OnInteract(Interactor interactor)
    {
        if (interactor is Carrier carrier)
            OnCarry(carrier);
        else if (interactor is Thrower thrower)
            thrower.TryLift(this);
    }

    public void OnCarry(Carrier carrier)
    {
        if (carrier != null && carrier.HasStateAuthority)
            carrier.TryCarry(this);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_RequestCarry(Carrier carrier, RpcInfo info = default)
    {
        if (carrier == null || (!Runner.IsSinglePlayer && info.Source != carrier.Object.StateAuthority))
            return;

        if (!CanBeCarried || rb == null || IsFlying || carrier.Head == null || IsCarried)
        {
            if (CurrentCarrier != carrier)
                carrier.RPC_RequestPassengerDrop(this);
            return;
        }

        if (input != null)
            input.JumpInput = false;

        CurrentCarrier = carrier;
        attached = true;
        ApplyPhysics();
        transform.SetParent(carrier.Head.transform, true);
        transform.localPosition = Vector3.zero;
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_RequestDrop(Carrier carrier, RpcInfo info = default)
    {
        if (carrier != null && CurrentCarrier == carrier &&
            (Runner.IsSinglePlayer || info.Source == carrier.Object.StateAuthority))
            OnDrop();
    }

    public void OnDrop()
    {
        if (!HasStateAuthority || !IsCarried)
            return;

        Carrier carrier = CurrentCarrier;
        Thrower lifter = CurrentLifter;
        CurrentCarrier = null;
        CurrentLifter = null;
        Detach();
        if (carrier != null)
            carrier.RPC_RequestPassengerDrop(this);
        if (lifter != null)
            lifter.RPC_ClearLift(this);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_Lift(Thrower lifter, RpcInfo info = default)
    {
        if (lifter == null || (!Runner.IsSinglePlayer && info.Source != lifter.Object.StateAuthority))
            return;

        if (!CanBeCarried || rb == null || IsFlying || IsCarried || lifter.HoldPoint == null)
        {
            if (CurrentLifter != lifter)
                lifter.RPC_ClearLift(this);
            return;
        }

        if (input != null)
            input.JumpInput = false;
        CurrentLifter = lifter;
        attached = true;
        ApplyPhysics();
        transform.SetParent(lifter.HoldPoint, true);
        transform.localPosition = Vector3.zero;
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_DropLift(Thrower lifter, RpcInfo info = default)
    {
        if (lifter != null && CurrentLifter == lifter && (Runner.IsSinglePlayer || info.Source == lifter.Object.StateAuthority))
            OnDrop();
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_Throw(Thrower lifter, Vector2 destination, float speed, float height, RpcInfo info = default)
    {
        if (lifter == null || CurrentLifter != lifter || (!Runner.IsSinglePlayer && info.Source != lifter.Object.StateAuthority))
            return;

        Vector2 start = lifter.HoldPoint.position;
        OnDrop();
        rb.position = start;

        Vector2 delta = destination - start;
        float duration = Mathf.Max(Runner.DeltaTime, delta.magnitude / Mathf.Max(0.1f, speed));
        height = Mathf.Max(0.12f, height);
        originalGravity = rb.gravityScale;
        rb.gravityScale = 8f * height / (duration * duration * Mathf.Max(0.01f, -Physics2D.gravity.y));
        rb.linearVelocity = (delta + Vector2.up * (4f * height)) / duration;
        Flight = TickTimer.CreateFromSeconds(Runner, duration);
    }

    private void Detach()
    {
        attached = false;
        if (transform.parent != originalParent)
            transform.SetParent(originalParent, true);

        if (input != null)
            input.JumpInput = false;

        ApplyPhysics();
    }

    private void ApplyPhysics()
    {
        if (rb == null || physicsSuppressed == IsCarried)
            return;

        physicsSuppressed = IsCarried;
        if (physicsSuppressed)
        {
            wasSimulated = rb.simulated;
            if (HasStateAuthority && rb.bodyType == RigidbodyType2D.Dynamic)
            {
                rb.linearVelocity = Vector2.zero;
                rb.angularVelocity = 0f;
            }
            rb.simulated = false;
        }
        else
        {
            rb.simulated = wasSimulated;
        }
    }

    public void SetOutlineMat(bool isOn)
    {
        if (outlineMat != null)
            outlineMat.SetOutline(isOn);
    }
}
