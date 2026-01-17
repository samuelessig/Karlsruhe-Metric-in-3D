using UnityEngine;
using Random = UnityEngine.Random;

public class ReferencePointHandler : MonoBehaviour
{
    public Vector3 euclideanPosition;
    private Vector3 _oldEuclideanPosition;
    public float radius;
    public Vector2 sphericalPosition;
    private Vector2 _oldSphericalPosition;
    public Color color;
    private Color _oldColor;
    public float pointRadius;
    private float _oldPointRadius;

    private SphereGenerator _sphereGenerator;

    void Awake()
    {
        if (_sphereGenerator == null)
        {
            _sphereGenerator = transform.parent.GetComponent<SphereGenerator>();
        }
    }

    void Update()
    {

        if (euclideanPosition != _oldEuclideanPosition)
        {
            SetEuclideanPosition(euclideanPosition);
        }

        if (sphericalPosition != _oldSphericalPosition)
        {
            SetSphericalPosition(sphericalPosition);
        }

        var renderer = GetComponent<Renderer>();

        color = renderer.material.color;
        if (color != _oldColor)
        {
            SetColor(color);
            _sphereGenerator.UpdatePointColor();
        }

        if (_sphereGenerator.Is2DShader())
        {
            // Make the points render on top in 2D mode
            renderer.material.renderQueue = 3100;
        } else
        {
            // Set default render queue
            renderer.material.renderQueue = -1;
        }

        if (pointRadius != _oldPointRadius)
        {
            SetPointRadius(pointRadius);
        }
    }

    public void InitializePoint(float r, float referencePointRadius, Vector2? position = null)
    {
        radius = r;
        if (position == null)
        {
            SetRandomSphericalPosition(r, true);
        }
        else
        {
            SetSphericalPosition((Vector2) position, r, true);
        }

        SetPointRadius(referencePointRadius);
        SetRandomColor();
    }

    public void SetEuclideanPosition(Vector3 position)
    {
        transform.position = position;
        _oldEuclideanPosition = transform.localPosition;
        euclideanPosition = transform.localPosition;

        // Convert to spherical with radius
        Vector3 spherical = ConvertWorldPosToSpherical(transform.localPosition);
        radius = spherical.x;
        sphericalPosition = new Vector2(spherical.y, spherical.z);
        _oldSphericalPosition = sphericalPosition;

        _sphereGenerator.UpdatePointPositionSpherical();
    }

    public void SetSphericalPosition(Vector2 position, float r = -1, bool init = false)
    {
        if (r > -1) {
            radius = r;
        }

        float theta = position.x % (Mathf.PI * 2f);
        theta = theta > 0 ? theta : (Mathf.PI * 2f) - theta;

        float phi = position.y % Mathf.PI;
        phi = phi > 0 ? phi : Mathf.PI - phi;

        sphericalPosition = new Vector2(theta, phi);
        _oldSphericalPosition = sphericalPosition;

        euclideanPosition = ConvertToEuclidean(radius, sphericalPosition);
        _oldEuclideanPosition = euclideanPosition;

        SetWorldPosition(euclideanPosition);
        if (!init)
        {
            _sphereGenerator.UpdatePointPositionSpherical();
        }

    }

    public void SetColor(Color color)
    {
        this.color = color;
        _oldColor = color;
        GetComponent<Renderer>().material.color = color;
    }

    public void SetPointRadius(float pointRadius)
    {
        this.pointRadius = pointRadius;
        _oldPointRadius = pointRadius;

        transform.localScale = new Vector3(
            pointRadius / transform.parent.transform.lossyScale.x,
            pointRadius / transform.parent.transform.lossyScale.y,
            pointRadius / transform.parent.transform.lossyScale.z
        );
    }

    public void SetRandomSphericalPosition(float r, bool init = false)
    {
        float phi = Random.Range(0, Mathf.PI);
        float theta = Random.Range(0, Mathf.PI * 2f);
        SetSphericalPosition(new Vector2(theta, phi), r, init);
    }

    public void SetRandomColor()
    {
        SetColor(GeneratedRandomColor());
    }

private Vector3 ConvertWorldPosToSpherical(Vector3 euclidPos)
{
    // Radius (distance from origin)
    float r = euclidPos.magnitude;

    if (r == 0)
    {
        // Radius is zero, we are on the origin,
        // return zero spherical coordinates to avoid computing NaN
        return new Vector3(0, 0, 0);
    }

    // Azimuth angle thetha [0, 2PI]
    float theta = Mathf.Atan2(euclidPos.y, euclidPos.x);

    // Polar angle phi  [0, PI]
    // z is the vertical axis
    float phi = Mathf.Acos(euclidPos.z / r);

    return new Vector3(r, theta, phi);
}

    private static Vector3 ConvertToEuclidean(float r, Vector2 sphericalPos)
    {
        float x = r * Mathf.Sin(sphericalPos.y) * Mathf.Cos(sphericalPos.x);
        float y = r * Mathf.Sin(sphericalPos.y) * Mathf.Sin(sphericalPos.x);
        float z = r * Mathf.Cos(sphericalPos.y);

        return new Vector3(x, y, z);
    }

    private void SetWorldPosition(Vector3 localPoint)
    {
        Quaternion rotation = transform.parent.transform.rotation;
        Vector3 rotatedPoint = rotation * localPoint;
        Vector3 newPoint = transform.parent.transform.position + rotatedPoint;

        transform.position = newPoint;
        transform.rotation = rotation;
    }
    private Color GeneratedRandomColor()
    {
      return Color.HSVToRGB(Random.value, Random.value, 0.8f);
      //return new Color(Random.value, Random.value, Random.value);
    }
}
