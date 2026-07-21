using System;
using System.IO;
using System.Reflection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using FNA.Test;

namespace TrailEffectDemo
{
    /// <summary>
    /// GPU-instanced cube trail effect. A cube orbits the origin while self-rotating.
    /// Each frame records position+rotation into a Trail instance. The entire trail
    /// is drawn in a single DrawInstancedPrimitives call, with per-instance color
    /// gradient: red/opaque (head) → blue/transparent (tail).
    ///
    /// Demonstrates the reusable Trail class (Common/Trail.cs).
    /// </summary>
    public class TrailEffectGame : Game
    {
        private GraphicsDeviceManager graphics;
        private Effect effect;
        private Trail trail;

        // ── Config (ImGui-tweakable) ────────────────────────────────────
        private int maxTrailLength = 256;
        private float orbitSpeed = 1.5f;
        private float orbitRadiusX = 2.0f;
        private float orbitRadiusY = 1.5f;
        private float orbitRadiusZ = 1.5f;
        private float selfRotationSpeed = 3.0f;
        private float cubeScale = 0.5f;
        private float cameraDistance = 6.0f;
        private float cameraHeight = 1.5f;
        private bool pauseOrbit = false;

        // ── State ───────────────────────────────────────────────────────
        private float totalTime;

        public TrailEffectGame()
        {
            graphics = new GraphicsDeviceManager(this)
            {
                PreferredBackBufferWidth = 800,
                PreferredBackBufferHeight = 600,
                SynchronizeWithVerticalRetrace = false
            };
            Window.Title = "Trail Effect Demo — GPU Instancing | ESC=quit";
            IsMouseVisible = true;
        }

        protected override void LoadContent()
        {
            // ── Load embedded FEB ────────────────────────────────────────
            using var stream = Assembly.GetExecutingAssembly()
                .GetManifestResourceStream("TrailEffectDemo.TrailEffect.feb");
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            effect = new Effect(GraphicsDevice, ms.ToArray());

            Console.WriteLine($"[TrailEffect] Effect loaded: {effect.Techniques.Count} techniques, {effect.Parameters.Count} params");

            // ── Create trail with scaled cube geometry ───────────────────
            trail = new Trail(effect, GraphicsDevice,
                ScalePositions(GeometryGen.CubePositions(), cubeScale),
                maxTrailLength);

            // ── ImGui ───────────────────────────────────────────────────
            ImGuiTestHarness.Init(GraphicsDevice);

            Console.WriteLine($"[TrailEffect] Ready. Max trail length: {maxTrailLength}");
        }

        protected override void Update(GameTime gameTime)
        {
            var kb = Keyboard.GetState();
            if (kb.IsKeyDown(Keys.Escape)) Exit();

            float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;

            if (!pauseOrbit)
                totalTime += dt;

            // ── Compute orbit position (3D Lissajous path) ───────────────
            float t = totalTime * orbitSpeed;
            var pos = new Vector3(
                orbitRadiusX * (float)Math.Cos(t),
                orbitRadiusY * (float)Math.Sin(t * 0.7f),
                orbitRadiusZ * (float)Math.Sin(t * 0.5f)
            );

            // ── Compute self-rotation ────────────────────────────────────
            var rotAxis = Vector3.Normalize(new Vector3(1.0f, 0.5f, 0.3f));
            float rotAngle = totalTime * selfRotationSpeed;
            var rot = Quaternion.CreateFromAxisAngle(rotAxis, rotAngle);

            // ── Record trail ────────────────────────────────────────────
            trail.AddRecord(pos, rot);

            // ── Headless test ───────────────────────────────────────────
            TestHarness.Tick(this, 3, () =>
            {
                var px = TestHarness.ReadBackbuffer(GraphicsDevice);
                int fails = 0;
                // Trail cubes should be visible on the dark background
                Color bg = new Color(10, 10, 26);
                fails += TestHarness.AssertCoverage(px, bg, 0.005f, "trail-coverage");
                TestHarness.Report("TrailEffect", fails);
            });
        }

        protected override void Draw(GameTime gameTime)
        {
            ImGuiTestHarness.NewFrame(GraphicsDevice);

            GraphicsDevice.Clear(new Color(10, 10, 26)); // dark blue-ish background

            // ── Camera ──────────────────────────────────────────────────
            var camPos = new Vector3(0, cameraHeight, cameraDistance);
            var view = Matrix.CreateLookAt(camPos, Vector3.Zero, Vector3.Up);
            var proj = Matrix.CreatePerspectiveFieldOfView(
                MathHelper.PiOver4,
                GraphicsDevice.Viewport.AspectRatio,
                0.1f, 100f
            );

            // ── Draw trail (handles render state, buffers, draw call) ───
            trail.Draw(view, proj);

            // ── ImGui panel ─────────────────────────────────────────────
            if (!TestHarness.Headless)
                DrawImGui();
        }

        private void DrawImGui()
        {
            ImGuiBindings.BeginPanel("Trail Effect");

            ImGuiBindings.ImGui_Checkbox("Pause Orbit", ref pauseOrbit);

            int[] trailLengths = { 32, 64, 128, 256, 512, 1024 };
            string[] trailNames = { "32", "64", "128", "256", "512", "1024" };
            int ti = Array.IndexOf(trailLengths, maxTrailLength);
            if (ti < 0) ti = 3;
            if (ImGuiBindings.Combo("Trail Length", ref ti, trailNames))
            {
                maxTrailLength = trailLengths[ti];
                trail.Resize(maxTrailLength);
            }

            bool geomRebuild = false;
            geomRebuild |= ImGuiBindings.ImGui_SliderFloat("Cube Scale", ref cubeScale, 0.05f, 1.0f);
            if (geomRebuild)
                trail.SetGeometry(ScalePositions(GeometryGen.CubePositions(), cubeScale));

            ImGuiBindings.ImGui_SliderFloat("Orbit Speed", ref orbitSpeed, 0.1f, 5.0f);
            ImGuiBindings.ImGui_SliderFloat("Orbit Radius X", ref orbitRadiusX, 0.5f, 4.0f);
            ImGuiBindings.ImGui_SliderFloat("Orbit Radius Y", ref orbitRadiusY, 0.5f, 4.0f);
            ImGuiBindings.ImGui_SliderFloat("Orbit Radius Z", ref orbitRadiusZ, 0.5f, 4.0f);
            ImGuiBindings.ImGui_SliderFloat("Self-Rot Speed", ref selfRotationSpeed, 0.1f, 10.0f);
            ImGuiBindings.ImGui_SliderFloat("Cam Distance", ref cameraDistance, 2.0f, 15.0f);
            ImGuiBindings.ImGui_SliderFloat("Cam Height", ref cameraHeight, 0.0f, 5.0f);

            ImGuiBindings.ImGui_Text($"Instances: {trail.RecordCount}");
            ImGuiBindings.ImGui_Text($"FPS: {(int)(1.0 / Math.Max(0.001, TargetElapsedTime.TotalSeconds))}");
            ImGuiBindings.EndPanel();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                trail?.Dispose();
                effect?.Dispose();
            }
            base.Dispose(disposing);
        }

        /// <summary>Scale a position array in-place (new array).</summary>
        private static Vector3[] ScalePositions(Vector3[] positions, float scale)
        {
            var scaled = new Vector3[positions.Length];
            for (int i = 0; i < positions.Length; i++)
                scaled[i] = positions[i] * scale;
            return scaled;
        }

        static void Main(string[] args)
        {
            TestHarness.ParseArgs(args);
            using var g = new TrailEffectGame();
            g.Run();
        }
    }
}
