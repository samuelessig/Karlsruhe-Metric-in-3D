#ifndef ALGORITHMS_INCLUDED
#define ALGORITHMS_INCLUDED


#ifndef EPS_SPH_IS_ORIGIN
// If we are close to the origin (in spherical coords),
// we consider the point to be the origin if abs(radius) <= EPS_SPH_IS_ORIGIN
// This avoids numerical issues with undefined theta/phi at the origin as well as expensive trig-function calls
#define EPS_SPH_IS_ORIGIN 1e-8
#define COS_TWO -0.4161468365471424
#endif


/*
// Convert spherical coordinates to a unit direction
static inline float3 SphericalDirection(float theta, float phi)
{
    float sinPhi = sin(phi);
    float x = sinPhi * cos(theta);
    float y = sinPhi * sin(theta);
    float z = cos(phi);
    return float3(x, y, z);
}


static inline float3 SphericalToEuclidean(float3 sph)
{
    float r     = sph.x; // (r, theta, phi)
    float theta = sph.y;  // theta € [0, 2PI) - azimuth
    float phi   = sph.z;  // phi € [0, PI] - polar angle

    float sinPhi = sin(phi);
    float x = r * sinPhi * cos(theta);
    float y = r * sinPhi * sin(theta);
    float z = r * cos(phi);

    return float3(x, y, z);
}
*/

// Unit direction from spherical coordinates
// Optimized version computing sin/cos pairs together
static inline float3 SphericalDirection(float theta, float phi)
{
    float sPhi, cPhi;
    float sTh, cTh;

    sincos(phi, sPhi, cPhi);
    sincos(theta, sTh, cTh);

    return float3(sPhi * cTh, // x
                  sPhi * sTh, // y
                  cPhi); // z
}


static inline float3 SphericalToEuclidean(float3 sph) // (r, theta, phi)
{
    float r = sph.x;
    float theta = sph.y; // theta € [0, 2PI) - azimuth
    float phi = sph.z; // phi € [0, PI] - polar angle

    // If r~=0, assume we are on the origin and avoid the trig-functions
    if (abs(r) <= EPS_SPH_IS_ORIGIN)
        return float3(0,0,0);

    return r * SphericalDirection(theta, phi);
}


static inline float3 EuclideanToSpherical(float3 euclidean)
{
    float radius = length(euclidean);
    float theta = atan2(euclidean.y, euclidean.x);
    float phi = acos(euclidean.z / radius);

    return float3(radius, theta, phi);
}

// Wrap an angle to (-PI, PI]
static inline float WrapPi(float a)
{
    const float TWO_PI = 2.0 * UNITY_PI;

    a = fmod(a + UNITY_PI, TWO_PI);
    if (a < 0.0) a += TWO_PI;
    return a - UNITY_PI;
}

// Absolute azimuth difference with wrap around
static inline float AngleDiff(float a, float b)
{
    return abs(WrapPi(a - b));
}

static inline float AngularDistance(float3 sphA, float3 sphB)
{
    // float3: (r, theta, phi)
    // theta € [0, 2PI) - azimuth
    // phi € [0, PI] - polar angle

    float thetaA = sphA.y;
    float phiA = sphA.z;
    float thetaB = sphB.y;
    float phiB = sphB.z;

    float deltaTheta = AngleDiff(thetaA, thetaB);

    float cosDelta =
        sin(phiA) * sin(phiB) * cos(deltaTheta) +
        cos(phiA) * cos(phiB);

    // avoid small floating-point errors
    cosDelta = clamp(cosDelta, -1.0, 1.0);

    return acos(cosDelta); // angle in radians
}

static inline float GetKarlsruheDistance(float3 X, float3 S)
{
    // float3: (r, theta, phi)
    float rX = X.x;
    float rS = S.x;

    // 1) Path via origin
    float d_origin = rX + rS;

    // 2) Path via a spherical shell at radius r_min
    float ang = AngularDistance(X, S); // [0, PI]
    float r_min = min(rX, rS);
    float d_shell = abs(rX - rS) + r_min * ang;

    // Take the min of both paths
    return min(d_origin, d_shell);
}

