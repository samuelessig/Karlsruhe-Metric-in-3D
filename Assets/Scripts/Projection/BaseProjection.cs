using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public abstract class BaseProjection<RefPoint> : MonoBehaviour
    where RefPoint : BaseReferencePoint
{
    protected ComputeBuffer _pointSphericalCoordsBuffer;
    protected ComputeBuffer _pointColorsBuffer;


    [Header("Projection Settings")] [SerializeField]
    public GameObject sphere;

    [SerializeField] protected Material _projectionMaterial;
    [SerializeField] public Shader voronoiShader;


    protected EMetricType _metricType = EMetricType.Karlsruhe;
    protected bool _useClosestDistance = true;
    protected bool _showCoordGrid = true;

    void Start()
    {
        _projectionMaterial = GetComponent<Renderer>().material;
    }

    public void SetPointRadius(float radius)
    {
        ResizePoints(radius);
    }

    public void SetUseClosestDistance(bool useClosest)
    {
        _useClosestDistance = useClosest;
        SetShaderMetricProperties();
    }

    public void SetShowCoordGrid(bool showCoordGrid)
    {
        _showCoordGrid = showCoordGrid;
        SetShaderMetricProperties();
    }

    public void SetMetricType(EMetricType metricType)
    {
        _metricType = metricType;
        SetShaderMetricProperties();
    }

    public void UpdateGrowAnimation(float maxDistancePercent)
    {
        _projectionMaterial.SetFloat("_MaxDistancePercentage", maxDistancePercent);
    }


    public void UpdatePointPositions()
    {
        foreach (var point in GetReferencePointsHandlers())
        {
            point.UpdatePosition();
        }

        SetPointPositionShaderData();
    }

    protected void DeleteChildObjects()
    {
        foreach (var point in GetReferencePoints())
        {
            DestroyImmediate(point.gameObject);
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


    public void ResizePoints(float refPointRadius)
    {
        foreach (Transform point in GetReferencePoints())
        {
            point.GetComponent<RefPoint>().SetPointRadius(refPointRadius);
        }
    }

    public List<RefPoint> GetReferencePointsHandlers()
    {
        List<RefPoint> spherePoints = GetReferencePoints()
            .Select(point => point.GetComponent<RefPoint>())
            .ToList();
        return spherePoints;
    }

    public void SetPointPositionShaderData()
    {
        List<RefPoint> pointHandlers = GetReferencePoints()
            .Select((point) => point.GetComponent<RefPoint>())
            .ToList();
        int count = pointHandlers.Count;
        if (count == 0) return;

        if (Utils.CanUseStructuredBuffers())
        {
            //Debug.Log("Using StructuredBuffers for shader data");

            if (_pointSphericalCoordsBuffer == null || _pointSphericalCoordsBuffer.count != count)
            {
                if (_pointSphericalCoordsBuffer != null)
                    _pointSphericalCoordsBuffer.Release();

                _pointSphericalCoordsBuffer = new ComputeBuffer(count, sizeof(float) * 4);
            }

            _pointSphericalCoordsBuffer
                .SetData(
                    pointHandlers
                        .Select((p) => p.GetSphericalPosition4())
                        .ToList()
                );

            _projectionMaterial.SetBuffer("_PointSphericalCoords", _pointSphericalCoordsBuffer);
        }
        else
        {
            //Debug.Log("Using vector array for shader data (WebGL compatible)");
            // WebGL: use fixed-size arrays
            var data = pointHandlers
                .Take(Utils.MAX_POINTS)
                .Select(p => p.GetSphericalPosition4())
                .ToArray();
            if (count > Utils.MAX_POINTS)
            {
                Debug.LogWarning(
                    $"Reducing point count in shader from {count} to MAX_POINTS={Utils.MAX_POINTS} for WebGL.");
            }

            _projectionMaterial.SetVectorArray("_PointSphericalCoords", data);
        }
    }


    protected void SetPointColorShaderData()
    {
        List<RefPoint> pointHandlers = GetReferencePoints()
            .Select((point) => point.GetComponent<RefPoint>())
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
                        .Select((p) => p.GetColor())
                        .ToList()
                );

            _projectionMaterial.SetBuffer("_Colors", _pointColorsBuffer);
        }
        else
        {
            // WebGL: use fixed-size arrays
            var data = pointHandlers
                .Take(Utils.MAX_POINTS)
                .Select(p =>
                {
                    var color = p.GetColor();
                    return new Vector4(color.r, color.g, color.b, color.a);
                })
                .ToArray();
            if (count > Utils.MAX_POINTS)
            {
                Debug.LogWarning(
                    $"Reducing point colors in shader from {count} to MAX_POINTS={Utils.MAX_POINTS} for WebGL.");
            }

            _projectionMaterial.SetVectorArray("_Colors", data);
        }
    }

    protected void GeneratePoints(float refPointRadius, float radius, List<ReferencePointHandler> spherePoints)
    {
        Renderer renderer = GetComponent<Renderer>();
        Destroy(renderer.material);

        Vector2 projectionScale = new Vector2(transform.lossyScale.x, transform.lossyScale.z);

        var sphereGenerator = sphere.GetComponent<SphereGenerator>();

        foreach (var spherePoint in spherePoints)
        {
            InitProjectionPoint(spherePoint, transform, projectionScale, sphereGenerator);
        }

        if (spherePoints.Count > 0)
        {
            _projectionMaterial = new Material(voronoiShader);
            _projectionMaterial.SetInt("_PointCount", spherePoints.Count);
            _projectionMaterial.SetFloat("_Radius", radius);
            _projectionMaterial.SetVector("_Scale", projectionScale);
            SetPointPositionShaderData();
            SetPointColorShaderData();
            SetShaderMetricProperties();
            MoreShaderSetup();
            renderer.material = _projectionMaterial;
        }
    }


    public void InitializeProjection(float refPointRadius, float radius, List<ReferencePointHandler> spherePoints)
    {
        GeneratePoints(refPointRadius, radius, spherePoints);
    }

    public void UpdatePoints(float refPointRadius, float radius, List<ReferencePointHandler> spherePoints)
    {
        DeleteChildObjects();
        GeneratePoints(refPointRadius, radius, spherePoints);
    }


    virtual protected void SetShaderMetricProperties()
    {
        _projectionMaterial.SetFloat("_ClosestDistance", _useClosestDistance ? 1f : 0f);
        _projectionMaterial.SetFloat("_MetricType", (float)_metricType);
        _projectionMaterial.SetFloat("_ShowGrid", _showCoordGrid ? 1f : 0f);
        UpdateGrowAnimation(0f);
    }

    virtual protected void MoreShaderSetup()
    {
        // Called after setting the shader data in GeneratePoints()
    }

    protected abstract void InitProjectionPoint(
        ReferencePointHandler spherePoint,
        Transform container,
        Vector2 scale,
        SphereGenerator sphereGenerator);
}