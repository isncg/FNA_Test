using System;
using System.Collections.Generic;
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
	/// GPU-instanced deformable mesh trail. The mesh vertex shader applies
	/// a procedural wave deformation and captures world-space positions to a
	/// storage buffer. The trail vertex shader reads these captured positions
	/// back to render ghost instances in a single DrawInstancedPrimitives call.
	///
	/// This demonstrates the general pattern: any vertex shader can write its
	/// output world-space positions to a storage buffer during normal rendering,
	/// and the trail replay is independent of the deformation method.
	/// </summary>
	public class TrailCaptureGame : Game
	{
		private GraphicsDeviceManager graphics;
		private Effect meshEffect;
		private Effect trailEffect;
		private IndexBuffer indexBuffer;
		private VertexBuffer geometryBuffer;      // slot 0, freq=0
		private VertexBuffer instanceColorBuffer;  // slot 1, freq=1
		private StorageBuffer ringBuffer;          // captured world-pos ring
		private Texture2D dummyTex;

		// Effect parameters (Mesh)
		private EffectParameter meshWorldViewProj;
		private EffectParameter meshTime;
		private EffectParameter meshAmplitude;
		private EffectParameter meshLightDir;

		// Effect parameters (Trail)
		private EffectParameter trailViewProj;
		private EffectParameter trailVertexCount;

		// Vertex declarations
		private VertexDeclaration geometryDecl;
		private VertexDeclaration instanceDecl;

		// Config (ImGui-tweakable)
		private int maxTrailLength = 128;
		private int meshStacks = 6;
		private int meshSlices = 12;
		private float orbitSpeed = 1.0f;
		private float waveAmplitude = 0.25f;
		private float cameraDistance = 5.0f;
		private float cameraHeight = 1.0f;

		// State
		private int vertexCount;
		private int primitiveCount;
		private int ringHead;
		private float totalTime;
		private bool meshNeedsRebuild = true;

		[StructLayout(LayoutKind.Sequential)]
		private struct ColorData
		{
			public Vector4 Color;
		}

		public TrailCaptureGame()
		{
			graphics = new GraphicsDeviceManager(this)
			{
				PreferredBackBufferWidth = 800,
				PreferredBackBufferHeight = 600,
				SynchronizeWithVerticalRetrace = false
			};
			Window.Title = "Trail Effect Deform — GPU Instancing + Storage Buffer | ESC=quit";
			IsMouseVisible = true;
		}

		protected override void LoadContent()
		{
			// Load embedded FEBs
			meshEffect = LoadEmbeddedEffect("TrailEffectCaptureDemo.Mesh.feb");
			trailEffect = LoadEmbeddedEffect("TrailEffectCaptureDemo.Trail.feb");

			meshWorldViewProj = meshEffect.Parameters["WorldViewProj"];
			meshTime          = meshEffect.Parameters["Time"];
			meshAmplitude     = meshEffect.Parameters["Amplitude"];
			meshLightDir      = meshEffect.Parameters["LightDir"];

			trailViewProj    = trailEffect.Parameters["ViewProj"];
			trailVertexCount = trailEffect.Parameters["VertexCount"];

			// Vertex declarations
			geometryDecl = new VertexDeclaration(
				new VertexElement(0,  VertexElementFormat.Vector3, VertexElementUsage.Position, 0),
				new VertexElement(12, VertexElementFormat.Vector3, VertexElementUsage.Normal, 0),
				new VertexElement(24, VertexElementFormat.Vector2, VertexElementUsage.TextureCoordinate, 0)
			);
			instanceDecl = new VertexDeclaration(
				new VertexElement(0, VertexElementFormat.Vector4,
					VertexElementUsage.TextureCoordinate, 0)
			);

			// Build mesh
			RebuildMeshGeometry();

			// Dummy texture
			dummyTex = TextureGen.White(GraphicsDevice);

			// Ring buffer (allocate at max trail length)
			ringBuffer = new StorageBuffer(GraphicsDevice,
				maxTrailLength * vertexCount * 12, // float3 per vertex
				vertexWrite: true,
				vertexRead: true
			);

			ImGuiTestHarness.Init(GraphicsDevice);
		}

		private Effect LoadEmbeddedEffect(string resourceName)
		{
			using var stream = Assembly.GetExecutingAssembly()
				.GetManifestResourceStream(resourceName);
			using var ms = new MemoryStream();
			stream.CopyTo(ms);
			return new Effect(GraphicsDevice, ms.ToArray());
		}

		private void RebuildMeshGeometry()
		{
			geometryBuffer?.Dispose();
			indexBuffer?.Dispose();

			var sphere = GeometryGen.Sphere(meshStacks, meshSlices);
			vertexCount = sphere.Length;
			primitiveCount = vertexCount / 3;

			geometryBuffer = new VertexBuffer(GraphicsDevice, geometryDecl,
				vertexCount, BufferUsage.WriteOnly);
			geometryBuffer.SetData(sphere);

			// Sequential index buffer for instanced trail rendering
			var indices = new uint[vertexCount];
			for (uint i = 0; i < vertexCount; i++)
				indices[i] = i;

			indexBuffer = new IndexBuffer(GraphicsDevice,
				IndexElementSize.ThirtyTwoBits,
				vertexCount, BufferUsage.WriteOnly);
			indexBuffer.SetData(indices);

			trailVertexCount.SetValue(vertexCount);

			// Rebuild ring buffer with new vertex count
			ringBuffer?.Dispose();
			ringBuffer = new StorageBuffer(GraphicsDevice,
				maxTrailLength * vertexCount * 12,
				vertexWrite: true, vertexRead: true);

			ringHead = 0;
			meshNeedsRebuild = false;
		}

		private void RebuildInstanceColorBuffer(int count)
		{
			instanceColorBuffer?.Dispose();

			var colors = new ColorData[count];
			for (int i = 0; i < count; i++)
			{
				float t = (float)i / Math.Max(count - 1, 1);
				colors[i] = new ColorData
				{
					Color = new Vector4(1.0f - t, 0.0f, t, 1.0f - t)
				};
			}

			instanceColorBuffer = new VertexBuffer(GraphicsDevice,
				instanceDecl, count, BufferUsage.WriteOnly);
			instanceColorBuffer.SetData(colors);
		}

		protected override void Update(GameTime gameTime)
		{
			var kb = Keyboard.GetState();
			if (kb.IsKeyDown(Keys.Escape)) Exit();

			float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
			totalTime += dt;

			if (meshNeedsRebuild)
				RebuildMeshGeometry();

			// Headless test
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

			int trailLen = Math.Min(maxTrailLength, ringHead);
			if (trailLen == 0) trailLen = 1; // first frame: head only

			// Camera
			var camPos = new Vector3(0, cameraHeight, cameraDistance);
			var view = Matrix.CreateLookAt(camPos, Vector3.Zero, Vector3.Up);
			var proj = Matrix.CreatePerspectiveFieldOfView(
				MathHelper.PiOver4,
				GraphicsDevice.Viewport.AspectRatio,
				0.1f, 100f
			);

			// Orbit: the mesh slowly spins in place
			float orbitAngle = totalTime * orbitSpeed;
			var world = Matrix.CreateRotationY(orbitAngle);

			// ── Pass 1: Render deformed mesh + capture ────────────────
			meshAmplitude.SetValue(waveAmplitude);
			meshTime.SetValue(totalTime);
			meshLightDir.SetValue(new Vector3(0.577f, -0.577f, -0.577f));
			meshWorldViewProj.SetValue(world * view * proj);

			GraphicsDevice.BlendState = BlendState.Opaque;
			GraphicsDevice.DepthStencilState = DepthStencilState.Default;
			GraphicsDevice.RasterizerState = RasterizerState.CullCounterClockwise;

			// Bind ring buffer as writable for capture
			GraphicsDevice.SetVertexStorageBuffersWritable(0, ringBuffer);

			meshEffect.CurrentTechnique.Passes[0].Apply();
			GraphicsDevice.SetVertexBuffer(geometryBuffer);
			GraphicsDevice.DrawPrimitives(PrimitiveType.TriangleList, 0, primitiveCount);

			ringHead = (ringHead + 1) % maxTrailLength;

			// ── Pass 2: Render trail instanced ────────────────────────
			trailViewProj.SetValue(view * proj);

			// Rebuild instance colors for current trail length
			RebuildInstanceColorBuffer(trailLen);

			GraphicsDevice.BlendState = BlendState.NonPremultiplied;
			GraphicsDevice.DepthStencilState = DepthStencilState.Default;
			GraphicsDevice.RasterizerState = RasterizerState.CullCounterClockwise;
			GraphicsDevice.Textures[0] = dummyTex;

			// Bind ring buffer as readable
			GraphicsDevice.SetVertexStorageBuffers(0, ringBuffer);

			trailEffect.CurrentTechnique.Passes[0].Apply();

			GraphicsDevice.SetVertexBuffers(
				new VertexBufferBinding(geometryBuffer, 0, 0),     // slot 0: ignored
				new VertexBufferBinding(instanceColorBuffer, 0, 1)  // slot 1: color
			);
			GraphicsDevice.Indices = indexBuffer;

			GraphicsDevice.DrawInstancedPrimitives(
				PrimitiveType.TriangleList,
				baseVertex: 0,
				minVertexIndex: 0,
				numVertices: vertexCount,
				startIndex: 0,
				primitiveCount: primitiveCount,
				instanceCount: trailLen
			);

			// ImGui
			if (!TestHarness.Headless)
				DrawImGui();
		}

		private void DrawImGui()
		{
			ImGuiBindings.BeginPanel("Trail Effect Deform");

			bool rebuild = false;

			int[] trailLengths = { 32, 64, 128, 256, 512 };
			string[] names = { "32", "64", "128", "256", "512" };
			int ti = Array.IndexOf(trailLengths, maxTrailLength);
			if (ti < 0) ti = 2;
			if (ImGuiBindings.Combo("Trail Length", ref ti, names))
			{
				maxTrailLength = trailLengths[ti];
				ringHead = 0;
				rebuild = true;
			}

			rebuild |= ImGuiBindings.ImGui_SliderFloat("Wave Amp", ref waveAmplitude, 0.0f, 0.5f);
			rebuild |= ImGuiBindings.ImGui_SliderFloat("Orbit Speed", ref orbitSpeed, 0.0f, 3.0f);

			int[] stacks = { 4, 6, 8, 12, 16 };
			string[] snames = { "4", "6", "8", "12", "16" };
			int si = Array.IndexOf(stacks, meshStacks);
			if (si < 0) si = 1;
			if (ImGuiBindings.Combo("Stacks", ref si, snames))
			{
				meshStacks = stacks[si];
				meshNeedsRebuild = true;
			}

			int[] slices = { 6, 8, 12, 16, 24, 32 };
			string[] slnames = { "6", "8", "12", "16", "24", "32" };
			int sli = Array.IndexOf(slices, meshSlices);
			if (sli < 0) sli = 1;
			if (ImGuiBindings.Combo("Slices", ref sli, slnames))
			{
				meshSlices = slices[sli];
				meshNeedsRebuild = true;
			}

			ImGuiBindings.ImGui_SliderFloat("Cam Distance", ref cameraDistance, 2.0f, 10.0f);
			ImGuiBindings.ImGui_SliderFloat("Cam Height", ref cameraHeight, 0.0f, 4.0f);

			if (rebuild)
			{
				ringBuffer?.Dispose();
				ringBuffer = new StorageBuffer(GraphicsDevice,
					maxTrailLength * vertexCount * 12,
					vertexWrite: true, vertexRead: true);
				ringHead = 0;
			}

			ImGuiBindings.ImGui_Text($"Vertices: {vertexCount}  Instances: {Math.Min(maxTrailLength, ringHead)}");
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
				instanceColorBuffer?.Dispose();
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
	}
}
