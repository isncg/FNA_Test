using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using FNA.Test;

namespace SkinnedDemo
{
    /// <summary>
    /// Demonstrates SkinnedEffect: GPU bone skinning with oscillating bend animation.
    /// A cylinder bends back and forth using 2 bones with interpolated weights.
    /// </summary>
    public class SkinnedDemo : Game
    {
        private GraphicsDeviceManager graphics;
        private SkinnedEffect effect;
        private VertexBuffer cylinder;
        private Texture2D checkerTex;
        private float time;
        private int weightsMode = 2; // index into Wpvs: 0→1, 1→2, 2→4
        private int lightMode = 1;   // 0=vertex, 1=pixel
        private static readonly int[] Wpvs = { 1, 2, 4 };

        public SkinnedDemo()
        {
            graphics = new GraphicsDeviceManager(this) {
                PreferredBackBufferWidth = 800, PreferredBackBufferHeight = 600,
                SynchronizeWithVerticalRetrace = false
            };
            Window.Title = "SkinnedEffect Demo — ImGUI panel | ESC=quit";
        }

        protected override void LoadContent()
        {
            effect = new SkinnedEffect(GraphicsDevice);
            effect.EnableDefaultLighting();
            effect.AmbientLightColor = new Vector3(0.4f, 0.4f, 0.4f);
            effect.WeightsPerVertex = 4;
            checkerTex = TextureGen.Checkerboard(GraphicsDevice, 256, 32, Color.Lime, Color.DarkGreen);
            var cylVerts = GeometryGen.SkinnedCylinder(16, 32);
            cylinder = new VertexBuffer(GraphicsDevice, typeof(SkinnedVertex), cylVerts.Length, BufferUsage.WriteOnly);
            cylinder.SetData(cylVerts);
            ImGuiTestHarness.Init(GraphicsDevice);
        }

        protected override void Update(GameTime gt)
        {
            var kb = Keyboard.GetState();
            if (kb.IsKeyDown(Keys.Escape)) Exit();
            time += (float)gt.ElapsedGameTime.TotalSeconds;

            TestHarness.Tick(this, 3, () =>
            {
                var px = TestHarness.ReadBackbuffer(GraphicsDevice);
                int fails = 0;
                // Cylinder with skinning should have visible pixels
                fails += TestHarness.AssertCoverage(px, Color.CornflowerBlue, 0.02f, "skinned-coverage");
                TestHarness.Report("SkinnedEffect", fails);
            });
        }

        protected override void Draw(GameTime gt)
        {
            ImGuiTestHarness.NewFrame(GraphicsDevice);
            GraphicsDevice.Clear(Color.CornflowerBlue);
            float dist = 3.5f;
            var camPos = new Vector3((float)Math.Cos(time * 0.3f) * dist, 0.5f, dist);
            var view = Matrix.CreateLookAt(camPos, new Vector3(0, 0.5f, 0), Vector3.Up);
            var proj = Matrix.CreatePerspectiveFieldOfView(MathHelper.PiOver4, 800f / 600f, 0.1f, 100f);
            var world = Matrix.CreateRotationY(time * 0.6f);

            // Oscillating bend angle for bone1 (pivot at Y=+1)
            float angle = 0.6f * (float)Math.Sin(time * 2.5f);
            var bonePivot = Matrix.CreateTranslation(0, 1, 0);
            var bone1 = bonePivot * Matrix.CreateRotationZ(angle) * Matrix.Invert(bonePivot);

            effect.World = world;
            effect.View = view;
            effect.Projection = proj;
            effect.Texture = checkerTex;
            effect.PreferPerPixelLighting = lightMode == 1;
            effect.SetBoneTransforms(new[] { Matrix.Identity, bone1 });

            effect.CurrentTechnique.Passes[0].Apply();
            GraphicsDevice.SetVertexBuffer(cylinder);
            var cylVerts = GeometryGen.SkinnedCylinder(16, 32);
            GraphicsDevice.DrawPrimitives(PrimitiveType.TriangleList, 0, cylVerts.Length / 3);

            if (!TestHarness.Headless)
            {
                ImGuiBindings.BeginPanel("SkinnedEffect");
                string[] wpvNames = { "1", "2", "4" };
                ImGuiBindings.Combo("Weights/Vertex", ref weightsMode, wpvNames);
                effect.WeightsPerVertex = Wpvs[weightsMode];
                string[] lNames = { "Vertex", "Pixel" };
                ImGuiBindings.Combo("Lighting", ref lightMode, lNames);
                ImGuiBindings.EndPanel();
            }
        }

        static void Main(string[] args)
        {
            TestHarness.ParseArgs(args);
            using var g = new SkinnedDemo();
            g.Run();
        }
    }
}
