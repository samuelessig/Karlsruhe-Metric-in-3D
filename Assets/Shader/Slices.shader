Shader "Custom/Slices"
{
    /*
     This shader shows two slices that can move through the sphere and show the voronoi diagram on the slices.
     Additionally the voronoi diagram can be shown on the surface of the sphere.

     Two passes are used to make the shader simpler:
     First pass paints only the rear part of the sphere, the voronoi on the rear surface part and optionally a grid on the rear surface.
     Second pass paints the two slices, and the front of the sphere surface. It calculates a color value for each slice and for the surface
     and stores the colors as "Layer".
     Then the three layers are sorted from back to front (in reference to the fragment/camera) and the color values are blended in this order.
     */
    SubShader
    {
        Tags
        {
            "Queue"="Transparent" "RenderType"="Transparent"
        }
        LOD 100

        CGINCLUDE
        // Shared code, same for both passes
        #include "UnityCG.cginc"
        #include "Common/Metrics.hlsl"
        #include "Common/Algorithms.hlsl"
        #include "Common/ShaderInputs.hlsl"

        float3 GetCameraPosOS()
        {
            return mul(unity_WorldToObject, float4(_WorldSpaceCameraPos, 1.0)).xyz;
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


        v2f vert(appdata v)
        {
            v2f o;
            o.pos = UnityObjectToClipPos(v.vertex);
            o.localPos = v.vertex.xyz;

            float3 camOS = GetCameraPosOS();
            o.viewDirOS = normalize(o.localPos - camOS);
            return o;
        }

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
            int bestId = -1;
            int secondId = -1;
            float4 baseCol = float4(0, 0, 0, 0);

            bool sameLabel = (bestIdE == bestIdK);

            if (_MetricType == METRIC_EUCLIDEAN)
            {
                best = bestE;
                second = secondE;
                bestId = bestIdE;
                secondId = secondIdE;
                baseCol = _Colors[max(bestIdE, 0)];
            }
            else if (_MetricType == METRIC_KARLSRUHE)
            {
                best = bestK;
                second = secondK;
                bestId = bestIdK;
                secondId = secondIdK;
                baseCol = _Colors[max(bestIdK, 0)];
            }
            else if (_MetricType == METRIC_DIFF_EUCLIDEAN_KARLSRUHE_ONLY)
            {
                // Show only cells where Euclidean and Karlsruhe disagree
                best = bestK;
                second = secondK;
                bestId = bestIdK;
                secondId = secondIdK;

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
                bestId = bestIdK;
                secondId = secondIdK;

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

            // Bisector via top-2 distance gap
            float gap = second - best;
            float gapW = max(fwidth(gap), 1e-5);
            float bisMask = 1.0 - smoothstep(0.0, bisWidthPx * gapW, abs(gap));

            float4 bisCol = float4(0, 0, 0, 1);
            float bisStrength = saturate(0.85 * bisMask);

            float4 col = baseCol;
            col.rgb = lerp(col.rgb, bisCol.rgb, bisStrength);
            col.a = max(col.a, bisStrength * 0.75);

            // Origin marker
            col = ApplyOriginMarker(col, X);
            return col;
        }
        ENDCG

        Pass
        {
            // First pass: back grid/back voronoi on the sphere surface
            Cull Front
            ZWrite Off
            Blend SrcAlpha OneMinusSrcAlpha

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment fragBack

            fixed4 fragBack(v2f i) : SV_Target
            {
                float4 col = float4(0, 0, 0, 0);

                // Show a subtle grid on the sphere surface
                if (_ShowGrid > 0.5)
                {
                    float4 grid = GridOnSurface(_ShowGrid, _Radius, i.localPos);
                    grid.a *= 0.35;
                    col = grid;
                }

                // Show a suble Voronoi on the sphere surface
                if (_ShowSurfaceVoronoi > 0.5 && _PointCount > 0)
                {
                    // Ensure point is exactly on sphere surface, not a mesh approximation
                    float3 Xsurf = normalize(i.localPos) * _Radius;

                    float4 vorCol = ShadeVoronoiAtPoint(
                        Xsurf,
                        _SurfaceVoronoiOpacity,
                        _SurfaceBisectorWidthPx,
                        _Radius
                    );

                    // Blend Voronoi over the grid
                    float a = saturate(vorCol.a);
                    col.rgb = lerp(col.rgb, vorCol.rgb, a);
                    col.a = max(col.a, a);
                }

                return col;
            }
            ENDCG
        }

        Pass
        {
            // Second pass: front grid on sphere surface and voronoi on slices
            ZWrite Off
            Blend SrcAlpha OneMinusSrcAlpha
            Cull Back

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            float4 ShadeSlice(
                float3 ro, float3 rd,
                float tEnter, float tExit,
                float3 n, float w,
                float sliceOpacity,
                float bisWidthPx,
                float R,
                out float tPlaneOut)
            {
                float tPlane;
                bool hitPlane = RayPlane(ro, rd, n, w, tPlane);
                if (!hitPlane || tPlane < tEnter || tPlane > tExit)
                {
                    tPlaneOut = 1e30; // invalid, very far depth
                    return float4(0, 0, 0, 0);
                }

                tPlaneOut = tPlane;
                float3 X = ro + tPlane * rd;
                return ShadeVoronoiAtPoint(X, sliceOpacity, bisWidthPx, R);
            }


            struct Layer
            {
                float t;
                float4 col;
            };

            Layer NewLayer(float t, float4 col)
            {
                Layer L;
                L.t = t;
                L.col = col;
                return L;
            }

            // Porter-Duff "SrcOver"
            // https://apoorvaj.io/alpha-compositing-opengl-blending-and-premultiplied-alpha
            float4 blendSrcOverDst(float4 dst, float4 src)
            {
                float a = saturate(src.a);
                dst.rgb = lerp(dst.rgb, src.rgb, a);
                dst.a = 1.0 - (1.0 - dst.a) * (1.0 - a);
                return dst;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                if (_PointCount <= 0)
                    return float4(0, 0, 0, 0);

                float3 ro = GetCameraPosOS();
                float3 rd = normalize(i.viewDirOS);

                float tEnter, tExit;
                bool hitSphere;
                RaySphere(ro, rd, _Radius, tEnter, tExit, hitSphere);
                if (!hitSphere)
                    return float4(0, 0, 0, 0);

                // Build layers: front surface, slice 0, slice 1
                Layer layers[3];
                int layerCount = 0;

                // Sphere front surface as one layer (grid and Voronoi on sphere surface)
                {
                    float4 surfaceBase = float4(0, 0, 0, 0);

                    // Grid overlay
                    if (_ShowGrid > 0.5)
                    {
                        float4 gridFront = GridOnSurface(_ShowGrid, _Radius, i.localPos);
                        gridFront.a *= 0.85;
                        surfaceBase = gridFront;
                    }

                    // Voronoi on the sphere surface
                    if (_ShowSurfaceVoronoi > 0.5 && _PointCount > 0)
                    {
                        // Ensure point is exactly on sphere surface, not a mesh approximation
                        float3 Xsurf = normalize(i.localPos) * _Radius;

                        float4 vorCol = ShadeVoronoiAtPoint(
                            Xsurf,
                            _SurfaceVoronoiOpacity,
                            _SurfaceBisectorWidthPx,
                            _Radius
                        );

                        float a = saturate(vorCol.a);
                        surfaceBase.rgb = lerp(surfaceBase.rgb, vorCol.rgb, a);
                        surfaceBase.a = max(surfaceBase.a, a);
                    }

                    if (surfaceBase.a > 0.001)
                    {
                        layers[layerCount] = NewLayer(tEnter, surfaceBase); // front sphere intersection
                        layerCount++;
                    }
                }

                // Slice #0
                if (true)
                {
                    float3 n1 = normalize(_SlicePlane0.xyz);
                    float w1 = _SlicePlane0.w;

                    float tPlane0;
                    float4 col1 = ShadeSlice(
                        ro, rd,
                        tEnter, tExit,
                        n1, w1,
                        _SliceOpacity0,
                        _BisectorWidthPx0,
                        _Radius,
                        tPlane0);

                    if (col1.a > 0.001)
                    {
                        layers[layerCount] = NewLayer(tPlane0, col1);
                        layerCount++;
                    }
                }

                // Slice #1
                if (_UseSlice1 > 0.5)
                {
                    float3 n2 = normalize(_SlicePlane1.xyz);
                    float w2 = _SlicePlane1.w;
                    float bw2 = (_BisectorWidthPx1 > 0.0) ? _BisectorWidthPx1 : _BisectorWidthPx0;

                    float tPlane1;
                    float4 col2 = ShadeSlice(
                        ro, rd,
                        tEnter, tExit,
                        n2, w2,
                        _SliceOpacity1,
                        bw2,
                        _Radius,
                        tPlane1);

                    if (col2.a > 0.001)
                    {
                        layers[layerCount] = NewLayer(tPlane1, col2);
                        layerCount++;
                    }
                }

                if (layerCount == 0)
                    return float4(0, 0, 0, 0);

                // Sort layers front to back with selection sort
                for (int iL = 0; iL < layerCount; ++iL)
                {
                    for (int jL = iL + 1; jL < layerCount; ++jL)
                    {
                        if (layers[jL].t < layers[iL].t)
                        {
                            Layer tmp = layers[iL];
                            layers[iL] = layers[jL];
                            layers[jL] = tmp;
                        }
                    }
                }

                // Composite transparency from back to front using "src over dst"
                float4 outCol = float4(0, 0, 0, 0); // background

                // layers[0] is nearest, layers[layerCount-1] is farthest
                for (int idx = layerCount - 1; idx >= 0; --idx)
                {
                    outCol = blendSrcOverDst(outCol, layers[idx].col);
                }
                return outCol;
            }
            ENDCG
        }
    }

    FallBack Off
}