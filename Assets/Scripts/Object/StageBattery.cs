using Fusion;
using UnityEngine;

public class StageBattery : NetworkBehaviour
{
    [SerializeField] private Vector2 exitPosition;
    [Networked] private NetworkBool IsAvailable { get; set; }
    [Networked] private NetworkBool Returned { get; set; }
    [Networked] public NetworkBool HasBeenPickedUp { get; private set; }

    private StagePuzzleDirector director;

    internal void SetDirector(StagePuzzleDirector stageDirector) => director = stageDirector;

    internal void TrySpawn()
    {
        if (Object != null && Object.IsValid)
            RPC_TrySpawn();
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_TrySpawn()
    {
        if (!Returned && Carrier == null)
            IsAvailable = true;
    }
    [SerializeField] private float exitRadius = 1.75f;
    [Networked] private NetworkObject Carrier { get; set; }

    private Collider2D pickup;
    private Vector3 home;


    private void Awake()
    {
        pickup = GetComponent<Collider2D>();
        home = transform.position;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (Object == null || !Object.IsValid || !HasStateAuthority || !IsAvailable || Carrier != null)
            return;

        var body = other.attachedRigidbody;
        if (body != null && body.CompareTag("Player") && body.TryGetComponent(out NetworkObject player))
        {
            Carrier = player;
            HasBeenPickedUp = true;
        }
    }

    public override void FixedUpdateNetwork()
    {
        if (!HasStateAuthority)
            return;

        if (Returned)
        {
            director?.TryExitByElevator(Runner);
            return;
        }

        if (Carrier == null)
        {
            transform.position = home;
            return;
        }

        transform.position = Carrier.transform.position + Vector3.up * 1.65f;
        if (Vector2.Distance(Carrier.transform.position, exitPosition) <= exitRadius)
        {
            Returned = true;
            IsAvailable = false;
            Carrier = null;
        }
    }

    public override void Render()
    {
        pickup.enabled = IsAvailable && Carrier == null;
        if (TryGetComponent(out SpriteRenderer sprite))
            sprite.enabled = IsAvailable;
        if (!HasStateAuthority)
            transform.position = Carrier != null ? Carrier.transform.position + Vector3.up * 1.65f : home;
    }
}
