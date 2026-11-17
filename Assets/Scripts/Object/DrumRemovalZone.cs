using UnityEngine;

public sealed class DrumRemovalZone : MonoBehaviour
{
    [SerializeField] private bool oneUse;

    internal bool OneUse => oneUse;
}