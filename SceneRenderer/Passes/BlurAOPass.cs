using System;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace SceneRenderer;

/// <summary>Bilateral blur for SSAO de-noising.</summary>
public class BlurAOPass : IRenderPass
{
    private GraphicsDevice _device = null!;
    private Effect _effect = null!;
    private RenderTarget2D _blurRT = null!;
    private int _width, _height;

    public string Name => "BlurAO";
    public Texture2D? DebugOutput => _blurRT;

    public float Sharpness = 0.1f;

    public void Initialize(GraphicsDevice device, int width, int height)
    {
        _device = device;
        _width = width; _height = height;

        using var stream = typeof(BlurAOPass).Assembly
            .GetManifestResourceStream("SceneRenderer.Shaders.BlurAO.feb")!;
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        _effect = new Effect(device, ms.ToArray());

        CreateRT();
    }

    private void CreateRT()
    {
        _blurRT?.Dispose();
        _blurRT = new RenderTarget2D(_device, _width, _height, false,
            SurfaceFormat.Single, DepthFormat.None);
    }

    public void Resize(int width, int height)
    {
        _width = width; _height = height;
        CreateRT();
    }

    public void Execute(RenderContext ctx)
    {
        if (ctx.SSAORT == null || ctx.GBufferRT2 == null) return;

        _device.SetRenderTarget(_blurRT);
        _device.Clear(new Color(1.0f, 1.0f, 1.0f, 1.0f));
        _device.DepthStencilState = DepthStencilState.None;
        _device.RasterizerState = RasterizerState.CullNone;
        _device.BlendState = BlendState.Opaque;

        _device.Textures[0] = ctx.SSAORT;
        _device.SamplerStates[0] = SamplerState.PointClamp;
        _device.Textures[1] = ctx.GBufferRT2;
        _device.SamplerStates[1] = SamplerState.PointClamp;

        _effect.Parameters["TexelSize"].SetValue(
            new Vector2(1.0f / _width, 1.0f / _height));
        _effect.Parameters["BlurSharpness"].SetValue(Sharpness);

        _effect.CurrentTechnique!.Passes[0].Apply();
        _device.DrawPrimitives(PrimitiveType.TriangleList, 0, 3);

        _device.SetRenderTarget(null);
        ctx.SSAOBlurRT = _blurRT;
    }

    public void Dispose()
    {
        _effect?.Dispose();
        _blurRT?.Dispose();
    }
}
