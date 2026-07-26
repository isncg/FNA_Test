using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using FNA.Test;

namespace AsteroidFieldDemo
{
    /// <summary>
    /// GPU-instanced orbiting debris field using StructuredBuffer for instance data.
    /// Demonstrates GPU-driven rendering with storage buffers — an alternative to
    /// VertexBuffer hardware instancing that lifts vertex attribute count limits.
    ///
    /// Tests the full StorageBuffer API lifecycle:
    ///   Gen → SetData → SetVertexStorageBuffers → DrawInstancedPrimitives → GetData
    ///
    /// Two storage buffers:
    ///   t1 — StructuredBuffer&lt;InstanceData&gt; (read-only): per-instance transform + color
    ///   u0 — RWStructuredBuffer&lt;uint&gt; (read-write): VS writes visibility flags, CPU reads back
    /// </summary>
    public class AsteroidFieldGame : Game
    {
        private GraphicsDeviceManager graphics;
        private Effect effect;
        private VertexBuffer geometryBuffer;
        private IndexBuffer indexBuffer;
        private StorageBuffer instanceBuffer;      // StructuredBuffer<InstanceData> (read-only)
        private StorageBuffer visibilityBuffer;    // RWStructuredBuffer<uint> (read-write)

        // ── Effect parameters ───────────────────────────────────────────────
        private EffectParameter worldViewProjParam;
        private EffectParameter lightDirParam;
        private EffectParameter ambientColorParam;
        private EffectParameter cameraPosParam;
        private EffectParameter elapsedTimeParam;

        // ── Config (ImGui-tweakable) ────────────────────────────────────────
        private int instanceCount = 512;
        private float orbitRadiusMin = 1.5f;
        private float orbitRadiusMax = 4.0f;
        private float orbitSpeedMin = 0.3f;
        private float orbitSpeedMax = 1.2f;
        private float scaleMin = 0.08f;
        private float scaleMax = 0.35f;
        private float cameraDistance = 8.0f;
        private float cameraHeight = 2.0f;
        private bool pauseOrbit = false;

        // ── State ───────────────────────────────────────────────────────────
        private float totalTime;

        // ── RNG ─────────────────────────────────────────────────────────────
        private static readonly Random rng = new Random();

        // ═════════════════════════════════════════════════════════════════════
        //  Vertex types
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>Position + Normal vertex (C2: exact VS_INPUT match).</summary>
        [StructLayout(LayoutKind.Sequential)]
        private struct VertexPN : IVertexType
        {
            public Vector3 Position;
            public Vector3 Normal;

            public VertexPN(Vector3 p, Vector3 n) { Position = p; Normal = n; }

            public static readonly VertexDeclaration VertexDeclaration = new(
                new VertexElement(0,  VertexElementFormat.Vector3, VertexElementUsage.Position, 0),
                new VertexElement(12, VertexElementFormat.Vector3, VertexElementUsage.Normal, 0)
            );
            VertexDeclaration IVertexType.VertexDeclaration => VertexDeclaration;
        }

        /// <summary>Per-instance data stored in the StructuredBuffer.</summary>
        [StructLayout(LayoutKind.Sequential)]
        private struct InstanceData
        {
            public Vector4 Position;   // xyz=orbit offset, w=orbit speed
            public Vector4 Rotation;   // rotation quaternion (xyzw)
            public Vector4 Color;      // rgb=diffuse color, a=scale

            public InstanceData(Vector4 pos, Vector4 rot, Vector4 col)
            {
                Position = pos; Rotation = rot; Color = col;
            }
        }

        // ═════════════════════════════════════════════════════════════════════

        public AsteroidFieldGame()
        {
            graphics = new GraphicsDeviceManager(this)
            {
                PreferredBackBufferWidth = 800,
                PreferredBackBufferHeight = 600,
                SynchronizeWithVerticalRetrace = false
            };
            Window.Title = "Asteroid Field Demo — Storage Buffers | ESC=quit";
            IsMouseVisible = true;
        }

