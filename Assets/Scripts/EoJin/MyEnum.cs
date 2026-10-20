using System;

public static class MyEnum
{
    [Serializable]
    public enum CharacterType
    {
        None, Rabbit, Bear, Mouse, Frog, Count
    }

    public enum Layer
    {
        Default = 0, Player = 3, Ground = 6
    }
}
