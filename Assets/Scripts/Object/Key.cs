using Fusion;
using UnityEngine;

public class Key : NetworkBehaviour, IInteractable
{
    [Networked] public NetworkBool isEarned { get; set; } //획득했는가
    [SerializeField] bool disactiveOnEarned = false; //얻을 시 사라지는 오브젝트인가
    [SerializeField] InteractableOutline outline;
    [Header("탑승할 수 있는 키")]
    [SerializeField] Passenger passenger;
    [Header("옮길 수 있는 키")]
    [SerializeField] MovingObject movingObject;

    public void OnInteract(Interactor interactor)
    {
        Debug.Log($"Key: {gameObject.name} Key 를 얻음");
        isEarned = true;

        if (passenger != null)
            passenger.OnInteract(interactor);
        if (movingObject != null)
            movingObject.OnInteract(interactor);

        if (disactiveOnEarned)
            gameObject.SetActive(false);
    }

    public void SetOutlineMat(bool isOn)
    {
        outline.SetOutline(isOn);
    }
}
