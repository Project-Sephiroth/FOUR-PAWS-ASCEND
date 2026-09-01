using TMPro;
using UnityEngine;
using UnityEngine.Events;

public class LoadingUI : MonoBehaviour
{
    [SerializeField] GameObject screen;
    [SerializeField] TMP_Text loadingText;

    private void Start()
    {
        screen.SetActive(true);
    }

    public void StartLoading(UnityAction endAction, string loadingText = "")
    {
        this.loadingText.text = loadingText;
        screen.SetActive(true);

        endAction += StopLoading;
    }

    void StopLoading()
    {
        screen.SetActive(false);
    }
}
