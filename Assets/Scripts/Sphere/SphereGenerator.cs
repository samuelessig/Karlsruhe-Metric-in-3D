using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using System.Collections;
using System;

public class SphereGenerator : MonoBehaviour
{
    private ComputeBuffer _pointSphericalCoordsBuffer;
    private ComputeBuffer _pointColorsBuffer;

    static private float pi = (float) Math.PI;

    private float sphereRadius = 0.5f;

    private bool _showCoordGrid = false;
    private int _numOfPoints = 2;
    private float _refPointRadius = 2f;
    private float _radius;

    public EMetricType _metricType = EMetricType.Karlsruhe;
    private bool _useClosestDistance = true;
    private bool _showPoles = true;

    [SerializeField] public Shader voronoiShader;
    [SerializeField] private Material _sphereMaterial;

    [SerializeField] public GameObject mercatorProjection;
    [SerializeField] private CreateMercatorProjection _mercatorProjectionData;

    [SerializeField] public GameObject azimuthalProjectionNp;
    [SerializeField] private CreateAzimuthalProjection _azimuthalProjectionNpData;
    [SerializeField] public GameObject azimuthalProjectionSp;
    [SerializeField] private CreateAzimuthalProjection _azimuthalProjectionSpData;

    [SerializeField] private bool _showReferencePoints = true;

    [HideInInspector]
    public bool showReferencePoints
    {
        get => _showReferencePoints;
        set => SetShowReferencePoints(value);
    }

    [HideInInspector]
    public bool showCoordGrid
    {
        get { return _showCoordGrid; }
        set { SetShowCoordGrid(value); }
    }

    [HideInInspector]
    public int numOfPoints
    {
        get { return _numOfPoints; }
        set { SetPointCount(value); }
    }

    [HideInInspector]
    public float refPointRadius
    {
        get { return _refPointRadius; }
        set { SetPointRadius(value); }
    }

    [HideInInspector]
    public EMetricType metricType
    {
        get { return _metricType; }
        set { SetMetricType(value); }
    }

    [HideInInspector]
    public bool useClosestDistance
    {
        get { return _useClosestDistance; }
        set { SetUseClosestDistance(value); }
    }

    [HideInInspector]
    public bool showPoles
    {
        get { return _showPoles; }
        set { SetShowPoles(value); }
    }


    [HideInInspector]
    public float radius
    {
        get { return sphereRadius; }
    }

    void Start()
    {
        _sphereMaterial = GetComponent<Renderer>().material;
        _radius = transform.localScale.x * sphereRadius;

        GeneratePoints();

        List<ReferencePointHandler> spherePoints = GetReferencePointsHandlers();

        _mercatorProjectionData = mercatorProjection.GetComponent<CreateMercatorProjection>();
        _mercatorProjectionData.InitializeProjection(_refPointRadius, _radius, spherePoints);

        _azimuthalProjectionNpData = azimuthalProjectionNp.GetComponent<CreateAzimuthalProjection>();
        _azimuthalProjectionNpData.InitializeProjection(_refPointRadius, _radius, spherePoints);

        _azimuthalProjectionSpData = azimuthalProjectionSp.GetComponent<CreateAzimuthalProjection>();
        _azimuthalProjectionSpData.InitializeProjection(_refPointRadius, _radius, spherePoints);


        StartCoroutine(DelayedPushToGPU(0.2f));
    }

    public void SetPointCount(int countString)
    {
        _numOfPoints = countString;
        DeleteChildObjects();
        GeneratePoints();

        List<ReferencePointHandler> spherePoints = GetReferencePointsHandlers();
        _mercatorProjectionData.UpdatePoints(_refPointRadius, _radius, spherePoints);
        _azimuthalProjectionNpData.UpdatePoints(_refPointRadius, _radius, spherePoints);
        _azimuthalProjectionSpData.UpdatePoints(_refPointRadius, _radius, spherePoints);

        StartCoroutine(DelayedPushToGPU(0.2f));
    }

