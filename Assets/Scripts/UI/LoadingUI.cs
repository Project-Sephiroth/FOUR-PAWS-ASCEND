using TMPro;
using UnityEngine;
using UnityEngine.Events;

public class LoadingUI : MonoBehaviour
{
    [SerializeField] GameObject screen;
    [SerializeField] TMP_Text loadingText;

    public void StartLoading(string loadingText = "")
    {
        this.loadingText.text = loadingText;
        screen.SetActive(true);
    }

    public void StopLoading()
    {
        screen.SetActive(false);
    }
}
