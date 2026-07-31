using System;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace SceneRenderer;

/// <summary>Skybox pass: renders equirectangular env map as HDR background.</summary>
public class SkyboxPass : IRenderPass
{
    private GraphicsDevice _device = null!;
    private Effect _effect = null!;
    private int _width, _height;

    public string Name => "Skybox";
    public Texture2D? DebugOutput => null; // writes into HDR scene RT
    public bool Enabled = true;

    public void Initialize(GraphicsDevice device, int width, int height)
    {
        _device = device;
        _width = width; _height = height;

        using var stream = typeof(SkyboxPass).Assembly
            .GetManifestResourceStream("SceneRenderer.Shaders.Skybox.feb")!;
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        _effect = new Effect(device, ms.ToArray());
    }

    public void Resize(int width, int height) { _width = width; _height = height; }

    public void Execute(RenderContext ctx)
    {
        if (ctx.Scene.EnvMap == null || ctx.HdrSceneRT == null) return;

        var eyePos = ctx.Camera.GetEyePosition();
        var forward = ctx.Camera.Forward;
        var right = Vector3.Normalize(Vector3.Cross(Vector3.Up, forward));
        var up = Vector3.Cross(forward, right);

        float fov = ctx.Camera.FovY;
        float aspect = ctx.Camera.AspectRatio;
        float tanHalfFov = MathF.Tan(fov / 2f);
        float fovX = tanHalfFov * aspect;
        float fovY = tanHalfFov;

        // Set render target and blend to add onto existing HDR scene
        _device.SetRenderTarget(ctx.HdrSceneRT);
        _device.DepthStencilState = DepthStencilState.None;
        _device.RasterizerState = RasterizerState.CullNone;
        _device.BlendState = BlendState.Additive;

        _device.Textures[0] = ctx.Scene.EnvMap;
        _device.SamplerStates[0] = SamplerState.LinearClamp;
        _device.Textures[1] = ctx.GBufferRT2;
        _device.SamplerStates[1] = SamplerState.PointClamp;

        _effect.Parameters["CameraForward"].SetValue(forward);
        _effect.Parameters["CameraRight"].SetValue(right);
        _effect.Parameters["CameraUp"].SetValue(up);
        _effect.Parameters["FovParams"].SetValue(new Vector2(fovX, fovY));

        _effect.CurrentTechnique!.Passes[0].Apply();
        _device.DrawPrimitives(PrimitiveType.TriangleList, 0, 3);

        _device.SetRenderTarget(null);
    }

    public void Dispose() { _effect?.Dispose(); }
}
