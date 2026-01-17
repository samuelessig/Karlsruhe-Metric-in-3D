using System.Collections.Generic;
using UnityEngine;

public class CreateMercatorProjection : BaseProjection<MercatorReferencePoint>
{
    protected override void InitProjectionPoint(
        ReferencePointHandler spherePoint, Transform container, Vector2 scale, SphereGenerator gen)
    {
        GameObject obj = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        obj.AddComponent<MercatorReferencePoint>()
            .InitializePoint(spherePoint, container, scale, gen);
    }
}