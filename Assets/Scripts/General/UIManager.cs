using System;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using System.Collections;

public class UIManager : MonoBehaviour
{
    [SerializeField] public Button quitButton;

    [Header("Sphere")]
    [SerializeField] public GameObject sphereObject;
    [SerializeField] public SphereGenerator _sphereGenerator;
    [SerializeField] public GrowVoronoi _growAnimator;
    [SerializeField] public RotateSphere _rotateAnimator;

    [Header("Display Settings")]
    [SerializeField] public Toggle showPolesInput;
    [SerializeField] public Toggle showCoordGridInput;
    [SerializeField] public Toggle rotateSphereInput;
    [SerializeField] public TMP_InputField rotationSpeedInput;

    [Header("Generator Settings")]
    [SerializeField] public TMP_InputField numOfPointsInput;
    [SerializeField] public TMP_InputField pointRadiusInput;

    [Header("Metric Settings")]
    [SerializeField] public TMP_Dropdown metricTypeInput;
    [SerializeField] public Toggle useClosestDistanceInput;
    [SerializeField] public Toggle growAnimationActiveInput;
    [SerializeField] public TMP_InputField growSpeedInput;

    // Import/Export UI:
    [Header("Import/Export (JSON)")]

    [Tooltip("On import use colors stored in JSON or assign new random colors")]
    [SerializeField] public Toggle useStoredColorsOnImportToggle;

    [Tooltip("Text label to show status messages")]
    [SerializeField] public TMP_Text statusLabel;

    [Tooltip("Default filename")]
    [SerializeField] public string defaultFileName = "points_state.json";

    [SerializeField] private GameObject importButton;
    [SerializeField] private GameObject exportButton;
    [SerializeField] public TMP_InputField stateJsonInput;



    private Coroutine _numPointsDebounceCoroutine;

    void Start()
    {
        if (_sphereGenerator == null)
        _sphereGenerator = sphereObject.GetComponent<SphereGenerator>();
        if (_growAnimator == null)
        _growAnimator = sphereObject.GetComponent<GrowVoronoi>();
        if (_rotateAnimator == null)
        _rotateAnimator = sphereObject.GetComponent<RotateSphere>();

        Initialize();
        AddListeners();

        //Debug.Log($"Save path: {Application.persistentDataPath}");

        #if !UNITY_EDITOR
            // hide the file export/import buttons when not in editor, as file picker is not implemented
            if (importButton) importButton.SetActive(false);
            if (exportButton) exportButton.SetActive(false);
        #endif

        // Hide quit button where it has no effect (WebGL,Unity Editor) or is discouraged (Android,iOS)
        #if UNITY_EDITOR || UNITY_WEBGL || UNITY_ANDROID || UNITY_IOS
            if (quitButton) quitButton.gameObject.SetActive(false);
        #endif
    }

    public void Quit()
    {
        Application.Quit();
    }

    private void Initialize()
    {
        showPolesInput.isOn = _sphereGenerator.showPoles;
        showCoordGridInput.isOn = _sphereGenerator.showCoordGrid;
        rotateSphereInput.isOn = _rotateAnimator.rotateSphere;
        rotationSpeedInput.text = _rotateAnimator.rotationSpeed.ToString();

        numOfPointsInput.text = _sphereGenerator.numOfPoints.ToString();
        pointRadiusInput.text = _sphereGenerator.refPointRadius.ToString();

        metricTypeInput.value = (int)_sphereGenerator.metricType;
        metricTypeInput.RefreshShownValue();
        useClosestDistanceInput.isOn = _sphereGenerator.useClosestDistance;
        growAnimationActiveInput.isOn = _growAnimator.growVoronoi;
        growSpeedInput.text = _growAnimator.growSpeed.ToString();

        if (useStoredColorsOnImportToggle) useStoredColorsOnImportToggle.isOn = true;
    }

    private void AddListeners()
    {
        showPolesInput.onValueChanged.AddListener(OnShowPolesChanged);
        showCoordGridInput.onValueChanged.AddListener(OnShowCoordGridChanged);
        rotateSphereInput.onValueChanged.AddListener(OnRotateSphereActiveChanged);
        rotationSpeedInput.onValueChanged.AddListener(OnRotationSpeedChanged);

        numOfPointsInput.onValueChanged.AddListener(OnNumberOfPointsChangedDelayed);

        pointRadiusInput.onValueChanged.AddListener(OnPointRadiusChanged);

        metricTypeInput.onValueChanged.AddListener(OnMetricTypeChanged);
        useClosestDistanceInput.onValueChanged.AddListener(OnUseClosestDistanceChanged);
        growAnimationActiveInput.onValueChanged.AddListener(OnGrowAnimationActiveChanged);
        growSpeedInput.onValueChanged.AddListener(OnGrowAnimationSpeedChanged);

        metricTypeInput.RefreshShownValue();
    }

