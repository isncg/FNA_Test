using System;
using System.IO;
using System.Reflection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using FNA.Test;

namespace ParticleFireDemo
{
    /// <summary>
    /// GPU-instanced fire particle system. Particle simulation runs entirely
    /// in the vertex shader (analytic formulas from birth state + ElapsedTime).
    /// Uses hardware instancing: one geometry buffer (4 quad corners) × N instances.
    /// Single DrawInstancedPrimitives call per frame. Scales to 100K+ particles.
    /// </summary>
    public class ParticleFireGame : Game
    {
        private GraphicsDeviceManager graphics;
        private Effect effect;
        private IndexBuffer indexBuffer;
        private VertexBuffer geometryBuffer;   // slot 0, freq=0: 4 quad corners
        private VertexBuffer instanceBuffer;    // slot 1, freq=1: N particle birth states
        private Texture2D glowTex;
        private float totalTime;

        // ── Effect parameters ───────────────────────────────────────────
        private EffectParameter worldViewProjParam;
        private EffectParameter elapsedTimeParam;
        private EffectParameter cameraRightParam;
        private EffectParameter cameraUpParam;

        // ── Vertex declarations ─────────────────────────────────────────
        private VertexDeclaration cornerDecl;
        private VertexDeclaration instanceDecl;

        // ── Config (ImGui-tweakable) ────────────────────────────────────
        private int particleCount = 10000;
        private float emitRadius = 0.3f;
        private float lifetimeMin = 1.0f;
        private float lifetimeMax = 3.0f;
        private float speedMin = 0.5f;
        private float speedMax = 2.0f;
        private float sizeMin = 0.05f;
        private float sizeMax = 0.2f;
        private float cameraDistance = 3.0f;
        private float cameraHeight = 1.0f;

        // ── Instance data struct ────────────────────────────────────────
        private struct ParticleBirthData
        {
            public Vector4 BirthData0; // X=spawnRadius, Y=spawnAngle, Z=lifetime, W=speed
            public Vector4 BirthData1; // X=size, Y=seed, Z=0, W=0
        }

        // ── RNG ─────────────────────────────────────────────────────────
        private static readonly Random rng = new Random();

        public ParticleFireGame()
        {
            graphics = new GraphicsDeviceManager(this)
            {
                PreferredBackBufferWidth = 800,
                PreferredBackBufferHeight = 600,
                SynchronizeWithVerticalRetrace = false
            };
            Window.Title = "Particle Fire Demo — GPU Instancing | ESC=quit";
            IsMouseVisible = true;
        }

        protected override void LoadContent()
        {
            // ── Load embedded FEB ────────────────────────────────────────
            using var stream = Assembly.GetExecutingAssembly()
                .GetManifestResourceStream("ParticleFireDemo.ParticleEffect.feb");
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            effect = new Effect(GraphicsDevice, ms.ToArray());

            worldViewProjParam = effect.Parameters["WorldViewProj"];
            elapsedTimeParam   = effect.Parameters["ElapsedTime"];
            cameraRightParam   = effect.Parameters["CameraRight"];
            cameraUpParam      = effect.Parameters["CameraUp"];

            Console.WriteLine($"[ParticleFire] Effect loaded: {effect.Techniques.Count} techniques, {effect.Parameters.Count} params");

            // ── Vertex declarations ─────────────────────────────────────
            cornerDecl = new VertexDeclaration(
                new VertexElement(0, VertexElementFormat.Vector2,
                    VertexElementUsage.TextureCoordinate, 0)
            );
            instanceDecl = new VertexDeclaration(
                new VertexElement(0,  VertexElementFormat.Vector4,
                    VertexElementUsage.TextureCoordinate, 1),
                new VertexElement(16, VertexElementFormat.Vector4,
                    VertexElementUsage.TextureCoordinate, 2)
            );

            // ── Geometry buffer: 4 quad corners ─────────────────────────
            geometryBuffer = new VertexBuffer(GraphicsDevice, cornerDecl,
                4, BufferUsage.WriteOnly);
            geometryBuffer.SetData(new[]
            {
                new Vector2(-1, -1),
                new Vector2( 1, -1),
                new Vector2( 1,  1),
                new Vector2(-1,  1),
            });

            // ── Index buffer: unit quad (2 triangles) ───────────────────
            indexBuffer = new IndexBuffer(GraphicsDevice, IndexElementSize.ThirtyTwoBits,
                6, BufferUsage.WriteOnly);
            indexBuffer.SetData(new uint[] { 0, 1, 2, 0, 2, 3 });

            // ── Instance buffer: per-particle birth data ────────────────
            RebuildInstanceBuffer();

            // ── Glow texture ────────────────────────────────────────────
            glowTex = TextureGen.RadialGradient(GraphicsDevice, 64);

            // ── ImGui ───────────────────────────────────────────────────
            ImGuiTestHarness.Init(GraphicsDevice);

            Console.WriteLine($"[ParticleFire] {particleCount} particles ready.");
        }

