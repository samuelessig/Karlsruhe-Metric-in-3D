using UnityEngine;
using UnityEngine.EventSystems;
using TMPro;

[RequireComponent(typeof(Renderer))]
[RequireComponent(typeof(Collider))]
public class ShaderParameters : MonoBehaviour
{
    [Header("Surface")]
    public bool showSurfaceVoronoi = false;
    [Range(0f, 1f)]
    public float surfaceVoronoiOpacity = 0.5f;
    public float surfaceBisectorWidthPx = 0.9f;

    [Header("Slice 1")]
    public Vector3 normal0OS = Vector3.up;
    [Range(-1f, 1f)] public float offset0 = 0f;
    [Range(0f, 1f)] public float opacity0 = 0.9f;

    [Header("Slice 2")]
    public Vector3 normal1OS = Vector3.right;
    [Range(-1f, 1f)] public float offset1 = 0f;
    [Range(0f, 1f)] public float opacity1 = 0.9f;

    [Header("Mouse controls (hold ALT)")]
    public float tiltSensitivity = 10.0f;
    public float slidePerScroll = 0.02f;
    public bool lockCursorWhileTilting = false;

    [Header("Slice settings")]
    public float bisectorWidthPx = 1.75f;

    // Active slice index: 0 or 1
    [Range(0, 1)] public int _activeSlice = 0;
    public bool useSlice1 = false;

    [Header("Bisector Surface Shader")]
    [Range(0, 3)]
    public int cutSphere = 0; // 0 = no cut, 1 = cut A, 2 = cut B, 3 = cut both

    [Header("Cell Boundary Visualization")]
    [Range(0, 100)]
    public int cellBoundaryIndex = 0;

    [Header("Iso-lines")]
    [Range(0, 2)]
    public int showIsoLines = 0; // 0 = off, 1 = iso-lines, 2 = unit-sphere
    [Range(0.001f, 0.2f)] public float isoLineStep = 0.1f;
    [Range(0.001f, 0.2f)] public float isoLineThickness = 0.04f;


    [Header("Voronoi path mode")]
    public bool keepPointsVoronoi = true; // 1 = keep normal Voronoi, 0 = exclude path points from voronoi

    static readonly int _ShowProbesVoronoiID = Shader.PropertyToID("_ShowProbesVoronoi");


    [Header("UI")] [SerializeField]
    private TMP_Text controlsHintText;

    [Header("References")]
    public SphereGenerator sphereGenerator;
    public PathVisualizer pathVisualizer;

    private float _radius = 0.5f;

    // Shader property names
    static readonly int _SlicePlane0ID = Shader.PropertyToID("_SlicePlane0");
    static readonly int _SliceOpacity0ID = Shader.PropertyToID("_SliceOpacity0");
    static readonly int _BisectorWidthPx0ID = Shader.PropertyToID("_BisectorWidthPx0");

    static readonly int _UseSlice1ID = Shader.PropertyToID("_UseSlice1");
    static readonly int _SlicePlane1ID = Shader.PropertyToID("_SlicePlane1");
    static readonly int _SliceOpacity1ID = Shader.PropertyToID("_SliceOpacity1");
    static readonly int _BisectorWidthPx1ID = Shader.PropertyToID("_BisectorWidthPx1");

    static readonly int _RadiusID = Shader.PropertyToID("_Radius");
    static readonly int _ShowSurfaceVoronoiID = Shader.PropertyToID("_ShowSurfaceVoronoi");
    static readonly int _SurfaceVoronoiOpacityID = Shader.PropertyToID("_SurfaceVoronoiOpacity");
    static readonly int _SurfaceBisectorWidthPxID = Shader.PropertyToID("_SurfaceBisectorWidthPx");
    static readonly int _CutSphereID = Shader.PropertyToID("_CutSphere");

    static readonly int _ShowIsoLinesID = Shader.PropertyToID("_ShowIsoLines");
    static readonly int _IsoLineStepID = Shader.PropertyToID("_IsoLineStep");
    static readonly int _IsoLineThicknessID = Shader.PropertyToID("_IsoLineThickness");

    static readonly int _CellBoundaryIndexID = Shader.PropertyToID("_CellBoundaryIndex");

    Renderer _renderer;
    MaterialPropertyBlock _mpb;
    bool _tilting;

