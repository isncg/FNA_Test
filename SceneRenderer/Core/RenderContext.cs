using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace SceneRenderer;

/// <summary>Per-frame state passed between render passes.</summary>
public class RenderContext
{
    public GraphicsDevice GraphicsDevice = null!;
    public SceneCamera Camera = null!;
    public Scene Scene = null!;
    public List<SceneObject> VisibleObjects = null!;

    // Shared resources populated by prior passes
    public RenderTarget2D? GBufferRT0;
    public RenderTarget2D? GBufferRT1;
    public RenderTarget2D? GBufferRT2;
    public RenderTarget2D? ShadowMap;
    public RenderTarget2D? SSAORT;
    public RenderTarget2D? SSAOBlurRT;
    public RenderTarget2D? SSRRT;
    public RenderTarget2D? HdrSceneRT;

    // Bloom output (from BloomPass for TonemapPass)
    public RenderTarget2D? BloomRT;

    public ResourcePool Resources = null!;

    // Previous frame ViewProjection for motion vectors
    public Matrix PrevViewProj = Matrix.Identity;

    // Shadow mapping
    public Matrix LightViewProj;

    // Resolution
    public int Width;
    public int Height;
}
