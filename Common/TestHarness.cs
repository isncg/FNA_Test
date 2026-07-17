using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace FNA.Test
{
    /// <summary>
    /// Headless test harness for automated pixel-level assertion testing.
    /// Usage in Main: TestHarness.ParseArgs(args);
    /// In Update:   TestHarness.Tick(this, 3, () => { ... assert ... });
    /// </summary>
    public static class TestHarness
    {
        public static bool Headless;
        public static int FrameCount;

        /// <summary>Parse command-line args and env vars for headless mode.</summary>
        public static void ParseArgs(string[] args)
        {
            foreach (var a in args)
                if (a == "--headless") Headless = true;
            if (Environment.GetEnvironmentVariable("FNA_TEST_HEADLESS") == "1")
                Headless = true;
        }

        /// <summary>
        /// Call each frame from Game.Update. When headless, increments a frame counter
        /// and on the target frame runs the assertion action then exits.
        /// </summary>
        public static void Tick(Game game, int assertFrame, Action assertAction)
        {
            if (!Headless) return;
            FrameCount++;
            if (FrameCount == assertFrame)
            {
                assertAction();
                game.Exit();
            }
        }

        /// <summary>Read the entire backbuffer into a Color array.</summary>
        public static Color[] ReadBackbuffer(GraphicsDevice dev)
        {
            var pp = dev.PresentationParameters;
            int w = pp.BackBufferWidth, h = pp.BackBufferHeight;
            var px = new Color[w * h];
            dev.GetBackBufferData(px);
            return px;
        }

        /// <summary>
        /// Assert a pixel at (x,y) matches expected color within tolerance.
        /// Returns 0 on pass, 1 on fail. Prints diagnostic on failure.
        /// </summary>
        public static int AssertPixel(Color[] px, int w, int x, int y,
            Color expected, int tol = 3, string label = null)
        {
            var got = px[y * w + x];
            bool match = Math.Abs(got.R - expected.R) <= tol &&
                         Math.Abs(got.G - expected.G) <= tol &&
                         Math.Abs(got.B - expected.B) <= tol &&
                         Math.Abs(got.A - expected.A) <= tol;
            if (!match)
            {
                Console.WriteLine($"FAIL [{label ?? "pixel"}]: at ({x},{y}) expected {expected} got {got}");
                return 1;
            }
            return 0;
        }

        /// <summary>
        /// Assert at least minRatio of pixels are not the clear color.
        /// Returns 0 on pass, 1 on fail. Prints diagnostic on failure.
        /// </summary>
        public static int AssertCoverage(Color[] px, Color clearColor,
            float minRatio, string label = null)
        {
            int nonClear = 0;
            foreach (var c in px)
                if (c.PackedValue != clearColor.PackedValue)
                    nonClear++;
            float ratio = (float)nonClear / px.Length;
            if (ratio < minRatio)
            {
                Console.WriteLine($"FAIL [{label ?? "coverage"}]: coverage {ratio:F4} < {minRatio}");
                return 1;
            }
            return 0;
        }

        /// <summary>Print PASS/FAIL result and set Environment.ExitCode.</summary>
        public static int Report(string testName, int failures)
        {
            if (failures == 0)
            {
                Console.WriteLine($"RESULT: {testName} PASS");
                return 0;
            }
            Console.WriteLine($"RESULT: {testName} FAIL ({failures} failures)");
            Environment.ExitCode = 1;
            return failures;
        }
    }
}
