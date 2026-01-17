using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;

public class CreateAzimuthalProjection : BaseProjection<AzimuthalReferencePoint>
{
    public bool isNorthCenter = true;

    protected override void InitProjectionPoint(
        ReferencePointHandler spherePoint, Transform container, Vector2 scale, SphereGenerator gen)
    {
        GameObject obj = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        obj.AddComponent<AzimuthalReferencePoint>()
            .InitializePoint(spherePoint, container, scale, gen, isNorthCenter);
    }

    protected override void MoreShaderSetup()
    {
        _projectionMaterial.SetFloat("_IsNorthCenter", isNorthCenter ? 1 : 0);
    }

    override protected void SetShaderMetricProperties()
    {
        _projectionMaterial.SetFloat("_IsNorthCenter", isNorthCenter ? 1f : 0f);
        base.SetShaderMetricProperties();
    }
}