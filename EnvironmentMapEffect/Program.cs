using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using FNA.Test;

namespace EnvMapDemo
{
    /// <summary>
    /// Demonstrates EnvironmentMapEffect: reflective sphere with cubemap,
    /// Fresnel effect, specular highlights, fog, 3-light directional lighting.
    /// </summary>
    public class EnvDemo : Game
    {
        private GraphicsDeviceManager graphics;
        private EnvironmentMapEffect effect;
        private VertexBuffer sphere;
        private Texture2D checkerTex;
        private TextureCube cubeMap;
        private float time;
        private bool fresnelEnabled = true;
        private float envAmount = 1f;
        private bool specularEnabled = true;
        private bool fogEnabled;

        public EnvDemo()
        {
            graphics = new GraphicsDeviceManager(this) {
                PreferredBackBufferWidth = 800, PreferredBackBufferHeight = 600,
                SynchronizeWithVerticalRetrace = false
            };
            Window.Title = "EnvironmentMapEffect Demo — F=fresnel +/-=amount S=spec G=fog ESC=quit";
        }

        protected override void LoadContent()
        {
            effect = new EnvironmentMapEffect(GraphicsDevice);
            effect.EnableDefaultLighting();
            checkerTex = TextureGen.Checkerboard(GraphicsDevice, 256, 32, Color.Gray, Color.DarkGray);
            cubeMap = TextureGen.CheckerCube(GraphicsDevice, 128, 4);
            var sVerts = GeometryGen.Sphere(16, 32);
            sphere = new VertexBuffer(GraphicsDevice, typeof(VertexPositionNormalTexture), sVerts.Length, BufferUsage.WriteOnly);
            sphere.SetData(sVerts);
        }

        protected override void Update(GameTime gt)
        {
            var kb = Keyboard.GetState();
            if (kb.IsKeyDown(Keys.Escape)) Exit();
            if (KeyPress(kb, Keys.F)) fresnelEnabled = !fresnelEnabled;
            if (KeyPress(kb, Keys.S)) specularEnabled = !specularEnabled;
            if (KeyPress(kb, Keys.G)) fogEnabled = !fogEnabled;
            if (KeyPress(kb, Keys.OemPlus) || KeyPress(kb, Keys.Add))
                envAmount = Math.Min(2f, envAmount + 0.1f);
            if (KeyPress(kb, Keys.OemMinus) || KeyPress(kb, Keys.Subtract))
                envAmount = Math.Max(0f, envAmount - 0.1f);
            prevKb = kb;
            time += (float)gt.ElapsedGameTime.TotalSeconds;

            TestHarness.Tick(this, 3, () =>
            {
                var px = TestHarness.ReadBackbuffer(GraphicsDevice);
                int fails = 0;
                // Sphere with cubemap reflection should have visible pixels
                fails += TestHarness.AssertCoverage(px, Color.CornflowerBlue, 0.02f, "envmap-coverage");
                TestHarness.Report("EnvironmentMapEffect", fails);
            });
        }
        private KeyboardState prevKb;
        private bool KeyPress(KeyboardState kb, Keys k) => kb.IsKeyDown(k) && !prevKb.IsKeyDown(k);

        protected override void Draw(GameTime gt)
        {
            GraphicsDevice.Clear(Color.CornflowerBlue);
            float dist = 2.5f;
            var camPos = new Vector3((float)Math.Cos(time * 0.4f) * dist, 0.5f, (float)Math.Sin(time * 0.4f) * dist);
            var view = Matrix.CreateLookAt(camPos, Vector3.Zero, Vector3.Up);
            var proj = Matrix.CreatePerspectiveFieldOfView(MathHelper.PiOver4, 800f / 600f, 0.1f, 100f);
            var world = Matrix.CreateRotationY(time * 0.5f);

            effect.World = world;
            effect.View = view;
            effect.Projection = proj;
            effect.Texture = checkerTex;
            effect.EnvironmentMap = cubeMap;
            effect.EnvironmentMapAmount = specularEnabled ? envAmount : 0f;
            effect.FresnelFactor = fresnelEnabled ? 0.1f : 0f;
            effect.EnvironmentMapSpecular = specularEnabled ? Vector3.One : Vector3.Zero;
            effect.FogEnabled = fogEnabled;
            if (fogEnabled) { effect.FogStart = 1.5f; effect.FogEnd = 3.5f; effect.FogColor = Color.Red.ToVector3(); }

            effect.CurrentTechnique.Passes[0].Apply();
            GraphicsDevice.SetVertexBuffer(sphere);
            GraphicsDevice.DrawPrimitives(PrimitiveType.TriangleList, 0, GeometryGen.Sphere(16, 32).Length / 3);

            if (!TestHarness.Headless)
                Window.Title = $"EnvMap | Fresnel={(fresnelEnabled?"ON":"OFF")} | Amount={envAmount:F1} | Spec={(specularEnabled?"ON":"OFF")} | Fog={(fogEnabled?"ON":"OFF")}";
        }

        static void Main(string[] args)
        {
            TestHarness.ParseArgs(args);
            using var g = new EnvDemo();
            g.Run();
        }
    }
}
