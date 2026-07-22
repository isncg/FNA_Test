using System;
using System.IO;
using System.Reflection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using FNA.Test;

namespace JFAOutlineDemo
{
    /// <summary>
    /// Manages the Jump Flood Algorithm outline + X-ray occlusion pipeline.
    /// </summary>
    public class OutlineRenderer : IDisposable
    {
        private GraphicsDevice _device;
        private int _width, _height;
        private int _jfaPassCount;

        // ── Render targets ─────────────────────────────────────────────
        /// <summary>Scene color (depth-tested, geometry with lighting).</summary>
        public RenderTarget2D SceneRT { get; private set; }
        /// <summary>Full silhouette mask — render WITHOUT depth test.</summary>
        public RenderTarget2D FullMaskRT { get; private set; }
        /// <summary>Visible-surface mask — render WITH depth test.</summary>
        public RenderTarget2D VisibleMaskRT { get; private set; }
        private RenderTarget2D _jfaPing;
        private RenderTarget2D _jfaPong;

        // ── Effects ────────────────────────────────────────────────────
        private Effect _sceneMaskEffect;
        private Effect _jfaInitEffect;
        private Effect _jfaFloodEffect;
        private Effect _jfaCompositeEffect;

        // ── Cached techniques ──────────────────────────────────────────
        private EffectTechnique _sceneTechnique;
        private EffectTechnique _maskTechnique;
        private EffectTechnique _blackTechnique;

        // ── Cached effect parameters ───────────────────────────────────
        private EffectParameter _wvpParam;
        private EffectParameter _diffuseColorParam;
        private EffectParameter _lightDirParam;
        private EffectParameter _ambientColorParam;
        private EffectParameter _rtInvSizeParam;
        private EffectParameter _stepSizeParam;
        private EffectParameter _floodRtInvSizeParam;
        private EffectParameter _outlineWidthParam;
        private EffectParameter _outlineColorParam;
        private EffectParameter _screenSizeParam;
        private EffectParameter _xRayColorParam;
        private EffectParameter _xRayEnableParam;

        // ── Fullscreen quad ────────────────────────────────────────────
        private VertexBuffer _fullscreenQuad;

        public OutlineRenderer(GraphicsDevice device, int width, int height)
        {
            _device = device;
            _width = width;
            _height = height;

            SceneRT = new RenderTarget2D(device, width, height, false,
                SurfaceFormat.Color, DepthFormat.Depth24Stencil8);
            FullMaskRT = new RenderTarget2D(device, width, height, false,
                SurfaceFormat.Color, DepthFormat.None);
            VisibleMaskRT = new RenderTarget2D(device, width, height, false,
                SurfaceFormat.Color, DepthFormat.Depth24Stencil8);
            _jfaPing = new RenderTarget2D(device, width, height, false,
                SurfaceFormat.Vector2, DepthFormat.None);
            _jfaPong = new RenderTarget2D(device, width, height, false,
                SurfaceFormat.Vector2, DepthFormat.None);

            int maxDim = Math.Max(width, height);
            _jfaPassCount = 0;
            int s = 1;
            while (s < maxDim) { s <<= 1; _jfaPassCount++; }

            _sceneMaskEffect    = LoadEffect("JFAOutlineDemo.SceneMask.feb");
            _jfaInitEffect      = LoadEffect("JFAOutlineDemo.JFAInit.feb");
            _jfaFloodEffect     = LoadEffect("JFAOutlineDemo.JFAFlood.feb");
            _jfaCompositeEffect = LoadEffect("JFAOutlineDemo.JFAComposite.feb");

            _sceneTechnique = _sceneMaskEffect.Techniques["Scene"];
            _maskTechnique  = _sceneMaskEffect.Techniques["Mask"];
            _blackTechnique = _sceneMaskEffect.Techniques["Black"];

            _wvpParam          = _sceneMaskEffect.Parameters["WorldViewProj"];
            _diffuseColorParam = _sceneMaskEffect.Parameters["DiffuseColor"];
            _lightDirParam     = _sceneMaskEffect.Parameters["LightDir"];
            _ambientColorParam = _sceneMaskEffect.Parameters["AmbientColor"];
            _rtInvSizeParam    = _jfaInitEffect.Parameters["RTInvSize"];
            _stepSizeParam     = _jfaFloodEffect.Parameters["StepSize"];
            _floodRtInvSizeParam = _jfaFloodEffect.Parameters["RTInvSize"];
            _outlineWidthParam = _jfaCompositeEffect.Parameters["OutlineWidth"];
            _outlineColorParam = _jfaCompositeEffect.Parameters["OutlineColor"];
            _screenSizeParam   = _jfaCompositeEffect.Parameters["ScreenSize"];
            _xRayColorParam    = _jfaCompositeEffect.Parameters["XRayColor"];
            _xRayEnableParam   = _jfaCompositeEffect.Parameters["XRayEnable"];

            var quadVerts = GeometryGen.Quad();
            _fullscreenQuad = new VertexBuffer(device, typeof(VertexPositionTexture),
                6, BufferUsage.WriteOnly);
            _fullscreenQuad.SetData(quadVerts);
        }

