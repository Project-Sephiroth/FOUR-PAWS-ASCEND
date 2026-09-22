using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Camera))]
public class PcsLocalCameraFollow : MonoBehaviour
{
    [System.Serializable]
    public class CameraRegion
    {
        public string name;
        [Tooltip("-1 accepts every section; otherwise 0 tutorial, 1 first puzzle/corridor, 2 shaft, 3 complete.")]
        public int section = -1;
        public MyEnum.CharacterType role;
        [Tooltip("Actor world position selects the first matching region in this list.")]
        public Rect selectionBounds;
        [Tooltip("Preferred world limits for the visible area; player visibility takes precedence at an edge.")]
        public Rect viewBounds;
        [Min(0.1f)] public float orthographicSize = 5.6f;
    }

    [SerializeField] private Vector2 offset = new Vector2(0f, 1f);
    [SerializeField, Min(0f)] private float followTime = 0.12f;
    [SerializeField, Min(0f), Tooltip("0 preserves the existing camera size, including legacy GameScene cameras.")]
    private float fallbackOrthographicSize;
    [SerializeField, Min(0f), Tooltip("World-space room around the actor kept inside the viewport during smoothing.")]
    private float playerViewMargin = 1.2f;
    [SerializeField] private CameraRegion[] regions = new CameraRegion[0];

    private Transform target;
    private PcsPlayerAbilities targetAbilities;
    private Camera viewCamera;
    private Vector3 velocity;
    private float sizeVelocity;
    private float originalSize;

    private void InitializeCamera()
    {
        if (viewCamera != null) return;
        viewCamera = GetComponent<Camera>();
        originalSize = viewCamera.orthographicSize;
    }

    public void Bind(Transform localPlayer)
    {
        InitializeCamera();
        target = localPlayer;
        targetAbilities = target != null ? target.GetComponent<PcsPlayerAbilities>() : null;
        velocity = Vector3.zero;
        sizeVelocity = 0f;
        if (target != null) UpdateView(true);
    }

    public void Unbind(Transform localPlayer)
    {
        if (target != localPlayer) return;
        target = null;
        targetAbilities = null;
        velocity = Vector3.zero;
        sizeVelocity = 0f;
    }

    private CameraRegion SelectRegion(Vector2 actor, int section, MyEnum.CharacterType role)
    {
        if (regions == null) return null;
        foreach (CameraRegion region in regions)
            if (region != null && region.orthographicSize > 0f &&
                region.selectionBounds.width > 0f && region.selectionBounds.height > 0f &&
                region.viewBounds.width > 0f && region.viewBounds.height > 0f &&
                (region.section < 0 || region.section == section) &&
                (region.role == MyEnum.CharacterType.None || region.role == role) &&
                region.selectionBounds.Contains(actor)) return region;
        return null;
    }

    // Also allows Editor checks to evaluate saved regions without moving a camera or actor.
    public Vector3 CalculateView(Vector3 actor, float aspect, int section, MyEnum.CharacterType role, out float size)
    {
        InitializeCamera();
        CameraRegion region = SelectRegion(actor, section, role);
        bool legacy = regions == null || regions.Length == 0;
        size = region != null ? region.orthographicSize :
            !legacy && fallbackOrthographicSize > 0f ? fallbackOrthographicSize : originalSize;
        Vector3 desired = new Vector3(actor.x + offset.x, actor.y + offset.y, transform.position.z);
        return legacy ? desired : ConstrainPosition(desired, actor, region, size, aspect, playerViewMargin);
    }

    private static Vector3 ConstrainPosition(Vector3 candidate, Vector3 actor, CameraRegion region,
        float size, float aspect, float margin)
    {
        float halfHeight = Mathf.Max(0.1f, size);
        float halfWidth = halfHeight * Mathf.Max(0.1f, aspect);
        candidate.x = ConstrainAxis(candidate.x, actor.x, halfWidth, margin,
            region != null, region != null ? region.viewBounds.xMin : 0f, region != null ? region.viewBounds.xMax : 0f);
        candidate.y = ConstrainAxis(candidate.y, actor.y, halfHeight, margin,
            region != null, region != null ? region.viewBounds.yMin : 0f, region != null ? region.viewBounds.yMax : 0f);
        return candidate;
    }

    private static float ConstrainAxis(float candidate, float actor, float halfView, float margin,
        bool bounded, float minimum, float maximum)
    {
        float visibleRadius = Mathf.Max(0.05f, halfView - Mathf.Min(margin, halfView * 0.8f));
        float visibleMinimum = actor - visibleRadius;
        float visibleMaximum = actor + visibleRadius;
        if (!bounded) return Mathf.Clamp(candidate, visibleMinimum, visibleMaximum);
        float lower = minimum + halfView;
        float upper = maximum - halfView;
        // A narrow region cannot fill a wide viewport; center it without inventing a new zoom.
        if (lower > upper) lower = upper = (minimum + maximum) * 0.5f;
        // An actor crossing an edge must remain visible even when that relaxes the preferred region bound.
        if (upper < visibleMinimum) return visibleMinimum;
        if (lower > visibleMaximum) return visibleMaximum;
        return Mathf.Clamp(candidate, Mathf.Max(lower, visibleMinimum), Mathf.Min(upper, visibleMaximum));
    }

    private void UpdateView(bool immediate)
    {
        InitializeCamera();
        // Dynamically attached legacy cameras have no regions: retain their existing size and follow behavior.
        if (regions == null || regions.Length == 0)
        {
            Vector3 legacyDestination = new Vector3(target.position.x + offset.x, target.position.y + offset.y, transform.position.z);
            transform.position = immediate || followTime <= 0f ? legacyDestination :
                Vector3.SmoothDamp(transform.position, legacyDestination, ref velocity, followTime);
            return;
        }
        PcsPuzzleDirector director = PcsPuzzleDirector.Instance;
        int section = director != null && director.CanSpawnPlayers ? director.ActiveSection : -1;
        MyEnum.CharacterType role = targetAbilities != null ? targetAbilities.CharacterType : MyEnum.CharacterType.None;
        CameraRegion region = SelectRegion(target.position, section, role);
        Vector3 destination = CalculateView(target.position, viewCamera.aspect, section, role, out float targetSize);
        if (viewCamera.orthographic)
            viewCamera.orthographicSize = immediate || followTime <= 0f ? targetSize :
                Mathf.SmoothDamp(viewCamera.orthographicSize, targetSize, ref sizeVelocity, followTime);
        Vector3 position = immediate || followTime <= 0f ? destination :
            Vector3.SmoothDamp(transform.position, destination, ref velocity, followTime);
        if (viewCamera.orthographic)
            position = ConstrainPosition(position, target.position, region, viewCamera.orthographicSize,
                viewCamera.aspect, playerViewMargin);
        position.z = transform.position.z;
        transform.position = position;
    }

    private void LateUpdate()
    {
        if (target != null) UpdateView(false);
    }
}