    private void OnNumberOfPointsChangedDelayed(string value)
    {
        if (!numOfPointsInput.isFocused) {
            return; // ignore programmatic changes
        }

        // Wait before applying change to number of points to avoid excessive regeneration while user is typing
        if (_numPointsDebounceCoroutine != null) {
            StopCoroutine(_numPointsDebounceCoroutine);
        }

        float delay = GetNumPointsDebounceDelay(value);

        _numPointsDebounceCoroutine = StartCoroutine(ApplyNumPointsAfterDelay(value, delay));
    }

    private float GetNumPointsDebounceDelay(string value)
    {
        if (!int.TryParse(value, out int n)) {
            return 0.7f;
        }

        // Large number, wait 2 seconds, user may be typing
        if (n >= 100)
        {
            return 2f;
        }

        return 0.7f;
    }

    private IEnumerator ApplyNumPointsAfterDelay(string value, float delay)
    {
        yield return new WaitForSeconds(delay);
        _numPointsDebounceCoroutine = null;

        OnNumberOfPointsChanged(value);
    }

    private void OnDisable()
    {
        if (_numPointsDebounceCoroutine != null)
        {
            StopCoroutine(_numPointsDebounceCoroutine);
            _numPointsDebounceCoroutine = null;
        }
    }

    private void OnShowPolesChanged(bool value)
    {
        _sphereGenerator.showPoles = value;
        Debug.Log("Set show poles flag: " + value);
    }

    private void OnShowCoordGridChanged(bool value)
    {
        _sphereGenerator.showCoordGrid = value;
        Debug.Log("Show grid flag changed: " + value);
    }

    private void OnRotateSphereActiveChanged(bool value)
    {
        _rotateAnimator.rotateSphere = value;
        Debug.Log("Rotatation flag changed: " + value);
    }

    private void OnRotationSpeedChanged(string value)
    {
        if (float.TryParse(value, out float rotationSpeed))
        {
            Debug.Log("Rotation speed changed: " + rotationSpeed);
        }
        else
        {
            Debug.LogWarning("Invalid number input: " + value);
        }
        _rotateAnimator.rotationSpeed = rotationSpeed;
    }

    private void OnNumberOfPointsChanged(string value)
    {
        int numOfPoints = _sphereGenerator.numOfPoints;

        if (int.TryParse(value, out int parsed))
        {
            numOfPoints = parsed;
            Debug.Log("Number of points changed: " + numOfPoints);
        }
        else
        {
            Debug.LogWarning("Invalid number input: " + value);
        }

        if (!Utils.CanUseStructuredBuffers() && numOfPoints > Utils.MAX_POINTS)
        {
            numOfPoints = Utils.MAX_POINTS;
            numOfPointsInput.text = numOfPoints.ToString();
            Debug.LogWarning($"Structured Buffers not supported. Limiting number of points to {Utils.MAX_POINTS}.");
        }

        _sphereGenerator.SetPointCount(numOfPoints);
    }

    private void OnPointRadiusChanged(string value)
    {
        if (float.TryParse(value, out float refPointRadius))
        {
            Debug.Log("Point radius changed: " + refPointRadius);
        }
        else
        {
            Debug.LogWarning("Invalid number input: " + value);
        }
        _sphereGenerator.SetPointRadius(refPointRadius);
    }

    private void OnMetricTypeChanged(int value)
    {
        _sphereGenerator.metricType = (EMetricType)value;
        Debug.Log("Metric type changed: " + value);
    }

    private void OnUseClosestDistanceChanged(bool value)
    {
        _sphereGenerator.useClosestDistance = value;
        Debug.Log("Use closest distance metric changed: " + value);
    }

    private void OnGrowAnimationActiveChanged(bool value)
    {
        _growAnimator.ActivateGrowAnimation(value);
        Debug.Log("Use closest distance metric changed: " + value);
    }

    private void OnGrowAnimationSpeedChanged(string value)
    {
        if (float.TryParse(value, out float growSpeed))
        {
            Debug.Log("Grow speed changed: " + growSpeed);
        }
        else
        {
            Debug.LogWarning("Invalid number input: " + value);
        }
        _growAnimator.growSpeed = growSpeed;
    }

    string DefaultPath() => Path.Combine(Application.persistentDataPath, defaultFileName);

    public void OnExportToClipboard()
    {
        try
        {
            var json = BuildJSON();
            GUIUtility.systemCopyBuffer = json;
            if (stateJsonInput) stateJsonInput.text = json;
            WriteStatus($"Exported to clipboard ({json.Length} chars).");
            Debug.Log($"[UIManager] Exported JSON to clipboard ({json.Length} chars).");
        }
        catch (Exception e)
        {
            Debug.LogError($"[UIManager] ExportToClipboard failed: {e.Message}");
            WriteStatus($"Export failed: {e.Message}");
        }
    }