    public void ApplyShader(Shader newShader)
    {
        if (newShader == null)
        {
            Debug.LogWarning("[SphereGenerator] ApplyShader newShader is null");
            return;
        }

        voronoiShader = newShader;

        var renderer = GetComponent<Renderer>();
        if (renderer == null)
        {
            Debug.LogWarning("[SphereGenerator] ApplyShader renderer is null");
            return;
        }

        // New material with the new shader
        _sphereMaterial = new Material(voronoiShader);
        _sphereMaterial.SetInt("_PointCount", _numOfPoints);
        _sphereMaterial.SetFloat("_Radius", _radius);

        // Send to GPU
        ApplyReferencePointsVisibility();
        SetPointPositionShaderData();
        SetPointColorShaderData();
        SetShaderMetricProperties();

        renderer.material = _sphereMaterial;

        if (Is2DShader())
        {
            ConvertTo2DPoints();
            // Reset sphere rotation so that the 2D plane is visible
            transform.rotation = Quaternion.Euler(270f + Camera.main.transform.rotation.eulerAngles.z, -90f, 90f);
        }

    }

    public bool Is2DShader(Shader shader = null)
    {
        if (shader == null)
        {
            shader = voronoiShader;
        }
        return Utils.Is2DShader(shader);
    }

    public void SetPointRadius(float radius)
    {
        _refPointRadius = radius;
        ResizePoints();

        _mercatorProjectionData.SetPointRadius(radius);
        _azimuthalProjectionNpData.SetPointRadius(radius);
        _azimuthalProjectionSpData.SetPointRadius(radius);
    }

    public void SetUseClosestDistance(bool useClosest)
    {
        _useClosestDistance = useClosest;
        SetShaderMetricProperties();

        _mercatorProjectionData.SetUseClosestDistance(useClosest);
        _azimuthalProjectionNpData.SetUseClosestDistance(useClosest);
        _azimuthalProjectionSpData.SetUseClosestDistance(useClosest);
    }

    public void SetShowCoordGrid(bool showCoordGrid)
    {
        _showCoordGrid = showCoordGrid;
        SetShaderMetricProperties();

        _mercatorProjectionData.SetShowCoordGrid(showCoordGrid);
        _azimuthalProjectionNpData.SetShowCoordGrid(showCoordGrid);
        _azimuthalProjectionSpData.SetShowCoordGrid(showCoordGrid);
    }

    public void SetMetricType(EMetricType metricType)
    {
        _metricType = metricType;
        SetShaderMetricProperties();

        _mercatorProjectionData.SetMetricType(_metricType);
        _azimuthalProjectionNpData.SetMetricType(_metricType);
        _azimuthalProjectionSpData.SetMetricType(_metricType);
    }

    public void SetShowPoles(bool showPoles)
    {
        _showPoles = showPoles;
        SwitchPolesVisibility();
    }

    public void SetShowReferencePoints(bool show)
    {
        _showReferencePoints = show;
        ApplyReferencePointsVisibility();
    }

    public void UpdateGrowAnimation(float maxDistancePercent)
    {
        _sphereMaterial.SetFloat("_MaxDistancePercentage", maxDistancePercent);
    }

    public void UpdatePointPositionSpherical()
    {
        SetPointPositionShaderData();

        List<ReferencePointHandler> spherePoints = GetReferencePointsHandlers();
        _mercatorProjectionData.UpdatePointPositions();
        _azimuthalProjectionNpData.UpdatePointPositions();
        _azimuthalProjectionSpData.UpdatePointPositions();
    }

    public void UpdatePointColor()
    {
        SetPointColorShaderData();
    }

