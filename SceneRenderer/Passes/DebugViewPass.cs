using System;
using System.IO;
using Microsoft.Xna.Framework.Graphics;

namespace SceneRenderer;

/// <summary>Minimal debug pass: copies a texture to the backbuffer for GBuffer inspection.</summary>
public class DebugViewPass : IRenderPass
{
    private GraphicsDevice _device = null!;
    private Effect _effect = null!;

    public string Name => "DebugView";
    public Texture2D? DebugOutput => null;

    public void Initialize(GraphicsDevice device, int width, int height)
    {
        _device = device;

        using var stream = typeof(DebugViewPass).Assembly
            .GetManifestResourceStream("SceneRenderer.Shaders.DebugView.feb")!;
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        _effect = new Effect(device, ms.ToArray());
    }

    public void Resize(int width, int height) { }

    public void Execute(RenderContext ctx)
    {
        // Not used in pipeline — use RenderDebug() directly
    }

    /// <summary>
    /// Render a texture directly to the backbuffer for debugging.
    /// channel: 0=RGB, 1=R grayscale, 2=G grayscale, 3=A grayscale.
    /// </summary>
    public void RenderDebug(Texture2D source, int channel)
    {
        _device.SetRenderTarget(null);
        _device.DepthStencilState = DepthStencilState.None;
        _device.RasterizerState = RasterizerState.CullNone;
        _device.BlendState = BlendState.Opaque;

        _device.Textures[0] = source;
        _device.SamplerStates[0] = SamplerState.PointClamp;

        _effect.Parameters["DebugChannel"].SetValue((float)channel);
        _effect.CurrentTechnique!.Passes[0].Apply();
        _device.DrawPrimitives(PrimitiveType.TriangleList, 0, 3);
    }

    public void Dispose()
    {
        _effect?.Dispose();
    }
}
