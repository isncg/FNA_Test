using System;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace SceneRenderer;

/// <summary>Screen-Space Reflections via linear ray marching.</summary>
public class SSRPass : IRenderPass
{
    private GraphicsDevice _device = null!;
    private Effect _effect = null!;
    private RenderTarget2D _ssrRT = null!;
    private int _width, _height;

    public string Name => "SSR";
    public Texture2D? DebugOutput => _ssrRT;

    public bool Enabled = true;
    public int MaxSteps = 64;
    public float StepSize = 0.5f;
    public float MaxRoughness = 0.6f;
    public float FadeDistance = 0.15f;

    public void Initialize(GraphicsDevice device, int width, int height)
    {
        _device = device;
        _width = width; _height = height;

        using var stream = typeof(SSRPass).Assembly
            .GetManifestResourceStream("SceneRenderer.Shaders.SSR.feb")!;
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        _effect = new Effect(device, ms.ToArray());

        CreateRT();
    }

    private void CreateRT()
    {
        _ssrRT?.Dispose();
        _ssrRT = new RenderTarget2D(_device, _width, _height, false,
            SurfaceFormat.HalfVector4, DepthFormat.None);
    }

    public void Resize(int width, int height)
    {
        _width = width; _height = height;
        CreateRT();
    }

    public void Execute(RenderContext ctx)
    {
        if (!Enabled || ctx.GBufferRT0 == null || ctx.GBufferRT1 == null
            || ctx.GBufferRT2 == null) return;

        _device.SetRenderTarget(_ssrRT);
        _device.Clear(new Color(0, 0, 0, 0));
        _device.DepthStencilState = DepthStencilState.None;
        _device.RasterizerState = RasterizerState.CullNone;
        _device.BlendState = BlendState.Opaque;

        _device.Textures[0] = ctx.GBufferRT0;
        _device.SamplerStates[0] = SamplerState.PointClamp;
        _device.Textures[1] = ctx.GBufferRT1;
        _device.SamplerStates[1] = SamplerState.PointClamp;
        _device.Textures[2] = ctx.GBufferRT2;
        _device.SamplerStates[2] = SamplerState.PointClamp;

        _effect.Parameters["ViewProj"].SetValue(ctx.Camera.ViewProjectionMatrix);
        _effect.Parameters["InvViewProj"].SetValue(ctx.Camera.InvViewProjection);
        _effect.Parameters["EyePosition"].SetValue(ctx.Camera.GetEyePosition());
        _effect.Parameters["Projection"].SetValue(ctx.Camera.ProjectionMatrix);
        _effect.Parameters["SSRParams"].SetValue(
            new Vector4(MaxSteps, StepSize, MaxRoughness, FadeDistance));

        _effect.CurrentTechnique!.Passes[0].Apply();
        _device.DrawPrimitives(PrimitiveType.TriangleList, 0, 3);

        _device.SetRenderTarget(null);
        ctx.SSRRT = _ssrRT;
    }

    public void Dispose()
    {
        _effect?.Dispose();
        _ssrRT?.Dispose();
    }
}
