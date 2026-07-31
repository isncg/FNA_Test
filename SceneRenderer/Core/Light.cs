using System;
using Microsoft.Xna.Framework;

namespace SceneRenderer;

public enum LightType : int
{
    Directional = 0,
    Point = 1,
    Spot = 2,
}

/// <summary>Base class for scene lights.</summary>
public abstract class Light
{
    public string Name = "";
    public Vector3 Color = Vector3.One;
    public float Intensity = 1.0f;
    public bool CastsShadows;

    public abstract LightType Type { get; }

    /// <summary>Pack this light into a float[] starting at the given offset.
    /// Each light uses 16 floats (4 float4s).
    /// Layout: [Type, Intensity, Range, CastsShadows?1:0],
    ///         [PosOrDir.x, PosOrDir.y, PosOrDir.z, InnerConeCos],
    ///         [Color*Intensity.r, Color*Intensity.g, Color*Intensity.b, OuterConeCos],
    ///         [SpotDir.x, SpotDir.y, SpotDir.z, Falloff]
    /// </summary>
    public abstract void Pack(float[] buffer, int offset);
}

public class DirectionalLight : Light
{
    public Vector3 Direction = Vector3.Down; // world-space direction TO light

    public override LightType Type => LightType.Directional;

    public override void Pack(float[] buffer, int offset)
    {
        buffer[offset + 0] = (float)LightType.Directional;
        buffer[offset + 1] = Intensity;
        buffer[offset + 2] = 0; // range unused
        buffer[offset + 3] = CastsShadows ? 1f : 0f;

        buffer[offset + 4] = Direction.X;
        buffer[offset + 5] = Direction.Y;
        buffer[offset + 6] = Direction.Z;
        buffer[offset + 7] = 0; // inner cone unused

        buffer[offset + 8] = Color.X * Intensity;
        buffer[offset + 9] = Color.Y * Intensity;
        buffer[offset + 10] = Color.Z * Intensity;
        buffer[offset + 11] = 0; // outer cone unused

        buffer[offset + 12] = 0; // spot dir unused
        buffer[offset + 13] = 0; // spot dir unused
        buffer[offset + 14] = 0; // spot dir unused
        buffer[offset + 15] = 0; // falloff unused
    }
}

public class PointLight : Light
{
    public Vector3 Position;
    public float Radius = 10f;
    public float FalloffExponent = 2f; // 2 = quadratic

    public override LightType Type => LightType.Point;

    public override void Pack(float[] buffer, int offset)
    {
        buffer[offset + 0] = (float)LightType.Point;
        buffer[offset + 1] = Intensity;
        buffer[offset + 2] = Radius;
        buffer[offset + 3] = CastsShadows ? 1f : 0f;

        buffer[offset + 4] = Position.X;
        buffer[offset + 5] = Position.Y;
        buffer[offset + 6] = Position.Z;
        buffer[offset + 7] = 0; // inner cone unused

        buffer[offset + 8] = Color.X * Intensity;
        buffer[offset + 9] = Color.Y * Intensity;
        buffer[offset + 10] = Color.Z * Intensity;
        buffer[offset + 11] = 0; // outer cone unused

        buffer[offset + 12] = 0; // spot dir unused
        buffer[offset + 13] = 0; // spot dir unused
        buffer[offset + 14] = 0; // spot dir unused
        buffer[offset + 15] = FalloffExponent;
    }
}

public class SpotLight : Light
{
    public Vector3 Position;
    public Vector3 Direction = Vector3.Down;
    public float Range = 20f;
    public float InnerConeAngle = 0.3f; // radians
    public float OuterConeAngle = 0.6f;

    public override LightType Type => LightType.Spot;

    public override void Pack(float[] buffer, int offset)
    {
        // Pre-compute cos(angle) for shader smoothstep
        float innerCos = MathF.Cos(InnerConeAngle);
        float outerCos = MathF.Cos(OuterConeAngle);

        buffer[offset + 0] = (float)LightType.Spot;
        buffer[offset + 1] = Intensity;
        buffer[offset + 2] = Range;
        buffer[offset + 3] = CastsShadows ? 1f : 0f;

        buffer[offset + 4] = Position.X;
        buffer[offset + 5] = Position.Y;
        buffer[offset + 6] = Position.Z;
        buffer[offset + 7] = innerCos;

        buffer[offset + 8] = Color.X * Intensity;
        buffer[offset + 9] = Color.Y * Intensity;
        buffer[offset + 10] = Color.Z * Intensity;
        buffer[offset + 11] = outerCos;

        buffer[offset + 12] = Direction.X;
        buffer[offset + 13] = Direction.Y;
        buffer[offset + 14] = Direction.Z;
        buffer[offset + 15] = 2f; // quadratic falloff
    }
}
