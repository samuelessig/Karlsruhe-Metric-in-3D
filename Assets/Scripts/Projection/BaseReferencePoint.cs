using UnityEngine;

public abstract class BaseReferencePoint : MonoBehaviour
{
    protected Vector2 _projectionScale;
    protected Color _color;

    protected Vector3 _euclideanPosition;
    protected Vector2 _sphericalPosition;
    protected float _sphereRadius;
    abstract public void UpdatePosition();

    abstract public void SetPointRadius(float pointRadius);

    public Vector4 GetSphericalPosition4()
    {
        return new Vector4(_sphereRadius, _sphericalPosition.x, _sphericalPosition.y, 0);
    }


    public Color GetColor()
    {
        return _color;
    }
}