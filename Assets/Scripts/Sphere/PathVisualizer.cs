using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Transform))]
public class PathVisualizer : MonoBehaviour
{
    [Header("Enable Path Visualization")]
    public bool pathEnabled = true;
    public bool showVoronoiOfProbes = false;

    [Header("Path Settings")]
    [Range(4, 128)]
    public int arcSegments = 32;

    [Range(1, 2)]
    [Tooltip("1 = show only the shortest path, 2 = show both paths")]
    public int numberOfPaths = 1;

    [Header("Tie Handling")]
    [Tooltip("If |d_origin - d_shell| <= tieEpsilon, treat as tie")]
    [Range(1e-7f, 1e-2f)]
    public float tieEpsilon = 0.005f;

    [Header("Rendering")]
    public Material lineMaterial;
    public float lineWidth = 0.02f;

    [Range(0f, 1f)]
    public float lineAlpha = 1.0f;

    [Header("Path colors")]
    public Color colorSinglePath = new Color(0.15f, 1.0f, 0.2f, 1f);
    public Color colorShortest = new Color(0.15f, 1.0f, 0.2f, 1f);
    public Color colorLongest = new Color(1.0f, 0.2f, 0.2f, 1f);

    [Header("References")]
    public SphereGenerator sphereGenerator;
    public ShaderParameters sliceControls;

    private LineRenderer _lineOrigin;
    private LineRenderer _lineShell;

    private Transform _sphereTransform;
    private Renderer _sphereRenderer;

    private const float EPS_R = 1e-5f;
    private const float EPS_ANG = 1e-5f;

    private void Awake()
    {
        if (sphereGenerator == null)
        {
            sphereGenerator = GetComponent<SphereGenerator>();
            if (sphereGenerator == null)
                sphereGenerator = GetComponentInParent<SphereGenerator>();
        }

        if (sphereGenerator != null)
        {
            _sphereTransform = sphereGenerator.transform;
            _sphereRenderer = sphereGenerator.GetComponent<Renderer>();
        }

        if (sliceControls == null && sphereGenerator != null)
        {
            sliceControls = sphereGenerator.GetComponent<ShaderParameters>();
        }

        _lineOrigin = CreateLine("KarlsruhePath_Origin");
        _lineShell = CreateLine("KarlsruhePath_Shell");
    }

    private LineRenderer CreateLine(string name)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(transform, false);

        var lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = true;
        lr.loop = false;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
        lr.positionCount = 0;

        lr.material = lineMaterial != null
            ? new Material(lineMaterial)
            : new Material(Shader.Find("Unlit/KarlsruhePath"));

        lr.startWidth = lineWidth;
        lr.endWidth = lineWidth;