        protected override void LoadContent()
        {
            // ── Load embedded FEB ────────────────────────────────────────────
            using var stream = Assembly.GetExecutingAssembly()
                .GetManifestResourceStream("AsteroidFieldDemo.AsteroidField.feb");
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            effect = new Effect(GraphicsDevice, ms.ToArray());

            worldViewProjParam = effect.Parameters["WorldViewProj"];
            lightDirParam      = effect.Parameters["LightDir"];
            ambientColorParam  = effect.Parameters["AmbientColor"];
            cameraPosParam     = effect.Parameters["CameraPos"];
            elapsedTimeParam   = effect.Parameters["ElapsedTime"];

            Console.WriteLine($"[AsteroidField] Effect loaded: {effect.Techniques.Count} techniques, {effect.Parameters.Count} params");

            // ── Geometry: unit cube with Position + Normal (36 verts) ────────
            CreateCubeGeometry();

            // ── Storage buffers ──────────────────────────────────────────────
            CreateStorageBuffers();

            // ── ImGui ────────────────────────────────────────────────────────
            ImGuiTestHarness.Init(GraphicsDevice);

            Console.WriteLine($"[AsteroidField] {instanceCount} instances ready.");
        }

        // ═════════════════════════════════════════════════════════════════════
        //  Geometry
        // ═════════════════════════════════════════════════════════════════════

        private void CreateCubeGeometry()
        {
            // 6 faces × 2 triangles × 3 verts = 36 vertices, flat-shaded
            var verts = new VertexPN[36];
            int vi = 0;

            void AddFace(Vector3 normal, Vector3 right, Vector3 up)
            {
                Vector3 halfN = normal * 0.5f;
                Vector3 halfR = right * 0.5f;
                Vector3 halfU = up * 0.5f;

                // Triangle 1: v0, v1, v2
                verts[vi++] = new VertexPN(halfN - halfR - halfU, normal); // bl
                verts[vi++] = new VertexPN(halfN + halfR - halfU, normal); // br
                verts[vi++] = new VertexPN(halfN + halfR + halfU, normal); // tr
                // Triangle 2: v0, v2, v3
                verts[vi++] = new VertexPN(halfN - halfR - halfU, normal); // bl
                verts[vi++] = new VertexPN(halfN + halfR + halfU, normal); // tr
                verts[vi++] = new VertexPN(halfN - halfR + halfU, normal); // tl
            }

            AddFace( Vector3.UnitZ,  Vector3.UnitX, Vector3.UnitY);  // front
            AddFace(-Vector3.UnitZ, -Vector3.UnitX, Vector3.UnitY);  // back
            AddFace( Vector3.UnitX, -Vector3.UnitZ, Vector3.UnitY);  // right
            AddFace(-Vector3.UnitX,  Vector3.UnitZ, Vector3.UnitY);  // left
            AddFace( Vector3.UnitY,  Vector3.UnitZ, Vector3.UnitX);  // top
            AddFace(-Vector3.UnitY, -Vector3.UnitZ, Vector3.UnitX);  // bottom

            geometryBuffer = new VertexBuffer(GraphicsDevice, VertexPN.VertexDeclaration,
                36, BufferUsage.WriteOnly);
            geometryBuffer.SetData(verts);

            // Sequential indices 0..35
            var indices = new uint[36];
            for (uint i = 0; i < 36; i++) indices[i] = i;

            indexBuffer = new IndexBuffer(GraphicsDevice, IndexElementSize.ThirtyTwoBits,
                36, BufferUsage.WriteOnly);
            indexBuffer.SetData(indices);
        }

        // ═════════════════════════════════════════════════════════════════════
        //  Storage buffers
        // ═════════════════════════════════════════════════════════════════════

        private void CreateStorageBuffers()
        {
            // Instance data buffer (read-only from VS)
            instanceBuffer = new StorageBuffer(GraphicsDevice,
                instanceCount * Marshal.SizeOf<InstanceData>(),
                vertexWrite: false,
                vertexRead: true);

            // Visibility buffer (VS writes, CPU reads back)
            visibilityBuffer = new StorageBuffer(GraphicsDevice,
                instanceCount * sizeof(uint),
                vertexWrite: true,
                vertexRead: true);  // needed for GetData readback

            RebuildInstanceBuffer();

            // Initialize visibility to zeros
            visibilityBuffer.SetData(new uint[instanceCount]);
        }

