using System;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace SceneRenderer;

/// <summary>Screen-Space Reflections via linear ray marching.</summary>
/// <remarks>
/// UE5-style temporal reflections: hits sample the previous frame's lit HDR
/// scene (direct light + IBL + sky) rather than the flat GBuffer albedo. The
/// history buffer is refreshed after the skybox pass via <see cref="UpdateHistory"/>.
/// </remarks>
public class SSRPass : IRenderPass
{
    private GraphicsDevice _device = null!;
    private Effect _effect = null!;
    private Effect _copyEffect = null!;
    private RenderTarget2D _ssrRT = null!;
    private RenderTarget2D _historyRT = null!;
    private int _width, _height;

    public string Name => "SSR";
    public Texture2D? DebugOutput => _ssrRT;

    public bool Enabled = true;
    public int MaxSteps = 64;
    public float StepSize = 0.5f;
    public float MaxRoughness = 0.6f;
    public float FadeDistance = 0.15f;

    /// <summary>
    /// Shared depth-stencil buffer injected by SceneRendererEngine before
    /// Initialize/Resize. The SSR target aliases it so this pass can
    /// stencil-test against the per-object SSR marks the GBuffer pass wrote
    /// (SceneObject.ReceivesSSR). When null, masking is skipped.
    /// </summary>
    public DepthStencilBuffer? SharedDepth;

    /* Stencil-only test: compute SSR exclusively on pixels the GBuffer pass
     * marked as SSR receivers. Depth testing stays off (fullscreen pass);
     * both winding sets use the same function so the fullscreen triangle's
     * orientation cannot bypass the test.
     */
    private static readonly DepthStencilState StencilReceiversOnly = new()
    {
        DepthBufferEnable = false,
        StencilEnable = true,
        ReferenceStencil = 1,
        StencilFunction = CompareFunction.Equal,
        StencilPass = StencilOperation.Keep,
        CounterClockwiseStencilFunction = CompareFunction.Equal,
        CounterClockwiseStencilPass = StencilOperation.Keep,
    };

    public void Initialize(GraphicsDevice device, int width, int height)
    {
        _device = device;
        _width = width; _height = height;

        using var stream = typeof(SSRPass).Assembly
            .GetManifestResourceStream("SceneRenderer.Shaders.SSR.feb")!;
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        _effect = new Effect(device, ms.ToArray());

        // Reuse the DebugView effect (channel 0 = RGB passthrough) as a
        // fullscreen copy for maintaining the temporal history buffer.
        using var copyStream = typeof(SSRPass).Assembly
            .GetManifestResourceStream("SceneRenderer.Shaders.DebugView.feb")!;
        using var copyMs = new MemoryStream();
        copyStream.CopyTo(copyMs);
        _copyEffect = new Effect(device, copyMs.ToArray());

        CreateRT();
    }

    private void CreateRT()
    {
        _ssrRT?.Dispose();

        /* Aliasing the shared buffer gives this pass the stencil marks the
         * GBuffer wrote. PreserveContents is required: DiscardContents would
         * make SetRenderTargets clear the shared depth+stencil and destroy
         * the marks (and the depth the Skybox pass still tests against).
         */
        if (SharedDepth != null)
        {
            _ssrRT = new RenderTarget2D(_device, _width, _height, false,
                SurfaceFormat.HalfVector4, SharedDepth,
                RenderTargetUsage.PreserveContents);
        }
        else
        {
            _ssrRT = new RenderTarget2D(_device, _width, _height, false,
                SurfaceFormat.HalfVector4, DepthFormat.None);
        }

        _historyRT?.Dispose();
        _historyRT = new RenderTarget2D(_device, _width, _height, false,
            SurfaceFormat.HalfVector4, DepthFormat.None);
        // Start from a known-black history so the first frame degrades to the
        // disocclusion fallback instead of sampling uninitialized memory.
        _device.SetRenderTarget(_historyRT);
        _device.Clear(Color.Black);
        _device.SetRenderTarget(null);
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
        // Colour only: the aliased shared buffer still holds the GBuffer
        // depth and the SSR stencil marks this pass tests against (a full
        // clear would wipe both).
        _device.Clear(ClearOptions.Target, new Color(0, 0, 0, 0), 1.0f, 0);
        _device.DepthStencilState = SharedDepth != null
            ? StencilReceiversOnly
            : DepthStencilState.None;
        _device.RasterizerState = RasterizerState.CullNone;
        _device.BlendState = BlendState.Opaque;

        _device.Textures[0] = ctx.GBufferRT0;
        _device.SamplerStates[0] = SamplerState.PointClamp;
        _device.Textures[1] = ctx.GBufferRT1;
        _device.SamplerStates[1] = SamplerState.PointClamp;
        _device.Textures[2] = ctx.GBufferRT2;
        _device.SamplerStates[2] = SamplerState.PointClamp;
        _device.Textures[3] = _historyRT;
        _device.SamplerStates[3] = SamplerState.LinearClamp;

        _effect.Parameters["ViewProj"].SetValue(ctx.Camera.ViewProjectionMatrix);
        _effect.Parameters["InvViewProj"].SetValue(ctx.Camera.InvViewProjection);
        _effect.Parameters["EyePosition"].SetValue(ctx.Camera.GetEyePosition());
        _effect.Parameters["Projection"].SetValue(ctx.Camera.ProjectionMatrix);
        _effect.Parameters["PrevViewProj"].SetValue(ctx.PrevViewProj);
        _effect.Parameters["SSRParams"].SetValue(
            new Vector4(MaxSteps, StepSize, MaxRoughness, FadeDistance));

        _effect.CurrentTechnique!.Passes[0].Apply();
        _device.DrawPrimitives(PrimitiveType.TriangleList, 0, 3);

        _device.SetRenderTarget(null);
        ctx.SSRRT = _ssrRT;
    }

    /// <summary>
    /// Copies the fully-lit HDR scene (after the skybox pass) into the history
    /// buffer so the next frame's SSR can reflect it. Must be called after the
    /// Skybox pass each frame.
    /// </summary>
    public void UpdateHistory(RenderContext ctx)
    {
        if (ctx.HdrSceneRT == null) return;

        _device.SetRenderTarget(_historyRT);
        _device.DepthStencilState = DepthStencilState.None;
        _device.RasterizerState = RasterizerState.CullNone;
        _device.BlendState = BlendState.Opaque;

        _device.Textures[0] = ctx.HdrSceneRT;
        _device.SamplerStates[0] = SamplerState.PointClamp;

        _copyEffect.Parameters["DebugChannel"].SetValue(0f); // RGB passthrough
        _copyEffect.CurrentTechnique!.Passes[0].Apply();
        _device.DrawPrimitives(PrimitiveType.TriangleList, 0, 3);

        _device.SetRenderTarget(null);
    }

    public void Dispose()
    {
        _effect?.Dispose();
        _copyEffect?.Dispose();
        _ssrRT?.Dispose();
        _historyRT?.Dispose();
    }
}
