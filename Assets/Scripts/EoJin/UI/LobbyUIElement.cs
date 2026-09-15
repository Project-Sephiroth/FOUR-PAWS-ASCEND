using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class LobbyUIElement : MonoBehaviour
{
    [SerializeField] public Button button;
    [SerializeField] private TMP_Text sessionNameText;
    [SerializeField] private TMP_Text sessionPlayerCountText;
    private string mySessionName;
    public string MySessionName => mySessionName;

    public void SetElement(string sessionName, int joinedCount)
    {
        mySessionName = sessionName;
        sessionNameText.text = sessionName;
        sessionPlayerCountText.text = $"{joinedCount} / {GameManager.Instance.MaxPlayerCount}";
    }
}