    void Awake()
    {
        _renderer = GetComponent<Renderer>();
        _mpb = new MaterialPropertyBlock();

        if (sphereGenerator == null)
        {
            sphereGenerator = GetComponent<SphereGenerator>();
            if (sphereGenerator == null)
                sphereGenerator = GetComponentInParent<SphereGenerator>();
        }

        if (pathVisualizer == null)
        {
            pathVisualizer = GetComponent<PathVisualizer>();
            if (pathVisualizer == null)
                pathVisualizer = GetComponentInParent<PathVisualizer>();
        }

        if (sphereGenerator != null)
        {
            _radius = sphereGenerator.radius;
        }

        // Normalize inputs
        normal0OS = (normal0OS.sqrMagnitude > 0f ? normal0OS : Vector3.up).normalized;

        // Make slice 1 orthogonal at first if it's not already
        if (Vector3.Dot(normal0OS, normal1OS.normalized) > 0.999f || normal1OS.sqrMagnitude <= 1e-8f)
        {
            Vector3 helper = (Mathf.Abs(normal0OS.y) < 0.9f) ? Vector3.up : Vector3.right;
            normal1OS = Vector3.Cross(normal0OS, helper).normalized;
        }
        else
        {
            normal1OS = normal1OS.normalized;
        }

        useSlice1 = false;
        offset0 = Mathf.Clamp(offset0, -_radius, _radius);
        offset1 = Mathf.Clamp(offset1, -_radius, _radius);
        opacity0 = Mathf.Clamp01(opacity0);
        opacity1 = Mathf.Clamp01(opacity1);


        if (controlsHintText != null)
        {
            controlsHintText.text = @"<b>Mouse:</b>

Left click and drag - to move a single point

Shift + Left click and drag - to move all points

Tab - Switch between dragging point freely, dragging along sphere surface, and dragging along radial direction

Alt + Middle-click - select which slice is controlled

Alt + Left-drag - tilt slice

Alt + Scroll - slide slice along its normal

Alt + Shift + Left-click (on the sphere) - snap plane offset to the picked point

Alt + Shift + Middle-click - snap plane normal to the camera forward

<b>Keyboard:</b>

1 - select slice control

2 - select slice control

Q - turn slice #2 on/off

T - slice normal orthogonal to the other slice

W - decrease opacity of slice by 5%

E - increase opacity of slice by 5%

A - toggle surface voronoi on/off

S - decrease opacity of surface voronoi by 5%

D - increase opacity of surface voronoi by 5%

C - Cut the sphere at the bisector

I - enabled iso-lines

J - show previous cell boundary

K - show next cell boundary

P - cycle path mode

O - toggle voronoi of probes
";
        }
    }

    void OnEnable()
    {
        PushToShader();
    }

#if UNITY_EDITOR
    // Called when a fild is changed in the Inspector
    void OnValidate()
    {
        if (_mpb != null)
        {
            PushToShader();
        }
    }
#endif


    public void SetKeepPointsVoronoi(bool keep)
    {
        keepPointsVoronoi = keep;
        PushToShader();
    }

    void Update()
    {
        HandleMouse();
        HandleKeyboard();
    }

    void HandleMouse()
    {
        bool alt = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
        bool uiBlocked = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

        if (!alt || uiBlocked)
        {
            // End tilting
            if (_tilting && lockCursorWhileTilting)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }

            _tilting = false;
            return;
        }

        // Active slice references
        ref Vector3 nRef = ref (_activeSlice == 0 ? ref normal0OS : ref normal1OS);
        ref float wRef = ref (_activeSlice == 0 ? ref offset0 : ref offset1);

        // Toggle active slice: Alt + MMB
        if (Input.GetMouseButtonDown(2) && !Input.GetKey(KeyCode.LeftShift) && !Input.GetKey(KeyCode.RightShift))
        {
            _activeSlice = 1 - _activeSlice; // toggle 0,1
            PushToShader();
            return;
        }

        // Start tilt: Alt + LMB
        if (Input.GetMouseButtonDown(0))
        {
            _tilting = true;
            if (lockCursorWhileTilting)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }

