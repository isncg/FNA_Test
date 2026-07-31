using System;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace SceneRenderer;

/// <summary>Deferred G-Buffer pass: renders 3D geometry into 3 MRTs.</summary>
public class GBufferPass : IRenderPass
{
    private GraphicsDevice _device = null!;
    private Effect _effect = null!;
    private int _width, _height;

    public string Name => "GBuffer";
    public Texture2D? DebugOutput => _rt0;

    private RenderTarget2D? _rt0, _rt1, _rt2;

    public void Initialize(GraphicsDevice device, int width, int height)
    {
        _device = device;
        _width = width; _height = height;

        // Load GBuffer effect from embedded FEB
        using var stream = typeof(GBufferPass).Assembly
            .GetManifestResourceStream("SceneRenderer.Shaders.GBuffer.feb")!;
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        _effect = new Effect(device, ms.ToArray());

        CreateRenderTargets();
    }

    private void CreateRenderTargets()
    {
        _rt0?.Dispose();
        _rt1?.Dispose();
        _rt2?.Dispose();

        _rt0 = new RenderTarget2D(_device, _width, _height, false,
            SurfaceFormat.Color, DepthFormat.Depth24Stencil8);
        _rt1 = new RenderTarget2D(_device, _width, _height, false,
            SurfaceFormat.HalfVector4, DepthFormat.None);
        _rt2 = new RenderTarget2D(_device, _width, _height, false,
            SurfaceFormat.HalfVector4, DepthFormat.None);
    }

    public void Resize(int width, int height)
    {
        _width = width; _height = height;
        CreateRenderTargets();
    }

    public void Execute(RenderContext ctx)
    {
        _device.SetRenderTargets(
            new RenderTargetBinding(_rt0!),
            new RenderTargetBinding(_rt1!),
            new RenderTargetBinding(_rt2!));
        _device.Clear(new Color(0, 0, 0, 0));
        _device.DepthStencilState = DepthStencilState.Default;
        _device.RasterizerState = RasterizerState.CullCounterClockwise;
        _device.BlendState = BlendState.Opaque;

        var view = ctx.Camera.ViewMatrix;
        var proj = ctx.Camera.ProjectionMatrix;

        // Per-frame camera uniforms (set once)
        _effect.Parameters["PrevViewProj"].SetValue(ctx.PrevViewProj);

        foreach (var obj in ctx.VisibleObjects)
        {
            if (obj.Mesh?.VertexBuffer == null || obj.Material == null) continue;

            var world = obj.LocalTransform;
            var worldViewProj = world * view * proj;
            var worldInvTransp = Matrix.Transpose(Matrix.Invert(world));

            _effect.Parameters["WorldViewProj"].SetValue(worldViewProj);
            _effect.Parameters["World"].SetValue(world);
            _effect.Parameters["WorldInverseTranspose"].SetValue(worldInvTransp);
            _effect.Parameters["AlbedoTint"].SetValue(obj.Material.AlbedoTint);
            _effect.Parameters["MetallicScale"].SetValue(obj.Material.MetallicScale);
            _effect.Parameters["RoughnessScale"].SetValue(obj.Material.RoughnessScale);

            // Bind material textures
            _device.Textures[0] = obj.Material.AlbedoMap ?? ctx.Scene.DefaultWhite;
            _device.SamplerStates[0] = SamplerState.AnisotropicWrap;
            _device.Textures[1] = obj.Material.NormalMap ?? ctx.Scene.DefaultNormal;
            _device.SamplerStates[1] = SamplerState.AnisotropicWrap;
            _device.Textures[2] = obj.Material.ORMMap ?? ctx.Scene.DefaultORM;
            _device.SamplerStates[2] = SamplerState.AnisotropicWrap;

            _effect.CurrentTechnique!.Passes[0].Apply();
            _device.SetVertexBuffer(obj.Mesh.VertexBuffer);
            _device.DrawPrimitives(PrimitiveType.TriangleList, 0, obj.Mesh.PrimitiveCount);
        }

        _device.SetRenderTargets(null);

        // Store output references in context
        ctx.GBufferRT0 = _rt0;
        ctx.GBufferRT1 = _rt1;
        ctx.GBufferRT2 = _rt2;
    }

    public void Dispose()
    {
        _effect?.Dispose();
        _rt0?.Dispose();
        _rt1?.Dispose();
        _rt2?.Dispose();
    }
}
