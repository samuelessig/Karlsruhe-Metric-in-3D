Shader "Custom/Onion Layers"
{
    SubShader
    {
        Tags
        {
            "Queue" = "Transparent" "RenderType" = "Transparent"
        }
        LOD 100

        //  transparency:
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
                // Point on the sphere
                float3 Xsph = EuclideanToSpherical(i.localPos);

                // Colors
                float4 colA = _Colors[0];
                float4 colB = _Colors[1];

                float3 Asph = _PointSphericalCoords[0].xyz;
                float3 Bsph = _PointSphericalCoords[1].xyz;

                if (_PointCount > 3.5 && _ShowProbesVoronoi < 0.5)
                {
                  Asph = _PointSphericalCoords[2].xyz;
                  Bsph = _PointSphericalCoords[3].xyz;

                  colA = _Colors[2];
                  colB = _Colors[3];
                }

                // Distances
                float dA = GetKarlsruheDistance(Xsph, Asph);
                float dB = GetKarlsruheDistance(Xsph, Bsph);
                float F = dA - dB;

                // Project the outer point onto the inner shell
                float3 sphProjectedPoint;
                float4 projectedPointColor;
                if (Asph.x < Bsph.x)
                {
                    sphProjectedPoint = float3(Asph.x, Bsph.y, Bsph.z);
                    projectedPointColor = colB;
                }
                else
                {
                    sphProjectedPoint = float3(Bsph.x, Asph.y, Asph.z);
                    projectedPointColor = colA;
                }
                projectedPointColor.a = 0.8;
                colA.a = 0.8;
                colB.a = 0.8;

                /*
                // Simple bisector, fails at small F
                float4 color;
                if (abs(F) < 0.001) { // on bisector
                    color = float4(1, 0, 0, 0.5); // red
                } else if (dA < dB) {
                    color = colA;
                } else {
                    color = colB;
                }
                */

                // Anti-aliased bisector
                float width = fwidth(F);
                float edge = smoothstep(-width, width, F); // 0..1 around F=0
                // Mix colors smoothly
                float4 color = lerp(colA, colB, edge);
                // Add red near the bisector
                float band = 1.0 - smoothstep(0.0, width * 1.5, abs(F));
                color = lerp(color, float4(1, 0, 0, 0.8), band);


                // Inner shell coloring
                float rInner = min(Asph.x, Bsph.x);
                float3 innerPoint = float3(rInner, Xsph.y, Xsph.z);

                float dA_inner = GetKarlsruheDistance(innerPoint, Asph);
                float dB_inner = GetKarlsruheDistance(innerPoint, Bsph);
                float F_inner = dA_inner - dB_inner;

                /*
                // Simple bisector fails at small F_inner
                float4 colorInner;
                if (abs(F_inner) < 0.001) { // on bisector
                    colorInner = float4(1, 0, 0, 0.5); // red
                } else if (dA_inner < dB_inner) {
                    colorInner = colA;
                } else {
                    colorInner = colB;
                }
                */
                // Anti-aliased bisector
                float width2 = fwidth(F_inner);
                float edge2 = smoothstep(-width2, width2, F_inner);
                // Mix colors smoothly
                float4 colorInner = lerp(colA, colB, edge2);
                // Add red near the bisector
                float band2 = 1.0 - smoothstep(0.0, width2 * 1.5, abs(F_inner));
                colorInner = lerp(colorInner, float4(1, 0, 0, 0.8), band2);

                // Paint inner shell
                float rMin = min(Asph.x, Bsph.x); // inner shell radius
                color = ApplySphereOfRadius(color, i.localPos, rMin, colorInner);

                // Paint the projected point on the inner shell
                float3 markerCenter = SphericalToEuclidean(sphProjectedPoint);
                // Convert to object-space cartesian position
                color = ApplySphereAt(
                    color,
                    i.localPos,
                    _Radius,
                    markerCenter,
                    0.01, // radius of projected point
                    projectedPointColor * 0.7 // Darken the projected color slightly
                );

                return ApplyOriginMarker(color, i.localPos);
            }
            ENDCG
        }
    }
    FallBack Off
}