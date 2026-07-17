using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using FNA.Test;

namespace BasicEffectDemo
{
    /// <summary>
    /// Demonstrates BasicEffect: rotating cube with 3-light rig, specular highlights,
    /// fog, texture, vertex/pixel lighting modes, vertex color.
    /// </summary>
    public class BasicDemo : Game
    {
        private GraphicsDeviceManager graphics;
        private BasicEffect effect;
        private VertexBuffer cube, cubeVc, cubePt;
        private Texture2D checkerTex;
        private float time;
        private int lightMode;       // 0=off, 1=vertex, 2=pixel
        private bool fogEnabled;
        private bool textureEnabled = true;
        private bool vcEnabled;

        public BasicDemo()
        {
            graphics = new GraphicsDeviceManager(this) {
                PreferredBackBufferWidth = 800, PreferredBackBufferHeight = 600,
                SynchronizeWithVerticalRetrace = false
            };
            Window.Title = "BasicEffect Demo — L=light F=fog T=tex V=vcol ESC=quit";
        }

        protected override void LoadContent()
        {
            effect = new BasicEffect(GraphicsDevice);
            effect.EnableDefaultLighting();
            checkerTex = TextureGen.Checkerboard(GraphicsDevice, 256, 32, Color.Orange, Color.White);

            cube = new VertexBuffer(GraphicsDevice, typeof(VertexPositionNormalTexture),
                GeometryGen.Cube().Length, BufferUsage.WriteOnly);
            cube.SetData(GeometryGen.Cube());

            // Color-tinted cube for vertex color mode
            var vcVerts = new VertexPositionColor[36];
            var baseVerts = GeometryGen.Cube();
            Color[] faceColors = { Color.Red, Color.Green, Color.Blue, Color.Yellow, Color.Magenta, Color.Cyan };
            for (int i = 0; i < 36; i++)
                vcVerts[i] = new VertexPositionColor(baseVerts[i].Position, faceColors[i / 6]);
            cubeVc = new VertexBuffer(GraphicsDevice, typeof(VertexPositionColor), 36, BufferUsage.WriteOnly);
            cubeVc.SetData(vcVerts);

            // VPT cube for PT technique (no lighting, no vertex color)
            var ptVerts = new VertexPositionTexture[36];
            for (int i = 0; i < 36; i++)
                ptVerts[i] = new VertexPositionTexture(baseVerts[i].Position, baseVerts[i].TextureCoordinate);
            cubePt = new VertexBuffer(GraphicsDevice, typeof(VertexPositionTexture), 36, BufferUsage.WriteOnly);
            cubePt.SetData(ptVerts);
        }

        protected override void Update(GameTime gt)
        {
            var kb = Keyboard.GetState();
            if (kb.IsKeyDown(Keys.Escape)) Exit();
            if (KeyPress(kb, Keys.L)) lightMode = (lightMode + 1) % 3;
            if (KeyPress(kb, Keys.F)) fogEnabled = !fogEnabled;
            if (KeyPress(kb, Keys.T)) textureEnabled = !textureEnabled;
            if (KeyPress(kb, Keys.V)) vcEnabled = !vcEnabled;
            prevKb = kb;
            time += (float)gt.ElapsedGameTime.TotalSeconds;

            TestHarness.Tick(this, 3, () =>
            {
                var px = TestHarness.ReadBackbuffer(GraphicsDevice);
                int fails = 0;
                // Default state: lit, fog off, tex on, vc off -> VPNT cube visible
                fails += TestHarness.AssertCoverage(px, Color.CornflowerBlue, 0.02f, "basic-coverage");
                TestHarness.Report("BasicEffect", fails);
            });
        }
        private KeyboardState prevKb;
        private bool KeyPress(KeyboardState kb, Keys k) => kb.IsKeyDown(k) && !prevKb.IsKeyDown(k);

        protected override void Draw(GameTime gt)
        {
            GraphicsDevice.Clear(Color.CornflowerBlue);
            float dist = 3.5f;
            var camPos = new Vector3((float)Math.Cos(time * 0.5f) * dist, 1f + (float)Math.Sin(time * 0.3f) * 0.5f, dist);
            var view = Matrix.CreateLookAt(camPos, Vector3.Zero, Vector3.Up);
            var proj = Matrix.CreatePerspectiveFieldOfView(MathHelper.PiOver4, 800f / 600f, 0.1f, 100f);
            var world = Matrix.CreateRotationY(time) * Matrix.CreateRotationX(time * 0.7f);

            effect.World = world;
            effect.View = view;
            effect.Projection = proj;
            effect.LightingEnabled = lightMode > 0;
            effect.PreferPerPixelLighting = lightMode == 2;
            effect.FogEnabled = fogEnabled;
            if (fogEnabled) { effect.FogStart = 4; effect.FogEnd = 8; }
            effect.TextureEnabled = textureEnabled;
            effect.Texture = textureEnabled ? checkerTex : null;
            effect.VertexColorEnabled = vcEnabled;

            effect.CurrentTechnique.Passes[0].Apply();
            // Select vertex buffer matching the current technique's input layout
            VertexBuffer vb;
            if (vcEnabled)
                vb = cubeVc;           // PC or PCT: Position+Color
            else if (lightMode > 0)
                vb = cube;             // PNT: Position+Normal+TexCoord
            else
                vb = cubePt;           // PT: Position+TexCoord
            GraphicsDevice.SetVertexBuffer(vb);
            GraphicsDevice.DrawPrimitives(PrimitiveType.TriangleList, 0, 12);

            if (!TestHarness.Headless)
            {
                string[] modes = { "Off", "Vertex", "Pixel" };
                Window.Title = $"BasicEffect | Light={modes[lightMode]} | Fog={(fogEnabled?"ON":"OFF")} | Tex={(textureEnabled?"ON":"OFF")} | VC={(vcEnabled?"ON":"OFF")}";
            }
        }

        static void Main(string[] args)
        {
            TestHarness.ParseArgs(args);
            using var g = new BasicDemo();
            g.Run();
        }
    }
}
