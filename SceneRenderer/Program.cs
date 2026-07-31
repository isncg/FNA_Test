using System;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using FNA.Test;

namespace SceneRenderer;

class Program : Game
{
    private GraphicsDeviceManager _gdm = null!;
    private SceneRendererEngine _renderer = null!;
    private Scene _scene = null!;
    private SceneCamera _camera = null!;

    // Teapot model
    private VertexBuffer _teapotVB = null!;
    private int _teapotPrims;

    // Default textures
    private Texture2D _defaultWhite = null!;
    private Texture2D _defaultNormal = null!;
    private Texture2D _defaultORM = null!;

    // Material palette
    private string[] _materialDirs = null!;
    private int _teapotMat;
    private int _floorMat;
    private Texture2D?[] _albedoMaps = null!;
    private Texture2D?[] _normalMaps = null!;
    private Texture2D?[] _ormMaps = null!;
    private Texture2D? _envMap;

    // Debug
    private int _debugViewMode;
    private DebugViewPass _debugPass = null!;
    private static readonly string[] DebugViewNames = { "Final", "Albedo", "Normal", "Roughness", "Depth", "Metallic", "SSAO", "SSR", "HdrScene" };

    // ── Initialisation ──────────────────────────────────────────────────────

    public Program()
    {
        _gdm = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = 1280,
            PreferredBackBufferHeight = 720,
            SynchronizeWithVerticalRetrace = false,
        };
        Window.Title = "SceneRenderer — Deferred PBR Pipeline | ESC=quit";
        IsMouseVisible = true;
    }

    protected override void LoadContent()
    {
        // Default textures
        _defaultWhite = MakeColorTex(Color.White);
        _defaultNormal = MakeColorTex(new Color(128, 128, 255, 255));
        _defaultORM = MakeColorTex(new Color(255, 128, 0, 255));

        // ── Scene setup ─────────────────────────────────────────────────────
        _scene = new Scene
        {
            DefaultWhite = _defaultWhite,
            DefaultNormal = _defaultNormal,
            DefaultORM = _defaultORM,
        };

        // ── Camera ───────────────────────────────────────────────────────────
        _camera = new SceneCamera
        {
            Distance = 8f,
            Pitch = 0.0f,
            AspectRatio = (float)_gdm.PreferredBackBufferWidth / _gdm.PreferredBackBufferHeight,
        };

        // ── Teapot ──────────────────────────────────────────────────────────
        string[] teapotCandidates =
        {
            "../../FNA3D_HLSL_Test/assets/models/teapot_bezier0.tris",
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..",
                "FNA3D_HLSL_Test", "assets", "models", "teapot_bezier0.tris"),
        };
        var teapotPath = teapotCandidates.FirstOrDefault(File.Exists);
        if (teapotPath != null)
        {
            Console.WriteLine($"Loading teapot from: {Path.GetFullPath(teapotPath)}");
            var (verts, triCount) = TeapotModel.Load(teapotPath);
            _teapotPrims = triCount;
            _teapotVB = new VertexBuffer(GraphicsDevice, typeof(TeapotModel.Vertex),
                verts.Length, BufferUsage.WriteOnly);
            _teapotVB.SetData(verts);

            float teapotYMin = verts.Min(v => v.Position.Y);
            var teapotWorld = Matrix.CreateTranslation(0, -teapotYMin, 0);
            _scene.Objects.Add(new SceneObject
            {
                Name = "Teapot",
                Mesh = new Mesh
                {
                    Name = "Teapot",
                    VertexBuffer = _teapotVB,
                    PrimitiveCount = _teapotPrims,
                    VertexCount = verts.Length,
                    Bounds = ComputeBounds(verts),
                },
                Position = new Vector3(0, -teapotYMin, 0),
            });
        }

        // ── Floor ────────────────────────────────────────────────────────────
        var floorVerts = CreateFloor(8f, 4);
        int floorPrims = floorVerts.Length / 3;
        var floorVB = new VertexBuffer(GraphicsDevice, typeof(TeapotModel.Vertex),
            floorVerts.Length, BufferUsage.WriteOnly);
        floorVB.SetData(floorVerts);

        _scene.Objects.Add(new SceneObject
        {
            Name = "Floor",
            Mesh = new Mesh
            {
                Name = "Floor",
                VertexBuffer = floorVB,
                PrimitiveCount = floorPrims,
                VertexCount = floorVerts.Length,
                Bounds = new BoundingSphere(Vector3.Zero, 12f),
            },
        });

        // ── Procedural spheres ──────────────────────────────────────────────
        var sphereVerts = CreateSpherePNT(0.5f, 24, 16);
        int spherePrims = sphereVerts.Length / 3;
        var sphereVB = new VertexBuffer(GraphicsDevice, typeof(TeapotModel.Vertex),
            sphereVerts.Length, BufferUsage.WriteOnly);
        sphereVB.SetData(sphereVerts);

        _scene.Objects.Add(new SceneObject
        {
            Name = "Sphere1",
            Mesh = new Mesh
            {
                Name = "Sphere",
                VertexBuffer = sphereVB,
                PrimitiveCount = spherePrims,
                VertexCount = sphereVerts.Length,
                Bounds = new BoundingSphere(Vector3.Zero, 0.5f),
            },
            Position = new Vector3(2.5f, 0.5f, 1.5f),
        });

        // ── Lights ──────────────────────────────────────────────────────────
        _scene.SunLight = new DirectionalLight
        {
            Name = "Sun",
            Direction = Vector3.Normalize(new Vector3(0.5f, 0.8f, 0.3f)),
            Color = new Vector3(1.0f, 0.95f, 0.85f),
            Intensity = 4.0f,
            CastsShadows = true,
        };
        _scene.Lights.Add(_scene.SunLight);

        _scene.Lights.Add(new PointLight
        {
            Name = "Point1",
            Position = new Vector3(-2f, 2f, 0f),
            Color = new Vector3(1f, 0.3f, 0.2f),
            Intensity = 20f,
            Radius = 8f,
        });

        _scene.Lights.Add(new PointLight
        {
            Name = "Point2",
            Position = new Vector3(1f, 1.5f, -2f),
            Color = new Vector3(0.2f, 0.4f, 1f),
            Intensity = 15f,
            Radius = 6f,
        });

        // ── Materials ───────────────────────────────────────────────────────
        var assetsBase = FindAssetsBase();
        _materialDirs = Directory.GetDirectories(assetsBase)
            .Where(d => !d.EndsWith("hdris"))
            .OrderBy(d => d)
            .ToArray();

        Console.WriteLine($"Found {_materialDirs.Length} materials in {assetsBase}");
        _albedoMaps = new Texture2D[_materialDirs.Length];
        _normalMaps = new Texture2D[_materialDirs.Length];
        _ormMaps = new Texture2D[_materialDirs.Length];

        for (int i = 0; i < _materialDirs.Length; i++)
        {
            var dir = _materialDirs[i];
            var name = Path.GetFileName(dir);
            _albedoMaps[i] = LoadTex(Path.Combine(dir, $"{name}_albedo.jpg")) ?? _defaultWhite;
            _normalMaps[i] = LoadTex(Path.Combine(dir, $"{name}_normal.jpg")) ?? _defaultNormal;
            _ormMaps[i] = LoadTex(Path.Combine(dir, $"{name}_packed.jpg")) ?? _defaultORM;
            Console.WriteLine($"  [{i}] {name}");

            _scene.MaterialPalette.Add(new Material
            {
                Name = name,
                AlbedoMap = _albedoMaps[i],
                NormalMap = _normalMaps[i],
                ORMMap = _ormMaps[i],
            });
        }

        // Assign default materials
        _floorMat = 5; // marble
        _teapotMat = 2; // metal

        // Link materials to objects
        if (_scene.Objects.Count > 0)
            _scene.Objects[0].Material = _scene.MaterialPalette[_teapotMat]; // teapot
        if (_scene.Objects.Count > 1)
            _scene.Objects[1].Material = _scene.MaterialPalette[_floorMat];  // floor
        if (_scene.Objects.Count > 2)
            _scene.Objects[2].Material = _scene.MaterialPalette[1];          // sphere

        // ── HDRI ────────────────────────────────────────────────────────────
        var hdriDir = Path.Combine(assetsBase, "hdris");
        if (Directory.Exists(hdriDir))
        {
            foreach (var hf in Directory.GetFiles(hdriDir, "*.hdr"))
            {
                try { _envMap = HdriLoader.Load(GraphicsDevice, hf, mipMap: true); break; }
                catch (Exception ex) { Console.WriteLine($"  HDRI failed: {ex.Message}"); }
            }
        }
        if (_envMap == null)
        {
            Console.WriteLine("Creating procedural env map");
            _envMap = MakeSkyGradient(GraphicsDevice, 512, 256);
        }

        // ── IBL precompute ──────────────────────────────────────────────────
        Console.Write("Generating irradiance map... ");
        _scene.IrradianceMap = GenerateIrradianceMap(_envMap);
        Console.WriteLine("done");

        Console.Write("Generating BRDF LUT... ");
        _scene.BrdfLut = GenerateBrdfLut();
        Console.WriteLine("done");

        Console.Write("Generating prefiltered mip chain... ");
        GeneratePrefilteredMipChain(_envMap);
        Console.WriteLine("done");

        _scene.EnvMap = _envMap;
        _scene.PrefilteredEnvMap = _envMap;

        // ── Renderer ────────────────────────────────────────────────────────
        ImGuiTestHarness.Init(GraphicsDevice);
        _renderer = new SceneRendererEngine(GraphicsDevice,
            _gdm.PreferredBackBufferWidth, _gdm.PreferredBackBufferHeight);

        // Debug GBuffer viewer
        _debugPass = new DebugViewPass();
        _debugPass.Initialize(GraphicsDevice,
            _gdm.PreferredBackBufferWidth, _gdm.PreferredBackBufferHeight);
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

        // Update camera aspect ratio
        _camera.AspectRatio = (float)_gdm.PreferredBackBufferWidth
                            / _gdm.PreferredBackBufferHeight;

        // Run render pipeline
        _renderer.Render(_scene, _camera);

        // ── GBuffer debug overlay (bypasses pipeline when debug mode active) ──
        if (_debugViewMode > 0 && _renderer.LastContext != null)
        {
            var ctx = _renderer.LastContext;
            Texture2D? debugTex = null;
            int channel = 0; // 0=RGB, 1=R, 2=G, 3=A

            switch (_debugViewMode)
            {
                case 1: debugTex = ctx.GBufferRT0; channel = 0; break; // Albedo (RGB)
                case 2: debugTex = ctx.GBufferRT1; channel = 0; break; // Normal (RGB)
                case 3: debugTex = ctx.GBufferRT1; channel = 3; break; // Roughness (A)
                case 4: debugTex = ctx.GBufferRT2; channel = 2; break; // Depth (G→GGG)
                case 5: debugTex = ctx.GBufferRT2; channel = 1; break; // Metallic (R→RRR)
                case 6: debugTex = ctx.SSAOBlurRT; channel = 0; break; // SSAO
                case 7: debugTex = ctx.SSRRT; channel = 0; break; // SSR
                case 8: debugTex = ctx.HdrSceneRT; channel = 0; break; // HdrScene (DeferredLighting output)
            }

            if (debugTex != null)
                _debugPass.RenderDebug(debugTex, channel);
        }

        // ── ImGui Debug UI ──────────────────────────────────────────────────
        if (!TestHarness.Headless)
        {
            GraphicsDevice.BlendState = BlendState.AlphaBlend;
            ImGuiBindings.BeginPanel("SceneRenderer Debug");

            ImGuiBindings.ImGui_Separator();
            ImGuiBindings.ImGui_Text("View Mode");
            ImGuiBindings.Combo("Debug View", ref _debugViewMode, DebugViewNames);

            ImGuiBindings.ImGui_Separator();
            ImGuiBindings.ImGui_Text("Materials");
            var matNames = _materialDirs.Select(Path.GetFileName).ToArray()!;
            ImGuiBindings.Combo("Teapot", ref _teapotMat, matNames);
            ImGuiBindings.Combo("Floor", ref _floorMat, matNames);

            // Update material assignments
            if (_scene.Objects.Count > 0 && _teapotMat < _scene.MaterialPalette.Count)
                _scene.Objects[0].Material = _scene.MaterialPalette[_teapotMat];
            if (_scene.Objects.Count > 1 && _floorMat < _scene.MaterialPalette.Count)
                _scene.Objects[1].Material = _scene.MaterialPalette[_floorMat];

            ImGuiBindings.ImGui_Separator();
            ImGuiBindings.ImGui_Text("SSAO");
            ImGuiBindings.ImGui_Checkbox("Enable##SSAO", ref _renderer.SSAO.Enabled);
            if (_renderer.SSAO.Enabled)
            {
                ImGuiBindings.ImGui_SliderFloat("Radius##SSAO", ref _renderer.SSAO.Radius, 0.1f, 3f);
                ImGuiBindings.ImGui_SliderFloat("Bias##SSAO", ref _renderer.SSAO.Bias, 0f, 0.1f);
                ImGuiBindings.ImGui_SliderFloat("Intensity##SSAO", ref _renderer.SSAO.Intensity, 0f, 3f);
            }

            ImGuiBindings.ImGui_Separator();
            ImGuiBindings.ImGui_Text("SSR");
            ImGuiBindings.ImGui_Checkbox("Enable##SSR", ref _renderer.SSR.Enabled);
            if (_renderer.SSR.Enabled)
            {
                float ssrSteps = _renderer.SSR.MaxSteps;
                ImGuiBindings.ImGui_SliderFloat("Max Steps##SSR", ref ssrSteps, 16, 128);
                _renderer.SSR.MaxSteps = (int)ssrSteps;
                ImGuiBindings.ImGui_SliderFloat("Max Roughness##SSR", ref _renderer.SSR.MaxRoughness, 0.1f, 1f);
            }

            ImGuiBindings.ImGui_Separator();
            ImGuiBindings.ImGui_Text("Bloom");
            ImGuiBindings.ImGui_Checkbox("Enable##Bloom", ref _renderer.Bloom.Enabled);
            if (_renderer.Bloom.Enabled)
            {
                ImGuiBindings.ImGui_SliderFloat("Threshold##Bloom", ref _renderer.Bloom.Threshold, 0.1f, 5f);
                ImGuiBindings.ImGui_SliderFloat("Intensity##Bloom", ref _renderer.Bloom.Intensity, 0f, 1f);
            }

            ImGuiBindings.ImGui_Separator();
            ImGuiBindings.ImGui_Text("Tonemap");
            ImGuiBindings.ImGui_SliderFloat("Exposure", ref _renderer.Tonemap.Exposure, 0.1f, 5f);

            ImGuiBindings.ImGui_Separator();
            ImGuiBindings.ImGui_Text("Debug");
            ImGuiBindings.ImGui_Checkbox("Show Albedo##DL", ref _renderer.DeferredLighting.DebugAlbedo);
            ImGuiBindings.ImGui_Checkbox("Skybox Enabled", ref _renderer.Skybox.Enabled);

            ImGuiBindings.ImGui_Separator();
            ImGuiBindings.ImGui_Text("IBL");
            ImGuiBindings.ImGui_SliderFloat("Env Intensity", ref _scene.EnvIntensity, 0f, 2f);

            ImGuiBindings.ImGui_Separator();
            ImGuiBindings.ImGui_Text("Shadow Bias");
            ImGuiBindings.ImGui_SliderFloat("Bias", ref _renderer.ShadowMap.ShadowBias, 0f, 0.1f);

            ImGuiBindings.EndPanel();
        }
        else
        {
            // Headless test
            TestHarness.Tick(this, 3, () =>
            {
                var px = TestHarness.ReadBackbuffer(GraphicsDevice);
                int fails = TestHarness.AssertCoverage(px, new Color(0, 0, 0), 0.80f,
                    "scene-coverage");
                TestHarness.Report("SceneRenderer", fails);
            });
        }
    }

    // ── Geometry helpers ────────────────────────────────────────────────────

    private static TeapotModel.Vertex[] CreateFloor(float halfSize, int tileCount)
    {
        var v = new TeapotModel.Vertex[6];
        var n = Vector3.Up;
        float s = halfSize;
        float t = tileCount;

        v[0] = new TeapotModel.Vertex(new Vector3(-s, 0, -s), n, new Vector2(0, 0));
        v[1] = new TeapotModel.Vertex(new Vector3( s, 0, -s), n, new Vector2(t, 0));
        v[2] = new TeapotModel.Vertex(new Vector3(-s, 0,  s), n, new Vector2(0, t));
        v[3] = new TeapotModel.Vertex(new Vector3( s, 0, -s), n, new Vector2(t, 0));
        v[4] = new TeapotModel.Vertex(new Vector3( s, 0,  s), n, new Vector2(t, t));
        v[5] = new TeapotModel.Vertex(new Vector3(-s, 0,  s), n, new Vector2(0, t));
        return v;
    }

    private static TeapotModel.Vertex[] CreateSpherePNT(float radius, int slices, int stacks)
    {
        var verts = new System.Collections.Generic.List<TeapotModel.Vertex>();
        for (int i = 0; i < stacks; i++)
        {
            float phi0 = MathHelper.Pi * i / stacks;
            float phi1 = MathHelper.Pi * (i + 1) / stacks;
            for (int j = 0; j < slices; j++)
            {
                float theta0 = MathHelper.TwoPi * j / slices;
                float theta1 = MathHelper.TwoPi * (j + 1) / slices;

                var p00 = SpherePoint(radius, phi0, theta0);
                var p10 = SpherePoint(radius, phi1, theta0);
                var p01 = SpherePoint(radius, phi0, theta1);
                var p11 = SpherePoint(radius, phi1, theta1);

                verts.Add(new TeapotModel.Vertex(p00.Pos, p00.Normal, new Vector2((float)j / slices, (float)i / stacks)));
                verts.Add(new TeapotModel.Vertex(p10.Pos, p10.Normal, new Vector2((float)j / slices, (float)(i + 1) / stacks)));
                verts.Add(new TeapotModel.Vertex(p11.Pos, p11.Normal, new Vector2((float)(j + 1) / slices, (float)(i + 1) / stacks)));

                verts.Add(new TeapotModel.Vertex(p00.Pos, p00.Normal, new Vector2((float)j / slices, (float)i / stacks)));
                verts.Add(new TeapotModel.Vertex(p11.Pos, p11.Normal, new Vector2((float)(j + 1) / slices, (float)(i + 1) / stacks)));
                verts.Add(new TeapotModel.Vertex(p01.Pos, p01.Normal, new Vector2((float)(j + 1) / slices, (float)i / stacks)));
            }
        }
        return verts.ToArray();
    }

    private static (Vector3 Pos, Vector3 Normal) SpherePoint(float r, float phi, float theta)
    {
        float sinPhi = MathF.Sin(phi), cosPhi = MathF.Cos(phi);
        float sinTheta = MathF.Sin(theta), cosTheta = MathF.Cos(theta);
        var n = new Vector3(sinPhi * cosTheta, cosPhi, sinPhi * sinTheta);
        return (n * r, n);
    }

    private static BoundingSphere ComputeBounds(TeapotModel.Vertex[] verts)
    {
        var center = Vector3.Zero;
        foreach (var v in verts) center += v.Position;
        center /= verts.Length;
        float r2 = 0;
        foreach (var v in verts)
        {
            float d2 = Vector3.DistanceSquared(v.Position, center);
            if (d2 > r2) r2 = d2;
        }
        return new BoundingSphere(center, MathF.Sqrt(r2));
    }

    // ── IBL precompute ─────────────────────────────────────────────────────

    private Texture2D GenerateIrradianceMap(Texture2D envMap)
    {
        const int irrW = 128, irrH = 64;
        var rt = new RenderTarget2D(GraphicsDevice, irrW, irrH, false,
            SurfaceFormat.HalfVector4, DepthFormat.None);

        // Load irradiance convolution effect
        using var stream = typeof(Program).Assembly
            .GetManifestResourceStream("SceneRenderer.Shaders.IrradianceConv.feb")!;
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        using var effect = new Effect(GraphicsDevice, ms.ToArray());

        GraphicsDevice.SetRenderTarget(rt);
        GraphicsDevice.Clear(Color.Black);
        GraphicsDevice.DepthStencilState = DepthStencilState.None;
        GraphicsDevice.RasterizerState = RasterizerState.CullNone;
        GraphicsDevice.BlendState = BlendState.Opaque;
        GraphicsDevice.Textures[0] = envMap;
        GraphicsDevice.SamplerStates[0] = SamplerState.LinearClamp;
        effect.CurrentTechnique!.Passes[0].Apply();
        GraphicsDevice.DrawPrimitives(PrimitiveType.TriangleList, 0, 3);
        GraphicsDevice.SetRenderTarget(null);

        var pixels = new Microsoft.Xna.Framework.Graphics.PackedVector.HalfVector4[irrW * irrH];
        rt.GetData(pixels);
        rt.Dispose();

        var result = new Texture2D(GraphicsDevice, irrW, irrH, false, SurfaceFormat.HalfVector4);
        result.SetData(pixels);
        return result;
    }

    private Texture2D GenerateBrdfLut()
    {
        const int size = 256;
        var rt = new RenderTarget2D(GraphicsDevice, size, size, false,
            SurfaceFormat.HalfVector4, DepthFormat.None);

        using var stream = typeof(Program).Assembly
            .GetManifestResourceStream("SceneRenderer.Shaders.BrdfLut.feb")!;
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        using var effect = new Effect(GraphicsDevice, ms.ToArray());

        GraphicsDevice.SetRenderTarget(rt);
        GraphicsDevice.Clear(Color.Black);
        GraphicsDevice.DepthStencilState = DepthStencilState.None;
        GraphicsDevice.RasterizerState = RasterizerState.CullNone;
        GraphicsDevice.BlendState = BlendState.Opaque;
        effect.CurrentTechnique!.Passes[0].Apply();
        GraphicsDevice.DrawPrimitives(PrimitiveType.TriangleList, 0, 3);
        GraphicsDevice.SetRenderTarget(null);

        var pixels = new Microsoft.Xna.Framework.Graphics.PackedVector.HalfVector4[size * size];
        rt.GetData(pixels);
        rt.Dispose();

        var result = new Texture2D(GraphicsDevice, size, size, false, SurfaceFormat.HalfVector4);
        result.SetData(pixels);
        return result;
    }

    private void GeneratePrefilteredMipChain(Texture2D envMap)
    {
        using var stream = typeof(Program).Assembly
            .GetManifestResourceStream("SceneRenderer.Shaders.PrefilterEnv.feb")!;
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        using var effect = new Effect(GraphicsDevice, ms.ToArray());

        int w = envMap.Width, h = envMap.Height;
        int levels = 1;
        while (w > 4 && h > 4) { w /= 2; h /= 2; levels++; }

        w = envMap.Width; h = envMap.Height;
        for (int level = 1; level < levels; level++)
        {
            w /= 2; h /= 2;
            float roughness = (float)level / (levels - 1);

            var rt = new RenderTarget2D(GraphicsDevice, w, h, false,
                SurfaceFormat.HalfVector4, DepthFormat.None);
            GraphicsDevice.SetRenderTarget(rt);
            GraphicsDevice.Clear(Color.Black);
            GraphicsDevice.DepthStencilState = DepthStencilState.None;
            GraphicsDevice.RasterizerState = RasterizerState.CullNone;
            GraphicsDevice.BlendState = BlendState.Opaque;
            GraphicsDevice.Textures[0] = envMap;
            GraphicsDevice.SamplerStates[0] = SamplerState.LinearClamp;
            effect.Parameters["MipRoughness"].SetValue(roughness);
            effect.CurrentTechnique!.Passes[0].Apply();
            GraphicsDevice.DrawPrimitives(PrimitiveType.TriangleList, 0, 3);
            GraphicsDevice.SetRenderTarget(null);

            var pixels = new Microsoft.Xna.Framework.Graphics.PackedVector.HalfVector4[w * h];
            rt.GetData(pixels);
            envMap.SetData(level, null, pixels, 0, pixels.Length);
            rt.Dispose();
        }
    }

    // ── Utilities ───────────────────────────────────────────────────────────

    private Texture2D? LoadTex(string path)
    {
        if (!File.Exists(path)) return null;
        try { using var s = File.OpenRead(path); return Texture2D.FromStream(GraphicsDevice, s); }
        catch (Exception ex) { Console.WriteLine($"  Failed: {ex.Message}"); return null; }
    }

    private Texture2D MakeColorTex(Color color)
    {
        var tex = new Texture2D(GraphicsDevice, 1, 1);
        tex.SetData(new[] { color });
        return tex;
    }

    private static Texture2D MakeSkyGradient(GraphicsDevice device, int w, int h)
    {
        var tex = new Texture2D(device, w, h);
        var data = new Color[w * h];
        var skyTop = new Color(200, 210, 230);
        var skyMid = new Color(240, 235, 225);
        var horizon = new Color(250, 245, 235);
        for (int y = 0; y < h; y++)
        {
            float v = (float)y / (h - 1);
            Color c;
            if (v < 0.25f) c = Color.Lerp(skyTop, skyMid, v / 0.25f);
            else if (v < 0.4f) c = Color.Lerp(skyMid, horizon, (v - 0.25f) / 0.15f);
            else c = horizon;
            for (int x = 0; x < w; x++) data[y * w + x] = c;
        }
        tex.SetData(data);
        return tex;
    }

    private static string FindAssetsBase()
    {
        string[] candidates =
        {
            Path.Combine(AppContext.BaseDirectory, "assets", "materials"),
            "../assets/materials",
            "../../assets/materials",
            "../../../assets/materials",
        };
        foreach (var c in candidates)
        {
            var full = Path.GetFullPath(c);
            if (Directory.Exists(full)) return full;
        }
        throw new DirectoryNotFoundException("Cannot find assets/materials");
    }

    // ── TeapotModel import ──────────────────────────────────────────────────

    // Imported from MaterialLib — simplified for SceneRenderer use
    private static class TeapotModel
    {
        public struct Vertex : IVertexType
        {
            public Vector3 Position;
            public Vector3 Normal;
            public Vector2 TexCoord;

            public Vertex(Vector3 p, Vector3 n, Vector2 t) { Position = p; Normal = n; TexCoord = t; }

            public static readonly VertexDeclaration VertexDeclaration = new(
                new VertexElement(0, VertexElementFormat.Vector3, VertexElementUsage.Position, 0),
                new VertexElement(12, VertexElementFormat.Vector3, VertexElementUsage.Normal, 0),
                new VertexElement(24, VertexElementFormat.Vector2, VertexElementUsage.TextureCoordinate, 0));

            readonly VertexDeclaration IVertexType.VertexDeclaration => VertexDeclaration;
        }

        public static (Vertex[], int) Load(string path)
        {
            // .tris format: first line = vertex count, then triangles as raw XYZ triples
            var lines = File.ReadAllLines(path);
            if (lines.Length < 4)
                throw new InvalidDataException($"Not enough data in {path}");

            var positions = new System.Collections.Generic.List<Vector3>();
            for (int i = 1; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (string.IsNullOrEmpty(line)) continue;
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 3)
                    positions.Add(new Vector3(
                        float.Parse(parts[0]), float.Parse(parts[1]), float.Parse(parts[2])));
            }

            var verts = new System.Collections.Generic.List<Vertex>();
            // Process triangles (every 3 positions = one face)
            for (int i = 0; i + 2 < positions.Count; i += 3)
            {
                var p0 = positions[i];
                var p1 = positions[i + 1];
                var p2 = positions[i + 2];

                // Compute face normal (p2 swapped for CW winding → CullCounterClockwise)
                // Cross(p1-p0, p2-p0) gives outward normal for CW {p0,p2,p1} in RH coords
                var fn = Vector3.Normalize(Vector3.Cross(p1 - p0, p2 - p0));

                verts.Add(MakeVertex(p0, fn));
                verts.Add(MakeVertex(p2, fn));
                verts.Add(MakeVertex(p1, fn));
            }

            if (verts.Count == 0)
                throw new InvalidDataException($"No faces in {path}");

            // Average normals for shared positions
            var smoothed = SmoothNormals(verts);

            return (smoothed, smoothed.Length / 3);
        }

        private static Vertex[] SmoothNormals(System.Collections.Generic.List<Vertex> verts)
        {
            var result = new Vertex[verts.Count];
            for (int i = 0; i < verts.Count; i++)
            {
                var p = verts[i].Position;
                var avgN = Vector3.Zero;
                int count = 0;
                for (int j = 0; j < verts.Count; j++)
                {
                    if (Vector3.DistanceSquared(p, verts[j].Position) < 0.0001f)
                    {
                        avgN += verts[j].Normal;
                        count++;
                    }
                }
                avgN = count > 0 ? Vector3.Normalize(avgN / count) : verts[i].Normal;
                result[i] = new Vertex(p, avgN, verts[i].TexCoord);
            }
            return result;
        }

        private static Vertex MakeVertex(Vector3 p, Vector3 n)
        {
            float u = 0.5f + MathF.Atan2(p.Z, p.X) / (2f * MathF.PI);
            float v = 0.5f - MathF.Asin(Math.Clamp(p.Y, -1f, 1f)) / MathF.PI;
            return new Vertex(p, n, new Vector2(u, v));
        }
    }

    // ── Cleanup ─────────────────────────────────────────────────────────────

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            ImGuiTestHarness.Shutdown(GraphicsDevice);
            _renderer?.Dispose();
            _debugPass?.Dispose();
            _teapotVB?.Dispose();
            _defaultWhite?.Dispose();
            _defaultNormal?.Dispose();
            _defaultORM?.Dispose();
            _envMap?.Dispose();
            _scene?.IrradianceMap?.Dispose();
            _scene?.BrdfLut?.Dispose();
            foreach (var t in _albedoMaps ?? Array.Empty<Texture2D?>()) t?.Dispose();
            foreach (var t in _normalMaps ?? Array.Empty<Texture2D?>()) t?.Dispose();
            foreach (var t in _ormMaps ?? Array.Empty<Texture2D?>()) t?.Dispose();
        }
        base.Dispose(disposing);
    }

    static void Main(string[] args)
    {
        TestHarness.ParseArgs(args);
        using var game = new Program();
        game.Run();
    }
}
