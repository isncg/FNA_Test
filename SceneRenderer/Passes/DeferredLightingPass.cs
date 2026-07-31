using System;
using System.IO;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace SceneRenderer;

/// <summary>Deferred lighting pass: fullscreen shader sampling GBuffer, IBL, SSAO, SSR, shadows.</summary>
public class DeferredLightingPass : IRenderPass
{
    private GraphicsDevice _device = null!;
    private Effect _effect = null!;
    private RenderTarget2D _hdrRT = null!;
    private int _width, _height;

    // Fallback textures
    private Texture2D _whiteTexR32F = null!;
    private Texture2D _blackTexHalf4 = null!;

    public string Name => "DeferredLighting";
    public Texture2D? DebugOutput => _hdrRT;
    public bool DebugAlbedo; // when true, overrides EnvIntensity=0 to trigger albedo diagnostic

    /// <summary>
    /// Shared depth-stencil buffer injected by SceneRendererEngine before
    /// Initialize/Resize. Attaching it to the HDR target lets the Skybox pass
    /// depth-test against the GBuffer depth. When null the HDR target has no
    /// depth buffer (legacy behaviour).
    /// </summary>
    public DepthStencilBuffer? SharedDepth;

    public void Initialize(GraphicsDevice device, int width, int height)
    {
        _device = device;
        _width = width; _height = height;

        using var stream = typeof(DeferredLightingPass).Assembly
            .GetManifestResourceStream("SceneRenderer.Shaders.DeferredLighting.feb")!;
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        _effect = new Effect(device, ms.ToArray());

        // Fallback textures
        _whiteTexR32F = new Texture2D(device, 1, 1, false, SurfaceFormat.Single);
        _whiteTexR32F.SetData(new[] { 1.0f });
        _blackTexHalf4 = new Texture2D(device, 1, 1, false, SurfaceFormat.HalfVector4);
        _blackTexHalf4.SetData(new Microsoft.Xna.Framework.Graphics.PackedVector.HalfVector4[]
            { new(0, 0, 0, 1) });

        CreateRT();
    }

    private void CreateRT()
    {
        _hdrRT?.Dispose();

        /* Share the GBuffer's depth so the Skybox pass can depth-test against
         * it. PreserveContents keeps SetRenderTarget from clearing that depth
         * (and this pass covers the whole target anyway).
         */
        if (SharedDepth != null)
        {
            _hdrRT = new RenderTarget2D(_device, _width, _height, false,
                SurfaceFormat.HalfVector4, SharedDepth,
                RenderTargetUsage.PreserveContents);
        }
        else
        {
            _hdrRT = new RenderTarget2D(_device, _width, _height, false,
                SurfaceFormat.HalfVector4, DepthFormat.None);
        }
    }

    public void Resize(int width, int height)
    {
        _width = width; _height = height;
        CreateRT();
    }

    public void Execute(RenderContext ctx)
    {
        _device.SetRenderTarget(_hdrRT);
        // Colour only: the shared depth buffer still holds the GBuffer depth
        // that the Skybox pass tests against.
        _device.Clear(ClearOptions.Target, Color.Black, 1.0f, 0);
        _device.DepthStencilState = DepthStencilState.None;
        _device.RasterizerState = RasterizerState.CullNone;
        _device.BlendState = BlendState.Opaque;

        // Bind GBuffer textures
        _device.Textures[0] = ctx.GBufferRT0 ?? (Texture2D)_blackTexHalf4;
        _device.SamplerStates[0] = SamplerState.PointClamp;
        _device.Textures[1] = ctx.GBufferRT1 ?? (Texture2D)_blackTexHalf4;
        _device.SamplerStates[1] = SamplerState.PointClamp;
        _device.Textures[2] = ctx.GBufferRT2 ?? (Texture2D)_blackTexHalf4;
        _device.SamplerStates[2] = SamplerState.PointClamp;

        // SSAO (slot 3)
        _device.Textures[3] = ctx.SSAOBlurRT ?? (Texture2D)_whiteTexR32F;
        _device.SamplerStates[3] = SamplerState.LinearClamp;

        // SSR (slot 4)
        _device.Textures[4] = ctx.SSRRT ?? (Texture2D)_blackTexHalf4;
        _device.SamplerStates[4] = SamplerState.LinearClamp;

        // Shadow map (slot 5)
        _device.Textures[5] = ctx.ShadowMap ?? (Texture2D)_whiteTexR32F;
        _device.SamplerStates[5] = SamplerState.PointClamp;

        // IBL textures (slots 6-7; BRDF LUT replaced by analytical approximation)
        _device.Textures[6] = ctx.Scene.IrradianceMap ?? (Texture2D)_blackTexHalf4;
        _device.SamplerStates[6] = SamplerState.LinearClamp;
        _device.Textures[7] = ctx.Scene.PrefilteredEnvMap ?? (Texture2D)_blackTexHalf4;
        _device.SamplerStates[7] = SamplerState.LinearClamp;

        // Lighting parameters
        _effect.Parameters["EyePosition"].SetValue(ctx.Camera.GetEyePosition());
        _effect.Parameters["AmbientLight"].SetValue(ctx.Scene.AmbientLight);
        // Debug: override EnvIntensity=0 to trigger albedo diagnostic in shader
        _effect.Parameters["EnvIntensity"].SetValue(DebugAlbedo ? 0.0f : ctx.Scene.EnvIntensity);

        // Matrix uniforms for world position reconstruction and shadow mapping
        _effect.Parameters["InvViewProj"].SetValue(ctx.Camera.InvViewProjection);
        _effect.Parameters["LightViewProj"].SetValue(ctx.LightViewProj);
        _effect.Parameters["Projection"].SetValue(ctx.Camera.ProjectionMatrix);

        // Cull and pack lights
        var culled = LightCuller.CullLights(ctx.Scene.Lights, ctx.Camera.Frustum);
        LightCuller.SetLightParameters(_effect,
            LightCuller.PackLightData(culled), culled.Count);

        _effect.CurrentTechnique!.Passes[0].Apply();
        _device.DrawPrimitives(PrimitiveType.TriangleList, 0, 3);

        _device.SetRenderTarget(null);
        ctx.HdrSceneRT = _hdrRT;
    }

    public void Dispose()
    {
        _effect?.Dispose();
        _hdrRT?.Dispose();
        _whiteTexR32F?.Dispose();
        _blackTexHalf4?.Dispose();
    }
}
