using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace TestSprite
{
    public class SpriteTest : Game
    {
        private GraphicsDeviceManager graphics;
        private SpriteBatch spriteBatch;
        private Texture2D whiteTexture;
        private AlphaTestEffect alphaTestEffect;
        private DualTextureEffect dualTextureEffect;
        private EnvironmentMapEffect envMapEffect;
        private BasicEffect basicEffect;
        private SkinnedEffect skinnedEffect;
        private int frameCount = 0;

        public SpriteTest()
        {
            graphics = new GraphicsDeviceManager(this);
            graphics.PreferredBackBufferWidth = 800;
            graphics.PreferredBackBufferHeight = 600;
            graphics.SynchronizeWithVerticalRetrace = false;
            IsMouseVisible = true;
        }

        protected override void LoadContent()
        {
            Console.WriteLine("=== Creating SpriteBatch ===");

            try
            {
                spriteBatch = new SpriteBatch(GraphicsDevice);
                Console.WriteLine("SpriteBatch created successfully!");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR creating SpriteBatch: {ex}");
                throw;
            }

            // Create a 1x1 white texture for solid-color drawing
            whiteTexture = new Texture2D(GraphicsDevice, 1, 1);
            whiteTexture.SetData(new[] { Color.White });
            Console.WriteLine("White texture created");

            // Test AlphaTestEffect loading
            try
            {
                Console.WriteLine("=== Loading AlphaTestEffect ===");
                alphaTestEffect = new AlphaTestEffect(GraphicsDevice);
                Console.WriteLine($"AlphaTestEffect: {alphaTestEffect.Parameters.Count} params, {alphaTestEffect.Techniques.Count} techniques");
            }
            catch (Exception ex) { Console.WriteLine($"ERROR AlphaTestEffect: {ex.Message}"); }

            // Test DualTextureEffect loading
            try
            {
                Console.WriteLine("=== Loading DualTextureEffect ===");
                dualTextureEffect = new DualTextureEffect(GraphicsDevice);
                Console.WriteLine($"DualTextureEffect: {dualTextureEffect.Parameters.Count} params, {dualTextureEffect.Techniques.Count} techniques");
                foreach (var p in dualTextureEffect.Parameters)
                {
                    Console.WriteLine($"  {p.Name}: {p.ParameterClass}.{p.ParameterType}");
                }
            }
            catch (Exception ex) { Console.WriteLine($"ERROR DualTextureEffect: {ex.Message}"); }

            // Test EnvironmentMapEffect loading
            try
            {
                Console.WriteLine("=== Loading EnvironmentMapEffect ===");
                envMapEffect = new EnvironmentMapEffect(GraphicsDevice);
                Console.WriteLine($"EnvironmentMapEffect: {envMapEffect.Parameters.Count} params, {envMapEffect.Techniques.Count} techniques");
            }
            catch (Exception ex) { Console.WriteLine($"ERROR EnvironmentMapEffect: {ex.Message}"); }

            // Test BasicEffect loading
            try
            {
                Console.WriteLine("=== Loading BasicEffect ===");
                basicEffect = new BasicEffect(GraphicsDevice);
                Console.WriteLine($"BasicEffect: {basicEffect.Parameters.Count} params, {basicEffect.Techniques.Count} techniques");
            }
            catch (Exception ex) { Console.WriteLine($"ERROR BasicEffect: {ex.Message}"); }

            // Test SkinnedEffect loading
            try
            {
                Console.WriteLine("=== Loading SkinnedEffect ===");
                skinnedEffect = new SkinnedEffect(GraphicsDevice);
                Console.WriteLine($"SkinnedEffect: {skinnedEffect.Parameters.Count} params, {skinnedEffect.Techniques.Count} techniques");
            }
            catch (Exception ex) { Console.WriteLine($"ERROR SkinnedEffect: {ex.Message}"); }

            Console.WriteLine("=== Initialization Complete ===");
        }

        protected override void Update(GameTime gameTime)
        {
            frameCount++;
            if (frameCount > 3)
            {
                Exit();
            }
        }

        protected override void Draw(GameTime gameTime)
        {
            GraphicsDevice.Clear(Color.CornflowerBlue);

            if (spriteBatch != null && whiteTexture != null)
            {
                try
                {
                    // Draw a few colored rectangles using SpriteBatch
                    spriteBatch.Begin();

                    // Red rectangle (top-left)
                    spriteBatch.Draw(
                        whiteTexture,
                        new Rectangle(100, 100, 200, 150),
                        Color.Red
                    );

                    // Green rectangle (top-right)
                    spriteBatch.Draw(
                        whiteTexture,
                        new Rectangle(400, 100, 200, 150),
                        Color.Green
                    );

                    // Blue rectangle (bottom center)
                    spriteBatch.Draw(
                        whiteTexture,
                        new Rectangle(250, 350, 300, 150),
                        Color.Blue
                    );

                    spriteBatch.End();

                    Console.WriteLine($"Frame {frameCount}: Draw completed");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"ERROR in Draw: {ex}");
                    Exit();
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                spriteBatch?.Dispose();
                whiteTexture?.Dispose();
                alphaTestEffect?.Dispose();
                dualTextureEffect?.Dispose();
                envMapEffect?.Dispose();
                basicEffect?.Dispose();
                skinnedEffect?.Dispose();
            }
            base.Dispose(disposing);
        }

        static void Main(string[] args)
        {
            Console.WriteLine("Starting SpriteEffect Test via SpriteBatch...");
            Console.WriteLine($"OS: {Environment.OSVersion}");
            Console.WriteLine($"64-bit: {Environment.Is64BitProcess}");

            // Ensure the native library is found
            var libPath = System.IO.Path.GetFullPath(
                System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                "../../../../FNA/bin/Debug/net8.0/")
            );
            Console.WriteLine($"Library path candidate: {libPath}");

            try
            {
                using (var game = new SpriteTest())
                {
                    game.Run();
                }
                Console.WriteLine("Test PASSED");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Test FAILED: {ex}");
                Environment.Exit(1);
            }
        }
    }
}
