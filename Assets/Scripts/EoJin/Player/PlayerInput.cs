using Fusion;
using UnityEngine;

// One local input source for movement and puzzle abilities.
public class PlayerInput : NetworkBehaviour
{
    public Vector2 MoveInput;
    public bool JumpInput;
    public bool CanMoveInput = true;
    public bool CanJumpInput = true;

    public bool InteractInput { get; private set; }
    public bool AbilityInput { get; private set; }
    public bool AbilityHeldInput { get; private set; }
    public bool CarryInput { get; private set; }
    public bool RefillInput { get; private set; }
    public bool ResetInput { get; private set; }
    public int ChannelInput { get; private set; } = -1;
    public float LadderInput { get; private set; }
    public Vector2 AimWorld { get; private set; }
    public int Facing { get; private set; } = 1;
    private Camera inputCamera;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    private bool developmentInput;
#endif

    private void Update()
    {
        if (Object == null || !HasStateAuthority)
            return;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (developmentInput)
            return;
#endif
        MoveInput = Vector2.zero;
        if (CanMoveInput)
        {
            if (Input.GetKey(KeyCode.LeftArrow) || Input.GetKey(KeyCode.A))
                MoveInput.x -= 1f;
            if (Input.GetKey(KeyCode.RightArrow) || Input.GetKey(KeyCode.D))
                MoveInput.x += 1f;
        }
        if (MoveInput.x != 0f)
            Facing = MoveInput.x > 0f ? 1 : -1;
        if (CanJumpInput && (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.UpArrow)))
            JumpInput = true;

        LadderInput = (Input.GetKey(KeyCode.W) ? 1f : 0f) - (Input.GetKey(KeyCode.S) ? 1f : 0f);
        InteractInput |= Input.GetKeyDown(KeyCode.E);
        AbilityInput |= Input.GetKeyDown(KeyCode.F);
        AbilityHeldInput = Input.GetKey(KeyCode.F);
        CarryInput |= Input.GetKeyDown(KeyCode.G);
        RefillInput |= Input.GetKeyDown(KeyCode.R);
        ResetInput |= Input.GetKeyDown(KeyCode.Backspace);
        for (int i = 0; i < 4; i++)
            if (Input.GetKeyDown((KeyCode)((int)KeyCode.Alpha1 + i)))
                ChannelInput = i;

        if (inputCamera == null)
            inputCamera = Camera.main;
        if (inputCamera != null)
        {
            Ray ray = inputCamera.ScreenPointToRay(Input.mousePosition);
            float dz = ray.direction.z;
            AimWorld = Mathf.Abs(dz) > 0.0001f
                ? (Vector2)(ray.origin + ray.direction * ((transform.position.z - ray.origin.z) / dz))
                : (Vector2)transform.position + Vector2.right * Facing;
        }
        else
            AimWorld = (Vector2)transform.position + Vector2.right * Facing;
    }

    // Button presses survive render frames until the owner's network tick consumes them.
    public void ConsumeActions()
    {
        InteractInput = false;
        AbilityInput = false;
        CarryInput = false;
        RefillInput = false;
        ResetInput = false;
        ChannelInput = -1;
    }

    public void ResetInputState()
    {
        ClearLocalInput();
        CanMoveInput = true;
        CanJumpInput = true;
    }

    private void ClearLocalInput()
    {
        ConsumeActions();
        MoveInput = Vector2.zero;
        JumpInput = false;
        LadderInput = 0f;
        AbilityHeldInput = false;
    }

    private void OnDisable()
    {
        ClearLocalInput();
    }

    private void OnApplicationFocus(bool focused)
    {
        if (!focused)
            ClearLocalInput();
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    // Tests still require a normally spawned, locally authoritative player and its normal Mover tick.
    public void InjectDevelopmentInput(Vector2 move, bool jump, bool interact = false,
        bool ability = false, bool carry = false, int channel = -1, Vector2? aim = null,
        float ladder = 0f, bool refill = false, bool abilityHeld = false)
    {
        if (Object == null || !HasStateAuthority)
            return;
        developmentInput = true;
        MoveInput = Vector2.ClampMagnitude(move, 1f);
        JumpInput |= jump;
        InteractInput |= interact;
        AbilityInput |= ability;
        AbilityHeldInput = abilityHeld;
        CarryInput |= carry;
        RefillInput |= refill;
        if (channel >= 0 && channel < 4)
            ChannelInput = channel;
        LadderInput = Mathf.Clamp(ladder, -1f, 1f);
        if (move.x != 0f)
            Facing = move.x > 0f ? 1 : -1;
        AimWorld = aim ?? ((Vector2)transform.position + Vector2.right * Facing);
    }

    public void ClearDevelopmentInput()
    {
        developmentInput = false;
        ResetInputState();
    }
#endif
}
