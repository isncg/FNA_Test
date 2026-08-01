using System;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Graphics.PackedVector;
using Microsoft.Xna.Framework.Input;
using FNA.Test;

namespace MaterialLib;

class Program : Game
{
    private GraphicsDeviceManager _gdm = null!;
    private Effect _effect = null!;
    private Effect _shadowEffect = null!;
    private Effect _envDebugEffect = null!;
    private Effect _skyboxEffect = null!;
    private Effect _irradianceConvEffect = null!;
    private Effect _brdfLutEffect = null!;
    private Effect _prefilterEnvEffect = null!;
    private Effect _gbufferEffect = null!;
    private Effect _ssaoEffect = null!;
    private Effect _blurAOEffect = null!;
    private RenderTarget2D _shadowMap = null!;
    private RenderTarget2D _gbufferRT = null!;
    private RenderTarget2D _ssaoRT = null!;
    private RenderTarget2D _blurAORT = null!;
    private Texture2D _irradianceMap = null!;
    private Texture2D _brdfLut = null!;
    private Texture2D _whiteTexR32F = null!;

    // SSAO state
    private bool _enableSSAO = true;
    private float _ssaoRadius = 0.5f;
    private float _ssaoBias = 0.025f;
    private float _ssaoIntensity = 1.0f;
    private float _blurSharpness = 0.1f;
    private int _ssaoW, _ssaoH; // cached RT dimensions for resize detection;

    // Env map debug view
    private VertexBuffer _fullscreenVB = null!;
    private bool _showEnvDebug;
    private float _envDebugPanX;
    private float _envDebugPanY = -0.15f; // center the photo studio in view
    private float _envDebugZoom = 1.0f;

    // Dedicated rasterizer for shadow pass — avoids corrupting the shared
    // RasterizerState.CullNone singleton when setting DepthBias per frame.
    private readonly RasterizerState _shadowRasterizer = new()
    {
        CullMode = CullMode.None,
        FillMode = FillMode.Solid,
        // CullNone: render both faces so mesh openings (spout, lid gap, top rim)
        // capture inner-surface depths instead of leaking to the floor.
    };

    // Teapot
    private VertexBuffer _teapotVB = null!;
    private int _teapotPrims;

    // Floor
    private VertexBuffer _floorVB = null!;
    private int _floorPrims;
    private static readonly Matrix FloorWorld = Matrix.Identity;
    private Matrix _teapotWorld;

    // Materials (shared pool, two independent selectors)
    private string[] _materialDirs = null!;
    private int _teapotMat;
    private int _floorMat;
    private Texture2D?[] _albedoMaps = null!;
    private Texture2D?[] _normalMaps = null!;
    private Texture2D?[] _ormMaps = null!;
    private Texture2D? _envMap;
    private Texture2D _defaultWhite = null!;
    private Texture2D _defaultNormal = null!;
    private Texture2D _defaultORM = null!;

    // Lighting
    private float _lightAzimuth = 0.8f;
    private float _lightAltitude = 0.6f;
    private float _lightIntensity = 3.0f;
    private float[] _teapotAlbedoTint = { 1f, 1f, 1f };
    private float _teapotMetallic = 1f;
    private float _teapotRoughness = 1f;
    private float[] _floorAlbedoTint = { 1f, 1f, 1f };
    private float _floorMetallic = 1f;
    private float _floorRoughness = 1f;

    private float _shadowBias = 0.02f;

    // Environment lighting
    private bool _useEnvOnly = true;
    private float _envIntensity = 1.0f;

    // Camera
    private OrbitCamera _camera = null!;

    // ── Initialisation ──────────────────────────────────────────────────────

