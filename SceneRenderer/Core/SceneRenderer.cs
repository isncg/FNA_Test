using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace SceneRenderer;

/// <summary>Main orchestrator: owns render passes and drives the frame pipeline.</summary>
public class SceneRendererEngine : IDisposable
{
    private readonly GraphicsDevice _device;
    private readonly List<IRenderPass> _passes;
    private readonly ResourcePool _resources;
    private int _width, _height;

    public GBufferPass GBuffer { get; }
    public ShadowMapPass ShadowMap { get; }
    public SSAOPass SSAO { get; }
    public BlurAOPass BlurAO { get; }
    public SSRPass SSR { get; }
    public DeferredLightingPass DeferredLighting { get; }
    public SkyboxPass Skybox { get; }
    public BloomPass Bloom { get; }
    public TonemapPass Tonemap { get; }

    public SceneRendererEngine(GraphicsDevice device, int width, int height)
    {
        _device = device;
        _width = width;
        _height = height;
        _resources = new ResourcePool(device);

        GBuffer = new GBufferPass();
        ShadowMap = new ShadowMapPass();
        SSAO = new SSAOPass();
        BlurAO = new BlurAOPass();
        SSR = new SSRPass();
        DeferredLighting = new DeferredLightingPass();
        Skybox = new SkyboxPass();
        Bloom = new BloomPass();
        Tonemap = new TonemapPass();

        _passes = new List<IRenderPass>
        {
            ShadowMap,
            GBuffer,
            SSAO,
            BlurAO,
            SSR,
            DeferredLighting,
            Skybox,
            Bloom,
            Tonemap,
        };

        foreach (var pass in _passes)
            pass.Initialize(device, width, height);
    }

    private Matrix _prevViewProj = Matrix.Identity;
    private bool _firstFrame = true;

    /// <summary>Last frame's render context, for debug inspection of GBuffer RTs.</summary>
    public RenderContext? LastContext { get; private set; }

    public void Render(Scene scene, SceneCamera camera)
    {
        // Frustum cull objects once per frame
        var visible = scene.GetVisibleObjects(camera.Frustum);

        var currentViewProj = camera.ViewMatrix * camera.ProjectionMatrix;

        var ctx = new RenderContext
        {
            GraphicsDevice = _device,
            Camera = camera,
            Scene = scene,
            VisibleObjects = visible,
            Resources = _resources,
            Width = _width,
            Height = _height,
            PrevViewProj = _firstFrame ? currentViewProj : _prevViewProj,
        };

        // Execute passes in order
        // Pass 1: Shadow Map
        ShadowMap.Execute(ctx);

        // Restore rasterizer after shadow pass
        _device.RasterizerState = RasterizerState.CullCounterClockwise;

        // Pass 2: GBuffer
        GBuffer.Execute(ctx);

        // Pass 3-4: SSAO + Blur
        if (SSAO.Enabled)
        {
            SSAO.Execute(ctx);
            BlurAO.Execute(ctx);
        }

        // Pass 5: SSR
        if (SSR.Enabled)
        {
            SSR.Execute(ctx);
        }

        // Pass 6: Deferred Lighting
        DeferredLighting.Execute(ctx);

        // Pass 7: Skybox (blended into HDR scene RT)
        if (Skybox.Enabled)
            Skybox.Execute(ctx);

        // Pass 8: Bloom
        if (Bloom.Enabled)
        {
            Bloom.Execute(ctx);
            // Route bloom output directly to the tonemap pass
            ctx.BloomRT = Bloom.DebugOutput as RenderTarget2D;
        }
        else
        {
            ctx.BloomRT = null;
        }

        // Pass 9: Tonemap → backbuffer
        Tonemap.Execute(ctx);

        // Store current ViewProj for next frame's motion vectors
        _prevViewProj = currentViewProj;
        _firstFrame = false;

        // Save context for debug inspection
        LastContext = ctx;
    }

    public void Resize(int width, int height)
    {
        _width = width;
        _height = height;
        foreach (var pass in _passes)
            pass.Resize(width, height);
    }

    public List<IRenderPass> GetPasses() => _passes;

    public void Dispose()
    {
        foreach (var pass in _passes)
            pass.Dispose();
        _resources.Dispose();
    }
}
