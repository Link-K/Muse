using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Runtime.CompilerServices;

namespace Muse.Services
{
	/// <summary>
	/// Windows-only low-level keyboard hook to detect Ctrl+V when our app is foreground.
	/// This avoids relying on Avalonia popup focus routing.
	/// </summary>
	internal static class KeyboardHook
	{
		private const int WH_KEYBOARD_LL = 13;
		private const int WM_KEYDOWN = 0x0100;
		private const int WM_SYSKEYDOWN = 0x0104;
		private static LowLevelKeyboardProc? _proc;
		private static IntPtr _hookId = IntPtr.Zero;

		public static event Action? CtrlVPressed;

		private static void AppendDebug(string msg)
		{
			try
			{
				var baseDir = AppContext.BaseDirectory ?? AppDomain.CurrentDomain.BaseDirectory ?? System.IO.Directory.GetCurrentDirectory();
				var dir = System.IO.Path.Combine(baseDir, ".muse", "debug");
				System.IO.Directory.CreateDirectory(dir);
				var path = System.IO.Path.Combine(dir, "clipboard.log");
				System.IO.File.AppendAllText(path, DateTime.UtcNow.ToString("o") + " [KeyboardHook] " + msg + Environment.NewLine);
			}
			catch { }
		}

		public static void Start()
		{
			if (!RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows)) return;
			if (_hookId != IntPtr.Zero) return;
			_proc = HookCallback;
			using var curProcess = Process.GetCurrentProcess();
			var module = GetModuleHandle(null);
			_hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, module, 0);
			try { Console.WriteLine("[DEBUG] KeyboardHook started"); } catch { }
			try { AppendDebug("started"); } catch { }
		}

		public static void Stop()
		{
			if (_hookId == IntPtr.Zero) return;
			UnhookWindowsHookEx(_hookId);
			_hookId = IntPtr.Zero;
			_proc = null;
			try { Console.WriteLine("[DEBUG] KeyboardHook stopped"); } catch { }
			try { AppendDebug("stopped"); } catch { }
		}

		private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

		private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
		{
			try
			{
				if (nCode >= 0 && (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN))
				{
					int vk = Marshal.ReadInt32(lParam);
					const int VK_V = 0x56;
					const int VK_CONTROL = 0x11;
					if (vk == VK_V)
					{
						// Check ctrl state
						short ctrlState = GetAsyncKeyState(VK_CONTROL);
						bool ctrlDown = (ctrlState & 0x8000) != 0;
						if (ctrlDown)
						{
							// Verify foreground window belongs to this process
							IntPtr fg = GetForegroundWindow();
							if (fg != IntPtr.Zero)
							{
								GetWindowThreadProcessId(fg, out uint pid);
								if (pid == (uint)Environment.ProcessId)
								{
									try { AppendDebug("Ctrl+V detected, invoking handler"); } catch { }
									try { Console.WriteLine("[DEBUG] KeyboardHook detected Ctrl+V"); } catch { }
									try { CtrlVPressed?.Invoke(); } catch { }
								}
							}
						}
					}
				}
			}
			catch { }
			return CallNextHookEx(_hookId, nCode, wParam, lParam);
		}

		[DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
		private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

		[DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
		[return: MarshalAs(UnmanagedType.Bool)]
		private static extern bool UnhookWindowsHookEx(IntPtr hhk);

		[DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
		private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

		[DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
		private static extern IntPtr GetModuleHandle(string? lpModuleName);

		[DllImport("user32.dll")]
		private static extern short GetAsyncKeyState(int vKey);

		[DllImport("user32.dll")]
		private static extern IntPtr GetForegroundWindow();

		[DllImport("user32.dll")]
		private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
	}
}