    public void OnExportToFile()
    {
        try
        {
            var json = BuildJSON();
            var path = DefaultPath();

            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, json);

            WriteStatus($"Saved: {path}");
            Debug.Log($"[UIManager] Saved state to: {path}");
        }
        catch (Exception e)
        {
            Debug.LogError($"[UIManager] ExportToFile failed: {e.Message}");
            WriteStatus($"Save failed: {e.Message}");
        }
    }

    #if UNITY_EDITOR
    // Editor-only file picker included in Unity Editor
    public void OnExportPickFile_Editor()
    {
        var json = BuildJSON();
        var path = UnityEditor.EditorUtility.SaveFilePanel("Export Points State",
            Application.persistentDataPath, defaultFileName, "json");
        if (!string.IsNullOrEmpty(path))
        {
            File.WriteAllText(path, json);
            WriteStatus($"Saved: {path}");
            Debug.Log($"[UIManager] Saved state to: {path}");
        }
    }

    public void OnImportPickFile_Editor()
    {
        var path = UnityEditor.EditorUtility.OpenFilePanel("Import Points State",
            Application.persistentDataPath, "json");
        if (!string.IsNullOrEmpty(path))
        {
            var json = File.ReadAllText(path);
            ApplyJSON(json, GetUseStoredColorsFlag());
        }
    }
    #endif

    public void OnExportPickFile()
    {
        #if UNITY_EDITOR
        OnExportPickFile_Editor();
        #else
        Debug.LogWarning("File picker is not implemented for this platform.");
        WriteStatus("File picker not available on this platform.");
        #endif
    }

    public void OnImportPickFile()
    {
        #if UNITY_EDITOR
        OnImportPickFile_Editor();
        #else
        Debug.LogWarning("File picker is not implemented for this platform.");
        WriteStatus("File picker not available on this platform.");
        #endif
    }

    public void OnImportFromClipboard()
    {
        var json = stateJsonInput && !string.IsNullOrWhiteSpace(stateJsonInput.text)
            ? stateJsonInput.text
            : GUIUtility.systemCopyBuffer;

        ApplyJSON(json, GetUseStoredColorsFlag());
    }

    public void OnImportFromFile()
    {
        var path = DefaultPath();
        if (!File.Exists(path))
        {
            WriteStatus($"File not found: {path}");
            Debug.LogWarning($"[UIManager] Import file not found: {path}");
            return;
        }
        var json = File.ReadAllText(path);
        ApplyJSON(json, GetUseStoredColorsFlag());
    }

    string BuildJSON()
    {
        var state = _sphereGenerator.BuildPointsState();

        if (string.IsNullOrEmpty(state.name))
            state.name = "Snapshot " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

        var json = JsonUtility.ToJson(state, true);
        if (stateJsonInput) stateJsonInput.text = json;
        return json;
    }

    public void ApplyJSON(string json, bool useStoredColors)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            Debug.LogWarning("[UIManager] Empty JSON on import.");
            WriteStatus("Import failed: empty JSON.");
            return;
        }

        PointsSetState state;
        try
        {
            state = JsonUtility.FromJson<PointsSetState>(json);
        }
        catch (Exception e)
        {
            Debug.LogError($"[UIManager] JSON parse error: {e.Message}");
            WriteStatus($"Import failed: {e.Message}");
            return;
        }

        if (state == null || state.points == null)
        {
            Debug.LogError("[UIManager] Invalid state JSON.");
            WriteStatus("Import failed: invalid JSON.");
            return;
        }

        if (!ValidateState(state, out string reason))
        {
            Debug.LogWarning($"[UIManager] State validation: {reason}");
        }

        numOfPointsInput.text = state.points.Length.ToString();

        _sphereGenerator.ApplyPointsState(state, useStoredColors);

        WriteStatus($"Imported {state.points.Length} points"
                    + (useStoredColors ? " (kept colors)" : " (random colors)"));
        Debug.Log($"[UIManager] Imported {state.points.Length} points. useStoredColors={useStoredColors}");
    }

    public bool GetUseStoredColorsFlag()
    {
        return useStoredColorsOnImportToggle ? useStoredColorsOnImportToggle.isOn : true;
    }

    bool ValidateState(PointsSetState st, out string reason)
    {
        if (st == null || st.points == null)
        {
            reason = "State or points array is null";
            return false;
        }

        for (int i = 0; i < st.points.Length; i++)
        {
            var p = st.points[i];

            if (float.IsNaN(p.position.x) || float.IsNaN(p.position.y) || float.IsNaN(p.position.z))
            {
                reason = $"point {i}: NaN position value";
                return false;
            }

            if (p.color.a < 0f || p.color.a > 1f)
            {
                Debug.LogWarning($"[UIManager] point {i}: invalid alpha {p.color.a}");
            }
        }

        reason = "ok";
        return true;
    }

    void WriteStatus(string msg)
    {
        if (statusLabel) statusLabel.text = msg;
    }
}
