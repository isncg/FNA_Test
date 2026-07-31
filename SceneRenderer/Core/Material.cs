using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace SceneRenderer;

/// <summary>PBR material using metallic-roughness workflow.</summary>
public class Material
{
    public string Name = "Unnamed";
    public Texture2D? AlbedoMap;
    public Texture2D? NormalMap;
    public Texture2D? ORMMap;  // R=AO, G=Roughness, B=Metallic (Poly Haven packed convention)

    public Vector3 AlbedoTint = Vector3.One;
    public float MetallicScale = 1.0f;
    public float RoughnessScale = 1.0f;
}