    private void GeneratePoints()
    {
        Renderer renderer = GetComponent<Renderer>();
        Destroy(renderer.material);

        for (int i = 0; i < _numOfPoints; i++)
        {
            GameObject pointObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            pointObject.transform.parent = transform;
            pointObject.tag = Constants.REFERENCE_POINT_TAG;
            pointObject.layer = LayerMask.NameToLayer("Points");
            ReferencePointHandler pointHandler = pointObject.AddComponent<ReferencePointHandler>();
            if (Is2DShader())
            {
                // Restrict to y=0 plane
                Vector2 onYPlane = new Vector2(0.0f, UnityEngine.Random.Range(-pi, pi));
                pointHandler.InitializePoint(UnityEngine.Random.Range(0f, _radius), _refPointRadius, onYPlane);
            }
            else
            {
                pointHandler.InitializePoint(UnityEngine.Random.Range(0f, _radius), _refPointRadius);
            }

            pointObject.name =
                $"Reference Point [{pointHandler.sphericalPosition.x}, {pointHandler.sphericalPosition.y}]";
        }

        if (_numOfPoints > 0)
        {
            _sphereMaterial = new Material(voronoiShader);
            _sphereMaterial.SetInt("_PointCount", _numOfPoints);
            _sphereMaterial.SetFloat("_Radius", _radius);
            ApplyReferencePointsVisibility();
            SetPointPositionShaderData();
            SetPointColorShaderData();
            SetShaderMetricProperties();
            renderer.material = _sphereMaterial;
        }
    }

    public List<Transform> GetReferencePoints()
    {
        List<Transform> points = new List<Transform>();
        for (int i = 0; i < transform.childCount; i++)
        {
            Transform child = transform.GetChild(i);
            if (child.CompareTag(Constants.REFERENCE_POINT_TAG))
            {
                points.Add(child);
            }
        }

        return points;
    }

    public List<ReferencePointHandler> GetReferencePointsHandlers()
    {
        List<ReferencePointHandler> spherePoints = GetReferencePoints()
            .Select(point => point.GetComponent<ReferencePointHandler>())
            .ToList();
        return spherePoints;
    }

    private void ResizePoints()
    {
        foreach (Transform point in GetReferencePoints())
        {
            point.GetComponent<ReferencePointHandler>().SetPointRadius(_refPointRadius);
        }
    }

    private void DeleteChildObjects()
    {
        foreach (Transform point in GetReferencePoints())
        {
            DestroyImmediate(point.gameObject);
        }
    }

    public void SetPointPositionShaderData()
    {
        List<ReferencePointHandler> pointHandlers = GetReferencePoints()
            .Select(point => point.GetComponent<ReferencePointHandler>())
            .ToList();

        int count = pointHandlers.Count;
        if (count == 0) return;

        // Both spherical and cartesian coords packed in a single buffer/array:
        // [0..count-1]          => spherical (r,theta,phi,0)
        // [count..2*count-1]    => cartesian (x,y,z,0)
        int packedCount = 2 * count;

        if (Utils.CanUseStructuredBuffers())
        {
            if (_pointSphericalCoordsBuffer == null || _pointSphericalCoordsBuffer.count != packedCount)
            {
                if (_pointSphericalCoordsBuffer != null)
                    _pointSphericalCoordsBuffer.Release();

                _pointSphericalCoordsBuffer = new ComputeBuffer(packedCount, sizeof(float) * 4);
            }

            var data = new Vector4[packedCount];

            // spherical block
            for (int i = 0; i < count; i++)
            {
                var p = pointHandlers[i];
                data[i] = new Vector4(p.radius, p.sphericalPosition.x, p.sphericalPosition.y, 0f);
            }

            // cartesian block
            for (int i = 0; i < count; i++)
            {
                var p = pointHandlers[i];
                Vector3 local = p.transform.localPosition;
                data[count + i] = new Vector4(local.x, local.y, local.z, 0f);
            }

            _pointSphericalCoordsBuffer.SetData(data);

            _sphereMaterial.SetBuffer("_PointSphericalCoords", _pointSphericalCoordsBuffer);
        }
        else
        {
            // WebGL: fixed-size arrays
            int n = Mathf.Min(count, Utils.MAX_POINTS);
            if (count > Utils.MAX_POINTS)
            {
                Debug.LogWarning(
                    $"Reducing point count in shader from {count} to MAX_POINTS={Utils.MAX_POINTS} for WebGL.");
            }

            var data = new Vector4[2 * n];

            // spherical block
            for (int i = 0; i < n; i++)
            {
                var p = pointHandlers[i];
                data[i] = new Vector4(p.radius, p.sphericalPosition.x, p.sphericalPosition.y, 0f);
            }

            // cartesian block
            for (int i = 0; i < n; i++)
            {
                var p = pointHandlers[i];
                Vector3 local = p.transform.localPosition;
                data[n + i] = new Vector4(local.x, local.y, local.z, 0f);
            }

            _sphereMaterial.SetVectorArray("_PointSphericalCoords", data);
        }
    }