        private void RebuildInstanceBuffer()
        {
            instanceBuffer?.Dispose();

            var data = new ParticleBirthData[particleCount];
            for (int i = 0; i < particleCount; i++)
            {
                float angle = (float)(rng.NextDouble() * Math.PI * 2.0);
                float radius = (float)(rng.NextDouble() * emitRadius);
                float lifetime = lifetimeMin + (float)(rng.NextDouble() * (lifetimeMax - lifetimeMin));
                float speed = speedMin + (float)(rng.NextDouble() * (speedMax - speedMin));
                float size = sizeMin + (float)(rng.NextDouble() * (sizeMax - sizeMin));
                float seed = (float)rng.NextDouble();

                data[i] = new ParticleBirthData
                {
                    BirthData0 = new Vector4(radius, angle, lifetime, speed),
                    BirthData1 = new Vector4(size, seed, 0, 0)
                };
            }

            instanceBuffer = new VertexBuffer(GraphicsDevice, instanceDecl,
                particleCount, BufferUsage.WriteOnly);
            instanceBuffer.SetData(data);
        }

        protected override void Update(GameTime gameTime)
        {
            var kb = Keyboard.GetState();
            if (kb.IsKeyDown(Keys.Escape)) Exit();

            float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
            totalTime += dt;

            // ── Headless test ───────────────────────────────────────────
            TestHarness.Tick(this, 3, () =>
            {
                var px = TestHarness.ReadBackbuffer(GraphicsDevice);
                int fails = 0;
                // Fire particles should be visible on the dark background
                Color bg = new Color(5, 5, 15);
                fails += TestHarness.AssertCoverage(px, bg, 0.02f, "fire-coverage");
                TestHarness.Report("ParticleFire", fails);
            });
        }

