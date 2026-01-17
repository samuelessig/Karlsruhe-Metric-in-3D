Shader "Custom/Single Cell Boundary"
{
    /*
    This shader shows the boundary surface of a single Voronoi cell. The cell index can be set
    via the _CellBoundaryIndex property.
    The shader uses ray marching to find the surface, similar to BisectorSurface.shader.
    The surface is colored differently depending on whether the ray came from inside or outside
    to distinguish the two sides of the boundary.

    Instead of finding the root of F(X) = dK(X,A) - dK(X,B) as in the bisector surface,
    we find the root of F(X) = d(X, P_i) - min_{k != i} d(X, P_k) where P_i is the point
    whose Voronoi cell boundary we want to show.
    These are exactly the points X where P_i and the nearest other site are are equally close.

    We compute it by looping over all points and keeping track of the
    closest one. This means F has to loop over all points for each evaluation, compared to
    only two points in the bisector surface shader. This makes it significantly slower.

    Same as the Bisector Surface shader, the ray marching fails when the boundary
    is a "thick" boundary region and not a thin surface.
    */
    SubShader
    {
        Tags
        {
            "Queue"="Transparent" "RenderType"="Transparent"
        }
        LOD 100

        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"
            #include "Common/Metrics.hlsl"
            #include "Common/Algorithms.hlsl"
            #include "Common/ShaderInputs.hlsl"

            #define STEPS 120
            #define EPS_GRAD 0.003

            // Fixed colors:
            static const float3 COLOR_INSIDE = float3(0.20, 0.85, 0.35);
            static const float3 COLOR_OUTSIDE = float3(0.90, 0.25, 0.25);
            static const float ALPHA_SURFACE = 0.80;

            struct appdata
            {
                float4 vertex : POSITION;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 localPos : TEXCOORD0;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.localPos = v.vertex.xyz;
                return o;
            }

            struct Ray
            {
                float3 o;
                float3 d;
            };

            bool RaySphereHit(Ray ray, float R, out float t0, out float t1)
            {
                bool hit;
                RaySphere(ray.o, ray.d, R, t0, t1, hit);
                return hit && (t1 > 0.0);
            }

            float3 LightDir_OS()
            {
                float3 Lw = normalize(_LightDirWorld);
                return normalize(mul((float3x3)unity_WorldToObject, Lw));
            }

            // Returns cartesian (x,y,z,0) for point i
            // See SphereGenerator.cs for data layout
            static inline float3 GetPointCartesian(int i)
            {
                return _PointSphericalCoords[i + _PointCount].xyz;
            }

            // To compute the cell boundary of point P_i
            // We find the closest competitor point P_K
            // F(X) = d(X, P_i) - ( min {k} of { d(X, P_k) } with k != i )
            float F(float3 Xos, out int closestIndex)
            {
                float3 Xsph = 0;
                if (_MetricType == METRIC_KARLSRUHE)
                    Xsph = EuclideanToSpherical(Xos);

                int i = _CellBoundaryIndex;

                float3 Pi = _PointSphericalCoords[i].xyz;
                float di = DistPToX(_MetricType, Pi, GetPointCartesian(i), Xsph, Xos);

                float dMinOther = 1e20;
                closestIndex = -1;

                POINT_LOOP(k)
                {
                    if (k < 2 && _ShowProbesVoronoi < 0.5 && _PointCount > 3.5)
                    {
                        // Path mode is enabled, skip the first and second point
                        // because they are the endpoints of the path
                        continue;
                    }

                    if (k != i)
                    {
                        float3 Pk = _PointSphericalCoords[k].xyz;
                        float dk = DistPToX(_MetricType, Pk, GetPointCartesian(k), Xsph, Xos);

                        if (dk < dMinOther)
                        {
                            dMinOther = dk;
                            closestIndex = k;
                        }
                    }
                }

                return di - dMinOther;
            }

            // Used for gradient computation between two points only.
            // Distance between P_k (the selected Voronoi cell) and
            // P_j (the currently closest competitor) at point X.
            // If we would use F(X), we would have to loop over all points
            // at each gradient evaluation i.e. 6x for central differences.
            float PairwiseBisectorF(float3 Xos, int k, int j)
            {
                float3 Xsph = 0;
                if (_MetricType == METRIC_KARLSRUHE)
                    Xsph = EuclideanToSpherical(Xos);

                float3 Pk = _PointSphericalCoords[k].xyz;
                float3 Pj = _PointSphericalCoords[j].xyz;

                float dk = DistPToX(_MetricType, Pk, GetPointCartesian(k), Xsph, Xos);
                float dj = DistPToX(_MetricType, Pj, GetPointCartesian(j), Xsph, Xos);

                return dk - dj;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // See BisectorSurface.shader for detailed comments on the ray marching approach
                float4 outCol = float4(0, 0, 0, 0);

                float3 camOS = mul(unity_WorldToObject, float4(_WorldSpaceCameraPos, 1)).xyz;
                float3 dirOS = normalize(i.localPos - camOS);

                Ray ray;
                ray.o = camOS;
                ray.d = dirOS;

                float t0, t1;
                if (!RaySphereHit(ray, _Radius, t0, t1))
                    return outCol;

                t0 = max(t0, 0.0);
                float t = t0;
                float dt = (t1 - t0) / (float)STEPS;
                float prevF = 0.0;
                bool hasPrev = false;
                float hitT = -1.0;

                bool isFromInside = false;

                [loop]
                for (int s = 0; s < STEPS; ++s)
                {
                    float3 X = ray.o + t * ray.d;

                    int _unusedValueIndex;
                    // We don't use the closest index here, as it doesn't have the secant refinement
                    // So it is computed again below after the refined hit point is found
                    float f = F(X, _unusedValueIndex);

                    if (hasPrev && sign(f) != sign(prevF))
                    {
                        // From which side did we come, for coloring
                        isFromInside = (prevF <= 0.0);

                        // Secant refinement between (t-dt, t)
                        float tA = t - dt, fA = prevF;
                        float tB = t, fB = f;
                        float denom = (fB - fA);
                        hitT = (abs(denom) > 1e-6)
                            ? (tA - fA * (tB - tA) / denom)
                            : 0.5 * (tA + tB);
                        break;
                    }
                    prevF = f;
                    hasPrev = true;
                    t += dt;
                }

                if (hitT <= 0.0)
                    return outCol;

                float3 Xhit = ray.o + hitT * ray.d;

                // Find new closest point with the refined hit point
                int closestIndex;
                F(Xhit, closestIndex);
                if (closestIndex < 0)
                    return outCol;

                // Central-differences gradient between selected point and closest competitor
                float eps = EPS_GRAD * _Radius;
                float3 ex = float3(eps, 0, 0);
                float3 ey = float3(0, eps, 0);
                float3 ez = float3(0, 0, eps);

                float fx = PairwiseBisectorF(Xhit + ex, _CellBoundaryIndex, closestIndex) - PairwiseBisectorF(Xhit - ex, _CellBoundaryIndex, closestIndex);
                float fy = PairwiseBisectorF(Xhit + ey, _CellBoundaryIndex, closestIndex) - PairwiseBisectorF(Xhit - ey, _CellBoundaryIndex, closestIndex);
                float fz = PairwiseBisectorF(Xhit + ez, _CellBoundaryIndex, closestIndex) - PairwiseBisectorF(Xhit - ez, _CellBoundaryIndex, closestIndex);

                float3 gradF = float3(fx, fy, fz);
                float3 N = normalize(gradF);

                float3 baseCol = isFromInside ? COLOR_INSIDE : COLOR_OUTSIDE;

                // Blinn-Phong Shading (supposed to be better with approximated normals)
                float3 V = normalize(camOS - Xhit);
                float3 L = LightDir_OS();
                float diff = saturate(dot(N, L));
                float3 H = normalize(L + V);
                float spec = pow(saturate(dot(N, H)), 48.0);
                float3 shaded = baseCol * (0.25 + 0.75 * diff) + baseCol * (0.25 * spec);

                outCol.rgb = saturate(shaded);
                outCol.a = ALPHA_SURFACE;

                return outCol;
            }
            ENDCG
        }
    }
    FallBack "Diffuse"
}