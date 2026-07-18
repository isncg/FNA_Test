using System;
using System.Runtime.InteropServices;

namespace FNA.Test
{
	/// <summary>
	/// Hand-written P/Invoke bindings for the dcimgui C API exported from libFNA3D.
	/// Covers lifecycle (FNA3D_ImGui_*EXT) and the ~12 widget functions used by
	/// the interactive test programs. Headless-safe: callers gate on TestHarness.Headless.
	/// </summary>
	internal static class ImGuiBindings
	{
		private const string Lib = "FNA3D";

		// ---- dcimgui / Dear ImGui constants ----
		public const int ImGuiCond_FirstUseEver = 4;    // 1 << 2
		public const int ImGuiWindowFlags_AlwaysAutoResize = 64; // 1 << 6

		// ---- ImVec2 struct matching dcimgui layout ----
		[StructLayout(LayoutKind.Sequential)]
		public struct ImVec2
		{
			public float x, y;
		}

		// ---- Lifecycle (FNA3D_ImGui.h) ----

		[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
		public static extern void FNA3D_ImGui_InitEXT(IntPtr device);

		[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
		public static extern void FNA3D_ImGui_NewFrameEXT(IntPtr device);

		[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
		public static extern void FNA3D_ImGui_ShutdownEXT(IntPtr device);

		// ---- Window management ----

		[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
		[return: MarshalAs(UnmanagedType.I1)]
		private static extern bool ImGui_Begin(string name, IntPtr p_open, int flags);

		[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
		public static extern void ImGui_End();

		[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
		public static extern void ImGui_SetNextWindowPos(ImVec2 pos, int cond);

		[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
		public static extern void ImGui_SetNextWindowSize(ImVec2 size, int cond);

		[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
		public static extern void ImGui_Separator();

		[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
		public static extern void ImGui_SameLine();

		// ---- Widgets ----

		[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
		[return: MarshalAs(UnmanagedType.I1)]
		public static extern bool ImGui_Checkbox(string label, [MarshalAs(UnmanagedType.I1)] ref bool v);

		[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
		[return: MarshalAs(UnmanagedType.I1)]
		public static extern bool ImGui_SliderFloat(string label, ref float v, float v_min, float v_max);

		[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
		[return: MarshalAs(UnmanagedType.I1)]
		private static extern bool ImGui_Combo(string label, ref int current_item, byte[] items_separated_by_zeros);

		[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
		public static extern void ImGui_Text(string fmt);

		[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
		[return: MarshalAs(UnmanagedType.I1)]
		public static extern bool ImGui_ColorEdit3(string label, float[] col, int flags);

		[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
		public static extern void ImGui_BeginDisabled([MarshalAs(UnmanagedType.I1)] bool disabled);

		[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
		public static extern void ImGui_EndDisabled();

		// ---- Convenience wrappers ----

		/// <summary>Begin a window with no close button.</summary>
		public static bool Begin(string name, int flags = 0)
		{
			return ImGui_Begin(name, IntPtr.Zero, flags);
		}

		/// <summary>
		/// Combo box from a string array.
		/// Builds the null-separated string format that ImGui_Combo expects.
		/// </summary>
		public static bool Combo(string label, ref int current, string[] items)
		{
			// Build null-separated UTF-8: "Item1\0Item2\0\0"
			int totalLen = 0;
			foreach (var s in items) totalLen += System.Text.Encoding.UTF8.GetByteCount(s) + 1;
			totalLen += 1; // final null

			byte[] buf = new byte[totalLen];
			int offset = 0;
			foreach (var s in items)
			{
				int n = System.Text.Encoding.UTF8.GetBytes(s, 0, s.Length, buf, offset);
				offset += n;
				buf[offset++] = 0; // null separator
			}
			buf[offset] = 0; // final null
			return ImGui_Combo(label, ref current, buf);
		}

		/// <summary>
		/// Convenience: position a panel top-left, auto-resize, then Begin.
		/// Call once per frame before widget calls.
		/// </summary>
		public static bool BeginPanel(string title)
		{
			ImVec2 pos = new ImVec2 { x = 10, y = 10 };
			ImVec2 size = new ImVec2 { x = 0, y = 0 };
			ImGui_SetNextWindowPos(pos, ImGuiCond_FirstUseEver);
			ImGui_SetNextWindowSize(size, ImGuiCond_FirstUseEver);
			return Begin(title, ImGuiWindowFlags_AlwaysAutoResize);
		}

		/// <summary>End the panel started with BeginPanel.</summary>
		public static void EndPanel()
		{
			ImGui_End();
		}
	}
}
