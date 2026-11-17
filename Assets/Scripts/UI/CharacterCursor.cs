using UnityEngine;
using Character = MyEnum.CharacterType;

public sealed class CharacterCursor : MonoBehaviour
{
    private const string CursorPath = "img/UI/Pointer/";
    private Character appliedCharacter = Character.Count;

    private void LateUpdate()
    {
        var character = ScenePresentation.LocalCharacter();
        if (character == Character.None)
            character = Character.Mouse;
        if (character == appliedCharacter)
            return;

        appliedCharacter = character;
        string pointer = character switch
        {
            Character.Bear => "pointer_bear",
            Character.Frog => "pointer_frog",
            Character.Rabbit => "pointer_rabbit",
            _ => "pointer_mouse"
        };

        var texture = Resources.Load<Texture2D>(CursorPath + pointer);
        if (texture == null)
        {
            Debug.LogError($"Missing cursor texture: {CursorPath}{pointer}");
            Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
            return;
        }

        var hotspot = new Vector2(texture.width * 0.5f, texture.height * 0.5f);
        Cursor.SetCursor(texture, hotspot, CursorMode.Auto);
    }

    private void OnDestroy() => Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
}
