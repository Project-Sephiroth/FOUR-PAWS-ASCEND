using UnityEditor;

/// <summary>
/// 스테이지 내 퍼즐 요소를 편하게 보려고 만듦
/// </summary>
[CustomEditor(typeof(StagePuzzleDirector))]
public sealed class StagePuzzleDirectorEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        var stage = serializedObject.FindProperty("stage");
        EditorGUILayout.PropertyField(stage);

        string group = ((NetworkGameManager.SceneName)stage.intValue) switch
        {
            NetworkGameManager.SceneName.Malkuth => "malkuth",
            NetworkGameManager.SceneName.Yesod => "yesod",
            NetworkGameManager.SceneName.Hod => "hod",
            NetworkGameManager.SceneName.Netzach => "netzach",
            _ => null
        };
        if (group != null)
            EditorGUILayout.PropertyField(serializedObject.FindProperty(group), includeChildren: true);
        else
            EditorGUILayout.HelpBox("Select a gameplay stage.", MessageType.Warning);

        serializedObject.ApplyModifiedProperties();
    }
}