    private void SetPointColorShaderData()
    {
        List<ReferencePointHandler> pointHandlers = GetReferencePoints()
            .Select((point) => point.GetComponent<ReferencePointHandler>())
            .ToList();
        int count = pointHandlers.Count;
        if (count == 0) return;

        if (Utils.CanUseStructuredBuffers())
        {
            if (_pointColorsBuffer == null || _pointColorsBuffer.count != count)
            {
                if (_pointColorsBuffer != null)
                    _pointColorsBuffer.Release();

                _pointColorsBuffer = new ComputeBuffer(count, sizeof(float) * 4);
            }

            _pointColorsBuffer
                .SetData(
                    pointHandlers
                        .Select((p) => p.color)
                        .ToList()
                );

            _sphereMaterial.SetBuffer("_Colors", _pointColorsBuffer);
        }
        else
        {
            // WebGL: use fixed-size arrays
            var data = pointHandlers
                .Take(Utils.MAX_POINTS)
                .Select(p => new Vector4(p.color.r, p.color.g, p.color.b, p.color.a))
                .ToArray();
            if (count > Utils.MAX_POINTS)
            {
                Debug.LogWarning(
                    $"Reducing point colors in shader from {count} to MAX_POINTS={Utils.MAX_POINTS} for WebGL.");
            }

            _sphereMaterial.SetVectorArray("_Colors", data);
        }
    }

    private void SetShaderMetricProperties()
    {
        _sphereMaterial.SetFloat("_ClosestDistance", _useClosestDistance ? 1f : 0f);
        _sphereMaterial.SetFloat("_MetricType", (float)_metricType);
        _sphereMaterial.SetFloat("_ShowGrid", _showCoordGrid ? 1f : 0f);

        // Add light positions
        Light mainLight = null;
        Light[] directionalLights = FindObjectsByType<Light>(
            FindObjectsSortMode.None
        ).Where(l => l.type == LightType.Directional).ToArray();

        if (directionalLights.Length > 0)
        {
            mainLight = directionalLights[0];
        }

        _sphereMaterial.SetVector("_LightDirWorld",
            mainLight ? mainLight.transform.forward : new Vector3(0.4f, 0.7f, 0.2f));

        UpdateGrowAnimation(0f);
    }

    private void SwitchPolesVisibility()
    {
        for (int i = 0; i < transform.childCount; i++)
        {
            Transform child = transform.GetChild(i);
            if (child.CompareTag(Constants.POLE_TAG))
            {
                child.gameObject.SetActive(_showPoles);
            }
        }
    }

    private IEnumerator DelayedPushToGPU(float delaySeconds = 1f)
    {
        yield return new WaitForEndOfFrame();

        PushToGPU();

        // Push to GPU
        SetPointPositionShaderData();
        SetPointColorShaderData();
    }

