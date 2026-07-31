using Microsoft.Xna.Framework.Graphics;

namespace SceneRenderer;

/// <summary>Interface for all render passes in the pipeline.</summary>
public interface IRenderPass
{
    string Name { get; }
    void Initialize(GraphicsDevice device, int width, int height);
    void Resize(int width, int height);
    void Execute(RenderContext ctx);
    void Dispose();

    /// <summary>Debug output texture (may be null if pass has no RT).</summary>
    Texture2D? DebugOutput { get; }
}
