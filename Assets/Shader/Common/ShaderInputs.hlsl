#ifndef SHADER_INPUT_INCLUDED
#define SHADER_INPUT_INCLUDED

// Inputs from C#

#define MAX_POINTS 64 // Maximum number of points supported in WebGL build. Keep in sync with Utils.cs
#if defined(SHADER_API_D3D11) \
 || defined(SHADER_API_METAL) \
 || defined(SHADER_API_VULKAN) \
 || defined(SHADER_API_GLCORE) \
 || defined(SHADER_API_D3D12)
    // StructuredBuffer with arbitrary size
    // Contains both spherical and cartesian coords
    uniform StructuredBuffer<float4> _PointSphericalCoords; // [(r, theta, phi), ..., (x, y, z), ...]
    uniform StructuredBuffer<float4> _Colors;
    uniform int _PointCount;
    #define POINT_LOOP(k) \
        for (int k = 0; k < _PointCount; k++)
#else
    // WebGL requires fixed-size arrays and for loops with constant bounds MAX_POINTS
    // 2*MAX_POINTS size array because both spherical and cartesian coords
    uniform float4 _PointSphericalCoords[2*MAX_POINTS]; // (r0, theta0, phi0, 0), (r1, theta1, phi1, 0), ..., (x0, y0, z0, 0), (x1, y1, z1, 0), ...
    uniform float4 _Colors[MAX_POINTS];
    uniform float _PointCount; // How many points are actually used
    #define POINT_LOOP(k) \
        [loop] for (int k = 0; k < MAX_POINTS; k++) if (k >= _PointCount) break; else
#endif

uniform float _ShowGrid;
uniform float _Radius;
uniform float _ShowProbesVoronoi; // 0/1 toggle

uniform float3 _LightDirWorld;

// Surface Voronoi controls
uniform float _ShowSurfaceVoronoi; // 0/1 toggle
uniform float _SurfaceVoronoiOpacity;
uniform float _SurfaceBisectorWidthPx; // bisector with in pixels

// Metric controls
uniform float _ClosestDistance;
uniform float _MetricType;
uniform float _MaxDistancePercentage;

// Slice planes
uniform float4 _SlicePlane0; // xyz = unit normal, w = offset
uniform float _BisectorWidthPx0; // AA line width in pixels
uniform float _SliceOpacity0;

uniform float _UseSlice1; // 0/1 toggle
uniform float4 _SlicePlane1;
uniform float _BisectorWidthPx1;
uniform float _SliceOpacity1;

// Bisector surface cut
uniform int _CutSphere; // 0 = no cut, 1 = cut A, 2 = cut B, 3 = cut both

// Single cell boundary
uniform int _CellBoundaryIndex; // index of the point to show the boundary for


// Iso-lines
uniform float _ShowIsoLines; // 0/1 toggle
uniform float _IsoLineStep; // in world units
uniform float _IsoLineThickness; // in [0, 0.5]

#endif