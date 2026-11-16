using System;
using UnityEngine;

public enum IntroCutScene
{
    A, B, C, D, E
}

[CreateAssetMenu(menuName = "Intro/Narration")]
public sealed class IntroNarration : ScriptableObject
{
    [Serializable]
    public sealed class Cut
    {
        public IntroCutScene id;
        [TextArea(2, 5)] public string[] lines;
    }

    public Cut[] cuts;
}
