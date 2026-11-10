using Fusion;
using UnityEngine;

public class Key : Passenger, IInteractable
{
    [Networked] public NetworkBool isEarned { get; set; } //획득했는가
    [SerializeField] bool disactiveOnEarned = false; //얻을 시 사라지는 오브젝트인가
    [SerializeField] bool canHold = false; //들을 수 있는 오브젝트인가
    [SerializeField] InteractableOutline outline;

    public override void OnInteract(Interactor interactor)
    {
        //들고가는 키일 경우
        if (canHold)
            base.OnInteract(interactor);

        Debug.Log($"Key: {gameObject.name} Key 를 얻음");
        isEarned = true;

        if (disactiveOnEarned)
            gameObject.SetActive(false);
    }
}
