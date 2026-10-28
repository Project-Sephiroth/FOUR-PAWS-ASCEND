using UnityEngine;
using UnityEngine.InputSystem;

namespace FourPaws.PCS.CircuitHack
{
    // P4의 장치 상태만 읽는다. 팀 입력 맵이나 액터를 찾아 변경하지 않는다.
    public sealed class CircuitInputAdapter : MonoBehaviour
    {
        private bool released;
        public int ObservedDirectionFrames {get;private set;}
        public bool ReleaseReady=>released;
        public bool LastKeyboardPresent {get;private set;}
        public bool LastKeyboardEnabled {get;private set;}
        public double LastKeyboardUpdate {get;private set;}
        public int ObservedKeyPressFrames {get;private set;}
        public void RequireRelease(){released=false;}
        // Both 모드에서는 P1/P3와 같은 키보드 경로를 사용한다. 패드는 새 입력 시스템에서 독립적으로 읽는다.
        public string KeyboardBackend {
            get {
#if ENABLE_LEGACY_INPUT_MANAGER
                return "UnityEngine.Input";
#else
                return "InputSystem.Keyboard";
#endif
            }
        }
        public bool ClosePressed {
            get {
#if ENABLE_LEGACY_INPUT_MANAGER
                return Input.GetKeyDown(KeyCode.Escape);
#else
                return Keyboard.current!=null&&Keyboard.current.escapeKey.wasPressedThisFrame;
#endif
            }
        }
        public static Vector2Int Resolve(bool right,bool up,bool left,bool down)
        {
            if((right?1:0)+(up?1:0)+(left?1:0)+(down?1:0)!=1)return Vector2Int.zero;
            return right?Vector2Int.right:up?Vector2Int.up:left?Vector2Int.left:Vector2Int.down;
        }
        public bool Read(out Vector2Int direction)
        {
            direction=Vector2Int.zero;var k=Keyboard.current;var p=Gamepad.current;
            LastKeyboardPresent=k!=null;LastKeyboardEnabled=k!=null&&k.enabled;LastKeyboardUpdate=k==null?0:k.lastUpdateTime;
            bool right,up,left,down,held;
#if ENABLE_LEGACY_INPUT_MANAGER
            if(Input.anyKeyDown)ObservedKeyPressFrames++;
            right=Input.GetKeyDown(KeyCode.D)||Input.GetKeyDown(KeyCode.RightArrow);
            up=Input.GetKeyDown(KeyCode.W)||Input.GetKeyDown(KeyCode.UpArrow);
            left=Input.GetKeyDown(KeyCode.A)||Input.GetKeyDown(KeyCode.LeftArrow);
            down=Input.GetKeyDown(KeyCode.S)||Input.GetKeyDown(KeyCode.DownArrow);
            held=Input.GetKey(KeyCode.D)||Input.GetKey(KeyCode.RightArrow)||Input.GetKey(KeyCode.W)||Input.GetKey(KeyCode.UpArrow)||Input.GetKey(KeyCode.A)||Input.GetKey(KeyCode.LeftArrow)||Input.GetKey(KeyCode.S)||Input.GetKey(KeyCode.DownArrow);
#else
            if(k!=null&&k.anyKey.wasPressedThisFrame)ObservedKeyPressFrames++;
            right=k!=null&&(k.dKey.wasPressedThisFrame||k.rightArrowKey.wasPressedThisFrame);
            up=k!=null&&(k.wKey.wasPressedThisFrame||k.upArrowKey.wasPressedThisFrame);
            left=k!=null&&(k.aKey.wasPressedThisFrame||k.leftArrowKey.wasPressedThisFrame);
            down=k!=null&&(k.sKey.wasPressedThisFrame||k.downArrowKey.wasPressedThisFrame);
            held=k!=null&&(k.dKey.isPressed||k.rightArrowKey.isPressed||k.wKey.isPressed||k.upArrowKey.isPressed||k.aKey.isPressed||k.leftArrowKey.isPressed||k.sKey.isPressed||k.downArrowKey.isPressed);
#endif
            held|=p!=null&&p.dpad.ReadValue()!=Vector2.zero;
            if(!released){if(!held)released=true;return false;}
            direction=Resolve(right||(p!=null&&p.dpad.right.wasPressedThisFrame),up||(p!=null&&p.dpad.up.wasPressedThisFrame),left||(p!=null&&p.dpad.left.wasPressedThisFrame),down||(p!=null&&p.dpad.down.wasPressedThisFrame));
            if(direction!=Vector2Int.zero)ObservedDirectionFrames++;
            return true;
        }
        private void OnDisable(){RequireRelease();}
    }
}
