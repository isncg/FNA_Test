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

    /* Direct references instead of Objects[0]/[1]/[2]: the teapot is only added
     * when its model file exists, and index-based lookups then silently shift
     * every material onto the wrong object.
     */
    private SceneObject? _teapotObj;
    private SceneObject? _floorObj;
    private SceneObject? _sphereObj;

    // Headless skybox/shared-depth verification state
    private int _headlessFrame;
    private Color[]? _skyOnPixels;
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
        /* Generated the way GLUT does it, from the 32 Bezier patches, rather
         * than loaded from a pre-triangulated dump. That gives UVs straight
         * from the patch parameter domain and analytic normals — the .tris file
         * carried neither, so both had to be guessed from vertex positions.
         *
         * scale 2 puts the height at ~3.15 units, matching the old mesh.
         */
        var teapotVerts = GlutTeapot.Build(grid: 10, scale: 2f);
        _teapotPrims = teapotVerts.Length / 3;
        _teapotVB = new VertexBuffer(GraphicsDevice, typeof(VertexPositionNormalTexture),
            teapotVerts.Length, BufferUsage.WriteOnly);
        _teapotVB.SetData(teapotVerts);

        float teapotYMin = teapotVerts.Min(v => v.Position.Y);
        Console.WriteLine($"Teapot: {_teapotPrims} triangles from 32 Bezier patches " +
            $"(grid 10), height {teapotVerts.Max(v => v.Position.Y) - teapotYMin:F2}");

        _scene.Objects.Add(new SceneObject
        {
            Name = "Teapot",
            Mesh = new Mesh
            {
                Name = "Teapot",
                VertexBuffer = _teapotVB,
                PrimitiveCount = _teapotPrims,
                VertexCount = teapotVerts.Length,
                Bounds = ComputeBounds(teapotVerts),
            },
            Position = new Vector3(0, -teapotYMin, 0),
        });
        _teapotObj = _scene.Objects[_scene.Objects.Count - 1];

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
        _floorObj = _scene.Objects[_scene.Objects.Count - 1];

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
        _sphereObj = _scene.Objects[_scene.Objects.Count - 1];

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
        var texSw = System.Diagnostics.Stopwatch.StartNew();
        _albedoMaps = new Texture2D[_materialDirs.Length];
        _normalMaps = new Texture2D[_materialDirs.Length];
        _ormMaps = new Texture2D[_materialDirs.Length];

        for (int i = 0; i < _materialDirs.Length; i++)
        {
            var dir = _materialDirs[i];
            var name = Path.GetFileName(dir);
            /* Albedo is sRGB-encoded, so it is tagged ColorSrgbEXT and the
             * texture unit decodes it to linear on sample (the pipeline is
             * linear until Tonemap applies gamma). Normal and packed maps carry
             * data, not colour, and stay linear. All get mips so the
             * anisotropic sampler has something to minify with.
             */
            _albedoMaps[i] = TextureLoader.Load(GraphicsDevice, Path.Combine(dir, $"{name}_albedo.jpg"),
                TextureLoader.Kind.SrgbColor) ?? _defaultWhite;
            _normalMaps[i] = TextureLoader.Load(GraphicsDevice, Path.Combine(dir, $"{name}_normal.jpg"),
                TextureLoader.Kind.NormalMap) ?? _defaultNormal;
            _ormMaps[i] = TextureLoader.Load(GraphicsDevice, Path.Combine(dir, $"{name}_packed.jpg"),
                TextureLoader.Kind.LinearData) ?? _defaultORM;
            Console.WriteLine($"  [{i}] {name}");

            _scene.MaterialPalette.Add(new Material
            {
                Name = name,
                AlbedoMap = _albedoMaps[i],
                NormalMap = _normalMaps[i],
                ORMMap = _ormMaps[i],
            });
        }
        texSw.Stop();
        Console.WriteLine($"  Textures decoded + mipped in {texSw.ElapsedMilliseconds} ms " +
            $"(albedo sRGB, normal/packed linear, mips on)");

        // Assign default materials
        _floorMat = 5; // marble
        _teapotMat = 2; // metal

        // Link materials to objects
        ApplyMaterials();
        if (_sphereObj != null && _scene.MaterialPalette.Count > 1)
        {
            _sphereObj.Material = _scene.MaterialPalette[1];
        }

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
            if (_teapotObj != null)
            {
                ImGuiBindings.Combo("Teapot", ref _teapotMat, matNames);
            }
            else
            {
                ImGuiBindings.ImGui_Text("Teapot: model not loaded");
            }
            ImGuiBindings.Combo("Floor", ref _floorMat, matNames);

            // Update material assignments
            ApplyMaterials();

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
            /* Headless test. Beyond coverage, verify the UE5-style shared depth
             * buffer: render one frame with the skybox and one without, then
             * compare. Sky pixels must change, geometry pixels must not — if the
             * depth test against the shared buffer were broken the sky would
             * paint over geometry (everything changes) or never appear at all
             * (nothing changes).
             *
             * Update runs before Draw, so the backbuffer read here holds the
             * previous frame.
             */
            _headlessFrame += 1;

            if (_headlessFrame == 3)
            {
                _skyOnPixels = TestHarness.ReadBackbuffer(GraphicsDevice);
                _renderer.Skybox.Enabled = false;
            }
            else if (_headlessFrame == 4)
            {
                var skyOff = TestHarness.ReadBackbuffer(GraphicsDevice);
                int fails = TestHarness.AssertCoverage(_skyOnPixels!, new Color(0, 0, 0),
                    0.80f, "scene-coverage");

                /* Pixels that are clearly lit with the skybox off are geometry;
                 * the depth test must leave every one of them untouched.
                 * Counting them separately keeps the check independent of how
                 * much of the frame the sky covers (this scene is ~97% sky).
                 *
                 * The threshold matters: deferred lighting leaves a faint
                 * ambient/IBL residue (luminance 1-4) on empty pixels, which
                 * the sky legitimately replaces. Only pixels well above that
                 * noise floor count as geometry.
                 */
                const int GeometryLuminance = 16;
                int geometryPixels = 0, overwritten = 0, skyPixels = 0;
                for (int i = 0; i < skyOff.Length; i += 1)
                {
                    bool changed = _skyOnPixels![i].PackedValue != skyOff[i].PackedValue;
                    int lum = Math.Max(skyOff[i].R, Math.Max(skyOff[i].G, skyOff[i].B));
                    if (lum >= GeometryLuminance)
                    {
                        geometryPixels += 1;
                        if (changed)
                        {
                            overwritten += 1;
                        }
                    }
                    else if (changed)
                    {
                        skyPixels += 1;
                    }
                }

                if (geometryPixels == 0)
                {
                    Console.WriteLine("FAIL [geometry-present]: no lit geometry to test against");
                    fails += 1;
                }
                else if (overwritten > 0)
                {
                    // Brightness of the overwritten pixels helps tell real
                    // geometry from lighting residue in empty space.
                    int maxLum = 0;
                    long sumLum = 0;
                    for (int i = 0; i < skyOff.Length; i += 1)
                    {
                        int lum = Math.Max(skyOff[i].R, Math.Max(skyOff[i].G, skyOff[i].B));
                        if (lum < GeometryLuminance) continue;
                        if (_skyOnPixels![i].PackedValue == skyOff[i].PackedValue) continue;
                        if (lum > maxLum) maxLum = lum;
                        sumLum += lum;
                    }
                    Console.WriteLine(
                        $"FAIL [skybox-depth-test]: sky overwrote {overwritten}/{geometryPixels} " +
                        $"geometry pixels; the shared depth test is not rejecting them " +
                        $"(overwritten luminance max={maxLum} mean={sumLum / overwritten})");
                    fails += 1;
                }

                if (skyPixels == 0)
                {
                    Console.WriteLine(
                        "FAIL [skybox-visible]: the skybox never reached the HDR target");
                    fails += 1;
                }

                if (fails == 0)
                {
                    Console.WriteLine(
                        $"[SceneRenderer] Shared depth OK: {skyPixels} sky pixels drawn, " +
                        $"all {geometryPixels} geometry pixels preserved.");
                }

                // Convex surfaces facing the camera must not be blacked out by
                // SSAO (regression: hemisphere flipped when GBuffer world normals
                // were used without a view-space transform).
                fails += CheckSsaoBlackSpots(_renderer.LastContext!);

                TestHarness.Report("SceneRenderer", fails);
                Exit();
            }
        }
    }

    // ── Materials ──────────────────────────────────────────────────

    /// <summary>
    /// Binds the selected palette entries to the teapot and floor. Keyed on the
    /// object references, so a missing teapot cannot shift the floor's material
    /// onto the sphere.
    /// </summary>
    private void ApplyMaterials()
    {
        if (_teapotObj != null && _teapotMat < _scene.MaterialPalette.Count)
        {
            _teapotObj.Material = _scene.MaterialPalette[_teapotMat];
        }
        if (_floorObj != null && _floorMat < _scene.MaterialPalette.Count)
        {
            _floorObj.Material = _scene.MaterialPalette[_floorMat];
        }
    }

    // ── Geometry helpers ─────────────────────────────────────────────

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

    /* SSAO black-spot regression check. The teapot body is convex, so a
     * surface patch that faces the camera can only be occluded by something
     * in front of it — and there is nothing. If the sampling hemisphere is
     * flipped (normal not correctly brought into the depth convention the
     * shader works in), the samples land inside the mesh and those patches
     * read as fully occluded. Count camera-facing geometry pixels whose AO
     * collapsed; there should be (almost) none.
     */
    private int CheckSsaoBlackSpots(RenderContext ctx)
    {
        if (ctx.GBufferRT1 == null || ctx.GBufferRT2 == null || ctx.SSAOBlurRT == null)
        {
            Console.WriteLine("FAIL [ssao-blackspot]: GBuffer/SSAO targets unavailable");
            return 1;
        }

        int w = ctx.Width, h = ctx.Height;
        var rt1 = new Microsoft.Xna.Framework.Graphics.PackedVector.HalfVector4[w * h];
        var rt2 = new Microsoft.Xna.Framework.Graphics.PackedVector.HalfVector4[w * h];
        var ssao = new float[w * h];
        ctx.GBufferRT1.GetData(rt1);
        ctx.GBufferRT2.GetData(rt2);
        ctx.SSAOBlurRT.GetData(ssao);

        // Camera-facing = world normal within ~45° of the view axis. On the
        // default view this selects the convex front of the teapot body and
        // excludes the floor and lid top (their normals are ~73° off-axis).
        Vector3 toCam = -ctx.Camera.Forward;

        int facing = 0, dark = 0;
        for (int i = 0; i < w * h; i += 1)
        {
            Vector4 n4 = rt1[i].ToVector4();
            var worldN = new Vector3(n4.X * 2f - 1f, n4.Y * 2f - 1f, n4.Z * 2f - 1f);
            if (worldN.LengthSquared() < 0.5f) continue; // unwritten/degenerate
            worldN = Vector3.Normalize(worldN);

            float viewZ = rt2[i].ToVector4().Y;
            if (viewZ <= 0.1f || viewZ >= 100f) continue; // sky / far plane

            if (Vector3.Dot(worldN, toCam) < 0.7f) continue; // not camera-facing

            facing += 1;
            if (ssao[i] < 0.5f) dark += 1;
        }

        if (facing == 0)
        {
            Console.WriteLine("FAIL [ssao-blackspot]: no camera-facing geometry pixels found");
            return 1;
        }

        float darkRatio = (float)dark / facing;
        Console.WriteLine(
            $"[SceneRenderer] SSAO blackspot: {dark}/{facing} camera-facing " +
            $"geometry pixels are dark ({darkRatio:P1})");

        // Guard against the opposite failure mode — AO silently disabled
        // (everything 1.0) would also pass the blackspot check. The teapot's
        // lid/body seam, handle and floor contact must still occlude.
        int occluded = 0;
        for (int i = 0; i < w * h; i += 1)
        {
            float viewZ = rt2[i].ToVector4().Y;
            if (viewZ <= 0.1f || viewZ >= 100f) continue;
            if (ssao[i] < 0.85f) occluded += 1;
        }
        Console.WriteLine($"[SceneRenderer] SSAO active: {occluded} geometry pixels show occlusion (<0.85)");

        if (occluded == 0)
        {
            Console.WriteLine(
                "FAIL [ssao-disabled]: no geometry pixel is occluded — AO is " +
                "producing nothing, which would also mask the blackspot bug");
            return 1;
        }

        if (darkRatio > 0.02f)
        {
            Console.WriteLine(
                $"FAIL [ssao-blackspot]: {darkRatio:P1} of camera-facing convex pixels " +
                $"are occluded — the SSAO hemisphere is sampling into the surface");
            return 1;
        }
        return 0;
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

    /// <summary>Same bounds computation for the stock PNT vertex type.</summary>
    private static BoundingSphere ComputeBounds(VertexPositionNormalTexture[] verts)
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

    // ── Mesh vertex type ───────────────────────────────────────────────────────

    /* PNT layout for the procedural floor and sphere. The teapot uses the stock
     * VertexPositionNormalTexture (same layout) via GlutTeapot; the .tris loader
     * that used to live here is gone.
     */
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
