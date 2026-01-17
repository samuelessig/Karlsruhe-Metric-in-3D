Shader "Custom/2D on Circle"
{
    /*
    This shader shows a single slice (fixed at y=0 plane), it is the same as the slice part of the Slices.shader.
    In the C# SphereGenerator.cs the points are moved into the y=0 plane (if a shader that contains "2D" in its name is loaded).

    The main difference is that this shader can show a polar/circle coordinate grid overlay on top of the Voronoi diagram.
    */
    SubShader
    {
        Tags
        {
            "Queue"="Transparent" "RenderType"="Transparent"
        }
        LOD 100
        Pass
        {
            ZWrite Off
            Blend SrcAlpha OneMinusSrcAlpha

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag


            #include "UnityCG.cginc"
            #include "Common/Metrics.hlsl"
            #include "Common/Algorithms.hlsl"
            #include "Common/ShaderInputs.hlsl"

            // Returns cartesian (x,y,z,0) for point i
            // See SphereGenerator.cs for data layout
            static inline float3 GetPointCartesian(int i)
            {
                return _PointSphericalCoords[i + _PointCount].xyz;
            }

            // Voronoi classification at a single 3D point inside the sphere, R is the sphere radius
            float4 ShadeVoronoiAtPoint(float3 X,float opacity,float bisWidthPx, float R)
            {
                float3 Xsph = EuclideanToSpherical(X);

                // Compute Euclidean and Karlsruhe distances for all points
                float bestE = 1e30, secondE = 1e30;
                int bestIdE = -1, secondIdE = -1;
                float bestK = 1e30, secondK = 1e30;
                int bestIdK = -1, secondIdK = -1;

                POINT_LOOP(k)
                {
                    if (k < 2 && _ShowProbesVoronoi < 0.5 && _PointCount > 3.5)
                    {
                        // Path mode is enabled, skip the first and second point
                        // because they are the endpoints of the path
                        continue;
                    }

                    float3 Pk_sph = _PointSphericalCoords[k].xyz;
                    float3 Pk_cart = GetPointCartesian(k);

                    // Euclidean distance in cartesian coords
                    float euclidRaw = length(X - Pk_cart);

                    // Karlsruhe distance in spherical and cartesian coords
                    float karlsRaw = GetKarlsruheDistance_SphCart(Pk_sph, Xsph, Pk_cart, X);
                    // Closest vs farthest toggle
                    float euclidVal = (_ClosestDistance == 1.0) ? euclidRaw : -euclidRaw;
                    float karlsVal = (_ClosestDistance == 1.0) ? karlsRaw : -karlsRaw;

                    ArgMin2(euclidVal, k, bestE, bestIdE, secondE, secondIdE);
                    ArgMin2(karlsVal, k, bestK, bestIdK, secondK, secondIdK);
                }

                // Which metric to use
                float best = 0.0;
                float second = 0.0;
                float4 baseCol = float4(0, 0, 0, 0);

                bool sameLabel = (bestIdE == bestIdK);

                if (_MetricType == METRIC_EUCLIDEAN)
                {
                    best = bestE;
                    second = secondE;
                    baseCol = _Colors[max(bestIdE, 0)];
                }
                else if (_MetricType == METRIC_KARLSRUHE)
                {
                    best = bestK;
                    second = secondK;
                    baseCol = _Colors[max(bestIdK, 0)];
                }
                else if (_MetricType == METRIC_DIFF_EUCLIDEAN_KARLSRUHE_ONLY)
                {
                    // Show only cells where Euclidean and Karlsruhe disagree
                    best = bestK;
                    second = secondK;

                    if (!sameLabel)
                    {
                        baseCol = _Colors[max(bestIdK, 0)];
                    }
                    else
                    {
                        // Agreement -> fully transparent
                        baseCol = float4(0, 0, 0, 0);
                    }
                }
                else if (_MetricType == METRIC_DIFF_EUCLIDEAN_KARLSRUHE_BLACK)
                {
                    // Karlsruhe Voronoi, but disagreement regions are black
                    best = bestK;
                    second = secondK;

                    if (sameLabel)
                    {
                        // Agreement: normal Karlsruhe color
                        baseCol = _Colors[max(bestIdK, 0)];
                    }
                    else
                    {
                        // Disagreement: black zone
                        baseCol = float4(0, 0, 0, 1);
                    }
                }

                // Apply overall opacity
                baseCol.a *= opacity;

                // Primary distance for the currently active metric
                float primaryDist;
                if (_MetricType == METRIC_EUCLIDEAN)
                {
                    primaryDist = bestE;
                }
                else
                {
                    // Karlsruhe and the diff modes use Karlsruhe distance
                    primaryDist = bestK;
                }

                float unsignedDist = (_ClosestDistance == 1.0) ? primaryDist : -primaryDist;

                // Growth Mode
                if (_MaxDistancePercentage > 0.0)
                {
                    // 100% distance = sphere diameter
                    float maxDist = 2.0 * R;
                    float distLimit = maxDist * (_MaxDistancePercentage / 100.0);

                    if (unsignedDist > distLimit)
                    {
                        // distance not reached yet -> fully transparent
                        baseCol.a = 0.0;
                    }
                }


                // Iso-lines / unit-sphere mode
                if (_ShowIsoLines > 0.5 && baseCol.a > 0.0)
                {
                    float step = (_IsoLineStep > 0.0) ? _IsoLineStep : (0.1 * R);
                    float thickness = (_IsoLineThickness > 0.0) ? _IsoLineThickness : 0.04;

                    // normalized distance
                    float v = unsignedDist / step;
                    float f = frac(v);
                    float distToLine = min(f, 1.0 - f); // 0 at ring center, 0.5 between rings

                    if (_ShowIsoLines < 1.5)
                    {
                        // Iso lines
                        float lineMask = smoothstep(thickness, 0.0, distToLine);

                        float3 isoColor = float3(1.0, 1.0, 1.0);
                        baseCol.rgb = lerp(baseCol.rgb, isoColor, lineMask * 0.6);
                        baseCol.a   = max(baseCol.a, lineMask * opacity * 0.4);
                    }
                    else
                    {
                        // Unit sphere
                        float shellMask = 1.0 - smoothstep(0.5, 0.5001, abs(v - 1.0));

                        float lineMask = smoothstep(thickness, 0.0, distToLine) * shellMask;

                        float3 isoColor = float3(1.0, 1.0, 1.0);
                        baseCol.rgb = lerp(baseCol.rgb, isoColor, lineMask * 0.6);
                        baseCol.a   = max(baseCol.a, lineMask * opacity * 0.4);
                    }
                }

                // Bisectors via top-2 distance gap
                float gap = second - best;
                float gapW = max(fwidth(gap), 1e-5);
                float bisMask = 1.0 - smoothstep(0.0, bisWidthPx * gapW, abs(gap));

                float4 bisCol = float4(0, 0, 0, 1);
                float bisStrength = saturate(0.85 * bisMask);

                float4 col = baseCol;
                col.rgb = lerp(col.rgb, bisCol.rgb, bisStrength);
                col.a = max(col.a, bisStrength * 0.75);

                return col;
            }

            float4 PolarGridOnCircle(float3 X, float R)
            {
                // Slice plane is fixed at y=0, so use (x,z) for disk coords
                float2 p = float2(X.x, X.z);
                float r = length(p);
                if (r > R + 1e-3)
                    return float4(0, 0, 0, 0);

                // Angle in 0-2*PI
                float ang = atan2(p.y, p.x);
                if (ang < 0.0) ang += 2.0 * UNITY_PI;

                // Rings
                float ringNumber = 5.0;
                float ringStep = R / ringNumber;
                float ringIdx = r / ringStep;
                float ringFw = max(fwidth(ringIdx), 1e-4);
                float ringLine = abs(frac(ringIdx + 0.5) - 0.5) / ringFw;
                float ringMask = saturate(1.0 - ringLine);

                // Spokes
                float angleStep = UNITY_PI / 8.0; // 22.5 degrees
                float aIdx = ang / angleStep;
                float aFw = max(fwidth(aIdx), 1e-4);
                float rayLine = abs(frac(aIdx + 0.5) - 0.5) / aFw;
                float rayMask = saturate(1.0 - rayLine);

                float gridMask = saturate(ringMask + rayMask);

                // Outer boundary circle
                float boundaryThickness = 0.02 * R;
                float dBoundary = abs(r - R);
                float boundary = 1.0 - smoothstep(boundaryThickness * 0.5,
                                                  boundaryThickness,
                                                  dBoundary);

                // Color the grid
                float3 gridColor = float3(0.4, 0.4, 0.4);
                float alpha = gridMask * 0.5;
                alpha = max(alpha, boundary);

                return float4(gridColor, alpha);
            }

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 localPos : TEXCOORD0;
                float3 viewDirOS : TEXCOORD1;
            };

            float3 GetCameraPosOS()
            {
                float4 camOS = mul(unity_WorldToObject, float4(_WorldSpaceCameraPos, 1.0));
                return camOS.xyz;
            }

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.localPos = v.vertex.xyz;

                float3 camOS = GetCameraPosOS();
                o.viewDirOS = normalize(o.localPos - camOS);
                return o;
            }

            float4 ShadeSlice(
                float3 ro, float3 rd,
                float tEnter, float tExit,
                float3 n, float w,
                float sliceOpacity,
                float bisWidthPx,
                float R,
                out float3 X)
            {
                float tPlane;
                bool hitPlane = RayPlane(ro, rd, n, w, tPlane);
                if (!hitPlane || tPlane < tEnter || tPlane > tExit)
                {
                    X = float3(0, 0, 0);
                    return float4(0, 0, 0, 0);
                }

                X = ro + tPlane * rd;
                return ShadeVoronoiAtPoint(X, sliceOpacity, bisWidthPx, R);
            }


            fixed4 frag(v2f i) : SV_Target
            {
                // Fix slice at y=0 plane
                float4 fixedSliceNormal = float4(0, 1, 0, 0);

                if (_PointCount <= 0)
                    return float4(0, 0, 0, 0);

                float3 ro = GetCameraPosOS();
                float3 rd = normalize(i.viewDirOS);

                float tEnter, tExit;
                bool hitSphere;
                RaySphere(ro, rd, _Radius, tEnter, tExit, hitSphere);
                if (!hitSphere)
                    return float4(0, 0, 0, 0);

                // Single slice
                float3 n1 = normalize(fixedSliceNormal.xyz);
                float w1 = fixedSliceNormal.w;
                float3 X; // The current point on the slice plane
                float4 col1 = ShadeSlice(ro, rd, tEnter, tExit,
                           n1, w1,
                           _SliceOpacity0,
                           _BisectorWidthPx0,
                           _Radius,
                           X);

                // Put everything together
                float4 surfaceBase = float4(0, 0, 0, 0);

                float4 outCol = surfaceBase;

                float a1 = saturate(col1.a);
                outCol.rgb = lerp(outCol.rgb, col1.rgb, a1);
                outCol.a = max(outCol.a, a1);

                // Draw grid over the Voronoi, if enabled
                if (_ShowGrid > 0.5 && a1 > 0.0)
                {
                    float4 grid = PolarGridOnCircle(X, _Radius);
                    float ga = saturate(grid.a);
                    outCol.rgb = lerp(outCol.rgb, grid.rgb, ga);
                    outCol.a = max(outCol.a, ga);
                }

                return outCol;
            }
            ENDCG
        }
    }

    FallBack Off
}