        // ── Scene rendering ────────────────────────────────────────────

        public void DrawSceneGeometry(VertexBuffer vb, int primitiveCount,
            Matrix world, Matrix view, Matrix proj, Vector3 diffuseColor)
        {
            Matrix wvp = world * view * proj;
            _wvpParam.SetValue(wvp);
            _diffuseColorParam.SetValue(new Vector4(diffuseColor, 1.0f));
            _lightDirParam.SetValue(new Vector4(0.577f, 0.577f, 0.577f, 0.0f));
            _ambientColorParam.SetValue(new Vector4(0.25f, 0.25f, 0.30f, 0.0f));

            _sceneMaskEffect.CurrentTechnique = _sceneTechnique;
            _sceneTechnique.Passes[0].Apply();
            _device.SetVertexBuffer(vb);
            _device.DrawPrimitives(PrimitiveType.TriangleList, 0, primitiveCount);
        }

        // ── Mask rendering (shared by FullMask and VisibleMask passes) ─

        /// <summary>
        /// Draw geometry as solid white silhouette. Caller controls depth state
        /// via DepthStencilState before calling (None for full mask, Default for visible).
        /// </summary>
        public void DrawMaskGeometry(VertexBuffer vb, int primitiveCount,
            Matrix world, Matrix view, Matrix proj)
        {
            Matrix wvp = world * view * proj;
            _wvpParam.SetValue(wvp);

            _sceneMaskEffect.CurrentTechnique = _maskTechnique;
            _maskTechnique.Passes[0].Apply();
            _device.SetVertexBuffer(vb);
            _device.DrawPrimitives(PrimitiveType.TriangleList, 0, primitiveCount);
        }

        /// <summary>
        /// Draw geometry as solid BLACK — used to write occluder depth
        /// into the VisibleMask depth buffer without contributing white.
        /// Call BEFORE DrawMaskGeometry on the same RT.
        /// </summary>
        public void DrawOccluderGeometry(VertexBuffer vb, int primitiveCount,
            Matrix world, Matrix view, Matrix proj)
        {
            Matrix wvp = world * view * proj;
            _wvpParam.SetValue(wvp);

            _sceneMaskEffect.CurrentTechnique = _blackTechnique;
            _blackTechnique.Passes[0].Apply();
            _device.SetVertexBuffer(vb);
            _device.DrawPrimitives(PrimitiveType.TriangleList, 0, primitiveCount);
        }

        // ── JFA pipeline (runs on FullMaskRT) ──────────────────────────

        public void RunJFA()
        {
            var rtInvSize = new Vector4(1.0f / _width, 1.0f / _height, _width, _height);

            // Init: FullMask → seed UVs
            _device.SetRenderTarget(_jfaPing);
            _device.Clear(ClearOptions.Target, new Vector4(-1, -1, 0, 0), 0, 0);

            _rtInvSizeParam.SetValue(rtInvSize);
            _jfaInitEffect.CurrentTechnique = _jfaInitEffect.Techniques["Init"];
            _jfaInitEffect.CurrentTechnique.Passes[0].Apply();
            _device.Textures[0] = FullMaskRT;
            _device.SamplerStates[0] = SamplerState.PointClamp;
            _device.SetVertexBuffer(_fullscreenQuad);
            _device.DrawPrimitives(PrimitiveType.TriangleList, 0, 2);

            _device.SetRenderTarget(null);

            // Flood: ping-pong
            int maxDim = Math.Max(_width, _height);
            int stepSize = 1;
            while (stepSize < maxDim) stepSize <<= 1;
            stepSize >>= 1;

            _floodRtInvSizeParam.SetValue(rtInvSize);

            for (int i = 0; i < _jfaPassCount; i++)
            {
                _device.SetRenderTarget(_jfaPong);
                _device.Clear(ClearOptions.Target, new Vector4(-1, -1, 0, 0), 0, 0);

                _stepSizeParam.SetValue((float)stepSize);
                _jfaFloodEffect.CurrentTechnique = _jfaFloodEffect.Techniques["Flood"];
                _jfaFloodEffect.CurrentTechnique.Passes[0].Apply();
                _device.Textures[0] = _jfaPing;
                _device.SamplerStates[0] = SamplerState.PointClamp;
                _device.SetVertexBuffer(_fullscreenQuad);
                _device.DrawPrimitives(PrimitiveType.TriangleList, 0, 2);

                _device.SetRenderTarget(null);

                var tmp = _jfaPing;
                _jfaPing = _jfaPong;
                _jfaPong = tmp;

                stepSize >>= 1;
            }
        }