    // Recompute spherical from each point's current transform
    private void PushToGPU()
    {
        var handlers = GetReferencePoints();
        foreach (var go in handlers)
        {
            var h = go.GetComponent<ReferencePointHandler>();
            if (h == null) continue;
            h.SetEuclideanPosition(go.transform.position);
        }
    }

    // Recompute spherical from each point's current transform
    private void ConvertTo2DPoints(bool pushToGPU = true)
    {
        Debug.Log("[SphereGenerator] ConvertTo2DPoints: Moving points into 2D plane (y=0)");
        var handlers = GetReferencePoints();
        foreach (var go in handlers)
        {
            var h = go.GetComponent<ReferencePointHandler>();
            if (h == null) continue;

            // Put the point on a plane (y=0)
            h.SetSphericalPosition(new Vector2(0.0f, UnityEngine.Random.Range(-pi, pi)), 2 * h.radius * _radius);
        }

        if (pushToGPU)
        {
            StartCoroutine(DelayedPushToGPU(0.2f));
        }
    }

    // Import/Export methods
    public PointsSetState BuildPointsState()
    {
        var handlers = GetReferencePointsHandlers();
        var pts = handlers.Select(h => new SinglePointState
        {
            position = transform.InverseTransformPoint(h.transform.position),
            color = h.color
        }).ToArray();

        return new PointsSetState
        {
            name = "My Scene",
            points = pts
        };
    }

    public void ApplyPointsState(PointsSetState state, bool useStoredColors)
    {
        if (state == null || state.points == null)
        {
            Debug.LogWarning("[SphereGenerator] ApplyPointsState: invalid state");
            return;
        }

        _numOfPoints = state.points.Length;
        DeleteChildObjects();

        var renderer = GetComponent<Renderer>();
        if (_sphereMaterial == null)
        {
            _sphereMaterial = new Material(voronoiShader);
            renderer.material = _sphereMaterial;
        }

        foreach (var dto in state.points)
        {
            var pointObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            pointObject.transform.SetParent(transform, worldPositionStays: false);
            pointObject.tag = Constants.REFERENCE_POINT_TAG;
            pointObject.layer = LayerMask.NameToLayer("Points");

            var handler = pointObject.AddComponent<ReferencePointHandler>();
            handler.InitializePoint(dto.position.magnitude, _refPointRadius);

            Vector3 worldPos = transform.TransformPoint(dto.position);
            pointObject.transform.position = worldPos;
            handler.SetEuclideanPosition(worldPos);

            if (useStoredColors && dto.color != null)
                handler.SetColor(dto.color);
            else
                handler.SetRandomColor();

            pointObject.name = $"Reference Point [{handler.transform.position}]";
        }

        _sphereMaterial.SetInt("_PointCount", _numOfPoints);
        _sphereMaterial.SetFloat("_Radius", _radius);
        ApplyReferencePointsVisibility();
        SetPointPositionShaderData();
        SetPointColorShaderData();
        SetShaderMetricProperties();

        var spherePoints = GetReferencePointsHandlers();
        _mercatorProjectionData.UpdatePoints(_refPointRadius, _radius, spherePoints);
        _azimuthalProjectionNpData.UpdatePoints(_refPointRadius, _radius, spherePoints);
        _azimuthalProjectionSpData.UpdatePoints(_refPointRadius, _radius, spherePoints);
    }


    private void ApplyReferencePointsVisibility()
    {
        foreach (var t in GetReferencePoints())
        {
            var r = t.GetComponent<Renderer>();
            if (r) r.enabled = _showReferencePoints;

            var col = t.GetComponent<Collider>();
            if (col) col.enabled = _showReferencePoints;
        }
    }

    private void OnDestroy()
    {
        if (_pointSphericalCoordsBuffer != null)
        {
            _pointSphericalCoordsBuffer.Release();
            _pointSphericalCoordsBuffer = null;
        }

        if (_pointColorsBuffer != null)
        {
            _pointColorsBuffer.Release();
            _pointColorsBuffer = null;
        }
    }
}