        private void RebuildInstanceBuffer()
        {
            var data = new InstanceData[instanceCount];
            for (int i = 0; i < instanceCount; i++)
            {
                // Random orbit parameters
                float angle = (float)(rng.NextDouble() * Math.PI * 2.0);
                float radius = orbitRadiusMin + (float)(rng.NextDouble() * (orbitRadiusMax - orbitRadiusMin));
                float speed = orbitSpeedMin + (float)(rng.NextDouble() * (orbitSpeedMax - orbitSpeedMin));
                float y = (float)(rng.NextDouble() * 0.6 - 0.3); // slight vertical spread
                float scale = scaleMin + (float)(rng.NextDouble() * (scaleMax - scaleMin));

                Vector3 orbitPos = new Vector3(
                    (float)Math.Cos(angle) * radius,
                    y,
                    (float)Math.Sin(angle) * radius
                );

                // Random rotation quaternion
                var rotAxis = Vector3.Normalize(new Vector3(
                    (float)(rng.NextDouble() - 0.5),
                    (float)(rng.NextDouble() - 0.5),
                    (float)(rng.NextDouble() - 0.5)
                ));
                float rotAngle = (float)(rng.NextDouble() * Math.PI * 2.0);
                var rot = Quaternion.CreateFromAxisAngle(rotAxis, rotAngle);

                // Random color (vibrant hues in HSL-like space)
                float hue = (float)rng.NextDouble();
                var color = HslToRgb(hue, 0.7f, 0.55f);

                data[i] = new InstanceData(
                    new Vector4(orbitPos, speed),
                    new Vector4(rot.X, rot.Y, rot.Z, rot.W),
                    new Vector4(color, scale)
                );
            }

            instanceBuffer.SetData(data);
        }

        private static Vector3 HslToRgb(float h, float s, float l)
        {
            float c = (1 - Math.Abs(2 * l - 1)) * s;
            float x = c * (1 - Math.Abs((h * 6) % 2 - 1));
            float m = l - c / 2;
            float r, g, b;
            if      (h < 1f/6) { r = c; g = x; b = 0; }
            else if (h < 2f/6) { r = x; g = c; b = 0; }
            else if (h < 3f/6) { r = 0; g = c; b = x; }
            else if (h < 4f/6) { r = 0; g = x; b = c; }
            else if (h < 5f/6) { r = x; g = 0; b = c; }
            else               { r = c; g = 0; b = x; }
            return new Vector3(r + m, g + m, b + m);
        }

        // ═════════════════════════════════════════════════════════════════════
        //  Game loop
        // ═════════════════════════════════════════════════════════════════════

        protected override void Update(GameTime gameTime)
        {
            var kb = Keyboard.GetState();
            if (kb.IsKeyDown(Keys.Escape)) Exit();

            float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;

            if (!pauseOrbit)
                totalTime += dt;

            // ── Headless test assertions ────────────────────────────────────
            TestHarness.Tick(this, 3, () =>
            {
                var px = TestHarness.ReadBackbuffer(GraphicsDevice);
                int w = GraphicsDevice.Viewport.Width;
                int fails = 0;

                // Layer 1: Visual coverage — debris should fill reasonable portion
                Color bg = new Color(5, 5, 15);
                fails += TestHarness.AssertCoverage(px, bg, 0.03f, "debris-coverage");

                // Layer 2: Storage buffer readback — verify VS wrote visibility
                var visibility = new uint[instanceCount];
                visibilityBuffer.GetData(visibility);
                int visible = visibility.Count(v => v == 1);
                Console.WriteLine($"[AsteroidField] Visible instances: {visible}/{instanceCount}");
                if (visible < instanceCount / 2)
                {
                    Console.WriteLine($"FAIL [visibility-count]: only {visible}/{instanceCount} visible (expected > {instanceCount / 2})");
                    fails++;
                }

                // Layer 3: Center pixel should not be background (camera looks at origin)
                fails += TestHarness.AssertPixel(px, w, 400, 300, bg, 0, "center-not-empty");

                TestHarness.Report("AsteroidField", fails);
            });
        }

