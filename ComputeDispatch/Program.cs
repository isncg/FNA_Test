using System;
using System.IO;
using System.Reflection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using FNA.Test;

namespace ComputeDispatchTest
{
    /// <summary>
    /// Phase 4 test: GPU compute dispatch writing to a storage buffer.
    ///
    /// The Doubler compute shader (RWStructuredBuffer&lt;float&gt; at u0,
    /// 64 threads/group) writes Output[i] = i * 2.0. We dispatch enough
    /// groups to cover Count elements, then read the buffer back and verify.
    ///
    /// This exercises the full Phase 4 path:
    ///   CreateEffect (compute pipeline) → SetComputeStorageBuffersWritable
    ///   → DispatchCompute → GetData readback
    /// </summary>
    public class ComputeDispatchGame : Game
    {
        private GraphicsDeviceManager graphics;
        private Effect effect;
        private StorageBuffer outputBuffer;

        private const int Count = 256;              // 4 groups of 64
        private const int ThreadsPerGroup = 64;

        private bool done;

        public ComputeDispatchGame()
        {
            graphics = new GraphicsDeviceManager(this)
            {
                PreferredBackBufferWidth = 320,
                PreferredBackBufferHeight = 240,
                SynchronizeWithVerticalRetrace = false
            };
            Window.Title = "ComputeDispatch — Phase 4 | ESC=quit";
        }

        protected override void LoadContent()
        {
            using var stream = Assembly.GetExecutingAssembly()
                .GetManifestResourceStream("ComputeDispatchTest.Doubler.feb");
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            effect = new Effect(GraphicsDevice, ms.ToArray());

            // vertexWrite=true → COMPUTE_STORAGE_WRITE; vertexRead=true so the
            // same buffer can be read back via GetData.
            outputBuffer = new StorageBuffer(GraphicsDevice,
                Count * sizeof(float),
                vertexWrite: true,
                vertexRead: true);

            Console.WriteLine($"[ComputeDispatch] Effect + {Count}-element buffer ready.");
        }

        private void RunCompute()
        {
            GraphicsDevice.SetComputeStorageBuffersWritable(outputBuffer);
            GraphicsDevice.DispatchCompute(
                effect.CurrentTechnique.Passes[0],
                Count / ThreadsPerGroup, 1, 1);
        }

        protected override void Draw(GameTime gameTime)
        {
            GraphicsDevice.Clear(Color.Black);

            if (!done)
            {
                RunCompute();
                done = true;
            }

            base.Draw(gameTime);
        }

        protected override void Update(GameTime gameTime)
        {
            if (Microsoft.Xna.Framework.Input.Keyboard.GetState()
                .IsKeyDown(Microsoft.Xna.Framework.Input.Keys.Escape))
            {
                Exit();
            }

            TestHarness.Tick(this, 3, RunAssertions);
            base.Update(gameTime);
        }

        private void RunAssertions()
        {
            int failures = 0;

            var result = new float[Count];
            outputBuffer.GetData(result);

            // Verify Output[i] == i * 2.0 across the whole buffer
            int mismatches = 0;
            int firstBad = -1;
            for (int i = 0; i < Count; i++)
            {
                if (Math.Abs(result[i] - i * 2.0f) > 0.001f)
                {
                    mismatches++;
                    if (firstBad < 0) firstBad = i;
                }
            }

            if (mismatches > 0)
            {
                Console.WriteLine(
                    $"FAIL [compute output]: {mismatches}/{Count} mismatches; " +
                    $"first at index {firstBad} = {result[firstBad]} (expected {firstBad * 2.0f})");
                failures += 1;
            }
            else
            {
                Console.WriteLine(
                    $"[ComputeDispatch] All {Count} values correct " +
                    $"(e.g. [1]={result[1]}, [100]={result[100]}, [255]={result[255]}).");
            }

            TestHarness.Report("ComputeDispatch", failures);
        }

        protected override void UnloadContent()
        {
            outputBuffer?.Dispose();
            effect?.Dispose();
            base.UnloadContent();
        }

        [STAThread]
        static void Main(string[] args)
        {
            TestHarness.ParseArgs(args);
            using var game = new ComputeDispatchGame();
            game.Run();
        }
    }
}
