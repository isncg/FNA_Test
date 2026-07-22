using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using FNA.Test;

namespace TrailEffectCaptureDemo
{
    /// <summary>
    /// Trail effect via concatenated vertex buffer (no instancing).
    ///
    /// Each frame, CPU computes world-space mesh positions + age-faded colors
    /// and stores them in a CPU ring. Before drawing, all valid ring entries
    /// are concatenated into a single flat vertex buffer and rendered with one
    /// DrawPrimitives call — no GPU instancing, no storage buffer needed.
    /// </summary>
    public class TrailCaptureGame : Game
    {
        private GraphicsDeviceManager graphics;
        private Effect meshEffect;
        private Effect trailEffect;
        private IndexBuffer indexBuffer;
        private VertexBuffer geometryBuffer;
        private VertexBuffer trailVertexBuffer;
        private Texture2D dummyTex;

        // Effect parameters (Mesh)
        private EffectParameter meshWorldViewProj;
        private EffectParameter meshTime;
        private EffectParameter meshAmplitude;
        private EffectParameter meshWorld;

        // Effect parameters (Trail)
        private EffectParameter trailViewProj;

        // Vertex declarations
        private VertexDeclaration geometryDecl;
        private VertexDeclaration trailDecl;

        // CPU ring of trail snapshots (world-space positions only; colors computed on build)
        private Vector3[][] ringSnapshots;
        private int ringHead;
        private int ringCount;

        [StructLayout(LayoutKind.Sequential)]
        private struct TrailVertex
        {
            public Vector3 Position;
            public Vector4 Color;
        }

        // Config (ImGui-tweakable)
        private int maxTrailLength = 128;
        private float orbitSpeed = 1.5f;
        private float orbitRadiusX = 2.0f;
        private float orbitRadiusY = 1.5f;
        private float orbitRadiusZ = 1.5f;
        private float selfRotationSpeed = 3.0f;
        private float cubeScale = 0.5f;
        private float waveAmplitude = 0.0f;
        private float cameraDistance = 6.0f;
        private float cameraHeight = 1.5f;
        private bool pauseOrbit = false;

        // State
        private int vertexCount;
        private int primitiveCount;
        private float totalTime;
        private int frameCount;
        private bool meshNeedsRebuild = true;
        private VertexPositionNormalTexture[] meshVertices;

        public TrailCaptureGame()
        {
            graphics = new GraphicsDeviceManager(this)
            {
                PreferredBackBufferWidth = 800,
                PreferredBackBufferHeight = 600,
                SynchronizeWithVerticalRetrace = false
            };
            Window.Title = "Trail Effect Capture — Concatenated VB Trail | ESC=quit";
            IsMouseVisible = true;
        }

        protected override void LoadContent()
        {
            meshEffect  = LoadEmbeddedEffect("TrailEffectCaptureDemo.Mesh.feb");
            trailEffect = LoadEmbeddedEffect("TrailEffectCaptureDemo.Trail.feb");

            meshWorldViewProj = meshEffect.Parameters["WorldViewProj"];
            meshTime          = meshEffect.Parameters["Time"];
            meshAmplitude     = meshEffect.Parameters["Amplitude"];
            meshWorld         = meshEffect.Parameters["World"];

            trailViewProj = trailEffect.Parameters["ViewProj"];

            geometryDecl = new VertexDeclaration(
                new VertexElement(0,  VertexElementFormat.Vector3, VertexElementUsage.Position, 0),
                new VertexElement(12, VertexElementFormat.Vector3, VertexElementUsage.Normal, 0),
                new VertexElement(24, VertexElementFormat.Vector2, VertexElementUsage.TextureCoordinate, 0)
            );

            trailDecl = new VertexDeclaration(
                new VertexElement(0,  VertexElementFormat.Vector3, VertexElementUsage.Position, 0),
                new VertexElement(12, VertexElementFormat.Vector4, VertexElementUsage.Color, 0)
            );

            RebuildMeshGeometry();
            dummyTex = TextureGen.White(GraphicsDevice);
            ImGuiTestHarness.Init(GraphicsDevice);
        }

