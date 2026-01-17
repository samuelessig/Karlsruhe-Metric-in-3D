using UnityEngine;

public class PointDragger : MonoBehaviour
{
    [SerializeField] public Camera cam;
    [SerializeField] private DragMode mode = DragMode.Volume;
    [SerializeField] private KeyCode toggleKey = KeyCode.Tab;
    [SerializeField] private LayerMask pointLayerMask;

    [Header("Target")]
    [SerializeField] private SphereGenerator sphereGenerator;

    [Header("UI")]
    [SerializeField] private bool groupDraggingEnabled = false; // Same as holding [Shift] but programmatically settable

    private bool isDragging;
    private Transform selectedChild;

    private Plane dragPlane;
    private Vector3 grabOffsetWorld;

    // Store the locked angular direction for Radial mode
    private Vector3 fixedDirection;

    // For group dragging in Volume mode
    private Vector3 lastSelectedWorldPos;

    private void Awake()
    {
        pointLayerMask = 1 << LayerMask.NameToLayer("Points");
    }

    void Update()
    {
        if (Input.GetKeyDown(toggleKey))
        {
            CycleMode();
        }

        HandleInput();
    }

    public void EnableDragAll(bool enabled) => groupDraggingEnabled = enabled;
    public bool IsDragAll() => groupDraggingEnabled;

    public DragMode GetMode() => mode;

    public void SetMode(DragMode m)
    {
        mode = m;
    }

    public void CycleMode()
    {
        mode = (DragMode)(((int)mode + 1) % 3);
    }

    void HandleInput()
    {
        if (cam == null) return;

        if (Input.GetMouseButtonDown(0) && cam.enabled)
        {
            //Debug.Log("Mouse down");
            Ray ray = cam.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out var hit, Mathf.Infinity, pointLayerMask) &&
                hit.transform != null &&
                hit.collider.transform.CompareTag(Constants.REFERENCE_POINT_TAG))
            {
                selectedChild = hit.collider.transform;
                isDragging = true;

                lastSelectedWorldPos = selectedChild.position;

                // Use a camera-facing plane for Volume and Radial
                if (mode == DragMode.Volume || mode == DragMode.Radial)
                {
                    dragPlane = new Plane(-cam.transform.forward, selectedChild.position);
                    if (dragPlane.Raycast(ray, out float t0))
                    {
                        var planeHit = ray.GetPoint(t0);
                        grabOffsetWorld = selectedChild.position - planeHit;
                    }
                    else grabOffsetWorld = Vector3.zero;
                }

                // Capture the current direction from center (locked for radial drags)
                if (mode == DragMode.Radial)
                {
                    Vector3 center = SphereCenter();
                    Vector3 dir = selectedChild.position - center;
                    fixedDirection = (dir.sqrMagnitude > 0f) ? dir.normalized : Vector3.forward;
                }
            }
        }

        if (Input.GetMouseButtonUp(0))
        {
            isDragging = false;
            selectedChild = null;
            grabOffsetWorld = Vector3.zero;
        }

        if (isDragging && selectedChild != null)
        {
            switch (mode)
            {
                case DragMode.Volume:
                  DragInsideVolume();
                  break;
                case DragMode.Surface: 
                  DragOnSurface();
                  break;
                case DragMode.Radial:
                  DragRadial();
                  break;
            }
        }
    }

    float SphereRadius() => sphereGenerator.radius * transform.lossyScale.x;
    Vector3 SphereCenter() => transform.position;

    void DragInsideVolume()
    {
        Ray ray = cam.ScreenPointToRay(Input.mousePosition);
        if (!dragPlane.Raycast(ray, out float t)) return;

        Vector3 target = ray.GetPoint(t) + grabOffsetWorld;
        Vector3 center = SphereCenter();
        float radius = SphereRadius();

        Vector3 offset = target - center;
        float r2 = radius * radius;
        if (offset.sqrMagnitude > r2) offset = offset.normalized * radius;

        var spherePoint = selectedChild.GetComponent<ReferencePointHandler>();
        var voronoiShader = sphereGenerator.voronoiShader;

        // New position of the selected point
        Vector3 newPos = center + offset;

        // 2D mode: clamp to y=0 plane in sphere object space
        if (sphereGenerator.Is2DShader())
        {
            Vector3 local = sphereGenerator.transform.InverseTransformPoint(newPos);
            local.y = 0f; // x-z plane in object space
            newPos = sphereGenerator.transform.TransformPoint(local);
        }

        // Compute how much the selected point moved in this frame
        Vector3 delta = newPos - lastSelectedWorldPos;

        bool shiftHeld = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        bool groupDrag = groupDraggingEnabled || shiftHeld;

        // If Shift (or groupDraggingEnabled) is active, also move all other points by the same vector
        if (delta.sqrMagnitude > 0f && groupDrag)
        {
            MoveAllOtherPoints(delta, voronoiShader);
        }

        // Apply to selected point
        if (spherePoint != null) spherePoint.SetEuclideanPosition(newPos);

        // Update last position for the next frame
        lastSelectedWorldPos = newPos;
    }

    void MoveAllOtherPoints(Vector3 delta, Shader voronoiShader)
    {
        if (sphereGenerator == null) return;

        var allPoints = sphereGenerator.GetComponentsInChildren<ReferencePointHandler>();
        if (allPoints == null) return;

        Vector3 center = SphereCenter();
        float radius = SphereRadius();
        float r2 = radius * radius;

        foreach (var rp in allPoints)
        {
            if (rp == null) continue;
            if (rp.transform == selectedChild) continue; // skip the one that was dragged

            Vector3 pos = rp.transform.position;
            Vector3 target = pos + delta;

            // Clamp inside sphere
            Vector3 offset = target - center;
            if (offset.sqrMagnitude > r2)
                offset = offset.normalized * radius;

            Vector3 newPos = center + offset;

            // In 2D mode, also clamp to the y=0 plane
            if (Utils.Is2DShader(voronoiShader))
            {
                Vector3 local = sphereGenerator.transform.InverseTransformPoint(newPos);
                local.y = 0f;
                newPos = sphereGenerator.transform.TransformPoint(local);
            }

            rp.SetEuclideanPosition(newPos);
        }
    }

    void DragOnSurface()
    {
        Ray ray = cam.ScreenPointToRay(Input.mousePosition);
        if (!Physics.Raycast(ray, out var hit)) return;

        var spherePoint = selectedChild.GetComponent<ReferencePointHandler>();
        if (spherePoint == null) return;

        Vector3 center = SphereCenter();
        float radius = SphereRadius();
        Vector3 direction = (hit.point - center).normalized;
        spherePoint.SetEuclideanPosition(center + direction * radius);
    }

    void DragRadial()
    {
        // Only change radius, keep theta/phi (direction) fixed
        Ray ray = cam.ScreenPointToRay(Input.mousePosition);
        if (!dragPlane.Raycast(ray, out float t)) return;

        Vector3 center = SphereCenter();
        float maxRadius = SphereRadius();

        // Project the mouse to the drag plane, then measure desired radius
        Vector3 target = ray.GetPoint(t) + grabOffsetWorld;
        float desiredR = Vector3.Distance(target, center);

        // Clamp inside sphere
        desiredR = Mathf.Clamp(desiredR, 0f, maxRadius);

        // Move only along the locked direction captured at mouse-down
        Vector3 newPos = center + fixedDirection * desiredR;

        var spherePoint = selectedChild.GetComponent<ReferencePointHandler>();
        if (spherePoint != null) spherePoint.SetEuclideanPosition(newPos);
    }
}