// Faster Karlsruhe distance using both spherical and cartesian coords.
// Both coordinates are generated in SphereGenerator.cs on CPU anyway,
// so we can avoid repeated conversions with trigonometry functions on GPU.
// We can avoid acos() unless angle < 2 rad (i.e. when cos > cos2)
// acos() function can be very slow on GPU, ~10x slower than cos()
// This concept is described here:
// https://seblagarde.wordpress.com/2014/12/01/inverse-trigonometric-functions-gpu-optimization-for-amd-gcn-architecture/
// This function runs ~2x faster in practice on an Intel integrated GPU
// compared to the regular GetKarlsruheDistance version above.
// Asph, Bsph : (r, theta, phi), (r, theta, phi)
// Acart, Bcart : (x,y,z), (x,y,z)
static inline float GetKarlsruheDistance_SphCart(
    float3 Asph, float3 Bsph,
    float3 Acart, float3 Bcart)
{
    float rA = Asph.x;
    float rB = Bsph.x;

    float dOrigin = rA + rB;

    // We are too close to the origin
    // angles are numerically meaningless, return origin path
    if (rA <= EPS_SPH_IS_ORIGIN || rB <= EPS_SPH_IS_ORIGIN)
        return dOrigin;

    // Normalize directions with radius, avoid divisions
    float3 Adir = Acart * rcp(rA);
    float3 Bdir = Bcart * rcp(rB);

    // Decide if shell path can even compete using angle > 2 rad
    // We compare the cosines to avoid computing acos if we don't have to
    // We take the unit directions of the points.
    // We have cos(angle) = dot(Adir, Bdir)
    // angle > 2 <=> cos(angle) < cos(2)
    float cosine = dot(Adir, Bdir);
    if (cosine <= COS_TWO) {
        return dOrigin; // If angle >= 2 rad -> return origin directly and avoid acos path
    }

    // Otherwise compute the shell path properly
    float ang = acos(clamp(cosine, -1.0, 1.0));
    float rmin = min(rA, rB);
    float dShell = abs(rA - rB) + rmin * ang;

    // return the best path, have to check both again as cosine test was only a upper bound
    return min(dOrigin, dShell);
}

// Takes point as both cartesian and spherical to get best speed depending
// on which metric is used, spherical coords may only be set for Karlsruhe metric
static inline float DistPToX(float metric, float3 Psph, float3 Pcart, float3 Xsph, float3 Xcart)
{
    if (metric == METRIC_KARLSRUHE)
    {
        return GetKarlsruheDistance_SphCart(Psph, Xsph, Pcart, Xcart);
    }
    else
    {
        return length(Pcart - Xcart);
    }
}

// Compute Euclidean distance between two points stored in spherical coordinates (r, theta, phi)
static inline float GetEuclideanDistance(float3 sphA, float3 sphB)
{
    float3 A = SphericalToEuclidean(sphA);
    float3 B = SphericalToEuclidean(sphB);
    return length(A - B);
}

float3 FromMercatorProjectionToSpherical(float3 localPos, float scale, float radius)
{
    float x = (localPos.x + (scale * 0.25)) / (scale * 0.5);
    float theta = x * (2.0 * UNITY_PI); // Longitude

    float y = localPos.z;
    float latitude = 2.0 * atan(exp(y)) - UNITY_PI * 0.5;
    float phi = UNITY_PI * 0.5 - latitude; // Polar angle

    return float3(radius, theta, phi);
}


float3 FromAzimuthalProjectionToSpherical(float3 localPos, bool isNorthCenter, float radius)
{
    float x = localPos.x * 2;
    float z = localPos.z * 2;

    float rProj = sqrt(x * x + z * z);
    float theta = atan2(z, x);
    float phi = UNITY_PI - asin(rProj);

    if (!isNorthCenter)
    {
        theta = 2 * UNITY_PI - theta;
    }
    else
    {
        phi = UNITY_PI - phi;
    }

    return float3(radius, theta, phi);
}


static inline float4 applyGrid(bool showGrid, float3 sphericalWorldPos, float4 color)
{
    if (showGrid)
    {
        bool isGrid = false;
        if (
            (round(degrees(sphericalWorldPos.y) * 2) / 2) % 90 == 0 ||
            (round(degrees(sphericalWorldPos.y) * 4) / 4) % 30 == 0 ||
            (round(degrees(sphericalWorldPos.y) * 6) / 6) % 10 == 0
        )
        {
            isGrid = true;
        }
        if (
            (round(degrees(sphericalWorldPos.z) * 2) / 2) % 90 == 0 ||
            (round(degrees(sphericalWorldPos.z) * 4) / 4) % 30 == 0 ||
            (round(degrees(sphericalWorldPos.z) * 6) / 6) % 10 == 0
        )
        {
            isGrid = true;
        }
        if (isGrid)
        {
            if (color.x == 0 && color.y == 0 && color.z == 0)
            {
                return float4(1, 1, 1, 1);
            }
            else
            {
                return float4(0, 0, 0, 1);
            }
        }
    }
    return color;
}


// evaluate grid on the sphere surfac
static inline float4 GridOnSurface(bool showGrid, float radius, float3 localPos)
{
    float3 surfOS = normalize(localPos) * radius;
    float3 sph = EuclideanToSpherical(surfOS);
    return applyGrid(showGrid, sph, float4(0, 0, 0, 0)); // returns lines over transparent
}


