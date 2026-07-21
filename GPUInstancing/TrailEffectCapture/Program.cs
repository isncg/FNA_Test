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
	/// GPU-instanced trail via storage buffer replay.
	///
	/// Each frame, CPU computes world-space mesh positions and uploads them
	/// to a ring buffer (GPU storage buffer). The trail vertex shader reads
	/// historical positions from the storage buffer to render faded ghost
	/// instances in a single DrawInstancedPrimitives call.
	///
	/// GPU-side capture is blocked by SDL3's lack of GRAPHICS_STORAGE_WRITE;
	/// once available, the CPU SetData step can be replaced with vertex shader
	/// RWStructuredBuffer writes.
	/// </summary>
	public class TrailCaptureGame : Game
	{
		private GraphicsDeviceManager graphics;
		private Effect meshEffect;
		private Effect trailEffect;
		private IndexBuffer indexBuffer;
		private VertexBuffer geometryBuffer;
		private StorageBuffer ringBuffer;
		private Texture2D dummyTex;

		// Effect parameters (Mesh)
		private EffectParameter meshWorldViewProj;
		private EffectParameter meshTime;
		private EffectParameter meshAmplitude;
		private EffectParameter meshWorld;

		// Effect parameters (Trail)
		private EffectParameter trailViewProj;
		private EffectParameter trailVertexCount;
		private EffectParameter trailRingHead;
		private EffectParameter trailMaxRingSize;

		// Vertex declarations
		private VertexDeclaration geometryDecl;

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
		private int ringHead;
		private int ringCount;
		private float totalTime;
		private int frameCount;
		private bool meshNeedsRebuild = true;
		private VertexPositionNormalTexture[] meshVertices;

		// Previous-frame snapshot for verification

		[StructLayout(LayoutKind.Sequential)]
		private struct Float4 { public float X, Y, Z, W; }

		public TrailCaptureGame()
		{
			graphics = new GraphicsDeviceManager(this)
			{
				PreferredBackBufferWidth = 800,
				PreferredBackBufferHeight = 600,
				SynchronizeWithVerticalRetrace = false
			};
			Window.Title = "Trail Effect Capture — Storage Buffer Trail | ESC=quit";
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

			trailViewProj    = trailEffect.Parameters["ViewProj"];
			trailVertexCount = trailEffect.Parameters["VertexCount"];
			trailRingHead    = trailEffect.Parameters["RingHead"];
			trailMaxRingSize = trailEffect.Parameters["MaxRingSize"];

			geometryDecl = new VertexDeclaration(
				new VertexElement(0,  VertexElementFormat.Vector3, VertexElementUsage.Position, 0),
				new VertexElement(12, VertexElementFormat.Vector3, VertexElementUsage.Normal, 0),
				new VertexElement(24, VertexElementFormat.Vector2, VertexElementUsage.TextureCoordinate, 0)
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

			ringBuffer?.Dispose();
			ringBuffer = new StorageBuffer(GraphicsDevice,
				maxTrailLength * vertexCount * 16,
				vertexWrite: true, vertexRead: true);

			trailVertexCount.SetValue((float)vertexCount);
			trailMaxRingSize.SetValue((float)maxTrailLength);

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

			// ── CPU capture: compute world positions → ring buffer ───
			var worldPositions = ComputeWorldPositions(world, totalTime);
			var captured = new Float4[vertexCount];
			for (int i = 0; i < vertexCount; i++)
			{
				captured[i] = new Float4
				{
					X = worldPositions[i].X,
					Y = worldPositions[i].Y,
					Z = worldPositions[i].Z,
					W = 0
				};
			}

			int byteOffset = ringHead * vertexCount * 16;
			ringBuffer.SetData(byteOffset, captured, 0, vertexCount);

			ringHead = (ringHead + 1) % maxTrailLength;
			if (ringCount < maxTrailLength)
				ringCount++;

			// ── Pass 1: Draw mesh head first (writes depth) ─────────
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

			// ── Pass 2: Draw trail on top (alpha blended) ──────────
			if (ringCount > 0)
			{
				trailViewProj.SetValue(viewProj);
				trailRingHead.SetValue((float)ringHead);

				GraphicsDevice.BlendState = BlendState.NonPremultiplied;
				GraphicsDevice.DepthStencilState = DepthStencilState.DepthRead;
				GraphicsDevice.RasterizerState = RasterizerState.CullCounterClockwise;

				trailEffect.CurrentTechnique.Passes[0].Apply();
				// TrailData at [[vk::binding(1, 0)]] → firstSlot 0
				GraphicsDevice.SetVertexStorageBuffers(0, ringBuffer);
				GraphicsDevice.SetVertexBuffer(geometryBuffer);
				GraphicsDevice.Indices = indexBuffer;
				GraphicsDevice.DrawInstancedPrimitives(
					PrimitiveType.TriangleList,
					baseVertex: 0,
					minVertexIndex: 0,
					numVertices: vertexCount,
					startIndex: 0,
					primitiveCount: primitiveCount,
					instanceCount: ringCount
				);
			}

			frameCount++;

			if (!TestHarness.Headless)
				DrawImGui();
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
			ImGuiBindings.BeginPanel("Trail Effect Capture (Storage Buffer)");

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
		ringBuffer?.Dispose();
				ringBuffer = new StorageBuffer(GraphicsDevice,
					maxTrailLength * vertexCount * 16,
					vertexWrite: true, vertexRead: true);
				trailMaxRingSize.SetValue((float)maxTrailLength);
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
				ringBuffer?.Dispose();
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
