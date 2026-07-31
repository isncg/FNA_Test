using Microsoft.Xna.Framework;

namespace SceneRenderer;

/// <summary>A renderable object in the scene: mesh + material + transform.</summary>
public class SceneObject
{
    public string Name = "Unnamed";
    public Mesh? Mesh;
    public Material? Material;

    public Vector3 Position;
    public Quaternion Rotation = Quaternion.Identity;
    public Vector3 Scale = Vector3.One;

    public Matrix LocalTransform
        => Matrix.CreateScale(Scale)
         * Matrix.CreateFromQuaternion(Rotation)
         * Matrix.CreateTranslation(Position);

    public BoundingSphere WorldBounds
    {
        get
        {
            if (Mesh == null) return new BoundingSphere(Position, 1f);
            var bounds = Mesh.Bounds;
            bounds.Center += Position;
            // Scale the radius by the max scale component (approximate)
            float maxScale = MathHelper.Max(MathHelper.Max(Scale.X, Scale.Y), Scale.Z);
            bounds.Radius *= maxScale;
            return bounds;
        }
    }
}
