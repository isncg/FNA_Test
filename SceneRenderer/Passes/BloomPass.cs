using System;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace SceneRenderer;

/// <summary>Bloom post-process: bright extract → downsample chain → upsample chain.</summary>
public class BloomPass : IRenderPass
{
    private GraphicsDevice _device = null!;
    private Effect _effect = null!;
    private int _width, _height;

    private RenderTarget2D? _brightRT;
    private RenderTarget2D? _down1, _down2, _down3, _down4;
    private RenderTarget2D? _up1, _up2, _up3, _up4;

    public string Name => "Bloom";
    public Texture2D? DebugOutput => _up1; // final bloom result

    public bool Enabled = true;
    public float Threshold = 1.0f;
    public float Intensity = 0.3f;

    public void Initialize(GraphicsDevice device, int width, int height)
    {
        _device = device;
        _width = width; _height = height;

        using var stream = typeof(BloomPass).Assembly
            .GetManifestResourceStream("SceneRenderer.Shaders.Bloom.feb")!;
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        _effect = new Effect(device, ms.ToArray());

        CreateRTs();
    }

    private void CreateRTs()
    {
        DisposeRTs();

        int w = _width, h = _height;
        _brightRT = new RenderTarget2D(_device, w / 2, h / 2, false,
            SurfaceFormat.HalfVector4, DepthFormat.None);
        w /= 2; h /= 2;
        _down1 = new RenderTarget2D(_device, w, h, false,
            SurfaceFormat.HalfVector4, DepthFormat.None);
        w /= 2; h /= 2;
        _down2 = new RenderTarget2D(_device, w, h, false,
            SurfaceFormat.HalfVector4, DepthFormat.None);
        w /= 2; h /= 2;
        _down3 = new RenderTarget2D(_device, w, h, false,
            SurfaceFormat.HalfVector4, DepthFormat.None);
        w /= 2; h /= 2;
        _down4 = new RenderTarget2D(_device, w, h, false,
            SurfaceFormat.HalfVector4, DepthFormat.None);

        // Upsample RTs (same sizes as corresponding downsample)
        _up4 = new RenderTarget2D(_device, _down4!.Width, _down4.Height, false,
            SurfaceFormat.HalfVector4, DepthFormat.None);
        _up3 = new RenderTarget2D(_device, _down3!.Width, _down3.Height, false,
            SurfaceFormat.HalfVector4, DepthFormat.None);
        _up2 = new RenderTarget2D(_device, _down2!.Width, _down2.Height, false,
            SurfaceFormat.HalfVector4, DepthFormat.None);
        _up1 = new RenderTarget2D(_device, _down1!.Width, _down1.Height, false,
            SurfaceFormat.HalfVector4, DepthFormat.None);
    }

    private void DisposeRTs()
    {
        _brightRT?.Dispose();
        _down1?.Dispose(); _down2?.Dispose(); _down3?.Dispose(); _down4?.Dispose();
        _up1?.Dispose(); _up2?.Dispose(); _up3?.Dispose(); _up4?.Dispose();
    }

    public void Resize(int width, int height)
    {
        _width = width; _height = height;
        CreateRTs();
    }

    public void Execute(RenderContext ctx)
    {
        if (!Enabled || ctx.HdrSceneRT == null) return;

        _device.DepthStencilState = DepthStencilState.None;
        _device.RasterizerState = RasterizerState.CullNone;

        // Pass 0: Bright extract to half-res
        _device.SetRenderTarget(_brightRT);
        _device.BlendState = BlendState.Opaque;
        _device.Textures[0] = ctx.HdrSceneRT;
        _device.SamplerStates[0] = SamplerState.LinearClamp;
        _effect.Parameters["BloomThreshold"].SetValue(Threshold);
        _effect.Parameters["TexelSize"].SetValue(new Vector2(1f / _width, 1f / _height));
        _effect.Parameters["ShaderIndex"].SetValue(0f);
        _effect.CurrentTechnique!.Passes[0].Apply();
        _device.DrawPrimitives(PrimitiveType.TriangleList, 0, 3);

        // Downsample chain (pass 1)
        Downsample(_brightRT, _down1!);
        Downsample(_down1!, _down2!);
        Downsample(_down2!, _down3!);
        Downsample(_down3!, _down4!);

        // Upsample chain (pass 2)
        Upsample(_down4!, _up4!, _down3!);
        Upsample(_up4!, _up3!, _down2!);
        Upsample(_up3!, _up2!, _down1!);
        Upsample(_up2!, _up1!, _brightRT!);

        _device.SetRenderTarget(null);
    }

    private void Downsample(RenderTarget2D src, RenderTarget2D dst)
    {
        _device.SetRenderTarget(dst);
        _device.Textures[0] = src;
        _device.SamplerStates[0] = SamplerState.LinearClamp;
        _effect.Parameters["TexelSize"].SetValue(
            new Vector2(1f / src.Width, 1f / src.Height));
        _effect.Parameters["ShaderIndex"].SetValue(1f);
        _effect.CurrentTechnique!.Passes[0].Apply();
        _device.DrawPrimitives(PrimitiveType.TriangleList, 0, 3);
    }

    private void Upsample(RenderTarget2D src, RenderTarget2D dst, RenderTarget2D blend)
    {
        _device.SetRenderTarget(dst);
        _device.Textures[0] = src;
        _device.SamplerStates[0] = SamplerState.LinearClamp;
        _device.Textures[1] = blend;
        _device.SamplerStates[1] = SamplerState.LinearClamp;
        _effect.Parameters["TexelSize"].SetValue(
            new Vector2(1f / src.Width, 1f / src.Height));
        _effect.Parameters["BloomIntensity"].SetValue(Intensity);
        _effect.Parameters["ShaderIndex"].SetValue(2f);
        _effect.CurrentTechnique!.Passes[0].Apply();
        _device.DrawPrimitives(PrimitiveType.TriangleList, 0, 3);
    }

    public void Dispose()
    {
        _effect?.Dispose();
        DisposeRTs();
    }
}