// Solve ray-sphere intersection: |ro + t rd| = R
// Return (tEnter, tExit, hitMask)
void RaySphere(float3 ro, float3 rd, float R, out float t0, out float t1, out bool hit)
{
    float b = dot(ro, rd);
    float c = dot(ro, ro) - R * R;
    float disc = b * b - c;
    hit = (disc >= 0.0);
    if (!hit)
    {
        t0 = t1 = 0.0;
        return;
    }

    float s = sqrt(max(disc, 0.0));
    // geometric roots for |ro + t rd| = R => t = -b -+ s
    float tNear = -b - s;
    float tFar = -b + s;

    // order
    t0 = min(tNear, tFar);
    t1 = max(tNear, tFar);
}


// Ray-plane intersection: Returns whether intersection occurs, and outputs the position t
bool RayPlane(float3 ro, float3 rd, float3 n, float w, out float t)
{
    float denom = dot(n, rd);
    if (abs(denom) < 1e-6)
    {
        t = 0.0;
        return false;
    }
    t = (w - dot(n, ro)) / denom;
    return true;
}

void ArgMin2(float d, int idx, inout float best, inout int bestId, inout float second, inout int secondId)
{
    if (d < best)
    {
        second = best;
        secondId = bestId;

        best = d;
        bestId = idx;
    }
    else if (d < second)
    {
        second = d;
        secondId = idx;
    }
}


// Sphere centered on origin
static inline float4 ApplySphereOfRadius(float4 color, float3 localPos, float radius, float4 shellCol)
{
    // Camera ray
    float3 ro = mul(unity_WorldToObject, float4(_WorldSpaceCameraPos, 1)).xyz; // to local space
    float3 rd = normalize(localPos - ro);

    // clip the ray to the outer sphere
    float R = length(localPos);
    float tEnter, tExit;
    bool hitOuter;
    RaySphere(ro, rd, R, tEnter, tExit, hitOuter);
    if (!hitOuter)
    {
        // no outer sphere hit
        return color;
    }
    tEnter = max(tEnter, 0.0);

    // intersect the inner shell (sphere of radius)
    float si0, si1;
    bool hitInner = false;
    if (radius > 1e-5)
    {
        RaySphere(ro, rd, radius, si0, si1, hitInner);
        if (hitInner)
        {
            // choose the first intersection that lies inside
            float tHit = -1.0;
            if (si0 >= tEnter && si0 <= tExit) tHit = si0;
            else if (si1 >= tEnter && si1 <= tExit) tHit = si1;

            if (tHit > 0.0)
            {
                float3 Xhit = ro + rd * tHit; // point on inner shell
                float3 Nhit = normalize(Xhit); // normal

                // shading for inner sphere
                float facing = saturate(dot(Nhit, -rd)); // brighter when facing camera
                shellCol.rgb *= lerp(0.6, 1.0, facing);

                // overlay the shell on top of normal surface color
                color.rgb = lerp(color.rgb, shellCol.rgb, shellCol.a);
                color.a = max(color.a, shellCol.a);
            }
        }
    }
    return color;
}


static inline float4 ApplyOriginMarker(float4 color, float3 localPos)
{
    float r = 0.008; // radius of marker
    float4 shellCol = float4(0, 0, 0, 0.7); // black semi-transparent

    return ApplySphereOfRadius(color, localPos, r, shellCol);
}


// Draw a shaded marker sphere anywhere inside the parent sphere
static inline float4 ApplySphereAt(
    float4 color,
    float3 localPosOS, // pos of current fragment
    float outerRadius, // radius of outer sphere
    float3 markerCenter, // center of marker sphere
    float markerRadius, // radius of marker sphere
    float4 markerCol // color of marker sphere
)
{
    if (markerCol.a <= 0 || markerRadius <= 0) return color;

    // Camera to object space
    float3 ro = mul(unity_WorldToObject, float4(_WorldSpaceCameraPos, 1)).xyz;
    float3 rd = normalize(localPosOS - ro);

    // Outer sphere intersection
    float tEnterOuter, tExitOuter;
    bool hitOuter;
    RaySphere(ro, rd, outerRadius, tEnterOuter, tExitOuter, hitOuter);
    if (!hitOuter) return color;

    tEnterOuter = max(tEnterOuter, 0.0);

    // Marker sphere intersection: translate origin into markerCenter
    float si0, si1;
    bool hitMarker;
    float3 roMarker = ro - markerCenter;
    RaySphere(roMarker, rd, markerRadius, si0, si1, hitMarker);
    if (!hitMarker) return color;

    // Hit within the clipped outer interval
    float tHit = -1.0;
    if (si0 >= tEnterOuter && si0 <= tExitOuter) tHit = si0;
    else if (si1 >= tEnterOuter && si1 <= tExitOuter) tHit = si1;
    if (tHit < 0.0) return color;

    // Shading
    float3 Xhit = ro + rd * tHit;
    float3 Nhit = normalize(Xhit - markerCenter);
    float facing = saturate(dot(Nhit, -rd));
    float3 lit = lerp(0.6, 1.0, facing);

    float3 finalRGB = markerCol.rgb * lit;
    color.rgb = lerp(color.rgb, finalRGB, markerCol.a);
    color.a = max(color.a, markerCol.a);
    return color;
}


#endif
