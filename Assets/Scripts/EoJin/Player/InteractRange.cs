using UnityEngine;
using UnityEngine.Events;

public class InteractRange : MonoBehaviour
{
    public event UnityAction<GameObject> OnDetect;

    private void OnTriggerEnter2D(Collider2D collision)
    {
        //Interactable 이 있을 경우 해당 개체를 반환
        IInteractable interactable = collision.GetComponent<IInteractable>();

        if (interactable == null)
            return;

        OnDetect?.Invoke(collision.gameObject);
    }
}