        // ── Composite (scene + JFA + x-ray) ────────────────────────────

        public void Composite(float outlineWidth, Vector3 outlineColor,
            Vector3 xRayColor, float xRayAlpha, bool xRayEnabled)
        {
            _outlineWidthParam.SetValue(outlineWidth);
            _outlineColorParam.SetValue(new Vector4(outlineColor, 1.0f));
            _screenSizeParam.SetValue(new Vector4(_width, _height,
                1.0f / _width, 1.0f / _height));
            _xRayColorParam.SetValue(new Vector4(xRayColor, xRayAlpha));
            _xRayEnableParam.SetValue(xRayEnabled ? 1.0f : 0.0f);

            _jfaCompositeEffect.CurrentTechnique = _jfaCompositeEffect.Techniques["Composite"];
            _jfaCompositeEffect.CurrentTechnique.Passes[0].Apply();

            // Bind 4 textures AFTER Apply
            _device.Textures[0] = SceneRT;
            _device.Textures[1] = _jfaPing;       // JFA result
            _device.Textures[2] = FullMaskRT;     // full silhouette
            _device.Textures[3] = VisibleMaskRT;  // visible surfaces
            _device.SamplerStates[0] = SamplerState.LinearClamp;
            _device.SamplerStates[1] = SamplerState.PointClamp;
            _device.SamplerStates[2] = SamplerState.PointClamp;
            _device.SamplerStates[3] = SamplerState.PointClamp;

            _device.SetVertexBuffer(_fullscreenQuad);
            _device.DrawPrimitives(PrimitiveType.TriangleList, 0, 2);
        }

        // ── Resize ─────────────────────────────────────────────────────

        public void Resize(int width, int height)
        {
            if (width == _width && height == _height) return;
            _width = width;
            _height = height;

            SceneRT?.Dispose();
            FullMaskRT?.Dispose();
            VisibleMaskRT?.Dispose();
            _jfaPing?.Dispose();
            _jfaPong?.Dispose();

            SceneRT = new RenderTarget2D(_device, width, height, false,
                SurfaceFormat.Color, DepthFormat.Depth24Stencil8);
            FullMaskRT = new RenderTarget2D(_device, width, height, false,
                SurfaceFormat.Color, DepthFormat.None);
            VisibleMaskRT = new RenderTarget2D(_device, width, height, false,
                SurfaceFormat.Color, DepthFormat.Depth24Stencil8);
            _jfaPing = new RenderTarget2D(_device, width, height, false,
                SurfaceFormat.Vector2, DepthFormat.None);
            _jfaPong = new RenderTarget2D(_device, width, height, false,
                SurfaceFormat.Vector2, DepthFormat.None);

            int maxDim = Math.Max(width, height);
            _jfaPassCount = 0;
            int s = 1;
            while (s < maxDim) { s <<= 1; _jfaPassCount++; }
        }

        // ── Helpers ────────────────────────────────────────────────────

        private Effect LoadEffect(string resourceName)
        {
            var asm = Assembly.GetExecutingAssembly();
            using var stream = asm.GetManifestResourceStream(resourceName);
            if (stream == null)
            {
                var names = asm.GetManifestResourceNames();
                throw new InvalidOperationException(
                    $"Resource '{resourceName}' not found. Available: {string.Join(", ", names)}");
            }
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            return new Effect(_device, ms.ToArray());
        }

        // ── Dispose ────────────────────────────────────────────────────

        public void Dispose()
        {
            SceneRT?.Dispose();
            FullMaskRT?.Dispose();
            VisibleMaskRT?.Dispose();
            _jfaPing?.Dispose();
            _jfaPong?.Dispose();
            _sceneMaskEffect?.Dispose();
            _jfaInitEffect?.Dispose();
            _jfaFloodEffect?.Dispose();
            _jfaCompositeEffect?.Dispose();
            _fullscreenQuad?.Dispose();
        }
    }
}