        protected override void Draw(GameTime gameTime)
        {
            ImGuiTestHarness.NewFrame(GraphicsDevice);

            GraphicsDevice.Clear(new Color(5, 5, 15)); // dark night sky

            // ── Camera ──────────────────────────────────────────────────
            float camAngle = totalTime * 0.3f;
            var camPos = new Vector3(
                (float)Math.Cos(camAngle) * cameraDistance,
                cameraHeight,
                (float)Math.Sin(camAngle) * cameraDistance
            );
            var view = Matrix.CreateLookAt(camPos, new Vector3(0, 0.5f, 0), Vector3.Up);
            var proj = Matrix.CreatePerspectiveFieldOfView(
                MathHelper.PiOver4,
                GraphicsDevice.Viewport.AspectRatio,
                0.1f, 100f
            );
            var worldViewProj = Matrix.Identity * view * proj;

            // Extract camera right/up from view matrix inverse
            var viewInv = Matrix.Invert(view);
            var cameraRight = viewInv.Right;   // first row (normalized)
            var cameraUp = viewInv.Up;         // second row (normalized)

            // ── Set effect parameters ────────────────────────────────────
            worldViewProjParam.SetValue(worldViewProj);
            elapsedTimeParam.SetValue(totalTime);
            cameraRightParam.SetValue(new Vector4(cameraRight, 0));
            cameraUpParam.SetValue(new Vector4(cameraUp, 0));

            // ── Render state for additive fire glow ─────────────────────
            GraphicsDevice.BlendState = BlendState.Additive;
            GraphicsDevice.DepthStencilState = DepthStencilState.DepthRead;
            GraphicsDevice.RasterizerState = RasterizerState.CullNone;

            // ── Bind texture ────────────────────────────────────────────
            GraphicsDevice.Textures[0] = glowTex;

            // ── Apply effect ────────────────────────────────────────────
            effect.CurrentTechnique.Passes[0].Apply();

            // ── Bind geometry + instance buffers ────────────────────────
            GraphicsDevice.SetVertexBuffers(
                new VertexBufferBinding(geometryBuffer, 0, 0),   // slot 0: per-vertex
                new VertexBufferBinding(instanceBuffer, 0, 1)    // slot 1: per-instance
            );
            GraphicsDevice.Indices = indexBuffer;

            // ── Draw instanced ──────────────────────────────────────────
            GraphicsDevice.DrawInstancedPrimitives(
                PrimitiveType.TriangleList,
                baseVertex: 0,
                minVertexIndex: 0,
                numVertices: 4,
                startIndex: 0,
                primitiveCount: 2,
                instanceCount: particleCount
            );

            // ── ImGui panel ─────────────────────────────────────────────
            if (!TestHarness.Headless)
            {
                GraphicsDevice.BlendState = BlendState.AlphaBlend;
                DrawImGui();
            }
        }

        private void DrawImGui()
        {
            ImGuiBindings.BeginPanel("Particle Fire");

            int[] counts = { 1000, 5000, 10000, 50000, 100000 };
            string[] countNames = { "1K", "5K", "10K", "50K", "100K" };
            int ci = Array.IndexOf(counts, particleCount);
            if (ci < 0) ci = 2;
            if (ImGuiBindings.Combo("Particles", ref ci, countNames))
            {
                particleCount = counts[ci];
                RebuildInstanceBuffer();
            }

            bool rebuild = false;
            rebuild |= ImGuiBindings.ImGui_SliderFloat("Radius", ref emitRadius, 0.05f, 1.0f);
            rebuild |= ImGuiBindings.ImGui_SliderFloat("Lifetime Min", ref lifetimeMin, 0.3f, 5.0f);
            rebuild |= ImGuiBindings.ImGui_SliderFloat("Lifetime Max", ref lifetimeMax, 0.5f, 8.0f);
            rebuild |= ImGuiBindings.ImGui_SliderFloat("Speed Min", ref speedMin, 0.1f, 3.0f);
            rebuild |= ImGuiBindings.ImGui_SliderFloat("Speed Max", ref speedMax, 0.3f, 5.0f);
            rebuild |= ImGuiBindings.ImGui_SliderFloat("Size Min", ref sizeMin, 0.01f, 0.3f);
            rebuild |= ImGuiBindings.ImGui_SliderFloat("Size Max", ref sizeMax, 0.03f, 0.5f);
            if (rebuild) RebuildInstanceBuffer();

            ImGuiBindings.ImGui_SliderFloat("Cam Dist", ref cameraDistance, 1.0f, 8.0f);
            ImGuiBindings.ImGui_SliderFloat("Cam Height", ref cameraHeight, 0.1f, 4.0f);

            ImGuiBindings.ImGui_Text($"FPS: {(int)(1.0 / Math.Max(0.001, TargetElapsedTime.TotalSeconds))}");
            ImGuiBindings.EndPanel();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                effect?.Dispose();
                geometryBuffer?.Dispose();
                instanceBuffer?.Dispose();
                indexBuffer?.Dispose();
                glowTex?.Dispose();
            }
            base.Dispose(disposing);
        }

        static void Main(string[] args)
        {
            TestHarness.ParseArgs(args);
            using var g = new ParticleFireGame();
            g.Run();
        }
    }
}
