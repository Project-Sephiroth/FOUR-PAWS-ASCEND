using System.Collections.Generic;
using Fusion;
using UnityEngine;
using UnityEngine.Events;

public class Switch : NetworkBehaviour, IInteractable
{
    [Header("플레이어가 밟고 있는 동안만 작동")]
    [SerializeField] private bool isWait = true;
    [SerializeField] private InteractableOutline outline;
    [SerializeField] private bool rockOnly;
    [SerializeField] private bool tongueOnly;
    [SerializeField] private bool repeatRockAction;
    [SerializeField] private bool ignoreRocks;
    public UnityEvent<bool> switchAction;

    [Networked] public bool isOn { get; set; }
    [Networked] private NetworkBool StaysOn { get; set; }

    private readonly HashSet<Collider2D> playersOnSwitch = new();
    private Animator anim;

    private void Awake()
    {
        anim = GetComponent<Animator>();
    }

    private bool CanProcessTrigger => Object != null && Object.IsValid && HasStateAuthority;

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (rockOnly || tongueOnly || !CanProcessTrigger || !IsPlayer(other) || !playersOnSwitch.Add(other))
            return;

        if (isWait)
            SetState(true);
        else if (playersOnSwitch.Count == 1 && !StaysOn)
            SetState(!isOn);
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        if (CanProcessTrigger && playersOnSwitch.Remove(other))
            RefreshPressureState();
    }

    public override void FixedUpdateNetwork()
    {
        if (HasStateAuthority && playersOnSwitch.RemoveWhere(c =>
                c == null || !c.enabled || !c.gameObject.activeInHierarchy ||
                (c.attachedRigidbody != null && !c.attachedRigidbody.simulated)) > 0)
            RefreshPressureState();
    }

    private static bool IsPlayer(Collider2D collider)
    {
        return collider.CompareTag("Player") ||
               (collider.attachedRigidbody != null && collider.attachedRigidbody.CompareTag("Player"));
    }

    private void RefreshPressureState()
    {
        if (isWait && !StaysOn && playersOnSwitch.Count == 0)
            SetState(false);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_Switch(bool doOn)
    {
        StaysOn = doOn;
        SetState(doOn || (isWait && playersOnSwitch.Count > 0));
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_Toggle()
    {
        if (!StaysOn)
            SetState(!isOn);
    }

    // 켜짐 값이 실제로 달라졌을 때만 연결된 동작을 한 번 호출합니다. 발판에 계속 서 있어도 같은 동작이 매번 반복되지 않게 합니다.
    private void SetState(bool value)
    {
        if (isOn == value)
            return;

        isOn = value;
        switchAction?.Invoke(value);
    }

    public override void Render()
    {
        if (anim != null)
            anim.SetBool("IsOn", isOn);
    }

    public void OnRockHit()
    {
        if (!ignoreRocks && Object != null && Object.IsValid)
        {
            if (repeatRockAction)
                RPC_RepeatRockAction();
            else
                RPC_Switch(true);
        }
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_RepeatRockAction()
    {
        if (repeatRockAction)
            switchAction?.Invoke(true);
    }

    // 직접 눌러도 되는지 표시하기 전에 돌 전용과 혀 전용 스위치를 구분합니다.
    public bool CanInteractWith(Interactor interactor) => Object != null && Object.IsValid &&
        !rockOnly && (!tongueOnly || interactor is Tongue);

    public void OnInteract(Interactor interactor)
    {
        if (rockOnly || (tongueOnly && interactor is not Tongue) || Object == null || !Object.IsValid)
            return;

        if (interactor == null || (interactor is Tongue && isWait))
            RPC_Switch(true);
        else
            RPC_Toggle();

        SetOutlineMat(false);
    }

    public void SetOutlineMat(bool isOn)
    {
        if (outline != null)
            outline.SetOutline(isOn);
    }
}