        return lr;
    }

    private void ClearLines()
    {
        if (_lineOrigin != null) _lineOrigin.positionCount = 0;
        if (_lineShell != null) _lineShell.positionCount = 0;
    }

    private void Update()
    {
        if (_sphereRenderer == null || sphereGenerator == null)
            return;

        if (!pathEnabled)
        {
            if (sliceControls != null)
                sliceControls.SetKeepPointsVoronoi(true);
            ClearLines();
            return;
        }

        if (sliceControls != null)
            sliceControls.SetKeepPointsVoronoi(showVoronoiOfProbes);

        var refs = sphereGenerator.GetReferencePoints();
        if (refs == null || refs.Count < 2 || refs[0] == null || refs[1] == null)
        {
            ClearLines();
            return;
        }

        Vector3 X = refs[0].localPosition;
        Vector3 S = refs[1].localPosition;

        int mode = Mathf.Clamp(numberOfPaths, 1, 2);

        // mode 1: single shortest path
        if (mode == 1)
        {
            List<Vector3> path = BuildKarlsruhePath3D(X, S, arcSegments);
            if (path == null || path.Count == 0)
            {
                ClearLines();
                return;
            }

            SetLine(_lineOrigin, path, WithAlpha(colorSinglePath, lineAlpha));
            _lineShell.positionCount = 0;
            return;
        }

        // mode 2: both paths, origin vs shell
        float rX = X.magnitude;
        float rS = S.magnitude;

        Vector3 dirX = (rX > EPS_R) ? X / rX : Vector3.forward;
        Vector3 dirS = (rS > EPS_R) ? S / rS : Vector3.forward;

        float dot = Mathf.Clamp(Vector3.Dot(dirX, dirS), -1f, 1f);
        float ang = Mathf.Acos(dot);
        float rMin = Mathf.Min(rX, rS);

        float dOrigin = rX + rS;
        float dShell = Mathf.Abs(rX - rS) + rMin * ang;

        List<Vector3> originPath = BuildOriginPath(X, S);

        bool shellValid = (rMin >= EPS_R) && (ang >= EPS_ANG);
        List<Vector3> shellPath = shellValid
            ? BuildShellPath(X, S, dirX, dirS, rMin, arcSegments)
            : null;

        if (!shellValid || shellPath == null || shellPath.Count == 0)
        {
            SetLine(_lineOrigin, originPath, WithAlpha(colorShortest, lineAlpha));
            _lineShell.positionCount = 0;
            return;
        }

        bool tie = Mathf.Abs(dOrigin - dShell) <= tieEpsilon;

        bool originShorter = dOrigin < dShell;
        bool shellShorter = dShell < dOrigin;

        Color colOrigin = tie
            ? colorShortest
            : (originShorter ? colorShortest : colorLongest);

        Color colShell = tie
            ? colorShortest
            : (shellShorter ? colorShortest : colorLongest);

        colOrigin = WithAlpha(colOrigin, lineAlpha);
        colShell = WithAlpha(colShell, lineAlpha);

        // Clip duplicate radial segments from the longer path
        // otherwise they overlap and it depends on the viewpoint which one is visible
        List<Vector3> originDraw = originPath;
        List<Vector3> shellDraw = shellPath;

        if (!tie)
        {
            if (originShorter)
            {
                shellDraw = ClipDuplicateFromShell(X, S, dirX, dirS, rX, rS, rMin, shellPath);
            }
            else
            {
                originDraw = ClipDuplicateFromOrigin(X, S, dirX, dirS, rX, rS, rMin, originPath);
            }
        }
        else
        {
            originDraw = ClipDuplicateFromOrigin(X, S, dirX, dirS, rX, rS, rMin, originPath);
        }

        SetLine(_lineOrigin, originDraw, colOrigin);
        SetLine(_lineShell, shellDraw, colShell);
    }

    private static List<Vector3> ClipDuplicateFromShell(
        Vector3 X, Vector3 S,
        Vector3 dirX, Vector3 dirS,
        float rX, float rS, float rMin,
        List<Vector3> shellPath)
    {
        Vector3 Xshell = dirX * rMin;
        Vector3 Sshell = dirS * rMin;

        var p = new List<Vector3>(shellPath);

        if (rX > rMin + 1e-6f && (p[0] - X).sqrMagnitude < 1e-8f)
            p[0] = Xshell;

        int last = p.Count - 1;
        if (rS > rMin + 1e-6f && (p[last] - S).sqrMagnitude < 1e-8f)
            p[last] = Sshell;

        RemoveZeroLengthSteps(p);
        return (p.Count >= 2) ? p : null;
    }

    private static List<Vector3> ClipDuplicateFromOrigin(
        Vector3 X, Vector3 S,
        Vector3 dirX, Vector3 dirS,
        float rX, float rS, float rMin,
        List<Vector3> originPath)
    {
        Vector3 Xshell = dirX * rMin;
        Vector3 Sshell = dirS * rMin;

        var p = new List<Vector3>(originPath);

        if (rX > rMin + 1e-6f && (p[0] - X).sqrMagnitude < 1e-8f)
            p[0] = Xshell;

        int last = p.Count - 1;
        if (rS > rMin + 1e-6f && (p[last] - S).sqrMagnitude < 1e-8f)
            p[last] = Sshell;

        RemoveZeroLengthSteps(p);
        return (p.Count >= 2) ? p : null;
    }

    private static void RemoveZeroLengthSteps(List<Vector3> p)
    {
        for (int i = p.Count - 2; i >= 0; --i)
            if ((p[i] - p[i + 1]).sqrMagnitude < 1e-10f)
                p.RemoveAt(i + 1);
    }

    private static Color WithAlpha(Color c, float a)
    {
        c.a = a;
        return c;
    }

    private void SetLine(LineRenderer lr, List<Vector3> localPath, Color color)
    {
        if (lr == null || localPath == null || localPath.Count == 0)
        {
            if (lr != null) lr.positionCount = 0;
            return;
        }

        lr.positionCount = localPath.Count;
        lr.startWidth = lineWidth;
        lr.endWidth = lineWidth;

        if (lr.material != null)
            lr.material.SetColor("_Color", color);

        for (int i = 0; i < localPath.Count; i++)
            lr.SetPosition(i, _sphereTransform.TransformPoint(localPath[i]));
    }

    // Path construction
    private static List<Vector3> BuildOriginPath(Vector3 X, Vector3 S)
    {
        return new List<Vector3> { X, Vector3.zero, S };
    }

    private static List<Vector3> BuildShellPath(
        Vector3 X, Vector3 S,
        Vector3 dirX, Vector3 dirS,
        float rMin, int arcSegments)
    {
        List<Vector3> pts = new List<Vector3>();

        pts.Add(X);
        pts.Add(dirX * rMin);

        int segs = Mathf.Max(2, arcSegments);
        for (int i = 1; i < segs; i++)
        {
            float t = i / (float)segs;
            pts.Add(Vector3.Slerp(dirX, dirS, t) * rMin);
        }

        pts.Add(dirS * rMin);
        pts.Add(S);

        return pts;
    }

    private List<Vector3> BuildKarlsruhePath3D(Vector3 X, Vector3 S, int arcSegments)
    {
        List<Vector3> pts = new List<Vector3>();

        float rX = X.magnitude;
        float rS = S.magnitude;

        if (rX < EPS_R && rS < EPS_R)
        {
            pts.Add(X);
            return pts;
        }

        Vector3 dirX = (rX > EPS_R) ? X / rX : Vector3.forward;
        Vector3 dirS = (rS > EPS_R) ? S / rS : Vector3.forward;

        float dot = Mathf.Clamp(Vector3.Dot(dirX, dirS), -1f, 1f);
        float ang = Mathf.Acos(dot);
        float rMin = Mathf.Min(rX, rS);

        float dOrigin = rX + rS;
        float dShell = Mathf.Abs(rX - rS) + rMin * ang;

        pts.Add(X);

        if (rMin < EPS_R || ang < EPS_ANG)
        {
            pts.Add(Vector3.zero);
            pts.Add(S);
        }
        else if (dOrigin <= dShell)
        {
            pts.Add(Vector3.zero);
            pts.Add(S);
        }
        else
        {
            pts.Add(dirX * rMin);

            int segs = Mathf.Max(2, arcSegments);
            for (int i = 1; i < segs; i++)
            {
                float t = i / (float)segs;
                pts.Add(Vector3.Slerp(dirX, dirS, t) * rMin);
            }

            pts.Add(dirS * rMin);
            pts.Add(S);
        }

        return pts;
    }

    public void SetPathEnabled(bool enabled)
    {
        pathEnabled = enabled;
    }

    public void SetKeepPointsVoronoi(bool keepPointsVoronoi)
    {
        showVoronoiOfProbes = keepPointsVoronoi;
    }

}