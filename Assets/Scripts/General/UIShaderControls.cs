using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class UIShaderControls : MonoBehaviour
{
    public ShaderParameters shaderParams;
    public RectTransform controlsPanelRoot;

    [Header("UI references")] 
    public Toggle tglShowSurfaceVoronoi;
    public Slider sldSurfaceOpacity;

    public Button btnSlice1, btnSlice2;
    public Toggle tglEnableSlice2;
    public Slider sldActiveSliceOpacity;

    public Slider sldOffsetSlice1; // offset0
    public Slider sldOffsetSlice2; // offset1

    public Slider sldTiltActiveSlice;
    public Button btnMakeOrthogonal;

    public Button btnCycleIsoLines;
    public Button btnCycleCutSphere;

    public TMP_InputField inpCellBoundaryIndex;
    public Button btnCellPrev, btnCellNext;

    public Toggle tglShowProbesVoronoi;
    public Button btnCyclePathMode;

    [Header("Point dragging UI")] 
    public PointDragger pointDragger;
    public Button btnCycleDragMode;
    public Toggle tglStickyGroupDrag;

    [Header("Slice button highlighting")] 
    public Color sliceActiveColor = new Color(0.30f, 0.60f, 1.00f, 1f);
    public Color sliceInactiveColor = new Color(1f, 1f, 1f, 0.60f);


    private Vector3 baseNormal0OS;
    private Vector3 baseNormal1OS;

    bool _updatingUI;

    void Awake()
    {
        if (shaderParams == null)
        {
            shaderParams = FindFirstObjectByType<ShaderParameters>();
        }

        if (controlsPanelRoot == null)
        {
            var go = GameObject.Find("ControlsPanel");
            if (go != null)
            {
                controlsPanelRoot = go.GetComponent<RectTransform>();
            }
        }

        PopulateUIFields();
        GetSliceNormals();
        WireEvents();
        HighlightActiveSliceButton();
    }

    void Start()
    {
        SyncUI();
    }

    void Update()
    {
        // Update UI when keyboard/mouse modifies ShaderParameters
        SyncUI();
    }

    void PopulateUIFields()
    {
        if (controlsPanelRoot == null) return;

        T Find<T>(string name) where T : Component
        {
            var t = controlsPanelRoot.Find(name);
            return t ? t.GetComponent<T>() : null;
        }

        tglShowSurfaceVoronoi ??= Find<Toggle>("TGL_ShowSurfaceVoronoi");
        sldSurfaceOpacity ??= Find<Slider>("SLD_SurfaceOpacity");

        btnSlice1 ??= Find<Button>("BTN_Slice1");
        btnSlice2 ??= Find<Button>("BTN_Slice2");
        tglEnableSlice2 ??= Find<Toggle>("TGL_EnableSlice2");

        sldActiveSliceOpacity ??= Find<Slider>("SLD_ActiveSliceOpacity");

        sldOffsetSlice1 ??= Find<Slider>("SLD_OffsetSlice1");
        sldOffsetSlice2 ??= Find<Slider>("SLD_OffsetSlice2");

        sldTiltActiveSlice ??= Find<Slider>("SLD_TiltActiveSlice");
        btnMakeOrthogonal ??= Find<Button>("BTN_MakeOrthogonal");

        btnCycleIsoLines ??= Find<Button>("BTN_CycleIsoLines");
        btnCycleCutSphere ??= Find<Button>("BTN_CycleCutSphere");

        inpCellBoundaryIndex ??= Find<TMP_InputField>("INP_CellBoundaryIndex");
        btnCellPrev ??= Find<Button>("BTN_CellPrev");
        btnCellNext ??= Find<Button>("BTN_CellNext");

        tglShowProbesVoronoi ??= Find<Toggle>("TGL_ShowProbesVoronoi");
        btnCyclePathMode ??= Find<Button>("BTN_CyclePathMode");

        if (pointDragger == null) pointDragger = FindFirstObjectByType<PointDragger>();
        btnCycleDragMode ??= Find<Button>("BTN_CycleDragMode");
        tglStickyGroupDrag ??= Find<Toggle>("TGL_StickyGroupDrag");
    }

    void WireEvents()
    {
        if (shaderParams == null) return;

        if (tglShowSurfaceVoronoi)
            tglShowSurfaceVoronoi.onValueChanged.AddListener(v =>
            {
                shaderParams.showSurfaceVoronoi = v;
                shaderParams.PushToShader();
            });

        if (sldSurfaceOpacity)
            sldSurfaceOpacity.onValueChanged.AddListener(v =>
            {
                shaderParams.surfaceVoronoiOpacity = v;
                shaderParams.PushToShader();
            });

        if (btnSlice1)
            btnSlice1.onClick.AddListener(() =>
            {
                shaderParams._activeSlice = 0;
                GetSliceNormals();
                ResetTiltSlider();
                HighlightActiveSliceButton();
            });

        if (btnSlice2)
            btnSlice2.onClick.AddListener(() =>
            {
                shaderParams._activeSlice = 1;

                if (shaderParams.useSlice1 == false)
                {
                    shaderParams.useSlice1 = true;
                    if (tglEnableSlice2) tglEnableSlice2.isOn = true;
                }

                GetSliceNormals();
                ResetTiltSlider();
                HighlightActiveSliceButton();
            });

        if (tglEnableSlice2)
            tglEnableSlice2.onValueChanged.AddListener(v =>
            {
                shaderParams.useSlice1 = v;

                if (!v && shaderParams._activeSlice == 1)
                {
                    shaderParams._activeSlice = 0;
                    HighlightActiveSliceButton();
                }

                shaderParams.PushToShader();
            });

        if (sldActiveSliceOpacity)
            sldActiveSliceOpacity.onValueChanged.AddListener(v =>
            {
                if (shaderParams._activeSlice == 0) shaderParams.opacity0 = v;
                else shaderParams.opacity1 = v;
                shaderParams.PushToShader();
            });

        if (sldOffsetSlice1)
            sldOffsetSlice1.onValueChanged.AddListener(v =>
            {
                shaderParams.offset0 = v;
                shaderParams.PushToShader();
            });

        if (sldOffsetSlice2)
            sldOffsetSlice2.onValueChanged.AddListener(v =>
            {
                shaderParams.offset1 = v;
                shaderParams.PushToShader();
            });

        // Tilt: use an axis orthogonal to the active slice normal
        if (sldTiltActiveSlice)
            sldTiltActiveSlice.onValueChanged.AddListener(angleDeg =>
            {
                Vector3 baseN = (shaderParams._activeSlice == 0) ? baseNormal0OS : baseNormal1OS;

                Vector3 axis = Vector3.Cross(baseN, Vector3.up);
                if (axis.sqrMagnitude < 1e-6f)
                    axis = Vector3.Cross(baseN, Vector3.right);
                axis.Normalize();

                Vector3 newN = Quaternion.AngleAxis(angleDeg, axis) * baseN;

                if (shaderParams._activeSlice == 0)
                    shaderParams.normal0OS = newN;
                else
                    shaderParams.normal1OS = newN;

                shaderParams.PushToShader();
            });

        if (btnMakeOrthogonal)
            btnMakeOrthogonal.onClick.AddListener(() =>
            {
                if (shaderParams._activeSlice == 0)
                {
                    shaderParams.normal0OS = Vector3.Cross(shaderParams.normal1OS, Vector3.up);
                    if (shaderParams.normal0OS.sqrMagnitude < 1e-6f)
                        shaderParams.normal0OS = Vector3.Cross(shaderParams.normal1OS, Vector3.right);
                    shaderParams.normal0OS.Normalize();
                }
                else
                {
                    shaderParams.normal1OS = Vector3.Cross(shaderParams.normal0OS, Vector3.up);
                    if (shaderParams.normal1OS.sqrMagnitude < 1e-6f)
                        shaderParams.normal1OS = Vector3.Cross(shaderParams.normal0OS, Vector3.right);
                    shaderParams.normal1OS.Normalize();
                }

                GetSliceNormals();
                ResetTiltSlider();
                shaderParams.PushToShader();
            });

        if (btnCycleIsoLines)
            btnCycleIsoLines.onClick.AddListener(() =>
            {
                shaderParams.showIsoLines = (shaderParams.showIsoLines + 1) % 3;
                shaderParams.PushToShader();
                UpdateCycleButtonLabel(btnCycleIsoLines, IsoLinesLabel(shaderParams.showIsoLines));
            });

        if (btnCycleCutSphere)
            btnCycleCutSphere.onClick.AddListener(() =>
            {
                shaderParams.cutSphere = (shaderParams.cutSphere + 1) % 4;
                shaderParams.PushToShader();
                UpdateCycleButtonLabel(btnCycleCutSphere, CutSphereLabel(shaderParams.cutSphere));
            });

        if (inpCellBoundaryIndex)
        {
            inpCellBoundaryIndex.onEndEdit.AddListener(s =>
            {
                if (int.TryParse(s, out int v))
                {
                    shaderParams.cellBoundaryIndex = Mathf.Max(0, v);
                    shaderParams.PushToShader();
                }

                inpCellBoundaryIndex.text = shaderParams.cellBoundaryIndex.ToString();
            });
        }

        if (btnCellPrev)
            btnCellPrev.onClick.AddListener(() =>
            {
                shaderParams.cellBoundaryIndex = Mathf.Max(0, shaderParams.cellBoundaryIndex - 1);
                shaderParams.PushToShader();
                if (inpCellBoundaryIndex) inpCellBoundaryIndex.text = shaderParams.cellBoundaryIndex.ToString();
            });

        if (btnCellNext)
            btnCellNext.onClick.AddListener(() =>
            {
                shaderParams.cellBoundaryIndex += 1;
                shaderParams.PushToShader();
                if (inpCellBoundaryIndex) inpCellBoundaryIndex.text = shaderParams.cellBoundaryIndex.ToString();
            });

        if (tglShowProbesVoronoi)
            tglShowProbesVoronoi.onValueChanged.AddListener(v =>
            {
                shaderParams.keepPointsVoronoi = v;
                if (shaderParams.pathVisualizer != null)
                    shaderParams.pathVisualizer.SetKeepPointsVoronoi(v);
                shaderParams.PushToShader();
            });

        if (btnCyclePathMode)
            btnCyclePathMode.onClick.AddListener(() =>
            {
                if (shaderParams.pathVisualizer != null)
                {
                    if (shaderParams.pathVisualizer.pathEnabled && shaderParams.pathVisualizer.numberOfPaths == 1)
                    {
                        shaderParams.pathVisualizer.numberOfPaths = 2;
                        UpdateCycleButtonLabel(btnCyclePathMode, "Path Mode: Shortest [P]");
                    }
                    else if (shaderParams.pathVisualizer.pathEnabled && shaderParams.pathVisualizer.numberOfPaths == 2)
                    {
                        shaderParams.pathVisualizer.pathEnabled = false;
                        UpdateCycleButtonLabel(btnCyclePathMode, "Path Mode: Both [P]");
                    }
                    else
                    {
                        shaderParams.pathVisualizer.pathEnabled = true;
                        shaderParams.pathVisualizer.numberOfPaths = 1;
                        UpdateCycleButtonLabel(btnCyclePathMode, "Path Mode: Off [P]");
                    }
                }
            });

        if (pointDragger != null)
        {
            if (tglStickyGroupDrag)
            {
                tglStickyGroupDrag.onValueChanged.AddListener(v =>
                {
                    pointDragger.EnableDragAll(v);
                    if (v && pointDragger.GetMode() != DragMode.Volume)
                    {
                        pointDragger.SetMode(DragMode.Volume);
                        UpdateCycleButtonLabel(btnCycleDragMode,
                            $"Drag mode: {pointDragger.GetMode().ToString()} [Tab]");
                    }
                });
            }

            if (btnCycleDragMode)
            {
                btnCycleDragMode.onClick.AddListener(() =>
                {
                    pointDragger.CycleMode();
                    UpdateCycleButtonLabel(btnCycleDragMode,
                        $"Drag mode: {pointDragger.GetMode().ToString()} [Tab]");
                });
            }
        }
    }

    void SyncUI()
    {
        if (_updatingUI || shaderParams == null) return;
        _updatingUI = true;

        if (tglShowSurfaceVoronoi) tglShowSurfaceVoronoi.isOn = shaderParams.showSurfaceVoronoi;
        if (sldSurfaceOpacity) sldSurfaceOpacity.value = shaderParams.surfaceVoronoiOpacity;

        if (tglEnableSlice2) tglEnableSlice2.isOn = shaderParams.useSlice1;

        if (sldActiveSliceOpacity)
        {
            float opacity = (shaderParams._activeSlice == 0) ? shaderParams.opacity0 : shaderParams.opacity1;
            sldActiveSliceOpacity.value = opacity;
        }

        // keep offsets synced
        if (sldOffsetSlice1) sldOffsetSlice1.value = shaderParams.offset0;
        if (sldOffsetSlice2) sldOffsetSlice2.value = shaderParams.offset1;

        if (btnCycleIsoLines) UpdateCycleButtonLabel(btnCycleIsoLines, IsoLinesLabel(shaderParams.showIsoLines));
        if (btnCycleCutSphere) UpdateCycleButtonLabel(btnCycleCutSphere, CutSphereLabel(shaderParams.cutSphere));

        if (inpCellBoundaryIndex && inpCellBoundaryIndex.text != shaderParams.cellBoundaryIndex.ToString())
            inpCellBoundaryIndex.text = shaderParams.cellBoundaryIndex.ToString();

        if (tglShowProbesVoronoi) tglShowProbesVoronoi.isOn = shaderParams.keepPointsVoronoi;

        // keep point-drag UI synced
        if (pointDragger != null)
        {
            if (tglStickyGroupDrag) tglStickyGroupDrag.isOn = pointDragger.IsDragAll();
            if (btnCycleDragMode)
                UpdateCycleButtonLabel(btnCycleDragMode,
                    $"Drag mode: {pointDragger.GetMode().ToString()} [Tab]");
        }

        if (btnCyclePathMode)
        {
            if (shaderParams.pathVisualizer.pathEnabled && shaderParams.pathVisualizer.numberOfPaths == 1)
            {
                UpdateCycleButtonLabel(btnCyclePathMode, "Path Mode: Shortest [P]");
            }
            else if (shaderParams.pathVisualizer.pathEnabled && shaderParams.pathVisualizer.numberOfPaths == 2)
            {
                UpdateCycleButtonLabel(btnCyclePathMode, "Path Mode: Both [P]");
            }
            else
            {
                UpdateCycleButtonLabel(btnCyclePathMode, "Path Mode: Off [P]");
            }
        }

        HighlightActiveSliceButton();

        _updatingUI = false;
    }

    void GetSliceNormals()
    {
        if (shaderParams == null) return;
        baseNormal0OS = shaderParams.normal0OS;
        baseNormal1OS = shaderParams.normal1OS;
    }

    void ResetTiltSlider()
    {
        if (sldTiltActiveSlice) sldTiltActiveSlice.value = 0f;
    }

    void HighlightActiveSliceButton()
    {
        if (btnSlice1 == null || btnSlice2 == null || shaderParams == null) return;

        bool slice0Active = shaderParams._activeSlice == 0;

        ToggleButtonHighlight(btnSlice1, slice0Active);
        ToggleButtonHighlight(btnSlice2, !slice0Active);
    }

    void ToggleButtonHighlight(Button btn, bool active)
    {
        var img = btn.GetComponent<Image>();
        if (img != null)
            img.color = active ? sliceActiveColor : sliceInactiveColor;

        var tmp = btn.GetComponentInChildren<TextMeshProUGUI>();
        if (tmp != null)
            tmp.alpha = active ? 1.0f : 0.6f;
    }

    static void UpdateCycleButtonLabel(Button btn, string label)
    {
        if (btn == null) return;
        var tmp = btn.GetComponentInChildren<TextMeshProUGUI>();
        if (tmp != null) tmp.text = label;
    }

    static string IsoLinesLabel(int v) => v switch
    {
        0 => "Show iso-lines: Off",
        1 => "Show iso-lines: Iso-lines",
        2 => "Show iso-lines: Unit-sphere",
        _ => "Show iso-lines"
    };

    static string CutSphereLabel(int v) => v switch
    {
        0 => "Cut sphere: None",
        1 => "Cut sphere: Cut A",
        2 => "Cut sphere: Cut B",
        3 => "Cut sphere: Cut both",
        _ => "Cut sphere"
    };
}