        // Tilting (Alt + LMB drag)
        if (_tilting && Input.GetMouseButton(0))
        {
            float dx = Input.GetAxisRaw("Mouse X");
            float dy = Input.GetAxisRaw("Mouse Y");

            if (Mathf.Abs(dx) > 0f || Mathf.Abs(dy) > 0f)
            {
                // degrees for full-screen drag
                float fullDragYawDeg = 180f;
                float fullDragPitchDeg = 180f;

                // convert px to degrees based on screen size
                float angleYaw = dx / Mathf.Max(1, Screen.width) * fullDragYawDeg * tiltSensitivity;
                float anglePitch = -dy / Mathf.Max(1, Screen.height) * fullDragPitchDeg * tiltSensitivity;

                // rotate
                Quaternion qYaw = Quaternion.AngleAxis(angleYaw, Vector3.up);
                Quaternion qPitch = Quaternion.AngleAxis(anglePitch, Vector3.right);

                nRef = qYaw * qPitch * nRef;
                nRef.Normalize();
                PushToShader();
            }
        }

        // End tilt
        if (_tilting && !Input.GetMouseButton(0))
        {
            _tilting = false;
            if (lockCursorWhileTilting)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
        }

        // Slide (Alt + scroll)
        float scroll = Input.mouseScrollDelta.y;
        if (Mathf.Abs(scroll) > 0f)
        {
            wRef += scroll * slidePerScroll;
            wRef = Mathf.Clamp(wRef, -_radius, _radius);
            PushToShader();
        }

        // Snap the normal to camera forward (Alt + Shift + MMB)
        if ((Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) && Input.GetMouseButtonDown(2))
        {
            Camera cam = Camera.main;
            if (cam)
            {
                Vector3 fwdOS = transform.InverseTransformDirection(cam.transform.forward).normalized;
                if (fwdOS.sqrMagnitude > 0f)
                {
                    nRef = fwdOS;
                    PushToShader();
                }
            }
        }

