using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace SceneRenderer;

/// <summary>CPU-side light culling: frustum + distance checks.</summary>
public static class LightCuller
{
    public const int MAX_LIGHTS = 16;

    /// <summary>Cull lights against camera frustum. Directional lights always pass.</summary>
    public static List<Light> CullLights(List<Light> allLights, BoundingFrustum frustum)
    {
        var active = new List<Light>(Math.Min(allLights.Count, MAX_LIGHTS));

        foreach (var light in allLights)
        {
            if (active.Count >= MAX_LIGHTS)
                break;

            if (light is DirectionalLight)
            {
                active.Add(light);
            }
            else if (light is PointLight pl)
            {
                var sphere = new BoundingSphere(pl.Position, pl.Radius);
                if (frustum.Contains(sphere) != ContainmentType.Disjoint)
                    active.Add(light);
            }
            else if (light is SpotLight sl)
            {
                var sphere = new BoundingSphere(sl.Position, sl.Range);
                if (frustum.Contains(sphere) != ContainmentType.Disjoint)
                    active.Add(light);
            }
        }

        return active;
    }

    /// <summary>Pack culled lights into a float array for the uniform buffer.
    /// Each light uses 16 floats (4 float4s). Unused slots are zeroed.</summary>
    public static float[] PackLightData(List<Light> culledLights)
    {
        var buffer = new float[MAX_LIGHTS * 16];

        for (int i = 0; i < culledLights.Count && i < MAX_LIGHTS; i++)
        {
            culledLights[i].Pack(buffer, i * 16);
        }

        return buffer;
    }

    /// <summary>Set light data into Effect parameters. Each light uses 4 float4 parameters.
    /// The FEB format doesn't track array counts, so we use SetValue(float[])
    /// to write all 64 float4s sequentially into the constant buffer.</summary>
    public static void SetLightParameters(Effect effect, float[] lightBuffer, int numLights)
    {
        effect.Parameters["NumActiveLights"].SetValue((float)numLights);

        // LightData is declared as FLOAT4 with count=64 in the FEB manifest, but
        // the FEB format doesn't support array element counts (elementCount=0).
        // Use SetValue(float[]) to write all float4s sequentially —
        // it advances by 16 bytes per float4, matching the constant buffer layout.
        effect.Parameters["LightData"].SetValue(lightBuffer);
    }
}
