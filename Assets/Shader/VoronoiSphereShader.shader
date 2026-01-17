Shader "Custom/VoronoiOnSphere"
{
    SubShader
    {
        Tags
        {
            "RenderType"="Opaque"
        }
        LOD 100

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "Common/Metrics.hlsl"
            #include "Common/Algorithms.hlsl"
            #include "Common/ShaderInputs.hlsl"

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
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


            fixed4 frag(v2f i) : SV_Target
            {
                float3 worldPos = i.localPos;
                float3 sphericalWorldPos = EuclideanToSpherical(worldPos);

                // Initialize minimum distance
                float refDistEuclid = _ClosestDistance == 1 ? 1e20 : -1e20;
                float finalColorIndexEuclid = 0;

                float refDistKarlsruhe = _ClosestDistance == 1 ? 1e20 : -1e20;
                float finalColorIndexKarlsruhe = 0;

                POINT_LOOP(j)
                {
                    // define points
                    float3 sphericalPointPos = _PointSphericalCoords[j].xyz;

                    // euclidean distance
                    float euclideanDistance = GetEuclideanDistance(sphericalPointPos, sphericalWorldPos);

                    // karlsruhe distance
                    float distanceKarlsruhe = GetKarlsruheDistance(sphericalPointPos, sphericalWorldPos);

                    if (_MetricType == METRIC_EUCLIDEAN || _MetricType == METRIC_DIFF_EUCLIDEAN_KARLSRUHE_BLACK ||
                        _MetricType == METRIC_DIFF_EUCLIDEAN_KARLSRUHE_ONLY)
                    {
                        if (_ClosestDistance == 1
                            ? euclideanDistance < refDistEuclid
                            : euclideanDistance > refDistEuclid)
                        {
                            refDistEuclid = euclideanDistance;
                            finalColorIndexEuclid = j;
                        }
                    }

                    if (_MetricType == METRIC_KARLSRUHE || _MetricType == METRIC_DIFF_EUCLIDEAN_KARLSRUHE_BLACK ||
                        _MetricType == METRIC_DIFF_EUCLIDEAN_KARLSRUHE_ONLY)
                    {
                        if (_ClosestDistance == 1
                            ? distanceKarlsruhe < refDistKarlsruhe
                            : distanceKarlsruhe > refDistKarlsruhe)
                        {
                            refDistKarlsruhe = distanceKarlsruhe;
                            finalColorIndexKarlsruhe = j;
                        }
                    }
                }

                float4 color = float4(0, 0, 0, 1);

                if (_MetricType == METRIC_EUCLIDEAN)
                {
                    if (_MaxDistancePercentage == 0 || refDistEuclid <= (UNITY_PI * _Radius) * (_MaxDistancePercentage /
                        100))
                    {
                        color = _Colors[finalColorIndexEuclid];
                    }
                }
                else if (_MetricType == METRIC_KARLSRUHE)
                {
                    if (_MaxDistancePercentage == 0 || refDistKarlsruhe <= (UNITY_PI * _Radius) * (
                        _MaxDistancePercentage / 100))
                    {
                        color = _Colors[finalColorIndexKarlsruhe];
                    }
                }
                else if (_MetricType == METRIC_DIFF_EUCLIDEAN_KARLSRUHE_BLACK)
                {
                    if ((finalColorIndexEuclid == finalColorIndexKarlsruhe) && (_MaxDistancePercentage == 0 ||
                        refDistKarlsruhe <= (UNITY_PI * _Radius) * (_MaxDistancePercentage / 100)))
                    {
                        color = _Colors[finalColorIndexKarlsruhe];
                    }
                }
                else if (_MetricType == METRIC_DIFF_EUCLIDEAN_KARLSRUHE_ONLY)
                {
                    if ((finalColorIndexEuclid != finalColorIndexKarlsruhe) && (_MaxDistancePercentage == 0 ||
                        refDistKarlsruhe <= (UNITY_PI * _Radius) * (_MaxDistancePercentage / 100)))
                    {
                        color = _Colors[finalColorIndexKarlsruhe];
                    }
                }

                return applyGrid(_ShowGrid, sphericalWorldPos, color);
            }
            ENDCG
        }
    }
    FallBack "Diffuse"
}