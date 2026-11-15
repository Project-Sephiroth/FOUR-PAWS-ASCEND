using System;
using UnityEngine;
using UnityEngine.UI;
using static NetworkGameManager;

public class StageMapUI : MonoBehaviour
{
    [Serializable]
    private struct StageNode
    {
        public SceneName stage;
        public Image icon;
        public Image glow;
        public Button button;
    }

    [SerializeField] private Text status;
    [SerializeField] private StageNode[] nodes;

    private void Awake()
    {
        foreach (StageNode node in nodes)
        {
            SceneName stage = node.stage;
            node.button.onClick.AddListener(() =>
            {
                SoundManager.Play(SoundEffect.UIClick);
                Instance?.SelectStage(stage);
            });
        }
    }

    // 현재 클리어 기록과 씬 선택 권한을 읽어 누를 수 있는 버튼을 갱신합니다. 씬 전환 요청은 버튼에서 NetworkGameManager로 보냅니다.
    private void Update()
    {
        var manager = Instance;
        bool connected = manager != null && manager.Object != null && manager.Object.IsValid;
        bool canChoose = connected && manager.CanSelectStage;
        status.text = !connected ? "Connecting to the room..." :
            canChoose ? "���������� �����ϼ���" :
            manager.Runner.IsSharedModeMasterClient ? "�������� �ε� ��..." : "������ ���������� ������ �־��";

        float glowAlpha = 0.62f + 0.17f * Mathf.Sin(Time.unscaledTime * 3f);
        foreach (StageNode node in nodes)
        {
            bool unlocked = connected && manager.IsStageSelectable(node.stage);
            bool cleared = connected && ((node.stage == SceneName.Malkuth && manager.MalkuthCleared) ||
                (node.stage == SceneName.Yesod && manager.YesodCleared));

            node.icon.color = unlocked ? Color.white : cleared ? new Color(0.8f, 0.72f, 0.57f) : new Color(0.46f, 0.46f, 0.5f);
            node.glow.enabled = unlocked;
            Color glowColor = node.glow.color;
            glowColor.a = glowAlpha;
            node.glow.color = glowColor;
            node.button.interactable = unlocked && canChoose;
        }
    }
}
