using System;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace SceneRenderer;

/// <summary>Directional shadow map pass: depth-only rendering from light POV.</summary>
public class ShadowMapPass : IRenderPass
{
    private GraphicsDevice _device = null!;
    private Effect _effect = null!;
    private RenderTarget2D _shadowMap = null!;
    private int _shadowRes = 2048;

    private readonly RasterizerState _shadowRasterizer = new()
    {
        CullMode = CullMode.None,
        FillMode = FillMode.Solid,
    };

    public string Name => "ShadowMap";
    public Texture2D? DebugOutput => _shadowMap;

    public float ShadowBias = 0.02f;

    public void Initialize(GraphicsDevice device, int width, int height)
    {
        _device = device;

        using var stream = typeof(ShadowMapPass).Assembly
            .GetManifestResourceStream("SceneRenderer.Shaders.ShadowMap.feb")!;
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        _effect = new Effect(device, ms.ToArray());

        _shadowMap = new RenderTarget2D(device, _shadowRes, _shadowRes, false,
            SurfaceFormat.Single, DepthFormat.Depth24Stencil8);
    }

    public void Resize(int width, int height) { /* shadow map is fixed res */ }

    public void Execute(RenderContext ctx)
    {
        if (ctx.Scene.SunLight == null) return;

        var lightDir = ctx.Scene.SunLight.Direction;
        var sceneCenter = new Vector3(0, 0.5f, 0);
        float halfSize = 10f;
        var lightPos = sceneCenter + lightDir * 25f;
        var up = MathF.Abs(lightDir.Y) > 0.999f ? Vector3.Forward : Vector3.Up;
        var lightView = Matrix.CreateLookAt(lightPos, sceneCenter, up);
        var lightProj = Matrix.CreateOrthographic(halfSize * 2, halfSize * 2, 0.1f, 50f);
        var lightVP = lightView * lightProj;

        _shadowRasterizer.DepthBias = ShadowBias;

        _device.SetRenderTarget(_shadowMap);
        _device.Clear(ClearOptions.Target | ClearOptions.DepthBuffer,
            new Color(1.0f, 1.0f, 1.0f, 1.0f), 1.0f, 0);
        _device.DepthStencilState = DepthStencilState.Default;
        _device.BlendState = BlendState.Opaque;
        _device.RasterizerState = _shadowRasterizer;

        foreach (var obj in ctx.VisibleObjects)
        {
            if (obj.Mesh?.VertexBuffer == null) continue;
            var wvp = obj.LocalTransform * lightVP;
            _effect.Parameters["WorldViewProj"].SetValue(wvp);
            _effect.CurrentTechnique!.Passes[0].Apply();
            _device.SetVertexBuffer(obj.Mesh.VertexBuffer);
            _device.DrawPrimitives(PrimitiveType.TriangleList, 0, obj.Mesh.PrimitiveCount);
        }

        _device.SetRenderTarget(null);
        ctx.ShadowMap = _shadowMap;
        ctx.LightViewProj = lightVP;
    }

    public void Dispose()
    {
        _effect?.Dispose();
        _shadowMap?.Dispose();
    }
}
