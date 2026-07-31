using System;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace SceneRenderer;

/// <summary>SSAO pass: fullscreen triangle sampling hemisphere around each GBuffer pixel.</summary>
public class SSAOPass : IRenderPass
{
    private GraphicsDevice _device = null!;
    private Effect _effect = null!;
    private RenderTarget2D _ssaoRT = null!;
    private int _width, _height;

    public string Name => "SSAO";
    public Texture2D? DebugOutput => _ssaoRT;

    public float Radius = 0.5f;
    public float Bias = 0.025f;
    public float Intensity = 1.0f;
    public bool Enabled = true;

    public void Initialize(GraphicsDevice device, int width, int height)
    {
        _device = device;
        _width = width; _height = height;

        using var stream = typeof(SSAOPass).Assembly
            .GetManifestResourceStream("SceneRenderer.Shaders.SSAO.feb")!;
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        _effect = new Effect(device, ms.ToArray());

        CreateRT();
    }

    private void CreateRT()
    {
        _ssaoRT?.Dispose();
        _ssaoRT = new RenderTarget2D(_device, _width, _height, false,
            SurfaceFormat.Single, DepthFormat.None);
    }

    public void Resize(int width, int height)
    {
        _width = width; _height = height;
        CreateRT();
    }

    public void Execute(RenderContext ctx)
    {
        if (!Enabled || ctx.GBufferRT1 == null || ctx.GBufferRT2 == null) return;

        _device.SetRenderTarget(_ssaoRT);
        _device.Clear(new Color(1.0f, 1.0f, 1.0f, 1.0f));
        _device.DepthStencilState = DepthStencilState.None;
        _device.RasterizerState = RasterizerState.CullNone;
        _device.BlendState = BlendState.Opaque;

        _device.Textures[0] = ctx.GBufferRT1;
        _device.SamplerStates[0] = SamplerState.PointClamp;
        _device.Textures[1] = ctx.GBufferRT2;
        _device.SamplerStates[1] = SamplerState.PointClamp;

        _effect.Parameters["Projection"].SetValue(ctx.Camera.ProjectionMatrix);
        _effect.Parameters["SSAOParams"].SetValue(new Vector4(Radius, Bias, Intensity, 0));
        _effect.Parameters["SSAOResolutionScale"].SetValue(new Vector2(1.0f / _width, 1.0f / _height));

        _effect.CurrentTechnique!.Passes[0].Apply();
        _device.DrawPrimitives(PrimitiveType.TriangleList, 0, 3);

        _device.SetRenderTarget(null);
        ctx.SSAORT = _ssaoRT;
    }

    public void Dispose()
    {
        _effect?.Dispose();
        _ssaoRT?.Dispose();
    }
}