        // Snap plane to picked point (Alt + Shift + LMB)
        if ((Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) && Input.GetMouseButtonDown(0))
        {
            Camera cam = Camera.main;
            Collider col = GetComponent<Collider>();
            if (cam && col && Physics.Raycast(cam.ScreenPointToRay(Input.mousePosition), out RaycastHit hit, 1000f))
            {
                if (hit.collider == col)
                {
                    Vector3 pOS = transform.InverseTransformPoint(hit.point);
                    wRef = Mathf.Clamp(Vector3.Dot(nRef, pOS), -_radius, _radius);
                    PushToShader();
                }
            }
        }
    }

    void HandleKeyboard()
    {
        float step = 0.05f * Time.deltaTime * 10f;

        // Continuous adjustments
        if (Input.GetKey(KeyCode.W))
        {
            if (_activeSlice == 0) opacity0 = Mathf.Clamp01(opacity0 - step);
            else opacity1 = Mathf.Clamp01(opacity1 - step);
            PushToShader();
        }

        if (Input.GetKey(KeyCode.E))
        {
            if (_activeSlice == 0) opacity0 = Mathf.Clamp01(opacity0 + step);
            else opacity1 = Mathf.Clamp01(opacity1 + step);
            PushToShader();
        }

        if (Input.GetKey(KeyCode.S))
        {
            surfaceVoronoiOpacity = Mathf.Clamp01(surfaceVoronoiOpacity - step);
            PushToShader();
        }

        if (Input.GetKey(KeyCode.D))
        {
            surfaceVoronoiOpacity = Mathf.Clamp01(surfaceVoronoiOpacity + step);
            PushToShader();
        }

        // Single press actions
        if (Input.GetKeyDown(KeyCode.Q))
        {
            useSlice1 = !useSlice1;
            PushToShader();
        }

        if (Input.GetKeyDown(KeyCode.A))
        {
            showSurfaceVoronoi = !showSurfaceVoronoi;
            PushToShader();
        }

        if (Input.GetKeyDown(KeyCode.Alpha1))
        {
            _activeSlice = 0;
        }

        if (Input.GetKeyDown(KeyCode.Alpha2))
        {
            _activeSlice = 1;
        }

        if (Input.GetKeyDown(KeyCode.T))
        {
            // Set normal of active slice to be orthogonal to the other slice
            if (_activeSlice == 0)
            {
                normal0OS = Vector3.Cross(normal1OS, Vector3.up);
                if (normal0OS.sqrMagnitude < 1e-6f)
                {
                    normal0OS = Vector3.Cross(normal1OS, Vector3.right);
                }

                normal0OS.Normalize();
            }
            else
            {
                normal1OS = Vector3.Cross(normal0OS, Vector3.up);
                if (normal1OS.sqrMagnitude < 1e-6f)
                {
                    normal1OS = Vector3.Cross(normal0OS, Vector3.right);
                }

                normal1OS.Normalize();
            }

            PushToShader();
        }

        if (Input.GetKeyDown(KeyCode.C))
        {
            cutSphere = (cutSphere + 1) % 4;
            PushToShader();
        }

        if (Input.GetKeyDown(KeyCode.I))
        {
            showIsoLines = (showIsoLines + 1) % 3;
            PushToShader();
        }

        if (Input.GetKeyDown(KeyCode.J))
        {
            int numPoints = sphereGenerator != null ? sphereGenerator.numOfPoints : Utils.MAX_POINTS;
            cellBoundaryIndex = (cellBoundaryIndex - 1 + numPoints) % numPoints;
            PushToShader();
        }

        if (Input.GetKeyDown(KeyCode.K))
        {
            int numPoints = sphereGenerator != null ? sphereGenerator.numOfPoints : Utils.MAX_POINTS;
            cellBoundaryIndex = (cellBoundaryIndex + 1) % numPoints;
            PushToShader();
        }

        if (Input.GetKeyDown(KeyCode.P))
        {
            if (pathVisualizer != null)
            {
                if (pathVisualizer.pathEnabled && pathVisualizer.numberOfPaths == 1)
                {
                    pathVisualizer.numberOfPaths = 2;
                }
                else if (pathVisualizer.pathEnabled && pathVisualizer.numberOfPaths == 2)
                {
                    pathVisualizer.pathEnabled = false;
                }
                else
                {
                    pathVisualizer.pathEnabled = true;
                    pathVisualizer.numberOfPaths = 1;
                }
            }
        }

        if (Input.GetKeyDown(KeyCode.O))
        {
            keepPointsVoronoi = !keepPointsVoronoi;
            if (pathVisualizer != null)
            {
                pathVisualizer.SetKeepPointsVoronoi(keepPointsVoronoi);
            }
        }
    }

    public void PushToShader()
    {
        // Clamp & normalize values
        normal0OS = SafeUnit(normal0OS, Vector3.up);
        normal1OS = SafeUnit(normal1OS, Vector3.right);
        offset0 = Mathf.Clamp(offset0, -_radius, _radius);
        offset1 = Mathf.Clamp(offset1, -_radius, _radius);
        opacity0 = Mathf.Clamp01(opacity0);
        opacity1 = Mathf.Clamp01(opacity1);

        _renderer.GetPropertyBlock(_mpb);

        // shared
        _mpb.SetFloat(_RadiusID, _radius);
        _mpb.SetFloat(_BisectorWidthPx0ID, bisectorWidthPx);
        _mpb.SetFloat(_BisectorWidthPx1ID, bisectorWidthPx);

        _mpb.SetFloat(_ShowSurfaceVoronoiID, showSurfaceVoronoi ? 1f : 0f);
        _mpb.SetFloat(_SurfaceVoronoiOpacityID, surfaceVoronoiOpacity);
        _mpb.SetFloat(_SurfaceBisectorWidthPxID, surfaceBisectorWidthPx);

        // slice 0
        _mpb.SetVector(_SlicePlane0ID, new Vector4(normal0OS.x, normal0OS.y, normal0OS.z, offset0));
        _mpb.SetFloat(_SliceOpacity0ID, opacity0);

        // slice 1
        _mpb.SetFloat(_UseSlice1ID, useSlice1 ? 1f : 0f);
        _mpb.SetVector(_SlicePlane1ID, new Vector4(normal1OS.x, normal1OS.y, normal1OS.z, offset1));
        _mpb.SetFloat(_SliceOpacity1ID, opacity1);

        // bisector surface shader
        _mpb.SetInt(_CutSphereID, cutSphere);

        // cell boundary index
        _mpb.SetInt(_CellBoundaryIndexID, cellBoundaryIndex);

        // iso-lines
        _mpb.SetFloat(_ShowIsoLinesID, showIsoLines);
        _mpb.SetFloat(_IsoLineStepID, isoLineStep);
        _mpb.SetFloat(_IsoLineThicknessID, isoLineThickness);

        // path mode
        _mpb.SetFloat(_ShowProbesVoronoiID, keepPointsVoronoi ? 1f : 0f);

        _renderer.SetPropertyBlock(_mpb);
    }

    static Vector3 SafeUnit(Vector3 v, Vector3 fallback)
    {
        if (v.sqrMagnitude < 1e-10f) return fallback.normalized;
        v.Normalize();
        return v;
    }
}