using UnityEngine;
using UnityEngine.Rendering;

public class Utils
{
    public static readonly int MAX_POINTS = 64; // Keep in sync with shader defines in "ShaderInputs.hlsl"

    public static bool CanUseStructuredBuffers()
    {
        var t = SystemInfo.graphicsDeviceType;
        return
            t == GraphicsDeviceType.Direct3D11 ||
            t == GraphicsDeviceType.Direct3D12 ||
            t == GraphicsDeviceType.Metal ||
            t == GraphicsDeviceType.Vulkan ||
            t == GraphicsDeviceType.OpenGLCore;
    }

   public static bool Is2DShader(Shader shader)
    {
        return shader != null && shader.name.Contains("2D");
    }
}