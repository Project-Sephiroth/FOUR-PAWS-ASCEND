using System.Collections.Generic;
using Fusion;
using UnityEngine;
using UnityEngine.EventSystems;

public class Interactor : NetworkBehaviour
{
    protected bool interactWithClick = true;
    protected bool interactWithRange = true;
    [SerializeField] private InteractRange interactRange;
    [SerializeField] private LayerMask detectLayer;
    [SerializeField] protected Transform holdPoint;

    protected GameObject curClick;
    protected IInteractable curInteratcble;
    protected Vector2 mousePos;
    public Transform HoldPoint => holdPoint;
    [Networked] public NetworkObject CurInteractObject { get; set; }

    private readonly HashSet<GameObject> candidates = new();
    private readonly HashSet<GameObject> outlined = new();
    private readonly HashSet<GameObject> dismissed = new();
    private readonly List<Collider2D> cursorHits = new();
    private static Interactor outlineOwner;

    protected virtual bool CanShowInteractionOutline => true;

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        if (outlineOwner == this)
            ReleaseOutlines();
    }

    public virtual void Update()
    {
        if (Object == null || !Object.IsValid || !HasStateAuthority || !WorldClickPressed())
            return;

        OnClick();
        if (curClick != null)
            DismissOutline(curClick);
        TryIntetact();
    }

    protected bool WorldClickPressed() =>
        CanUseWorldInput && Input.GetMouseButtonDown(0) && !PointerOnUi &&
        Camera.main is Camera camera && camera.pixelRect.Contains(Input.mousePosition);

    private bool CanUseWorldInput => !LocalMultiplayerDebug.BlocksWorldClick && !GameMenu.BlocksWorldClick &&
        !HackingConsole.IsLocalHackOpen && LocalMultiplayerDebug.AllowsInput(Object);

    private static bool PointerOnUi => EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

    protected virtual void LateUpdate()
    {
        if (Object == null || !Object.IsValid || !HasStateAuthority || !CanUseWorldInput ||
            !CanShowInteractionOutline || PointerOnUi)
        {
            if (outlineOwner == this)
                ReleaseOutlines();
            return;
        }

        if (outlineOwner != this)
        {
            if (outlineOwner != null)
                outlineOwner.ReleaseOutlines();
            outlineOwner = this;
        }

        candidates.Clear();
        if (interactRange != null)
            interactRange.CopyTargetsTo(candidates);
        candidates.RemoveWhere(target => !CanInteractWith(target));
        var cursor = FindCursorTarget();
        if (cursor != null)
            candidates.Add(cursor);

        dismissed.RemoveWhere(target => !candidates.Contains(target));
        outlined.RemoveWhere(target =>
        {
            if (candidates.Contains(target) && !dismissed.Contains(target))
                return false;
            InteractableOutline.SetTarget(target, false);
            return true;
        });

        foreach (var target in candidates)
            if (!dismissed.Contains(target) && outlined.Add(target))
                InteractableOutline.SetTarget(target, true);
    }

    private void DismissOutline(GameObject target)
    {
        if (target == null)
            return;
        dismissed.Add(target);
        if (outlined.Remove(target))
            InteractableOutline.SetTarget(target, false);
    }

    private void ReleaseOutlines()
    {
        foreach (var target in outlined)
            InteractableOutline.SetTarget(target, false);
        outlined.Clear();
        outlineOwner = null;
    }

    private bool CanInteractWith(GameObject target)
    {
        if (target == null || !target.activeInHierarchy || target.transform.IsChildOf(transform))
            return false;
        if (target.TryGetComponent(out Key key))
            return key.CanInteractWith(this);
        if (target.TryGetComponent(out Passenger passenger))
            return (this is Carrier || this is Thrower) && passenger.CanBeCarried && !passenger.IsCarried;
        if (target.TryGetComponent(out MovingObject item))
            return (this is Tongue || this is Thrower) && item.CanInteract;
        if (target.TryGetComponent(out Switch button))
            return button.CanInteractWith(this);
        return target.GetComponent<IInteractable>() != null;
    }

    private GameObject FindCursorTarget()
    {
        var camera = Camera.main;
        if (camera == null || !camera.pixelRect.Contains(Input.mousePosition))
            return null;
        mousePos = camera.ScreenToWorldPoint(Input.mousePosition);
        var filter = new ContactFilter2D { useTriggers = true };
        filter.SetLayerMask(detectLayer);
        Physics2D.OverlapPoint(mousePos, filter, cursorHits);
        foreach (var hit in cursorHits)
        {
            if (hit.GetComponentInParent<IInteractable>() is not Component component)
                continue;
            var target = component.gameObject;
            if (CanInteractWith(target) && IsInInteractionRange(target))
                return target;
        }
        return null;
    }

    private bool IsInInteractionRange(GameObject target) => !interactWithRange ||
        (interactRange != null && interactRange.Contains(target)) ||
        (target != null && target.TryGetComponent(out Key key) && key.IsReachableBy(this));

    protected virtual void OnClick() => curClick = FindCursorTarget();

    protected virtual void TryIntetact()
    {
        if (!HasStateAuthority)
            return;

        curInteratcble = null;
        CurInteractObject = null;
        GameObject target = curClick;
        if (!interactWithClick && interactRange != null)
        {
            candidates.Clear();
            interactRange.CopyTargetsTo(candidates);
            float closest = float.PositiveInfinity;
            target = null;
            foreach (var candidate in candidates)
            {
                float distance = (candidate.transform.position - transform.position).sqrMagnitude;
                if (distance < closest && CanInteractWith(candidate))
                {
                    target = candidate;
                    closest = distance;
                }
            }
        }
        if (!CanInteractWith(target) || !IsInInteractionRange(target))
            return;

        curInteratcble = target.GetComponent<IInteractable>();
        CurInteractObject = target.GetComponent<NetworkObject>();
        DismissOutline(target);
        curInteratcble?.OnInteract(this);
    }
}
