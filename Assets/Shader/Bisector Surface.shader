Shader "Custom/Bisector Surface"
{
    /*
    This shader shows the bisector surface between two points.
    It always uses the Karlsruhe metric for distance computations, regardless of the global metric setting.
    The number of sample points along the camera ray inside the sphere can be adjusted via the STEPS define.
    For higher screen resolutions, increase STEPS for better quality. 100 steps is sufficient for 1080p resolution.

    The ray marching algorithm looks for the root of the function F(X) = dK(X,A) - dK(X,B),
    where dK is the Karlsruhe distance and A,B are the two points. The bisector surface is defined by F(X) = 0.
    To find the root, we look for sign changes of F(X) along the ray and refine the root with a single secant step.
    The point is then colored with Phong shading. The gradient is perpendicular to the implicit surface at F(X)=0.
    We estimate the gradient via central differences.

    Since the root finder searches for a sign change, i.e. a bisector surface, it fails when the bisector
    is a "thick" region rather than a surface. This can happen when the two points have the same radius. In this case,
    the ray marcher may or may not find a sign change depending on numerical inaccuracies. The gradient will be close to
    zero in this case, and shading will look strange as well.
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
            #pragma vertex   vert
            #pragma fragment frag

            #include "UnityCG.cginc"
            #include "Common/Metrics.hlsl"
            #include "Common/Algorithms.hlsl"
            #include "Common/ShaderInputs.hlsl"

            // surface bisector band thickness in screen pixels
            #define BIS_BAND_PX 1.75

            // visiblity parameters for the surface bisector
            #define GLOW_STRENGTH 0.45

            // number of steps along the camera ray inside the sphere
            #define STEPS 140

            // small epsilon step (as a fraction of sphere radius) to estimate the gradient of F
            // the gradient gives the surface normal of F=0, necessary for phong shading
            #define EPS_GRAD 0.003

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

            // ray-cast from the camera through the current fragment into the sphere
            struct Ray
            {
                float3 o; // Origin of the ray
                float3 d; // Direction of the ray
            };

            // Ray–sphere intersection
            bool RaySphereHit(Ray ray, float R, out float t0, out float t1)
            {
                bool hit;
                RaySphere(ray.o, ray.d, R, t0, t1, hit);
                if (!hit)
                    return false;

                // Return true only if there is a positive intersection (in front of camera)
                return (t1 > 0.0);
            }

            // Returns cartesian (x,y,z,0) for point i
            // See SphereGenerator.cs for data layout
            static inline float3 GetPointCartesian(int i)
            {
                return _PointSphericalCoords[i + _PointCount].xyz;
            }

            // Function F(X) = dK(X, A) − dK(X, B), where dK is the Karlsruhe metric.
            // The bisector is the root F(X) = 0 meaning X has same distance to A and B.
            float F(float3 Xos, float3 Asph, float3 Bsph, float3 Acart, float3 Bcart)
            {
                float3 Xsph = 0;
                if (_MetricType == METRIC_KARLSRUHE) {
                    Xsph = EuclideanToSpherical(Xos);
                }
                float dA = DistPToX(_MetricType, Asph, Acart, Xsph, Xos);
                float dB = DistPToX(_MetricType, Bsph, Bcart, Xsph, Xos);
                return dA - dB;
            }

            // Anti-aliased (smoothstep) line mask along a scalar coordinate t with period 1.
            float LineAA(float t, float pxWidth, float halfLinePx)
            {
                float g = abs(frac(t) - 0.5);
                float w = max(1e-6, pxWidth);
                float halfParam = halfLinePx * w; // convert pixel width to param width
                float dist = 0.5 - g; // param-space distance to line
                return smoothstep(halfParam, 0.0, dist);
            }

            fixed4 frag(v2f i) : SV_Target
            {
                 // Camera in object space
                float3 camOS = mul(unity_WorldToObject, float4(_WorldSpaceCameraPos, 1)).xyz;

                float3 Asph = _PointSphericalCoords[0].xyz;
                float3 Bsph = _PointSphericalCoords[1].xyz;
                float3 Acart = GetPointCartesian(0);
                float3 Bcart = GetPointCartesian(1);

                if (_ShowProbesVoronoi < 0.5 && _PointCount > 3.5)
                {
                    Asph = _PointSphericalCoords[2].xyz;
                    Bsph = _PointSphericalCoords[3].xyz;
                    Acart = GetPointCartesian(2);
                    Bcart = GetPointCartesian(3);
                }

                // Use fixed colors instead of the random colors
                float4 colA = float4(0.8, 0.7, 0.1, 1); // yellow
                float4 colB = float4(0.0, 0.4, 0.7, 1); // blue

                // At each surface point, compute F(X) = dK(X,A) - dK(X,B)
                float F_s = F(i.localPos, Asph, Bsph, Acart, Bcart);

                bool isA_side = F_s < 0;

                // Determine which sides of the sphere to show based on _CutSphere
                // defined in ShaderInputs.hlsl: 0 = no cut, 1 = cut A, 2 = cut B, 3 = cut both
                bool cutA_side = (_CutSphere == 2.0 || _CutSphere == 3.0);
                bool cutB_side = (_CutSphere == 1.0 || _CutSphere == 3.0);
                bool culledSide = (isA_side && cutA_side) || (!isA_side && cutB_side);

                // Base color for sphere surface
                float4 color;
                if (culledSide)
                {
                    // On a removed side: sphere shell invisible
                    color = float4(0, 0, 0, 0);
                }
                else
                {
                    color = isA_side ? colA : colB;
                    color.a = 0.7;
                }

                // Surface bisector band on the outer sphere
                if (!culledSide)
                {
                    float w = max(1e-6, fwidth(F_s));
                    float edge = smoothstep(BIS_BAND_PX * w, 0.0, abs(F_s));
                    float3 bisColor = float3(1.0, 0.2, 0.2);
                    color.rgb = lerp(color.rgb, bisColor, edge);
                    color.a = saturate(color.a + edge * 0.25);
                }

                // Find implicit surface F(X)=0 inside the sphere
                float3 dirOS = normalize(i.localPos - camOS);
                Ray ray;
                ray.o = camOS;
                ray.d = dirOS;

                float t0, t1;
                if (RaySphereHit(ray, _Radius, t0, t1))
                {
                    t0 = max(t0, 0.0);
                    float t = t0;
                    float dt = (t1 - t0) / (float) STEPS;

                    float prevF = 0.0;
                    bool hasPrev = false;
                    float hitT = -1.0;

                    // To find the root of F(X), we look for a sign change along the ray
                    // Once we have the interval where the sign changed, we refine the
                    // root with a single secant refinement step.
                    [loop]
                    for (int s = 0; s < STEPS; ++s)
                    {
                        float3 X = ray.o + t * ray.d;
                        float f = F(X, Asph, Bsph, Acart, Bcart);

                        if (hasPrev && sign(f) != sign(prevF))
                        {
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

                    if (hitT > 0.0)
                    {
                        // Intersection point on the implicit surface
                        float3 X = ray.o + hitT * ray.d;

                        // Surface normal via gradient (central differences)
                        // https://computergraphics.stackexchange.com/a/8097
                        // https://en.wikipedia.org/wiki/Normal_(geometry)#Normal_to_general_surfaces_in_3D_space
                        float eps = EPS_GRAD * _Radius;
                        float3 ex = float3(eps, 0, 0);
                        float3 ey = float3(0, eps, 0);
                        float3 ez = float3(0, 0, eps);

                        float fx = F(X + ex, Asph, Bsph, Acart, Bcart) - F(X - ex, Asph, Bsph, Acart, Bcart);
                        float fy = F(X + ey, Asph, Bsph, Acart, Bcart) - F(X - ey, Asph, Bsph, Acart, Bcart);
                        float fz = F(X + ez, Asph, Bsph, Acart, Bcart) - F(X - ez, Asph, Bsph, Acart, Bcart);

                        float3 gradF = float3(fx, fy, fz);
                        float3 N = normalize(gradF);

                        // Phong shading
                        float shininess = 30.0;
                        float specular = 0.4;

                        // View direction
                        float3 V = normalize(camOS - X);

                        // Light direction (headlight-style)
                        float3 L = normalize(normalize(V) + 0.6 * normalize(float3(0.4, 0.7, 0.2)));

                        // Diffuse
                        float diffuse = saturate(dot(N, L));

                        // Specular
                        float3 R = normalize(reflect(-L, N));
                        float spec = pow(max(dot(R, V), 0.0), shininess);

                        // mix ambient, diffuse, specular mix
                        float3 diffuseColor = lerp(float3(0.7, 0.08, 0.08), float3(0.9, 0.3, 0.3), diffuse);
                        float3 ambientColor = 0.15 * diffuseColor; 
                        float3 specularColor = specular * spec * float3(1.0, 1.0, 1.0); // white spec highlight
                        float3 shadedCol = ambientColor + diffuseColor + specularColor;
                        shadedCol = saturate(shadedCol);

                        if (culledSide)
                        {
                            // On the removed side
                            color.rgb = shadedCol;
                            color.a = 0.85;
                        }
                        else
                        {
                            // On the kept side,  glowing overlay
                            color.rgb += shadedCol * GLOW_STRENGTH;
                            color.a = saturate(color.a + 0.12 * GLOW_STRENGTH);
                        }
                    }
                }
                return color;
            }
            ENDCG
        }
    }
    FallBack "Diffuse"
}
