using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using FourPaws.PCS.Hacking;
using FourPaws.PCS.CircuitHack;

// 명시 실행 전용 검수기. 보호 기준은 이 작업의 외부 기록이며 기존 생성기/Guard를 변경하지 않는다.
public static class PCS_VisualAudioValidation
{
    const string Run=@"C:\FOUR-PAWS-PCS-BRIEF\PCS_VISUAL_AUDIO_HANDOFF_20261023\run-20261023-223004-approved-e3437f06\phase-p4-ready";
    const string Panel="Assets/PCS/Hacking/Prefabs/PCS_HackingPanel.prefab";
    const string TimingPreview="Assets/PCS/Hacking/Scenes/PCS_TimingSpritePreview.unity";
    [Serializable] class Entry {public string path,sha256,guid;}
    [Serializable] class Baseline {public Entry[] files;}
    [Serializable] class Manifest {public string[] allowed;}
    static List<string> results=new List<string>();
    static void Check(bool valid,string id,string detail){results.Add((valid?"PASS ":"FAIL ")+id+" | "+detail);if(!valid)throw new InvalidOperationException(id+": "+detail);}
    static string Hash(string path){using(var hash=SHA256.Create())using(var file=File.OpenRead(path))return BitConverter.ToString(hash.ComputeHash(file)).Replace("-","");}
    static void SafePath(string path){for(var p=new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(path)));p!=null;p=p.Parent)if((p.Attributes&FileAttributes.ReparsePoint)!=0)throw new InvalidOperationException("연결 경로는 저장할 수 없습니다.");if(File.Exists(path)&&(File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new InvalidOperationException("연결 파일은 저장할 수 없습니다.");}
    static void Guard()
    {
        if(Application.dataPath.Replace('\\','/')!="C:/FOUR-PAWS-ASCEND-NEWPCS/Assets")throw new InvalidOperationException("프로젝트 경로 불일치");
        var baseline=JsonUtility.FromJson<Baseline>(File.ReadAllText(Path.Combine(Run,"baseline/state.json")));
        var allowed=new HashSet<string>(JsonUtility.FromJson<Manifest>(File.ReadAllText(Path.Combine(Run,"baseline/manifest.json"))).allowed);
        foreach(var f in baseline.files){SafePath(f.path);if(!allowed.Contains(f.path)&&((f.sha256==null&&File.Exists(f.path))||(f.sha256!=null&&(!File.Exists(f.path)||Hash(f.path)!=f.sha256))))throw new InvalidOperationException("보호 파일 변화: "+f.path);}
        var known=new HashSet<string>(baseline.files.Select(f=>f.path));
        foreach(string area in new[]{"Assets","Packages","ProjectSettings"})foreach(string file in Directory.GetFiles(area,"*",SearchOption.AllDirectories)){string path=file.Replace('\\','/');SafePath(path);if(!known.Contains(path)&&!allowed.Contains(path))throw new InvalidOperationException("허용되지 않은 새 파일: "+path);}
        foreach(string file in Directory.GetFiles(".")){string path=Path.GetFileName(file);if(!known.Contains(path))throw new InvalidOperationException("허용되지 않은 루트 파일: "+path);}
        if(EditorApplication.isPlayingOrWillChangePlaymode||PrefabStageUtility.GetCurrentPrefabStage()!=null)throw new InvalidOperationException("Play/Prefab Stage를 먼저 정상 종료하세요.");
        for(int i=0;i<UnityEngine.SceneManagement.SceneManager.sceneCount;i++)if(UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty)throw new InvalidOperationException("미저장 씬을 자동 처리하지 않습니다.");
    }
    static void Report(string name){File.WriteAllLines(Path.Combine(Run,"evidence",name+"_"+DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff")+".txt"),results);}
    [MenuItem("PCS/표시·소리 검수/1 P1 연결 및 새 검증 씬 만들기")]
    public static void CreateP1()
    {
        results.Clear();try{Guard();Check(!File.Exists(TimingPreview),"P1_NEW_PATH","새 씬 경로 충돌 없음");
        var root=PrefabUtility.LoadPrefabContents(Panel);
        try{var view=root.GetComponentInChildren<PCS_HackingView>(true);Check(view,"P1_VIEW","패널의 기존 View 유지");EnsureOverlay(view);Guard();PrefabUtility.SaveAsPrefabAsset(root,Panel,out bool ok);Check(ok,"P1_PREFAB_SAVE","허용 패널만 정상 프리팹 API로 저장");}
        finally{PrefabUtility.UnloadPrefabContents(root);}
        CopyScene("Assets/PCS/Hacking/Scenes/PCS_HackingDemo.unity",TimingPreview);
        ValidateP1();}finally{Report("P1_EDITOR");}
    }
    static RectTransform RectChild(Transform parent,string name){var go=new GameObject(name,typeof(RectTransform));go.transform.SetParent(parent,false);return go.GetComponent<RectTransform>();}
    static void Stretch(RectTransform t){t.anchorMin=Vector2.zero;t.anchorMax=Vector2.one;t.offsetMin=t.offsetMax=Vector2.zero;}
    static void EnsureOverlay(PCS_HackingView view)
    {
        if(view.timingSpriteOverlay)return;
        Check(!view.transform.Find("TimingSpriteOverlay"),"P1_HIERARCHY","기존 사용자 계층 충돌 없음");
        var rt=RectChild(view.transform,"TimingSpriteOverlay");Stretch(rt);var overlay=rt.gameObject.AddComponent<PCS_TimingSpriteOverlay>();
        overlay.pinsRoot=RectChild(rt,"Pins");Stretch(overlay.pinsRoot);
        overlay.pointerPivot=RectChild(rt,"PointerPivot");var image=RectChild(overlay.pointerPivot,"PointerImage");overlay.pointerImage=image.gameObject.AddComponent<Image>();
        overlay.displayMode=PCS_TimingSpriteOverlay.DisplayMode.Sprites;view.timingSpriteOverlay=overlay;
        Check(overlay.PreparePool(),"P1_POOL_AUTHORING","24개 핀 표시 풀 준비");overlay.Hide();
    }
    static void CopyScene(string source,string target)
    {
        Guard();Check(!File.Exists(target),"NEW_SCENE","새 경로만 저장");SafePath(target);
        var scene=EditorSceneManager.OpenScene(source,OpenSceneMode.Single);
        Check(EditorSceneManager.SaveScene(scene,target,true),"SCENE_COPY","원본 씬을 저장하지 않고 별도 검증 씬 복사");
        EditorSceneManager.OpenScene(target,OpenSceneMode.Single);
    }
    [MenuItem("PCS/표시·소리 검수/2 저장 P1·P2 검사 (자산 저장 없음)")]
    public static void ValidateP1Menu(){results.Clear();try{Guard();ValidateP1();}finally{Report("P1_RECHECK");}}
    static void ValidateP1()
    {
        var root=PrefabUtility.LoadPrefabContents(Panel);Texture2D texture=null;Sprite sprite=null;GameObject sessionGo=null;
        try{
            root.SetActive(true);var view=root.GetComponentInChildren<PCS_HackingView>(true);view.gameObject.SetActive(true);var o=view.timingSpriteOverlay;
            Check(o&&o.PoolCount==24&&o.pinImages.All(p=>p&&!p.raycastTarget)&&!o.pointerImage.raycastTarget,"P1_SAVED_REFS","재열기 계층/24핀/장식 Raycast Target 꺼짐");
            Check(!o.waitingSprite&&!o.connectedSprite&&!o.pointerSprite,"P1_EMPTY_SLOTS","저장 Sprite 슬롯은 비어 있음");
            var model=new PCS_TimingPuzzle();model.Begin();view.Bind(model);view.Show(0);
            Check(o.VisiblePinCount==0&&o.DrawLegacyPointer&&o.DrawLegacyPin(0),"P1_EMPTY_FALLBACK","빈 Sprite의 메시 유지/흰 사각형 없음");
            texture=new Texture2D(8,32){hideFlags=HideFlags.HideAndDontSave};var pixels=Enumerable.Repeat(Color.white,256).ToArray();texture.SetPixels(pixels);texture.Apply();
            sprite=Sprite.Create(texture,new Rect(0,0,8,32),new Vector2(.5f,0),100);sprite.hideFlags=HideFlags.HideAndDontSave;o.waitingSprite=o.pointerSprite=sprite;
            int objects=root.GetComponentsInChildren<Transform>(true).Length;
            foreach(int count in new[]{6,8,10,2,24,6}){
                model.TryConfigure(count,90,Mathf.Min(4,170f/count),3,out _);model.Begin();view.Bind(model);view.Show(0);
                Check(o.VisiblePinCount==count&&o.pinImages.Skip(count).All(p=>!p.enabled),"P1_COUNT_"+count,"핀 수와 숨김 일치");
                foreach(float degrees in new[]{0f,90f,180f,270f,359f}){model.Begin();model.Advance(degrees/90f);view.Show(0);Check(Mathf.Abs(Mathf.DeltaAngle(o.pointerPivot.localEulerAngles.z,-model.Angle))<.02f,"P1_ANGLE_"+count+"_"+degrees,"포인터 각도는 모델의 음수 Z");}
                model.Begin();view.Bind(model);model.Connect();view.Show(0);Check(o.pinImages[0].color==o.connectedTint&&model.Connect()==PCS_TimingPuzzle.Hit.AlreadyConnected&&model.MissCount==0,"P1_CONNECTED_"+count,"연결 색/중복 무벌점");
            }
            for(int i=0;i<300;i++)view.Show(0);
            Check(objects==root.GetComponentsInChildren<Transform>(true).Length,"P1_POOL_STABLE","반복 표시 뒤 객체 수 불변");
            o.displayMode=PCS_TimingSpriteOverlay.DisplayMode.Legacy;view.Show(0);Check(o.VisiblePinCount==0&&o.DrawLegacyPin(0),"P1_LEGACY","기존 메시 모드");
            o.displayMode=PCS_TimingSpriteOverlay.DisplayMode.Hybrid;view.Show(0);Check(o.VisiblePinCount==6&&o.DrawLegacyPin(0),"P1_HYBRID","메시와 그림 병행");
            o.displayMode=PCS_TimingSpriteOverlay.DisplayMode.Sprites;o.enabled=false;view.Show(0);Check(o.DrawLegacyPin(0)&&o.VisiblePinCount==0,"P1_DISABLED","비활성 fallback");o.enabled=true;view.Show(0);Check(o.VisiblePinCount==6,"P1_REENABLE","다시 켜면 현재 모델로 표시 복구");
            view.timingSpriteOverlay=null;view.Show(0);view.timingSpriteOverlay=o;Check(model.Running,"P1_MISSING_OVERLAY","참조 누락이 판정을 중단하지 않음");
            foreach(var size in new[]{new Vector2(240,360),new Vector2(430,430),new Vector2(720,500)}){view.rectTransform.sizeDelta=size;view.Show(0);float r=Mathf.Min(view.rectTransform.rect.width,view.rectTransform.rect.height)*.35f;Vector3 pin=view.rectTransform.InverseTransformPoint(o.pinImages[0].transform.position);Check(Mathf.Abs(pin.y-view.rectTransform.rect.center.y-r*o.pinRadiusScale)<.05f,"P1_RESIZE_"+size,"Rect 크기 변경 시 반지름 일치");}
            sessionGo=new GameObject("검수 전용 임시 세션");var session=sessionGo.AddComponent<PCS_HackingSession>();
            var timing=Enumerable.Range(1,3).Select(i=>AssetDatabase.LoadAssetAtPath<PCS_TimingStageData>("Assets/PCS/Hacking/Data/PCS_TimingStage0"+i+".asset")).ToArray();
            Check(session.StartSequence(timing),"P1_SEQUENCE_START","기존 단계 데이터로 순차 실행");int completed=0;session.OnHackCompleted+=_=>completed++;
            for(int stage=0;stage<3;stage++){view.Bind(session.timing);for(int pin=0;pin<session.timing.ActiveTargetCount;pin++){if(pin>0)session.Advance(360f/session.timing.ActiveTargetCount/session.timing.ActiveDegreesPerSecond);session.Press(session.AttemptId);view.Show(0);}Check(session.timing.Succeeded,"P1_SUCCESS_"+stage,"기존 판정 성공");if(stage<2)Check(completed==0&&session.NextStage(),"P1_NEXT_"+stage,"중간 단계 전체 완료 전달 없음");}
            Check(completed==1&&session.Deliveries==1,"P1_ONE_RESULT","전체 완료 한 번");float stopped=session.timing.Angle;session.Advance(5);Check(stopped==session.timing.Angle,"P1_STOP","성공 후 각도 정지");session.CloseHack();
            Check(session.StartSingleStage(timing[1]),"P1_SINGLE_2","2단계 단독");view.Bind(session.timing);session.Advance(22.5f/120f);for(int i=0;i<4;i++)session.Press(session.AttemptId);Check(session.timing.Failed,"P1_FAIL_LIMIT","4번째 실제 오입력 실패");session.RetryCurrentStage();view.Bind(session.timing);view.Show(0);Check(session.timing.MissCount==0&&session.timing.ConnectedCount==0,"P1_RETRY","재시도 상태 초기화");session.CloseHack();
            foreach(int stage in new[]{1,2,3}){var data=AssetDatabase.LoadAssetAtPath<PCS_RotationStageData>("Assets/PCS/Hacking/Data/PCS_RotationStage0"+stage+".asset");Check(session.StartSingleStage(data,2026),"P2_BEGIN_"+stage,"기존 회전 단계 실행");view.BindRotation(session.rotation,i=>session.Rotate(i,session.AttemptId));Check(o.VisiblePinCount==0&&!o.pointerImage.enabled,"P2_HIDE_"+stage,"P1 표시 정리");int count=session.rotation.TileCount;Check(count==new[]{7,13,19}[stage-1]&&view.rotationTiles.Count(t=>t.gameObject.activeSelf)==count,"P2_COUNT_"+stage,"타일 개수");var before=Enumerable.Range(0,count).Select(session.rotation.MaskAt).ToArray();foreach(int i in data.fixedTiles)view.rotationTiles[i].OnPointerClick(new PointerEventData(null){button=PointerEventData.InputButton.Left});Check(before.SequenceEqual(Enumerable.Range(0,count).Select(session.rotation.MaskAt)),"P2_FIXED_"+stage,"고정 클릭 회전 없음");var target=data.graphs[session.rotation.GraphIndex].solvedMasks;for(int i=0;i<count;i++)for(int turn=0;turn<6&&session.rotation.MaskAt(i)!=target[i];turn++)view.rotationTiles[i].OnPointerClick(new PointerEventData(null){button=PointerEventData.InputButton.Left});Check(session.rotation.Succeeded&&session.Deliveries==1,"P2_CLICK_SUCCESS_"+stage,"클릭 핸들러 주입으로 실제 그래프 성공/결과 한 번");session.RestartSession();var initial=Enumerable.Range(0,count).Select(session.rotation.MaskAt).ToArray();session.RetryCurrentStage();Check(initial.SequenceEqual(Enumerable.Range(0,count).Select(session.rotation.MaskAt)),"P2_RETRY_"+stage,"같은 판 재시도");Check(session.NewRotationConfiguration(),"P2_NEW_"+stage,"새 구성");session.CloseHack();}
            model.Begin();view.Bind(model);view.Show(0);Check(o.VisiblePinCount==6&&!view.rotationRoot.activeSelf,"P1_P2_P1","P1 재진입과 P2 숨김");view.ShowConfigurationError("검수용 설정 오류");Check(o.VisiblePinCount==0&&!o.pointerImage.enabled,"P1_ERROR_CLEAR","설정 오류 시 이전 그림 정리");
        }finally{if(sessionGo)UnityEngine.Object.DestroyImmediate(sessionGo);PrefabUtility.UnloadPrefabContents(root);if(sprite)UnityEngine.Object.DestroyImmediate(sprite);if(texture)UnityEngine.Object.DestroyImmediate(texture);}
        Debug.Log("PCS 표시 검수 완료. 실제 키 입력/실물 Sprite 검수와 구분합니다.");
    }

    const string CircuitRig="Assets/PCS/CircuitHack/Prefabs/PCS_CircuitDemoRig.prefab";
    const string CircuitPreview="Assets/PCS/CircuitHack/Scenes/PCS_CircuitTurnSfxPreview.unity";
    [MenuItem("PCS/표시·소리 검수/3 P4 연결 및 새 검증 씬 만들기")]
    public static void CreateP4()
    {
        results.Clear();try{Guard();Check(!File.Exists(CircuitPreview),"P4_NEW_PATH","새 검증 씬 경로 충돌 없음");
        var root=PrefabUtility.LoadPrefabContents(CircuitRig);
        try{var manager=root.GetComponent<CircuitHackManager>();Check(manager&&manager.failureAudio,"P4_MANAGER","기존 Manager와 충돌음 참조 유지");Check(!manager.turnSfx&&!root.transform.Find("PCS_CircuitTurnSfx"),"P4_NO_COLLISION","기존 사용자 SFX 구성 충돌 없음");
            var go=new GameObject("PCS_CircuitTurnSfx",typeof(AudioSource),typeof(CircuitTurnSfx));go.transform.SetParent(root.transform,false);
            var source=go.GetComponent<AudioSource>();source.playOnAwake=false;source.loop=false;source.spatialBlend=0;source.clip=null;
            manager.turnSfx=go.GetComponent<CircuitTurnSfx>();manager.turnSfx.turnSource=source;manager.turnSfx.turnClip=null;manager.turnSfx.volumeScale=.5f;
            Guard();PrefabUtility.SaveAsPrefabAsset(root,CircuitRig,out bool ok);Check(ok,"P4_PREFAB_SAVE","전용 소리 자식만 허용 프리팹에 저장");
        }finally{PrefabUtility.UnloadPrefabContents(root);}
        CopyScene("Assets/PCS/CircuitHack/Scenes/PCS_CircuitHackDemo.unity",CircuitPreview);
        ValidateP4Editor();}finally{Report("P4_EDITOR");}
    }
    [MenuItem("PCS/표시·소리 검수/4 저장 P4 검사 (자산 저장 없음)")]
    public static void ValidateP4Menu(){results.Clear();try{Guard();ValidateP4Editor();}finally{Report("P4_RECHECK");}}
    static void ValidateP4Editor()
    {
        var root=PrefabUtility.LoadPrefabContents(CircuitRig);GameObject probe=null;CircuitMazeData data=null;
        try{var m=root.GetComponent<CircuitHackManager>();var fx=m.turnSfx;
            Check(fx&&fx.transform.parent==m.transform&&fx.turnSource&&fx.turnSource!=m.failureAudio,"P4_SAVED_REFS","재열기 전용 Source/수명/충돌음 분리");
            Check(!fx.turnClip&&!fx.turnSource.clip&&!fx.turnSource.playOnAwake&&!fx.turnSource.loop&&fx.turnSource.spatialBlend==0,"P4_SAVED_EMPTY","빈 Clip과 전용 Source 저장 설정");
            probe=new GameObject("전환음 읽기 검수 전용");var manager=probe.AddComponent<CircuitHackManager>();var sfx=probe.AddComponent<CircuitTurnSfx>();manager.turnSfx=sfx;sfx.turnSource=probe.AddComponent<AudioSource>();sfx.turnSource.playOnAwake=false;
            data=ScriptableObject.CreateInstance<CircuitMazeData>();data.start=new Vector2(50,30);data.goal=new Rect(90,24,8,12);
            Check(manager.StartSingleStage(data),"P4_SINGLE_BEGIN","독립 Manager 단독 실행");Check(sfx.AcceptedTurnNotifications==0,"P4_BEGIN_SILENT","시작 방향 설정은 전환 알림 아님");
            manager.Advance(0,Vector2Int.up);Check(sfx.AcceptedTurnNotifications==1&&sfx.PlaybackRequests==0,"P4_90_EMPTY_CLIP","90도 알림 한 번, 빈 Clip 재생0");
            foreach(var direction in new[]{Vector2Int.up,Vector2Int.down,Vector2Int.zero,new Vector2Int(2,0),new Vector2Int(1,1)})manager.Advance(0,direction);
            Check(sfx.AcceptedTurnNotifications==1,"P4_INVALID_SILENT","동일/반대/0/무효 방향 전환0");
            sfx.turnSource=null;manager.Advance(0,Vector2Int.right);Check(manager.State==CircuitHackManager.HackState.Running&&sfx.AcceptedTurnNotifications==2&&sfx.PlaybackRequests==0,"P4_NULL_SOURCE","Source 누락은 이동 모델에 영향 없음");
            manager.RestartStage();Check(sfx.AcceptedTurnNotifications==2,"P4_RESTART_SILENT","자동 방향 초기화는 알림 없음");manager.CloseHack();manager.Advance(1,Vector2Int.up);Check(sfx.AcceptedTurnNotifications==2,"P4_CLOSED_SILENT","닫힌 상태 알림 없음");
            data.walls=new[]{new Rect(48,33,4,2)};manager.StartSingleStage(data);manager.Advance(1,Vector2Int.up);Check(manager.State==CircuitHackManager.HackState.FailFeedback&&sfx.AcceptedTurnNotifications==3,"P4_TURN_COLLISION","동일 Advance의 전환→충돌 순서 보존");manager.Advance(.75f,Vector2Int.right);Check(manager.State==CircuitHackManager.HackState.Running&&sfx.AcceptedTurnNotifications==3,"P4_AUTO_RETRY","실패 피드백과 자동 재시도는 새 전환0");manager.CloseHack();
            data.walls=Array.Empty<Rect>();int completed=0;manager.OnHackCompleted+=_=>completed++;manager.StartSingleStage(data);manager.Advance(6,Vector2Int.zero);Check(manager.State==CircuitHackManager.HackState.Completed&&completed==1,"P4_SINGLE_COMPLETE","단독 성공은 전체 완료1회");manager.Advance(1,Vector2Int.up);Check(sfx.AcceptedTurnNotifications==3,"P4_COMPLETED_SILENT","종결 이후 입력은 전환0");manager.CloseHack();
            manager.StartSequence(new[]{data,data});manager.Advance(6,Vector2Int.zero);Check(manager.State==CircuitHackManager.HackState.StageFeedback&&completed==1,"P4_SEQUENCE_INTERMEDIATE","중간 단계 성공은 전체 완료 아님");manager.Advance(.6f,Vector2Int.up);manager.Advance(6,Vector2Int.zero);Check(manager.State==CircuitHackManager.HackState.Completed&&completed==2,"P4_SEQUENCE_COMPLETE","선택 순차 세션 종결1회");manager.CloseHack();
        }finally{if(probe)UnityEngine.Object.DestroyImmediate(probe);if(data)UnityEngine.Object.DestroyImmediate(data);PrefabUtility.UnloadPrefabContents(root);}
    }
    static T SceneComponent<T>(UnityEngine.SceneManagement.Scene scene) where T:Component
    {foreach(var root in scene.GetRootGameObjects()){var value=root.GetComponentInChildren<T>(true);if(value)return value;}return null;}
    [MenuItem("PCS/표시·소리 검수/5 현재 검증 씬 Play API 검사")]
    public static void ValidateCurrentPlay()
    {
        results.Clear();try{
            Check(EditorApplication.isPlaying&&UnityEngine.SceneManagement.SceneManager.sceneCount==1,"PLAY_SCOPE","검증 씬 하나에서만 명시 실행");var scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if(scene.path==TimingPreview)ValidateTimingPlay(scene);else if(scene.path==CircuitPreview)ValidateCircuitPlay(scene);else throw new InvalidOperationException("허용된 검증 씬을 여세요.");
        }finally{Report("PLAY_API");}
    }
    static void ValidateTimingPlay(UnityEngine.SceneManagement.Scene scene)
    {
        var host=SceneComponent<PCS_HackingDemoController>(scene);Check(host&&host.presenter,"PLAY_P1_HOST","자기 데모 구성체 참조");host.Close();host.StartTimingStage(1);host.presenter.Refresh();var o=host.view.timingSpriteOverlay;
        Check(o&&o.DrawLegacyPointer&&o.VisiblePinCount==0,"PLAY_P1_EMPTY","빈 Sprite는 기존 포인터 유지");Texture2D texture=null;Sprite sprite=null;
        try{texture=new Texture2D(8,32){hideFlags=HideFlags.HideAndDontSave};texture.SetPixels(Enumerable.Repeat(Color.white,256).ToArray());texture.Apply();sprite=Sprite.Create(texture,new Rect(0,0,8,32),Vector2.one*.5f);sprite.hideFlags=HideFlags.HideAndDontSave;o.waitingSprite=o.pointerSprite=sprite;
            host.presenter.Refresh();Check(o.VisiblePinCount==8&&!o.DrawLegacyPointer,"PLAY_P1_MEMORY_SPRITE","메모리 검수 Sprite 8핀/포인터 표시");int count=o.transform.childCount+o.pinsRoot.childCount;
            for(int i=0;i<240;i++){host.session.Advance(1f/120);host.presenter.Refresh();}
            Check(Mathf.Abs(Mathf.DeltaAngle(o.pointerPivot.localEulerAngles.z,-host.session.timing.Angle))<.02f&&count==o.transform.childCount+o.pinsRoot.childCount,"PLAY_P1_SYNC_POOL","실제 Play 모델 각도 동기화/풀 증가 없음");
            host.session.timing.Begin();host.view.Bind(host.session.timing);host.session.Press(host.session.AttemptId);host.presenter.Refresh();Check(o.pinImages[0].color==o.connectedTint,"PLAY_P1_CONNECTED","연결 핀 표시");
            host.Restart();Check(o.VisiblePinCount==8&&!host.session.timing.IsConnected(0),"PLAY_P1_RETRY","재시도 이전 연결 정리");host.Close();Check(o.VisiblePinCount==0,"PLAY_P1_CLOSE","닫기 정리");host.StartRotationStage(2);host.presenter.Refresh();Check(o.VisiblePinCount==0&&!o.pointerImage.enabled,"PLAY_P1_P2_CLEAR","P2 진입 때 Sprite 숨김");host.Close();host.StartTimingStage(0);host.presenter.Refresh();Check(o.VisiblePinCount==6,"PLAY_P1_P2_P1","핀 감소와 P1 복귀");
        }finally{o.waitingSprite=o.pointerSprite=null;host.Close();if(sprite)UnityEngine.Object.Destroy(sprite);if(texture)UnityEngine.Object.Destroy(texture);}
    }
    static void ValidateCircuitPlay(UnityEngine.SceneManagement.Scene scene)
    {
        var manager=SceneComponent<CircuitHackManager>(scene);Check(manager&&manager.turnSfx&&manager.failureAudio,"PLAY_P4_REFS","저장된 전용 소리와 기존 실패음");var sfx=manager.turnSfx;var source=sfx.turnSource;var failureClip=manager.failureAudio.clip;Check(failureClip,"PLAY_P4_FAILURE_CLIP","기존 Host 생성 충돌음 유지");
        CircuitMazeData data=null;AudioClip clip=null;var oldClip=sfx.turnClip;int notifications=sfx.AcceptedTurnNotifications,requests=sfx.PlaybackRequests;
        try{manager.CloseHack();data=ScriptableObject.CreateInstance<CircuitMazeData>();data.start=new Vector2(50,30);data.goal=new Rect(90,24,8,12);manager.StartSingleStage(data);manager.Advance(0,Vector2Int.up);Check(sfx.AcceptedTurnNotifications==notifications+1&&sfx.PlaybackRequests==requests,"PLAY_P4_EMPTY_CLIP","빈 Clip이어도 실제 Play 전환과 진행 유지");
            foreach(var d in new[]{Vector2Int.up,Vector2Int.down,Vector2Int.zero,new Vector2Int(2,0),new Vector2Int(1,1)})manager.Advance(0,d);Check(sfx.AcceptedTurnNotifications==notifications+1,"PLAY_P4_INVALID","동일/반대/0/무효 입력 알림0");
            // 파일이 아닌 무음 메모리 Clip으로 재생 요청 경로만 검사한다. 실제 음원 청취 검수가 아니다.
            clip=AudioClip.Create("PCS_검수전용_무음_메모리",4410,1,44100,false);clip.hideFlags=HideFlags.HideAndDontSave;sfx.turnClip=clip;manager.Advance(0,Vector2Int.right);Check(sfx.PlaybackRequests==requests+1,"PLAY_P4_ONESHOT_REQUEST","실제 Play의 전용 Source PlayOneShot 요청1");
            sfx.turnSource=manager.failureAudio;manager.Advance(0,Vector2Int.up);Check(sfx.PlaybackRequests==requests+1&&manager.failureAudio.clip==failureClip,"PLAY_P4_SHARED_REJECT","공유 Source 전환음 거절/실패음 clip 보존");sfx.turnSource=source;
            sfx.enabled=false;manager.Advance(0,Vector2Int.right);Check(sfx.PlaybackRequests==requests+1,"PLAY_P4_DISABLED","컴포넌트 비활성은 재생0");sfx.enabled=true;
            sfx.turnSource=null;manager.Advance(0,Vector2Int.up);Check(sfx.PlaybackRequests==requests+1&&manager.State==CircuitHackManager.HackState.Running,"PLAY_P4_MISSING_SOURCE","Source 누락이어도 진행");sfx.turnSource=source;
            manager.RestartStage();Check(!source.isPlaying,"PLAY_P4_CLEAR","재시도에서 전용 재생 정리");manager.CloseHack();data.walls=new[]{new Rect(48,33,4,2)};manager.StartSingleStage(data);manager.Advance(1,Vector2Int.up);Check(manager.State==CircuitHackManager.HackState.FailFeedback&&manager.failureAudio.clip==failureClip&&sfx.PlaybackRequests==requests+2,"PLAY_P4_TURN_AND_FAIL","전환음 요청과 기존 충돌 경로 동시 유지");int count=sfx.AcceptedTurnNotifications;manager.Advance(.75f,Vector2Int.right);Check(sfx.AcceptedTurnNotifications==count&&!source.isPlaying,"PLAY_P4_FEEDBACK_RETRY","실패 피드백/자동 재시작 자체 알림0");
        }finally{sfx.turnSource=source;sfx.turnClip=oldClip;sfx.enabled=true;manager.CloseHack();if(data)UnityEngine.Object.Destroy(data);if(clip)UnityEngine.Object.Destroy(clip);}
    }

    public static void OpenCircuitPreview(){Guard();EditorSceneManager.OpenScene(CircuitPreview,OpenSceneMode.Single);}
    [MenuItem("PCS/표시·소리 검수/8 P1 검증 씬 열기 (Edit)")]
    public static void OpenTimingPreview(){Guard();EditorSceneManager.OpenScene(TimingPreview,OpenSceneMode.Single);}
    [MenuItem("PCS/표시·소리 검수/6 현재 Play 상태 기록 (읽기)")]
    public static void CapturePlayState()
    {
        results.Clear();var scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if(!EditorApplication.isPlaying||UnityEngine.SceneManagement.SceneManager.sceneCount!=1||(scene.path!=TimingPreview&&scene.path!=CircuitPreview))throw new InvalidOperationException("검증 씬 Play에서만 기록하세요.");
        var m=SceneComponent<CircuitHackManager>(scene);if(m)results.Add("P4 state="+m.State+" run="+m.RunId+" attempt="+m.AttemptId+" direction="+m.Signal.Direction+" accepted="+(m.turnSfx?m.turnSfx.AcceptedTurnNotifications:-1)+" requests="+(m.turnSfx?m.turnSfx.PlaybackRequests:-1)+" inputFrames="+(m.input?m.input.ObservedDirectionFrames:-1)+" release="+(m.input&&m.input.ReleaseReady)+" failureClip="+(m.failureAudio&&m.failureAudio.clip));
        var h=SceneComponent<PCS_HackingDemoController>(scene);if(h)results.Add("P1 state="+h.session.State+" pins="+h.session.timing.ActiveTargetCount+" connected="+h.session.timing.ConnectedCount+" angle="+h.session.timing.Angle+" pool="+h.view.timingSpriteOverlay.PoolCount+" visible="+h.view.timingSpriteOverlay.VisiblePinCount);
        Report("ACTUAL_PLAY_STATE");
    }

    [MenuItem("PCS/표시·소리 검수/7 P4 키 입력 검수판 (Play 메모리)")]
    public static void PrepareKeyboardProbe()
    {
        var scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();if(!EditorApplication.isPlaying||scene.path!=CircuitPreview||UnityEngine.SceneManagement.SceneManager.sceneCount!=1)throw new InvalidOperationException("P4 검증 씬 Play에서 실행하세요.");
        var m=SceneComponent<CircuitHackManager>(scene);m.CloseHack();var data=ScriptableObject.CreateInstance<CircuitMazeData>();data.hideFlags=HideFlags.HideAndDontSave;data.name="PCS_키검수_메모리";data.start=new Vector2(50,30);data.goal=new Rect(90,24,8,12);data.baseSpeed=.25f;
        try{if(!m.StartSingleStage(data))throw new InvalidOperationException(m.Error);}finally{UnityEngine.Object.Destroy(data);}
        // 시작 구성만 메모리로 준비한다. 다음 방향 입력은 기존 InputAdapter가 실제 키에서 읽는다.
        CapturePlayState();
    }

    static Texture2D previewTexture;static Sprite previewSprite;static PCS_TimingSpriteOverlay previewOverlay;static PCS_TimingSpriteOverlay.DisplayMode previewMode;
    [MenuItem("PCS/표시·소리 검수/9 P1 메모리 Sprite 겹쳐 보기 (Play)")]
    public static void PreviewMemorySprites()
    {
        var scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();if(!EditorApplication.isPlaying||scene.path!=TimingPreview||UnityEngine.SceneManagement.SceneManager.sceneCount!=1)throw new InvalidOperationException("P1 검증 씬 Play에서 실행하세요.");
        ClearPreviewSprites();var host=SceneComponent<PCS_HackingDemoController>(scene);host.Close();host.StartTimingStage(0);host.presenter.Refresh();var o=host.view.timingSpriteOverlay;
        if(o.waitingSprite||o.connectedSprite||o.pointerSprite)throw new InvalidOperationException("사용자 Sprite를 덮어쓰지 않습니다. 빈 검증 슬롯에서 실행하세요.");
        previewTexture=new Texture2D(8,32){hideFlags=HideFlags.HideAndDontSave};previewTexture.SetPixels(Enumerable.Repeat(Color.white,256).ToArray());previewTexture.Apply();previewSprite=Sprite.Create(previewTexture,new Rect(0,0,8,32),new Vector2(.5f,0));previewSprite.hideFlags=HideFlags.HideAndDontSave;
        previewOverlay=o;previewMode=o.displayMode;o.waitingSprite=o.pointerSprite=previewSprite;o.displayMode=PCS_TimingSpriteOverlay.DisplayMode.Hybrid;o.Show();EditorApplication.playModeStateChanged+=ClearOnPlayExit;
    }
    static void ClearOnPlayExit(PlayModeStateChange state){if(state==PlayModeStateChange.ExitingPlayMode)ClearPreviewSprites();}
    static void ClearPreviewSprites()
    {
        EditorApplication.playModeStateChanged-=ClearOnPlayExit;if(previewOverlay){if(previewOverlay.waitingSprite==previewSprite)previewOverlay.waitingSprite=null;if(previewOverlay.pointerSprite==previewSprite)previewOverlay.pointerSprite=null;previewOverlay.displayMode=previewMode;previewOverlay.Show();}previewOverlay=null;
        if(previewSprite)UnityEngine.Object.DestroyImmediate(previewSprite);if(previewTexture)UnityEngine.Object.DestroyImmediate(previewTexture);previewSprite=null;previewTexture=null;
    }
}
