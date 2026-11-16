using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

public sealed class IntroAdvanceInput : MonoBehaviour, IPointerClickHandler
{
    public event Action Advance;

    private void Update()
    {
        if (Keyboard.current?.spaceKey.wasPressedThisFrame == true)
            Advance?.Invoke();
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Left)
            Advance?.Invoke();
    }
}
