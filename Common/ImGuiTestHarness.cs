using Microsoft.Xna.Framework.Graphics;

namespace FNA.Test
{
	/// <summary>
	/// Thin lifecycle wrapper that ties ImGUI init/new-frame to the FNA Game
	/// and the existing TestHarness.Headless flag.
	/// </summary>
	public static class ImGuiTestHarness
	{
		private static bool _initialized;

		/// <summary>
		/// Initialize the ImGUI backend. Call once from Game.LoadContent()
		/// (or after GraphicsDevice is created). No-op in headless mode.
		/// </summary>
		public static void Init(GraphicsDevice device)
		{
			if (_initialized) return;
			if (TestHarness.Headless) return;

			ImGuiBindings.FNA3D_ImGui_InitEXT(device.NativeDevice);
			_initialized = true;
		}

		/// <summary>
		/// Start a new ImGUI frame. Call once per frame from Game.Draw()
		/// before building any ImGUI widgets. No-op in headless mode.
		/// </summary>
		public static void NewFrame(GraphicsDevice device)
		{
			if (!_initialized) return;
			ImGuiBindings.FNA3D_ImGui_NewFrameEXT(device.NativeDevice);
		}

		/// <summary>
		/// Shutdown ImGUI. Call from Game.Dispose() or similar.
		/// No-op in headless mode.
		/// </summary>
		public static void Shutdown(GraphicsDevice device)
		{
			if (!_initialized) return;
			ImGuiBindings.FNA3D_ImGui_ShutdownEXT(device.NativeDevice);
			_initialized = false;
		}
	}
}
