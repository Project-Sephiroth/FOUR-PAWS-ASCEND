using UnityEngine;
using UnityEngine.UI;

public class LobbyUI : MonoBehaviour
{
    [SerializeField] NetworkLauncher launcher;
    [SerializeField] LoadingUI loadingUI;
    [SerializeField] GameObject uiParent;

    [SerializeField] GameObject elementPrefab;
    [SerializeField] Transform poolParent;
    LobbyUIElement[] elementPool;
    private int poolSize = 10;
    private int index = 0;

    [SerializeField] Button creatNewSessionBttn;
    [SerializeField] WindowPanel createNewSession;
    [SerializeField] WindowPanel joinAlert;

    private void Awake()
    {
        launcher.OnLobbyJoined += ShowUI;
        launcher.OnSessionUpdated += SetUI;
    }

    private void Start()
    {
        InitPool();
        InitUI();
        
        loadingUI.StartLoading("서버 접속 중...");
    }

    void InitPool()
    {
        elementPool = new LobbyUIElement[poolSize];

        for (int i = 0; i < poolSize; i++)
        {
            elementPool[i] = Instantiate(elementPrefab, poolParent).GetComponent<LobbyUIElement>();
            elementPool[i].gameObject.SetActive(false);
        }
    }

    void InitUI()
    {
        //lobby ui element
        foreach (var element in elementPool)
        {
            element.button.onClick.AddListener(() => {
                //curJoinSession 을 자신이 할당 받은 세션 이름으로 설정
                curJoinSession = element.MySessionName;
                Show_JoinAlert();
            });
        }

        //join alert
        joinAlert.yes.onClick.AddListener(JoinSession);
        joinAlert.no.onClick.AddListener(Hide_JoinAlert);

        //creat new Session
        creatNewSessionBttn.onClick.AddListener(Show_CreatNewSession);
        createNewSession.yes.onClick.AddListener(CreatNewSession);
        createNewSession.no.onClick.AddListener(Hide_CreateNewSession);
    }

    void ShowUI()
    {
        loadingUI.StopLoading();
        uiParent.SetActive(true);
    }

    void HideUI()
    {
        uiParent.SetActive(false);
    }

    public void SetUI()
    {
        foreach (var element in elementPool)
            element.gameObject.SetActive(false);

        foreach (var session in launcher.Sessions)
            SetUI(session.Name, session.PlayerCount);
    }

    public void SetUI(string sessionName, int joinedCount)
    {
        if (index >= elementPool.Length)
            return;

        elementPool[index].SetElement(sessionName, joinedCount);
        elementPool[index].gameObject.SetActive(true);

        index++;
    }

    string curJoinSession;

    public void JoinSession()
    {
        if (!string.IsNullOrWhiteSpace(curJoinSession))
            JoinSession(curJoinSession);
    }

    public void JoinSession(string sessionName)
    {
        launcher.TryAccessSession(sessionName);
        HideUI();
    }

    public void CreatNewSession()
    {
        if (string.IsNullOrWhiteSpace(createNewSession.inputField.text))
            return;

        JoinSession(createNewSession.inputField.text);
    }

    void Show_CreatNewSession()
    {
        createNewSession.gameObject.SetActive(true);
    }

    void Hide_CreateNewSession()
    {
        createNewSession.gameObject.SetActive(false);
    }

    void Show_JoinAlert()
    {
        joinAlert.gameObject.SetActive(true);
    }

    void Hide_JoinAlert()
    {
        joinAlert.gameObject.SetActive(false);
    }
}