    public Program()
    {
        _gdm = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = 1280,
            PreferredBackBufferHeight = 720,
            SynchronizeWithVerticalRetrace = false,
        };
        Window.Title = "FNA Material Library — PBR Teapot + Floor | ESC=quit";
        IsMouseVisible = true;
    }

    protected override void LoadContent()
    {
        // Load PBR effect from embedded FEB
        using var febStream = typeof(Program).Assembly
            .GetManifestResourceStream("MaterialLib.Shaders.PbrMaterial.feb")!;
        byte[] febBytes;
        using (var ms = new MemoryStream())
        {
            febStream.CopyTo(ms);
            febBytes = ms.ToArray();
        }
        _effect = new Effect(GraphicsDevice, febBytes);

        // Load shadow map effect from embedded FEB
        using var shadowFebStream = typeof(Program).Assembly
            .GetManifestResourceStream("MaterialLib.Shaders.ShadowMap.feb")!;
        byte[] shadowFebBytes;
        using (var ms2 = new MemoryStream())
        {
            shadowFebStream.CopyTo(ms2);
            shadowFebBytes = ms2.ToArray();
        }
        _shadowEffect = new Effect(GraphicsDevice, shadowFebBytes);

        // Load env debug effect from embedded FEB
        using var envDbgFebStream = typeof(Program).Assembly
            .GetManifestResourceStream("MaterialLib.Shaders.EnvDebug.feb")!;
        byte[] envDbgFebBytes;
        using (var ms3 = new MemoryStream())
        {
            envDbgFebStream.CopyTo(ms3);
            envDbgFebBytes = ms3.ToArray();
        }
        _envDebugEffect = new Effect(GraphicsDevice, envDbgFebBytes);

        // Load skybox effect from embedded FEB
        using var skyboxFebStream = typeof(Program).Assembly
            .GetManifestResourceStream("MaterialLib.Shaders.Skybox.feb")!;
        byte[] skyboxFebBytes;
        using (var ms4b = new MemoryStream())
        {
            skyboxFebStream.CopyTo(ms4b);
            skyboxFebBytes = ms4b.ToArray();
        }
        _skyboxEffect = new Effect(GraphicsDevice, skyboxFebBytes);

        // Load irradiance convolution effect from embedded FEB
        using var irrConvFebStream = typeof(Program).Assembly
            .GetManifestResourceStream("MaterialLib.Shaders.IrradianceConv.feb")!;
        byte[] irrConvFebBytes;
        using (var ms4 = new MemoryStream())
        {
            irrConvFebStream.CopyTo(ms4);
            irrConvFebBytes = ms4.ToArray();
        }
        _irradianceConvEffect = new Effect(GraphicsDevice, irrConvFebBytes);

        // Load BRDF LUT generation effect from embedded FEB
        using var brdfLutFebStream = typeof(Program).Assembly
            .GetManifestResourceStream("MaterialLib.Shaders.BrdfLut.feb")!;
        byte[] brdfLutFebBytes;
        using (var ms5 = new MemoryStream())
        {
            brdfLutFebStream.CopyTo(ms5);
            brdfLutFebBytes = ms5.ToArray();
        }
        _brdfLutEffect = new Effect(GraphicsDevice, brdfLutFebBytes);

        // Load prefilter environment effect from embedded FEB
        using var prefilterEnvFebStream = typeof(Program).Assembly
            .GetManifestResourceStream("MaterialLib.Shaders.PrefilterEnv.feb")!;
        byte[] prefilterEnvFebBytes;
        using (var ms6 = new MemoryStream())
        {
            prefilterEnvFebStream.CopyTo(ms6);
            prefilterEnvFebBytes = ms6.ToArray();
        }
        _prefilterEnvEffect = new Effect(GraphicsDevice, prefilterEnvFebBytes);

        // Load GBuffer effect from embedded FEB (SSAO pre-pass)
        using var gbufferFebStream = typeof(Program).Assembly
            .GetManifestResourceStream("MaterialLib.Shaders.GBuffer.feb")!;
        byte[] gbufferFebBytes;
        using (var ms7 = new MemoryStream())
        {
            gbufferFebStream.CopyTo(ms7);
            gbufferFebBytes = ms7.ToArray();
        }
        _gbufferEffect = new Effect(GraphicsDevice, gbufferFebBytes);

        // Load SSAO effect from embedded FEB
        using var ssaoFebStream = typeof(Program).Assembly
            .GetManifestResourceStream("MaterialLib.Shaders.SSAO.feb")!;
        byte[] ssaoFebBytes;
        using (var ms8 = new MemoryStream())
        {
            ssaoFebStream.CopyTo(ms8);
            ssaoFebBytes = ms8.ToArray();
        }
        _ssaoEffect = new Effect(GraphicsDevice, ssaoFebBytes);

        // Load AO bilateral blur effect from embedded FEB
        using var blurAOFebStream = typeof(Program).Assembly
            .GetManifestResourceStream("MaterialLib.Shaders.BlurAO.feb")!;
        byte[] blurAOFebBytes;
        using (var ms9 = new MemoryStream())
        {
            blurAOFebStream.CopyTo(ms9);
            blurAOFebBytes = ms9.ToArray();
        }
        _blurAOEffect = new Effect(GraphicsDevice, blurAOFebBytes);

        // Default white texture for SSAO when disabled (R32F)
        _whiteTexR32F = new Texture2D(GraphicsDevice, 1, 1, false, SurfaceFormat.Single);
        _whiteTexR32F.SetData(new[] { 1.0f });

        // Shadow map render target: R32F depth + D24S8
        _shadowMap = new RenderTarget2D(GraphicsDevice, 2048, 2048, false,
            SurfaceFormat.Single, DepthFormat.Depth24Stencil8);

        // ── Teapot ──────────────────────────────────────────────────────────
        /* Generated from GLUT's 32 Bezier patches instead of a .tris dump: UVs
         * come from the patch parameter domain and normals from the analytic
         * derivatives, so nothing has to be inferred from vertex positions.
         * scale 2 keeps the ~3.15 unit height the loaded mesh had.
         */
        var verts = GlutTeapot.Build(grid: 10, scale: 2f);
        _teapotPrims = verts.Length / 3;
        _teapotVB = new VertexBuffer(GraphicsDevice, typeof(VertexPositionNormalTexture),
            verts.Length, BufferUsage.WriteOnly);
        _teapotVB.SetData(verts);
        Console.WriteLine($"Teapot: {_teapotPrims} triangles from 32 Bezier patches (grid 10)");

        // Position teapot so its bottom rests on the floor (Y = 0)
        float teapotYMin = verts.Min(v => v.Position.Y);
        _teapotWorld = Matrix.CreateTranslation(0, -teapotYMin, 0);

        // ── Floor ───────────────────────────────────────────────────────────
        var floorVerts = CreateFloor(8f, 4);
        _floorPrims = floorVerts.Length / 3;
        _floorVB = new VertexBuffer(GraphicsDevice, TeapotModel.VertexDeclaration,
            floorVerts.Length, BufferUsage.WriteOnly);
        _floorVB.SetData(floorVerts);

        // ── Fullscreen triangle for env debug pass ──────────────────────────
        // Single triangle covering the entire viewport. UVs beyond [0,1]
        // at vertices 1 and 2 interpolate correctly over the visible region.
        var fsVerts = new VertexPositionTexture[3];
        fsVerts[0] = new VertexPositionTexture(new Vector3(-1, -1, 0), new Vector2(0, 0));
        fsVerts[1] = new VertexPositionTexture(new Vector3( 3, -1, 0), new Vector2(2, 0));
        fsVerts[2] = new VertexPositionTexture(new Vector3(-1,  3, 0), new Vector2(0, 2));
        _fullscreenVB = new VertexBuffer(GraphicsDevice, typeof(VertexPositionTexture),
            3, BufferUsage.WriteOnly);
        _fullscreenVB.SetData(fsVerts);

        // ── Textures ────────────────────────────────────────────────────────
        _defaultWhite  = MakeColorTex(Color.White);
        _defaultNormal = MakeColorTex(new Color(128, 128, 255, 255));
        _defaultORM    = MakeColorTex(new Color(255, 128, 0, 255));

        var assetsBase = FindAssetsBase();
        _materialDirs = Directory.GetDirectories(assetsBase)
            .Where(d => !d.EndsWith("hdris"))
            .OrderBy(d => d)
            .ToArray();

        Console.WriteLine($"Found {_materialDirs.Length} materials in {assetsBase}");
        _albedoMaps = new Texture2D[_materialDirs.Length];
        _normalMaps = new Texture2D[_materialDirs.Length];
        _ormMaps    = new Texture2D[_materialDirs.Length];

        for (int i = 0; i < _materialDirs.Length; i++)
        {
            var dir = _materialDirs[i];
            var name = Path.GetFileName(dir);
            _albedoMaps[i] = LoadTex(Path.Combine(dir, $"{name}_albedo.jpg")) ?? _defaultWhite;
            _normalMaps[i] = LoadTex(Path.Combine(dir, $"{name}_normal.jpg")) ?? _defaultNormal;
            _ormMaps[i]    = LoadTex(Path.Combine(dir, $"{name}_packed.jpg"))   ?? _defaultORM;
            Console.WriteLine($"  [{i}] {name}");
        }

        // HDRI environment map for IBL
        var hdriDir = Path.Combine(assetsBase, "hdris");
        _envMap = null;
        if (Directory.Exists(hdriDir))
        {
            foreach (var hf in Directory.GetFiles(hdriDir, "*.hdr"))
            {
                try
                {
                    _envMap = HdriLoader.Load(GraphicsDevice, hf, mipMap: true);
                    break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  Failed to load HDRI {Path.GetFileName(hf)}: {ex.Message}");
                }
            }
        }
        if (_envMap == null)
        {
            Console.WriteLine("Creating procedural env map (HDRI not loadable)");
            _envMap = MakeSkyGradient(GraphicsDevice, 512, 256);
        }

        // Pre-compute diffuse irradiance map from the env map (game-engine standard IBL).
        Console.Write("Generating irradiance map... ");
        _irradianceMap = GenerateIrradianceMap(GraphicsDevice, _envMap);
        Console.WriteLine($"done ({_irradianceMap.Width}×{_irradianceMap.Height})");

        // Generate BRDF integration LUT (split-sum approximation)
        Console.Write("Generating BRDF LUT... ");
        _brdfLut = GenerateBrdfLut(GraphicsDevice);
        Console.WriteLine("done");

        // Generate GGX-prefiltered env map mip chain (specular IBL)
        Console.Write("Generating prefiltered mip chain... ");
        GeneratePrefilteredMipChain(GraphicsDevice, _envMap);
        Console.WriteLine("done");

        ImGuiTestHarness.Init(GraphicsDevice);
        _camera = new OrbitCamera { Distance = 8f, Pitch = 0.0f };
        _floorMat = 5; // default floor: marble
    }

    // ── Per-frame ───────────────────────────────────────────────────────────

    protected override void Update(GameTime gameTime)
    {
        var kb = Keyboard.GetState();
        if (kb.IsKeyDown(Keys.Escape)) Exit();
        _camera.Update(true);
    }

    protected override void Draw(GameTime gameTime)
    {
        ImGuiTestHarness.NewFrame(GraphicsDevice);
        GraphicsDevice.Clear(new Color(30, 30, 32));
        GraphicsDevice.DepthStencilState = DepthStencilState.Default;
        GraphicsDevice.BlendState = BlendState.Opaque;

        if (_showEnvDebug)
        {
            // ── Env map debug view ──────────────────────────────────────────
            GraphicsDevice.RasterizerState = RasterizerState.CullNone;
            GraphicsDevice.DepthStencilState = DepthStencilState.None;

            GraphicsDevice.Textures[0] = _envMap ?? _defaultWhite;
            GraphicsDevice.SamplerStates[0] = SamplerState.LinearClamp;

            _envDebugEffect.Parameters["PanOffset"].SetValue(
                new Vector2(_envDebugPanX, _envDebugPanY));
            _envDebugEffect.Parameters["Zoom"].SetValue(_envDebugZoom);

            _envDebugEffect.CurrentTechnique!.Passes[0].Apply();
            GraphicsDevice.SetVertexBuffer(_fullscreenVB);
            GraphicsDevice.DrawPrimitives(PrimitiveType.TriangleList, 0, 1);
        }
        else
        {
            float aspect = (float)_gdm.PreferredBackBufferWidth / _gdm.PreferredBackBufferHeight;
            var view = _camera.ViewMatrix;
            var proj = Matrix.CreatePerspectiveFieldOfView(MathHelper.PiOver4, aspect, 0.1f, 100f);

            var lightDir = new Vector3(
                MathF.Cos(_lightAltitude) * MathF.Sin(_lightAzimuth),
                MathF.Sin(_lightAltitude),
                MathF.Cos(_lightAltitude) * MathF.Cos(_lightAzimuth));
            var eyePos = _camera.GetEyePosition();

            // ── G-Buffer + SSAO + Blur pass ─────────────────────────────────
            if (_enableSSAO)
            {
                EnsureSSAORTs();
                RenderGBuffer(view, proj);
                RenderSSAO(proj);
                RenderBlurAO();
            }

            // ── Skybox (before geometry, behind everything) ──────────────────
            RenderSkybox(view, proj, eyePos);

            // Restore depth testing after skybox (which disables it)
            GraphicsDevice.DepthStencilState = DepthStencilState.Default;

            var lightCol = new Vector3(_lightIntensity, _lightIntensity * 0.9f, _lightIntensity * 0.8f);
            var lightVP = ComputeLightViewProj(lightDir);

            // ── Shadow pass (directional light only) ─────────────────────────
            if (!_useEnvOnly)
            {
                _shadowRasterizer.DepthBias = _shadowBias;
                RenderShadowMap(lightDir, lightVP);
            }
            GraphicsDevice.RasterizerState = RasterizerState.CullCounterClockwise;

            // ── Main pass ──────────────────────────────────────────────────
            GraphicsDevice.Textures[4] = _shadowMap;
            GraphicsDevice.SamplerStates[4] = SamplerState.PointClamp;

            // SSAO texture (blurred, or raw if blur unavailable; white fallback when disabled)
            GraphicsDevice.Textures[7] = _enableSSAO ? _blurAORT! : _whiteTexR32F;
            GraphicsDevice.SamplerStates[7] = SamplerState.LinearClamp;

            BindMaterial(_floorMat);
            SetPerFrameParams(eyePos, lightDir, lightCol,
                new Vector3(_floorAlbedoTint[0], _floorAlbedoTint[1], _floorAlbedoTint[2]),
                _floorMetallic, _floorRoughness, lightVP);
            DrawObject(_floorVB, _floorPrims, FloorWorld, view, proj);

            BindMaterial(_teapotMat);
            SetPerFrameParams(eyePos, lightDir, lightCol,
                new Vector3(_teapotAlbedoTint[0], _teapotAlbedoTint[1], _teapotAlbedoTint[2]),
                _teapotMetallic, _teapotRoughness, lightVP);
            DrawObject(_teapotVB, _teapotPrims, _teapotWorld, view, proj);
        }

        // ── ImGui ───────────────────────────────────────────────────────────
        var matNames = _materialDirs.Select(Path.GetFileName).ToArray()!;
        if (!TestHarness.Headless)
        {
            GraphicsDevice.BlendState = BlendState.AlphaBlend;
            ImGuiBindings.BeginPanel("Material Library");

            ImGuiBindings.ImGui_Separator();
            ImGuiBindings.ImGui_Text("View Mode");
            ImGuiBindings.ImGui_Checkbox("Env Map Debug", ref _showEnvDebug);
            if (_showEnvDebug)
            {
                ImGuiBindings.ImGui_SliderFloat("Pan X", ref _envDebugPanX, -1f, 1f);
                ImGuiBindings.ImGui_SliderFloat("Pan Y", ref _envDebugPanY, -1f, 1f);
                ImGuiBindings.ImGui_SliderFloat("Zoom", ref _envDebugZoom, 0.25f, 4f);
            }

            ImGuiBindings.ImGui_Separator();
            ImGuiBindings.Combo("Teapot", ref _teapotMat, matNames);
            ImGuiBindings.Combo("Floor", ref _floorMat, matNames);

            ImGuiBindings.ImGui_Separator();
            ImGuiBindings.ImGui_Text("Teapot Material");
            ImGuiBindings.ImGui_SliderFloat("Metallic##Teapot", ref _teapotMetallic, 0f, 2f);
            ImGuiBindings.ImGui_SliderFloat("Roughness##Teapot", ref _teapotRoughness, 0f, 2f);
            ImGuiBindings.ImGui_ColorEdit3("Albedo Tint##Teapot", _teapotAlbedoTint, 0);

            ImGuiBindings.ImGui_Separator();
            ImGuiBindings.ImGui_Text("Floor Material");
            ImGuiBindings.ImGui_SliderFloat("Metallic##Floor", ref _floorMetallic, 0f, 2f);
            ImGuiBindings.ImGui_SliderFloat("Roughness##Floor", ref _floorRoughness, 0f, 2f);
            ImGuiBindings.ImGui_ColorEdit3("Albedo Tint##Floor", _floorAlbedoTint, 0);

            ImGuiBindings.ImGui_Separator();
            ImGuiBindings.ImGui_Text("Lighting");
            ImGuiBindings.ImGui_Checkbox("Env Only (IBL)", ref _useEnvOnly);
            if (_useEnvOnly)
            {
                ImGuiBindings.ImGui_SliderFloat("Env Intensity", ref _envIntensity, 0.1f, 2f);
            }
            else
            {
                ImGuiBindings.ImGui_SliderFloat("Light Azimuth", ref _lightAzimuth, -MathHelper.Pi, MathHelper.Pi);
                ImGuiBindings.ImGui_SliderFloat("Light Altitude", ref _lightAltitude, -MathHelper.PiOver2, MathHelper.PiOver2);
                ImGuiBindings.ImGui_SliderFloat("Light Intensity", ref _lightIntensity, 0f, 10f);
            }

            ImGuiBindings.ImGui_Separator();
            ImGuiBindings.ImGui_Text("SSAO");
            ImGuiBindings.ImGui_Checkbox("Enable##SSAO", ref _enableSSAO);
            if (_enableSSAO)
            {
                ImGuiBindings.ImGui_SliderFloat("Radius##SSAO", ref _ssaoRadius, 0.1f, 3.0f);
                ImGuiBindings.ImGui_SliderFloat("Bias##SSAO", ref _ssaoBias, 0.0f, 0.1f);
                ImGuiBindings.ImGui_SliderFloat("Intensity##SSAO", ref _ssaoIntensity, 0.0f, 3.0f);
                ImGuiBindings.ImGui_SliderFloat("Blur Sharpness##SSAO", ref _blurSharpness, 0.02f, 0.5f);
            }

            ImGuiBindings.ImGui_Separator();
            ImGuiBindings.ImGui_Text("Shadow Bias");
            ImGuiBindings.ImGui_SliderFloat("Bias Factor", ref _shadowBias, 0.0f, 0.1f);

            ImGuiBindings.EndPanel();
        }
        else
        {
            // Headless: use directional light for reliable coverage testing
            _useEnvOnly = false;
            TestHarness.Tick(this, 3, () =>
            {
                var px = TestHarness.ReadBackbuffer(GraphicsDevice);
                int blankPixels = TestHarness.AssertCoverage(px, new Color(0, 0, 0), 0.90f,
                    $"not-blank-{matNames[_teapotMat]}");
                TestHarness.Report($"MaterialLib[{matNames[_teapotMat]}+{matNames[_floorMat]}]", blankPixels);
            });
        }
    }

    // ── Rendering helpers ───────────────────────────────────────────────────

    private Matrix ComputeLightViewProj(Vector3 lightDir)
    {
        var sceneCenter = new Vector3(0, 0.5f, 0);
        float halfSize = 10f;
        var lightPos = sceneCenter + lightDir * 25f;
        var up = MathF.Abs(lightDir.Y) > 0.999f ? Vector3.Forward : Vector3.Up;
        var lightView = Matrix.CreateLookAt(lightPos, sceneCenter, up);
        var lightProj = Matrix.CreateOrthographic(halfSize * 2, halfSize * 2, 0.1f, 50f);
        return lightView * lightProj;
    }

    private void EnsureSSAORTs()
    {
        int w = _gdm.PreferredBackBufferWidth;
        int h = _gdm.PreferredBackBufferHeight;
        if (_gbufferRT != null && _ssaoRT != null && w == _ssaoW && h == _ssaoH)
            return;

        _gbufferRT?.Dispose();
        _ssaoRT?.Dispose();
        _blurAORT?.Dispose();

        _gbufferRT = new RenderTarget2D(GraphicsDevice, w, h, false,
            SurfaceFormat.HalfVector4, DepthFormat.Depth24Stencil8);
        _ssaoRT = new RenderTarget2D(GraphicsDevice, w, h, false,
            SurfaceFormat.Single, DepthFormat.None);
        _blurAORT = new RenderTarget2D(GraphicsDevice, w, h, false,
            SurfaceFormat.Single, DepthFormat.None);
        _ssaoW = w;
        _ssaoH = h;
    }

    private void RenderGBuffer(Matrix view, Matrix proj)
    {
        GraphicsDevice.SetRenderTarget(_gbufferRT);
        GraphicsDevice.Clear(new Color(0, 0, 0, 0));
        GraphicsDevice.DepthStencilState = DepthStencilState.Default;
        GraphicsDevice.RasterizerState = RasterizerState.CullCounterClockwise;
        GraphicsDevice.BlendState = BlendState.Opaque;

        _gbufferEffect.CurrentTechnique!.Passes[0].Apply();

        // Floor
        var worldView = FloorWorld * view;
        _gbufferEffect.Parameters["WorldViewProj"].SetValue(FloorWorld * view * proj);
        _gbufferEffect.Parameters["WorldView"].SetValue(worldView);
        _gbufferEffect.CurrentTechnique!.Passes[0].Apply();
        GraphicsDevice.SetVertexBuffer(_floorVB);
        GraphicsDevice.DrawPrimitives(PrimitiveType.TriangleList, 0, _floorPrims);

        // Teapot
        worldView = _teapotWorld * view;
        _gbufferEffect.Parameters["WorldViewProj"].SetValue(_teapotWorld * view * proj);
        _gbufferEffect.Parameters["WorldView"].SetValue(worldView);
        _gbufferEffect.CurrentTechnique!.Passes[0].Apply();
        GraphicsDevice.SetVertexBuffer(_teapotVB);
        GraphicsDevice.DrawPrimitives(PrimitiveType.TriangleList, 0, _teapotPrims);

        GraphicsDevice.SetRenderTarget(null);
    }

    private void RenderSSAO(Matrix proj)
    {
        GraphicsDevice.SetRenderTarget(_ssaoRT);
        GraphicsDevice.Clear(new Color(1.0f, 1.0f, 1.0f, 1.0f)); // white = no occlusion
        GraphicsDevice.DepthStencilState = DepthStencilState.None;
        GraphicsDevice.RasterizerState = RasterizerState.CullNone;
        GraphicsDevice.BlendState = BlendState.Opaque;

        GraphicsDevice.Textures[0] = _gbufferRT;
        GraphicsDevice.SamplerStates[0] = SamplerState.PointClamp;

        _ssaoEffect.Parameters["Projection"].SetValue(proj);
        _ssaoEffect.Parameters["SSAOParams"].SetValue(
            new Vector4(_ssaoRadius, _ssaoBias, _ssaoIntensity, 0));
        _ssaoEffect.CurrentTechnique!.Passes[0].Apply();

        GraphicsDevice.SetVertexBuffer(_fullscreenVB);
        GraphicsDevice.DrawPrimitives(PrimitiveType.TriangleList, 0, 1);

        GraphicsDevice.SetRenderTarget(null);
    }

    private void RenderBlurAO()
    {
        GraphicsDevice.SetRenderTarget(_blurAORT);
        GraphicsDevice.Clear(new Color(1.0f, 1.0f, 1.0f, 1.0f));
        GraphicsDevice.DepthStencilState = DepthStencilState.None;
        GraphicsDevice.RasterizerState = RasterizerState.CullNone;
        GraphicsDevice.BlendState = BlendState.Opaque;

        GraphicsDevice.Textures[0] = _ssaoRT;
        GraphicsDevice.SamplerStates[0] = SamplerState.PointClamp;
        GraphicsDevice.Textures[1] = _gbufferRT;
        GraphicsDevice.SamplerStates[1] = SamplerState.PointClamp;

        float texelW = 1.0f / _ssaoW;
        float texelH = 1.0f / _ssaoH;
        _blurAOEffect.Parameters["TexelSize"].SetValue(new Vector2(texelW, texelH));
        _blurAOEffect.Parameters["BlurSharpness"].SetValue(_blurSharpness);
        _blurAOEffect.CurrentTechnique!.Passes[0].Apply();

        GraphicsDevice.SetVertexBuffer(_fullscreenVB);
        GraphicsDevice.DrawPrimitives(PrimitiveType.TriangleList, 0, 1);

        GraphicsDevice.SetRenderTarget(null);
    }

    private void RenderSkybox(Matrix view, Matrix proj, Vector3 eyePos)
    {
        // Extract camera basis from the view matrix (world-space directions).
        // The OrbitCamera's Target is the look-at point.
        var forward = Vector3.Normalize(_camera.Target - eyePos);
        var right   = Vector3.Normalize(Vector3.Cross(Vector3.Up, forward));
        var up      = Vector3.Cross(forward, right); // orthonormal

        float fov = MathHelper.PiOver4;
        float aspect = (float)_gdm.PreferredBackBufferWidth
                     / _gdm.PreferredBackBufferHeight;
        float tanHalfFov = MathF.Tan(fov / 2f);
        float fovX = tanHalfFov * aspect; // horizontal spread
        float fovY = tanHalfFov;          // vertical spread

        GraphicsDevice.RasterizerState = RasterizerState.CullNone;
        GraphicsDevice.DepthStencilState = DepthStencilState.None;
        GraphicsDevice.BlendState = BlendState.Opaque;

        GraphicsDevice.Textures[0] = _envMap ?? _defaultWhite;
        GraphicsDevice.SamplerStates[0] = SamplerState.LinearClamp;

        _skyboxEffect.Parameters["CameraForward"].SetValue(forward);
        _skyboxEffect.Parameters["CameraRight"].SetValue(right);
        _skyboxEffect.Parameters["CameraUp"].SetValue(up);
        _skyboxEffect.Parameters["FovParams"].SetValue(new Vector2(fovX, fovY));
        _skyboxEffect.CurrentTechnique!.Passes[0].Apply();

        GraphicsDevice.SetVertexBuffer(_fullscreenVB);
        GraphicsDevice.DrawPrimitives(PrimitiveType.TriangleList, 0, 1);
    }

    private void RenderShadowMap(Vector3 lightDir, Matrix lightVP)
    {
        GraphicsDevice.SetRenderTarget(_shadowMap);
        GraphicsDevice.Clear(ClearOptions.Target | ClearOptions.DepthBuffer,
            new Color(1.0f, 1.0f, 1.0f, 1.0f), 1.0f, 0);
        GraphicsDevice.DepthStencilState = DepthStencilState.Default;
        GraphicsDevice.BlendState = BlendState.Opaque;
        GraphicsDevice.RasterizerState = _shadowRasterizer;

        // Draw floor
        _shadowEffect.Parameters["WorldViewProj"].SetValue(FloorWorld * lightVP);
        _shadowEffect.CurrentTechnique!.Passes[0].Apply();
        GraphicsDevice.SetVertexBuffer(_floorVB);
        GraphicsDevice.DrawPrimitives(PrimitiveType.TriangleList, 0, _floorPrims);

        // Draw teapot
        _shadowEffect.Parameters["WorldViewProj"].SetValue(_teapotWorld * lightVP);
        _shadowEffect.CurrentTechnique!.Passes[0].Apply();
        GraphicsDevice.SetVertexBuffer(_teapotVB);
        GraphicsDevice.DrawPrimitives(PrimitiveType.TriangleList, 0, _teapotPrims);

        GraphicsDevice.SetRenderTarget(null);
    }

    private void BindMaterial(int matIndex)
    {
        GraphicsDevice.Textures[0] = _albedoMaps[matIndex];
        GraphicsDevice.SamplerStates[0] = SamplerState.AnisotropicWrap;
        GraphicsDevice.Textures[1] = _normalMaps[matIndex];
        GraphicsDevice.SamplerStates[1] = SamplerState.AnisotropicWrap;
        GraphicsDevice.Textures[2] = _ormMaps[matIndex];
        GraphicsDevice.SamplerStates[2] = SamplerState.AnisotropicWrap;
        GraphicsDevice.Textures[3] = _envMap ?? _defaultWhite;
        GraphicsDevice.SamplerStates[3] = SamplerState.LinearClamp;
        GraphicsDevice.Textures[5] = _irradianceMap;
        GraphicsDevice.SamplerStates[5] = SamplerState.LinearClamp;
        GraphicsDevice.Textures[6] = _brdfLut;
        GraphicsDevice.SamplerStates[6] = SamplerState.LinearClamp;
    }

    private void SetPerFrameParams(Vector3 eye, Vector3 lightDir, Vector3 lightCol,
        Vector3 albedoTint, float metallicScale, float roughnessScale, Matrix lightVP)
    {
        _effect.Parameters["EyePosition"].SetValue(eye);
        _effect.Parameters["LightDirection"].SetValue(lightDir);
        _effect.Parameters["LightColor"].SetValue(lightCol);
        _effect.Parameters["AlbedoTint"].SetValue(albedoTint);
        _effect.Parameters["MetallicScale"].SetValue(metallicScale);
        _effect.Parameters["RoughnessScale"].SetValue(roughnessScale);
        _effect.Parameters["LightViewProj"].SetValue(lightVP);

        _effect.Parameters["UseEnvOnly"].SetValue(_useEnvOnly ? 1.0f : 0.0f);
        _effect.Parameters["EnvIntensity"].SetValue(_envIntensity);
    }

    private void DrawObject(VertexBuffer vb, int primCount, Matrix world,
        Matrix view, Matrix proj)
    {
        var worldViewProj = world * view * proj;
        var worldInvTransp = Matrix.Transpose(Matrix.Invert(world));

        _effect.Parameters["WorldViewProj"].SetValue(worldViewProj);
        _effect.Parameters["World"].SetValue(world);
        _effect.Parameters["WorldInverseTranspose"].SetValue(worldInvTransp);

        _effect.CurrentTechnique!.Passes[0].Apply();
        GraphicsDevice.SetVertexBuffer(vb);
        GraphicsDevice.DrawPrimitives(PrimitiveType.TriangleList, 0, primCount);
    }

    // ── Floor geometry ──────────────────────────────────────────────────────

    private static TeapotModel.Vertex[] CreateFloor(float halfSize, int tileCount)
    {
        var v = new TeapotModel.Vertex[6]; // 2 triangles
        var n = Vector3.Up;
        float s = halfSize;
        float t = tileCount;

        // Two triangles: (0,0)-(1,0)-(0,1) and (1,0)-(1,1)-(0,1)
        v[0] = new TeapotModel.Vertex(new Vector3(-s, 0, -s), n, new Vector2(0, 0));
        v[1] = new TeapotModel.Vertex(new Vector3( s, 0, -s), n, new Vector2(t, 0));
        v[2] = new TeapotModel.Vertex(new Vector3(-s, 0,  s), n, new Vector2(0, t));
        v[3] = new TeapotModel.Vertex(new Vector3( s, 0, -s), n, new Vector2(t, 0));
        v[4] = new TeapotModel.Vertex(new Vector3( s, 0,  s), n, new Vector2(t, t));
        v[5] = new TeapotModel.Vertex(new Vector3(-s, 0,  s), n, new Vector2(0, t));

        return v;
    }

    // ── Cleanup ─────────────────────────────────────────────────────────────

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            ImGuiTestHarness.Shutdown(GraphicsDevice);
            _effect?.Dispose();
            _shadowEffect?.Dispose();
            _shadowMap?.Dispose();
            _teapotVB?.Dispose();
            _floorVB?.Dispose();
            _defaultWhite?.Dispose();
            _defaultNormal?.Dispose();
            _defaultORM?.Dispose();
            _envMap?.Dispose();
            _envDebugEffect?.Dispose();
            _skyboxEffect?.Dispose();
            _irradianceMap?.Dispose();
            _irradianceConvEffect?.Dispose();
            _brdfLut?.Dispose();
            _brdfLutEffect?.Dispose();
            _prefilterEnvEffect?.Dispose();
            _gbufferEffect?.Dispose();
            _ssaoEffect?.Dispose();
            _blurAOEffect?.Dispose();
            _gbufferRT?.Dispose();
            _ssaoRT?.Dispose();
            _blurAORT?.Dispose();
            _whiteTexR32F?.Dispose();
            foreach (var t in _albedoMaps) t?.Dispose();
            foreach (var t in _normalMaps) t?.Dispose();
            foreach (var t in _ormMaps) t?.Dispose();
        }
        base.Dispose(disposing);
    }

    // ── Texture helpers ─────────────────────────────────────────────────────

    private Texture2D? LoadTex(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            using var stream = File.OpenRead(path);
            return Texture2D.FromStream(GraphicsDevice, stream);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Failed to load {path}: {ex.Message}");
            return null;
        }
    }

    private Texture2D GenerateIrradianceMap(GraphicsDevice device, Texture2D envMap)
    {
        // Low-res is fine — irradiance is heavily blurred (hemisphere integral).
        const int irrW = 128, irrH = 64;
        var rt = new RenderTarget2D(device, irrW, irrH, false,
            SurfaceFormat.HalfVector4, DepthFormat.None);

        device.SetRenderTarget(rt);
        device.Clear(Color.Black);
        device.DepthStencilState = DepthStencilState.None;
        device.RasterizerState = RasterizerState.CullNone;
        device.BlendState = BlendState.Opaque;

        device.Textures[0] = envMap;
        device.SamplerStates[0] = SamplerState.LinearClamp;

        _irradianceConvEffect.CurrentTechnique!.Passes[0].Apply();
        device.SetVertexBuffer(_fullscreenVB);
        device.DrawPrimitives(PrimitiveType.TriangleList, 0, 1);

        device.SetRenderTarget(null);

        // Resolve through CPU memory to avoid potential Vulkan layout-transition
        // issues when using RenderTarget2D directly as a shader input.
        var pixels = new HalfVector4[irrW * irrH];
        rt.GetData(pixels);

        // Validate: check a few sample pixels are non-black
        float maxR = 0, maxG = 0, maxB = 0;
        foreach (var p in pixels)
        {
            var v = p.ToVector4();
            maxR = MathF.Max(maxR, v.X);
            maxG = MathF.Max(maxG, v.Y);
            maxB = MathF.Max(maxB, v.Z);
        }
        Console.WriteLine($"  irradiance peak RGB=({maxR:F3}, {maxG:F3}, {maxB:F3})");

        var result = new Texture2D(device, irrW, irrH, false, SurfaceFormat.HalfVector4);
        result.SetData(pixels);
        rt.Dispose();
        return result;
    }

    private Texture2D GenerateBrdfLut(GraphicsDevice device)
    {
        const int size = 256;
        var rt = new RenderTarget2D(device, size, size, false,
            SurfaceFormat.HalfVector4, DepthFormat.None);

        device.SetRenderTarget(rt);
        device.Clear(Color.Black);
        device.DepthStencilState = DepthStencilState.None;
        device.RasterizerState = RasterizerState.CullNone;
        device.BlendState = BlendState.Opaque;

        _brdfLutEffect.CurrentTechnique!.Passes[0].Apply();
        device.SetVertexBuffer(_fullscreenVB);
        device.DrawPrimitives(PrimitiveType.TriangleList, 0, 1);

        device.SetRenderTarget(null);

        var pixels = new HalfVector4[size * size];
        rt.GetData(pixels);
        rt.Dispose();

        // Validate
        float maxR = 0, maxG = 0;
        foreach (var p in pixels)
        {
            var v = p.ToVector4();
            maxR = MathF.Max(maxR, v.X);
            maxG = MathF.Max(maxG, v.Y);
        }
        Console.WriteLine($"  BRDF LUT peak RG=({maxR:F3}, {maxG:F3})");

        var result = new Texture2D(device, size, size, false, SurfaceFormat.HalfVector4);
        result.SetData(pixels);
        return result;
    }

    private void GeneratePrefilteredMipChain(GraphicsDevice device, Texture2D envMap)
    {
        int w = envMap.Width, h = envMap.Height;
        int levels = 1;
        while (w > 4 && h > 4) { w /= 2; h /= 2; levels++; }

        w = envMap.Width;
        h = envMap.Height;
        for (int level = 1; level < levels; level++)
        {
            w /= 2;
            h /= 2;
            float roughness = (float)level / (levels - 1);

            var rt = new RenderTarget2D(device, w, h, false,
                SurfaceFormat.HalfVector4, DepthFormat.None);
            device.SetRenderTarget(rt);
            device.Clear(Color.Black);
            device.DepthStencilState = DepthStencilState.None;
            device.RasterizerState = RasterizerState.CullNone;
            device.BlendState = BlendState.Opaque;

            device.Textures[0] = envMap; // SampleLevel(..., lod=0) reads only base mip
            device.SamplerStates[0] = SamplerState.LinearClamp;

            _prefilterEnvEffect.Parameters["MipRoughness"].SetValue(roughness);
            _prefilterEnvEffect.CurrentTechnique!.Passes[0].Apply();
            device.SetVertexBuffer(_fullscreenVB);
            device.DrawPrimitives(PrimitiveType.TriangleList, 0, 1);

            device.SetRenderTarget(null);
            device.Textures[0] = null; // unbind before modifying texture

            var pixels = new HalfVector4[w * h];
            rt.GetData(pixels);
            envMap.SetData(level, null, pixels, 0, pixels.Length);
            rt.Dispose();
        }

        Console.WriteLine($"{levels - 1} levels");
    }

    private Texture2D MakeColorTex(Color color)
    {
        var tex = new Texture2D(GraphicsDevice, 1, 1);
        tex.SetData(new[] { color });
        return tex;
    }

    private static Texture2D MakeSkyGradient(GraphicsDevice device, int width, int height)
    {
        var tex = new Texture2D(device, width, height);
        var data = new Color[width * height];
        var skyTop  = new Color(200, 210, 230);
        var skyMid  = new Color(240, 235, 225);
        var horizon = new Color(250, 245, 235);
        var ground  = new Color(200, 190, 170);

        for (int y = 0; y < height; y++)
        {
            float v = (float)y / (height - 1);
            Color baseColor;
            if (v < 0.25f)
                baseColor = Color.Lerp(skyTop, skyMid, v / 0.25f);
            else if (v < 0.4f)
                baseColor = Color.Lerp(skyMid, horizon, (v - 0.25f) / 0.15f);
            else if (v < 0.6f)
                baseColor = horizon;
            else
                baseColor = Color.Lerp(horizon, ground, (v - 0.6f) / 0.4f);

            for (int x = 0; x < width; x++)
            {
                float u = (float)x / (width - 1);
                float dx = u - 0.5f;
                float dy = v - 0.15f;
                float spotDist = MathF.Sqrt(dx * dx + dy * dy * 0.3f);
                float spot = MathF.Max(0, 1.0f - spotDist * 3.5f);
                spot *= spot;

                var c = baseColor;
                c.R = (byte)Math.Min(255, c.R + (int)(spot * 50));
                c.G = (byte)Math.Min(255, c.G + (int)(spot * 50));
                c.B = (byte)Math.Min(255, c.B + (int)(spot * 55));
                data[y * width + x] = c;
            }
        }
        tex.SetData(data);
        return tex;
    }

    private static string FindAssetsBase()
    {
        string[] candidates =
        {
            Path.Combine(AppContext.BaseDirectory, "assets", "materials"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "assets", "materials"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "assets", "materials"),
            "../assets/materials",
            "../../assets/materials",
            "../../../assets/materials",
        };

        foreach (var c in candidates)
        {
            var full = Path.GetFullPath(c);
            if (Directory.Exists(full)) return full;
        }

        throw new DirectoryNotFoundException(
            "Cannot find assets/materials directory. Tried: " +
            string.Join(", ", candidates.Select(Path.GetFullPath)));
    }

    static void Main(string[] args)
    {
        TestHarness.ParseArgs(args);
        using var game = new Program();
        game.Run();
    }
}
