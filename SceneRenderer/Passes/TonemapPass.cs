using System;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace SceneRenderer;

/// <summary>Tonemap pass: ACES filmic + gamma → backbuffer.</summary>
public class TonemapPass : IRenderPass
{
    private GraphicsDevice _device = null!;
    private Effect _effect = null!;

    private Texture2D _blackTexHalf4 = null!;

    public string Name => "Tonemap";
    public Texture2D? DebugOutput => null; // outputs to backbuffer

    public float Exposure = 1.0f;
    public float BloomIntensity = 0.3f;

    public void Initialize(GraphicsDevice device, int width, int height)
    {
        _device = device;

        using var stream = typeof(TonemapPass).Assembly
            .GetManifestResourceStream("SceneRenderer.Shaders.Tonemap.feb")!;
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        _effect = new Effect(device, ms.ToArray());

        _blackTexHalf4 = new Texture2D(device, 1, 1, false, SurfaceFormat.HalfVector4);
        _blackTexHalf4.SetData(new Microsoft.Xna.Framework.Graphics.PackedVector.HalfVector4[]
            { new(0, 0, 0, 1) });
    }

    public void Resize(int width, int height) { }

    public void Execute(RenderContext ctx)
    {
        _device.SetRenderTarget(null);
        _device.DepthStencilState = DepthStencilState.None;
        _device.RasterizerState = RasterizerState.CullNone;
        _device.BlendState = BlendState.Opaque;

        _device.Textures[0] = ctx.HdrSceneRT ?? (Texture2D)_blackTexHalf4;
        _device.SamplerStates[0] = SamplerState.LinearClamp;

        // Get bloom output (set by BloomPass via ctx.BloomRT)
        _device.Textures[1] = ctx.BloomRT ?? (Texture2D)_blackTexHalf4;
        _device.SamplerStates[1] = SamplerState.LinearClamp;

        _effect.Parameters["Exposure"].SetValue(Exposure);
        _effect.Parameters["BloomIntensity"].SetValue(BloomIntensity);

        _effect.CurrentTechnique!.Passes[0].Apply();
        _device.DrawPrimitives(PrimitiveType.TriangleList, 0, 3);
    }

    public void Dispose()
    {
        _effect?.Dispose();
        _blackTexHalf4?.Dispose();
    }
}
