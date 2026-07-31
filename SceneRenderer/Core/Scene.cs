using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace SceneRenderer;

/// <summary>Container for all scene data: objects, lights, materials, IBL.</summary>
public class Scene
{
    public List<SceneObject> Objects = new();
    public List<Light> Lights = new();

    public DirectionalLight? SunLight;

    public Vector3 AmbientLight = new(0.03f, 0.03f, 0.05f);
    public float EnvIntensity = 1.0f;

    // IBL textures (set from outside after precompute)
    public Texture2D? EnvMap;
    public Texture2D? IrradianceMap;
    public Texture2D? PrefilteredEnvMap;
    public Texture2D? BrdfLut;

    // Material palette
    public List<Material> MaterialPalette = new();

    // Default textures for fallback
    public Texture2D? DefaultWhite;
    public Texture2D? DefaultNormal; // (128, 128, 255) = flat normal
    public Texture2D? DefaultORM;   // (255, 128, 0) = AO=1, Roughness=0.5, Metallic=0

    /// <summary>Frustum-cull objects against the camera frustum.</summary>
    public List<SceneObject> GetVisibleObjects(BoundingFrustum frustum)
    {
        var visible = new List<SceneObject>(Objects.Count);
        foreach (var obj in Objects)
        {
            if (obj.Mesh == null) continue;
            if (frustum.Contains(obj.WorldBounds) != ContainmentType.Disjoint)
                visible.Add(obj);
        }
        return visible;
    }
}
