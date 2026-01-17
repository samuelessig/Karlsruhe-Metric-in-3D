using System;
using UnityEngine;

[Serializable]
public struct SinglePointState
{
    public Vector3 position;
    public Color color;
}

[Serializable]
public class PointsSetState
{
    public string name;
    public SinglePointState[] points;
}
