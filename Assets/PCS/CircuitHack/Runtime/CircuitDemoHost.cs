using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;

namespace FourPaws.PCS.CircuitHack
{
    // 독립 데모의 시작·결과와 자기 UI 입력/실패음 수명을 맡는다. 본편 연결은 하지 않는다.
    public sealed class CircuitDemoHost : MonoBehaviour
    {
        [Header("독립 데모 연결")]
        public CircuitHackManager manager;public Button launch;public Text result;
        public Button[] singleLaunches; public Button reloadLayout; public bool defaultSingle;
        public EventSystem eventSystem;public InputSystemUIInputModule uiModule;
        private InputActionAsset uiActions;private InputActionReference point,click;private AudioClip clip;
        private int completed,cancelled;private bool subscribed;
        private readonly System.Collections.Generic.List<UnityEngine.Events.UnityAction> stageActions=new System.Collections.Generic.List<UnityEngine.Events.UnityAction>();
        private void Awake()
        {
            if(!manager||!manager.input||!manager.view||!manager.view.ReferencesValid||!manager.overlay||manager.transform.IsChildOf(manager.overlay.transform)||!launch||!result||!eventSystem||!uiModule||!manager.failureAudio){Debug.LogError("P4 설정 오류: 데모 연결 참조를 검사하세요.",this);enabled=false;return;}
            // 저장된 모듈은 꺼진 상태다. 기본 공유 입력 대신 자기 마우스 액션만 연결한다.
            uiActions=ScriptableObject.CreateInstance<InputActionAsset>();uiActions.name="PCS_CircuitMouseUI";
            var map=new InputActionMap("PCS Circuit UI");uiActions.AddActionMap(map);
            var p=map.AddAction("Point",InputActionType.PassThrough,"<Mouse>/position",expectedControlLayout:"Vector2");
            var c=map.AddAction("Click",InputActionType.PassThrough,"<Mouse>/leftButton",expectedControlLayout:"Button");
            point=InputActionReference.Create(p);click=InputActionReference.Create(c);
            uiModule.actionsAsset=uiActions;uiModule.point=point;uiModule.leftClick=click;uiModule.move=null;uiModule.submit=null;uiModule.cancel=null;
            eventSystem.sendNavigationEvents=false;map.Enable();uiModule.enabled=true;
            clip=AudioClip.Create("PCS 회로 충돌음",4410,1,44100,false);var samples=new float[4410];
            for(int i=0;i<samples.Length;i++)samples[i]=Mathf.Sin(i*2*Mathf.PI*180/44100)*.2f*(1f-(float)i/samples.Length);
            clip.SetData(samples,0);manager.failureAudio.clip=clip;
            manager.overlay.SetActive(false);Subscribe();ShowResult("대기");
        }
        private void Subscribe()
        {
            if(subscribed)return;subscribed=true;launch.onClick.AddListener(Begin);manager.view.close.onClick.AddListener(manager.CloseHack);manager.view.restart.onClick.AddListener(manager.RestartStage);manager.view.newHack.onClick.AddListener(Begin);if(singleLaunches!=null)for(int i=0;i<singleLaunches.Length;i++){int index=i;UnityEngine.Events.UnityAction action=()=>BeginSingle(index);stageActions.Add(action);if(singleLaunches[i])singleLaunches[i].onClick.AddListener(action);}
            if(reloadLayout)reloadLayout.onClick.AddListener(manager.ReloadLayoutAndStart);manager.OnHackCompleted+=Completed;manager.OnHackCancelled+=Cancelled;
        }
        public void Begin(){if(!isActiveAndEnabled)return;if(defaultSingle&&manager.layouts!=null&&manager.layouts.Length==1)manager.StartSingleStage(manager.layouts[0]);else manager.StartHack();ShowResult(manager.Error==null?"진행 중":manager.Error);}
        public void BeginSingle(int index){if(!isActiveAndEnabled||manager.layouts==null||index<0||index>=manager.layouts.Length)return;manager.StartSingleStage(manager.layouts[index]);ShowResult(manager.Error==null?"단독 진행 중":manager.Error);}
        private void Update(){bool ready=manager&&(manager.State==CircuitHackManager.HackState.Closed||manager.State==CircuitHackManager.HackState.Completed);if(launch)launch.interactable=ready;if(singleLaunches!=null)foreach(var button in singleLaunches)if(button)button.interactable=ready;}
        private void Completed(int run){completed++;ShowResult("성공 / 해킹 "+run);}
        private void Cancelled(int run){cancelled++;ShowResult("취소 / 해킹 "+run);}
        private void ShowResult(string state){result.text="회로 결과: "+state+" · 성공 전달 "+completed+"회 · 취소 전달 "+cancelled+"회";launch.interactable=manager.State==CircuitHackManager.HackState.Closed||manager.State==CircuitHackManager.HackState.Completed;}
        private void OnEnable(){if(subscribed&&uiActions){uiActions.Enable();uiModule.enabled=true;}}
        private void OnDisable(){if(manager)manager.EndHost();if(uiModule)uiModule.enabled=false;if(uiActions)uiActions.Disable();}
        private void OnDestroy()
        {
            if(manager&&subscribed){launch.onClick.RemoveListener(Begin);if(manager.view){manager.view.close.onClick.RemoveListener(manager.CloseHack);manager.view.restart.onClick.RemoveListener(manager.RestartStage);manager.view.newHack.onClick.RemoveListener(Begin);}manager.OnHackCompleted-=Completed;manager.OnHackCancelled-=Cancelled;}
            if(singleLaunches!=null)for(int i=0;i<singleLaunches.Length&&i<stageActions.Count;i++)if(singleLaunches[i])singleLaunches[i].onClick.RemoveListener(stageActions[i]);
            if(reloadLayout&&manager)reloadLayout.onClick.RemoveListener(manager.ReloadLayoutAndStart);
            if(uiModule){uiModule.point=null;uiModule.leftClick=null;uiModule.actionsAsset=null;}
            if(uiActions){uiActions.Disable();Destroy(uiActions);}if(point)Destroy(point);if(click)Destroy(click);if(clip)Destroy(clip);
        }
    }
}