        private Effect LoadEmbeddedEffect(string resourceName)
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName);
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            return new Effect(GraphicsDevice, ms.ToArray());
        }

        private void RebuildMeshGeometry()
        {
            geometryBuffer?.Dispose();
            indexBuffer?.Dispose();

            var cube = ScalePositions(GeometryGen.Cube(), cubeScale);
            meshVertices = cube;
            vertexCount = cube.Length;
            primitiveCount = vertexCount / 3;

            geometryBuffer = new VertexBuffer(GraphicsDevice, geometryDecl,
                vertexCount, BufferUsage.WriteOnly);
            geometryBuffer.SetData(cube);

            var indices = new uint[vertexCount];
            for (uint i = 0; i < vertexCount; i++)
                indices[i] = i;

            indexBuffer = new IndexBuffer(GraphicsDevice,
                IndexElementSize.ThirtyTwoBits,
                vertexCount, BufferUsage.WriteOnly);
            indexBuffer.SetData(indices);

            // Trail vertex buffer: maxTrailLength frames × vertexCount vertices
            trailVertexBuffer?.Dispose();
            trailVertexBuffer = new VertexBuffer(GraphicsDevice, trailDecl,
                maxTrailLength * vertexCount, BufferUsage.WriteOnly);

            // CPU ring: allocate slots for all trail frames (world positions only)
            ringSnapshots = new Vector3[maxTrailLength][];
            for (int i = 0; i < maxTrailLength; i++)
                ringSnapshots[i] = new Vector3[vertexCount];

            ringHead = 0;
            ringCount = 0;
            meshNeedsRebuild = false;
        }

        protected override void Update(GameTime gameTime)
        {
            var kb = Keyboard.GetState();
            if (kb.IsKeyDown(Keys.Escape)) Exit();

            float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;

            if (!pauseOrbit)
                totalTime += dt;

            if (meshNeedsRebuild)
                RebuildMeshGeometry();

            TestHarness.Tick(this, 5, () =>
            {
                var px = TestHarness.ReadBackbuffer(GraphicsDevice);
                int fails = 0;
                Color bg = new Color(10, 10, 26);
                fails += TestHarness.AssertCoverage(px, bg, 0.005f, "trail-coverage");
                TestHarness.Report("TrailEffectCapture", fails);
            });
        }

        protected override void Draw(GameTime gameTime)
        {
            ImGuiTestHarness.NewFrame(GraphicsDevice);
            GraphicsDevice.Clear(new Color(10, 10, 26));

            // Camera
            var camPos = new Vector3(0, cameraHeight, cameraDistance);
            var view = Matrix.CreateLookAt(camPos, Vector3.Zero, Vector3.Up);
            var proj = Matrix.CreatePerspectiveFieldOfView(
                MathHelper.PiOver4,
                GraphicsDevice.Viewport.AspectRatio,
                0.1f, 100f
            );
            var viewProj = view * proj;

            // Orbit: 3D Lissajous path
            float t = totalTime * orbitSpeed;
            var pos = new Vector3(
                orbitRadiusX * (float)Math.Cos(t),
                orbitRadiusY * (float)Math.Sin(t * 0.7f),
                orbitRadiusZ * (float)Math.Sin(t * 0.5f)
            );

            var rotAxis = Vector3.Normalize(new Vector3(1.0f, 0.5f, 0.3f));
            float rotAngle = totalTime * selfRotationSpeed;
            var rot = Quaternion.CreateFromAxisAngle(rotAxis, rotAngle);
            var world = Matrix.CreateFromQuaternion(rot) * Matrix.CreateTranslation(pos);

            // ── CPU capture: compute world positions for this frame ─────
            var worldPositions = ComputeWorldPositions(world, totalTime);
            Array.Copy(worldPositions, ringSnapshots[ringHead], vertexCount);

            ringHead = (ringHead + 1) % maxTrailLength;
            if (ringCount < maxTrailLength)
                ringCount++;

            // ── Pass 1: Draw mesh head first (writes depth) ────────────────
            meshAmplitude.SetValue(waveAmplitude);
            meshTime.SetValue(totalTime);
            meshWorldViewProj.SetValue(world * viewProj);
            meshWorld.SetValue(world);

            GraphicsDevice.BlendState = BlendState.Opaque;
            GraphicsDevice.DepthStencilState = DepthStencilState.Default;
            GraphicsDevice.RasterizerState = RasterizerState.CullCounterClockwise;

            meshEffect.CurrentTechnique.Passes[0].Apply();
            GraphicsDevice.SetVertexBuffer(geometryBuffer);
            GraphicsDevice.DrawPrimitives(PrimitiveType.TriangleList, 0, primitiveCount);

            // ── Pass 2: Draw trail on top (alpha blended) ──────────────────
            if (ringCount > 0)
            {
                trailViewProj.SetValue(viewProj);

                GraphicsDevice.BlendState = BlendState.NonPremultiplied;
                GraphicsDevice.DepthStencilState = DepthStencilState.DepthRead;
                GraphicsDevice.RasterizerState = RasterizerState.CullCounterClockwise;

                // Concatenate ring slots (oldest→newest) with age-faded colors
                var flat = new TrailVertex[ringCount * vertexCount];
                int writeIdx = 0;
                for (int r = ringCount - 1; r >= 0; r--)
                {
                    int si = (ringHead - 1 - r + maxTrailLength) % maxTrailLength;
                    var color = ComputeTrailColor(r);
                    var src = ringSnapshots[si];
                    for (int v = 0; v < vertexCount; v++)
                    {
                        flat[writeIdx++] = new TrailVertex
                        {
                            Position = src[v],
                            Color = color
                        };
                    }
                }
                trailVertexBuffer.SetData(flat, 0, flat.Length);

                trailEffect.CurrentTechnique.Passes[0].Apply();
                GraphicsDevice.SetVertexBuffer(trailVertexBuffer);
                GraphicsDevice.DrawPrimitives(PrimitiveType.TriangleList, 0,
                    ringCount * primitiveCount);
            }

            frameCount++;

            if (!TestHarness.Headless)
                DrawImGui();
        }

        /// <summary>Age-based trail color: red/orange (new) → blue (old), fading alpha.</summary>
        private Vector4 ComputeTrailColor(int ageIndex)
        {
            float age = (float)ageIndex / maxTrailLength;
            float alpha = 1.0f - age;
            float r = 1.0f + (0.2f - 1.0f) * age;
            float g = 0.3f + (0.3f - 0.3f) * age;
            float b = 0.1f + (1.0f - 0.1f) * age;
            return new Vector4(r, g, b, alpha);
        }

        private Vector3[] ComputeWorldPositions(Matrix world, float time)
        {
            var result = new Vector3[vertexCount];
            for (int i = 0; i < vertexCount; i++)
            {
                Vector3 p = meshVertices[i].Position;
                float wave = MathF.Sin(p.X * 3.0f + time)
                           * MathF.Cos(p.Z * 3.0f + time * 0.7f)
                           * waveAmplitude;
                p.Y += wave;
                result[i] = Vector3.Transform(p, world);
            }
            return result;
        }

        private void DrawImGui()
        {
            ImGuiBindings.BeginPanel("Trail Effect Capture (Concatenated VB)");

            ImGuiBindings.ImGui_Checkbox("Pause Orbit", ref pauseOrbit);

            int[] trailLengths = { 32, 64, 128, 256, 512 };
            string[] names = { "32", "64", "128", "256", "512" };
            int ti = Array.IndexOf(trailLengths, maxTrailLength);
            if (ti < 0) ti = 2;
            if (ImGuiBindings.Combo("Trail Length", ref ti, names))
            {
                maxTrailLength = trailLengths[ti];
                ringHead = 0;
                ringCount = 0;
                trailVertexBuffer?.Dispose();
                trailVertexBuffer = new VertexBuffer(GraphicsDevice, trailDecl,
                    maxTrailLength * vertexCount, BufferUsage.WriteOnly);
                ringSnapshots = new Vector3[maxTrailLength][];
                for (int i = 0; i < maxTrailLength; i++)
                    ringSnapshots[i] = new Vector3[vertexCount];
            }

            bool geomRebuild = false;
            geomRebuild |= ImGuiBindings.ImGui_SliderFloat("Cube Scale", ref cubeScale, 0.05f, 1.0f);
            if (geomRebuild)
                meshNeedsRebuild = true;

            ImGuiBindings.ImGui_SliderFloat("Wave Amp", ref waveAmplitude, 0.0f, 0.5f);
            ImGuiBindings.ImGui_SliderFloat("Orbit Speed", ref orbitSpeed, 0.1f, 5.0f);
            ImGuiBindings.ImGui_SliderFloat("Orbit Radius X", ref orbitRadiusX, 0.5f, 4.0f);
            ImGuiBindings.ImGui_SliderFloat("Orbit Radius Y", ref orbitRadiusY, 0.5f, 4.0f);
            ImGuiBindings.ImGui_SliderFloat("Orbit Radius Z", ref orbitRadiusZ, 0.5f, 4.0f);
            ImGuiBindings.ImGui_SliderFloat("Self-Rot Speed", ref selfRotationSpeed, 0.1f, 10.0f);
            ImGuiBindings.ImGui_SliderFloat("Cam Distance", ref cameraDistance, 2.0f, 10.0f);
            ImGuiBindings.ImGui_SliderFloat("Cam Height", ref cameraHeight, 0.0f, 4.0f);

            ImGuiBindings.ImGui_Text($"Vertices: {vertexCount}  Trail: {ringCount}/{maxTrailLength}  Frame: {frameCount}");
            ImGuiBindings.ImGui_Text($"FPS: {(int)(1.0 / Math.Max(0.001, TargetElapsedTime.TotalSeconds))}");
            ImGuiBindings.EndPanel();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                meshEffect?.Dispose();
                trailEffect?.Dispose();
                geometryBuffer?.Dispose();
                indexBuffer?.Dispose();
                trailVertexBuffer?.Dispose();
                dummyTex?.Dispose();
            }
            base.Dispose(disposing);
        }

        static void Main(string[] args)
        {
            TestHarness.ParseArgs(args);
            using var g = new TrailCaptureGame();
            g.Run();
        }

        private static VertexPositionNormalTexture[] ScalePositions(
            VertexPositionNormalTexture[] vertices, float scale)
        {
            var scaled = new VertexPositionNormalTexture[vertices.Length];
            for (int i = 0; i < vertices.Length; i++)
            {
                scaled[i] = new VertexPositionNormalTexture(
                    vertices[i].Position * scale,
                    vertices[i].Normal,
                    vertices[i].TextureCoordinate
                );
            }
            return scaled;
        }
    }
}