        protected override void Draw(GameTime gameTime)
        {
            ImGuiTestHarness.NewFrame(GraphicsDevice);

            GraphicsDevice.Clear(new Color(5, 5, 15)); // dark space background

            // ── Camera ──────────────────────────────────────────────────────
            float camAngle = totalTime * 0.15f;
            var camPos = new Vector3(
                (float)Math.Cos(camAngle) * cameraDistance,
                cameraHeight,
                (float)Math.Sin(camAngle) * cameraDistance
            );
            var view = Matrix.CreateLookAt(camPos, Vector3.Zero, Vector3.Up);
            var proj = Matrix.CreatePerspectiveFieldOfView(
                MathHelper.PiOver4,
                GraphicsDevice.Viewport.AspectRatio,
                0.1f, 100f
            );
            var worldViewProj = Matrix.Identity * view * proj;

            // ── Lighting ────────────────────────────────────────────────────
            var lightDir = Vector3.Normalize(new Vector3(0.5f, 1.0f, 0.3f));
            var ambient = new Vector4(0.15f, 0.15f, 0.20f, 0);

            // ── Set effect parameters ───────────────────────────────────────
            worldViewProjParam.SetValue(worldViewProj);
            lightDirParam.SetValue(new Vector4(lightDir, 0));
            ambientColorParam.SetValue(ambient);
            cameraPosParam.SetValue(new Vector4(camPos, 0));
            elapsedTimeParam.SetValue(totalTime);

            // ── Render state ────────────────────────────────────────────────
            GraphicsDevice.BlendState = BlendState.Opaque;
            GraphicsDevice.DepthStencilState = DepthStencilState.Default;
            GraphicsDevice.RasterizerState = RasterizerState.CullCounterClockwise;

            // ── Apply effect ────────────────────────────────────────────────
            effect.CurrentTechnique.Passes[0].Apply();

            // ── Bind geometry ───────────────────────────────────────────────
            GraphicsDevice.SetVertexBuffers(
                new VertexBufferBinding(geometryBuffer, 0, 0)
            );
            GraphicsDevice.Indices = indexBuffer;

            // ── Bind storage buffers ────────────────────────────────────────
            // SDL3 logical slot order matches SPIR-V binding order:
            //   slot 0 = first storage buffer (t1→SPIR-V binding 1)
            //   slot 1 = second storage buffer (u0→SPIR-V binding 2)
            GraphicsDevice.SetVertexStorageBuffers(0, instanceBuffer, visibilityBuffer);

            // ── Draw instanced ──────────────────────────────────────────────
            GraphicsDevice.DrawInstancedPrimitives(
                PrimitiveType.TriangleList,
                baseVertex: 0,
                minVertexIndex: 0,
                numVertices: 36,
                startIndex: 0,
                primitiveCount: 12,
                instanceCount: instanceCount
            );

            // ── ImGui panel ─────────────────────────────────────────────────
            if (!TestHarness.Headless)
            {
                GraphicsDevice.BlendState = BlendState.AlphaBlend;
                DrawImGui();
            }
        }

        private void DrawImGui()
        {
            ImGuiBindings.BeginPanel("Asteroid Field");

            int[] counts = { 64, 128, 256, 512, 1024, 2048 };
            string[] countNames = { "64", "128", "256", "512", "1K", "2K" };
            int ci = Array.IndexOf(counts, instanceCount);
            if (ci < 0) ci = 3;
            if (ImGuiBindings.Combo("Instances", ref ci, countNames))
            {
                instanceCount = counts[ci];
                CreateStorageBuffers();
            }

            ImGuiBindings.ImGui_Checkbox("Pause Orbit", ref pauseOrbit);

            bool rebuild = false;
            rebuild |= ImGuiBindings.ImGui_SliderFloat("Orbit Radius Min", ref orbitRadiusMin, 0.5f, 4.0f);
            rebuild |= ImGuiBindings.ImGui_SliderFloat("Orbit Radius Max", ref orbitRadiusMax, 1.0f, 6.0f);
            rebuild |= ImGuiBindings.ImGui_SliderFloat("Orbit Speed Min", ref orbitSpeedMin, 0.1f, 2.0f);
            rebuild |= ImGuiBindings.ImGui_SliderFloat("Orbit Speed Max", ref orbitSpeedMax, 0.2f, 3.0f);
            rebuild |= ImGuiBindings.ImGui_SliderFloat("Scale Min", ref scaleMin, 0.02f, 0.5f);
            rebuild |= ImGuiBindings.ImGui_SliderFloat("Scale Max", ref scaleMax, 0.05f, 0.8f);
            if (rebuild) RebuildInstanceBuffer();

            ImGuiBindings.ImGui_SliderFloat("Cam Distance", ref cameraDistance, 3.0f, 20.0f);
            ImGuiBindings.ImGui_SliderFloat("Cam Height", ref cameraHeight, 0.0f, 8.0f);

            ImGuiBindings.ImGui_Text($"FPS: {(int)(1.0 / Math.Max(0.001, TargetElapsedTime.TotalSeconds))}");
            ImGuiBindings.EndPanel();
        }

        // ═════════════════════════════════════════════════════════════════════

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                effect?.Dispose();
                geometryBuffer?.Dispose();
                indexBuffer?.Dispose();
                instanceBuffer?.Dispose();
                visibilityBuffer?.Dispose();
            }
            base.Dispose(disposing);
        }

        static void Main(string[] args)
        {
            TestHarness.ParseArgs(args);
            using var g = new AsteroidFieldGame();
            g.Run();
        }
    }
}
