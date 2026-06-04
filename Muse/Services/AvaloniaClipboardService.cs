using System;
using System.Linq;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Threading;

namespace Muse.Services
{
	/// <summary>
	/// 尝试使用 Avalonia 可用的多种路径写入剪贴板，兼容不同版本。
	/// </summary>
	public class AvaloniaClipboardService : IClipboardService
	{
		private static void AppendDebugLog(string msg)
		{
			try
			{
				var baseDir = AppContext.BaseDirectory ?? AppDomain.CurrentDomain.BaseDirectory ?? Directory.GetCurrentDirectory();
				var dir = Path.Combine(baseDir, ".muse", "debug");
				Directory.CreateDirectory(dir);
				var path = Path.Combine(dir, "clipboard.log");
				File.AppendAllText(path, DateTime.UtcNow.ToString("o") + " " + msg + Environment.NewLine);
			}
			catch { }
		}

		private static byte[]? RunGetImageFromWin32ClipboardOnSta()
		{
			byte[]? result = null;
			var th = new Thread(() =>
			{
				try
				{
					result = GetImageFromWin32Clipboard();
				}
				catch (Exception ex)
				{
					try { Console.WriteLine($"[DEBUG] STA GetImageFromWin32Clipboard error: {ex.Message}"); } catch { }
				}
			});
			th.SetApartmentState(ApartmentState.STA);
			th.IsBackground = true;
			th.Start();
			if (!th.Join(5000)) { try { Console.WriteLine("[DEBUG] STA GetImageFromWin32Clipboard timed out"); } catch { } }
			return result;
		}

		private static void LogDebug(string msg)
		{
			try { Console.WriteLine(msg); } catch { }
			AppendDebugLog(msg);
		}
		public async Task<bool> SetTextAsync(string text)
		{
			if (string.IsNullOrWhiteSpace(text))
				return false;

			// Try Application.Current.Clipboard via reflection
			try
			{
				var appType = Type.GetType("Avalonia.Application, Avalonia");
				if (appType is not null)
				{
					var currentProp = appType.GetProperty("Current", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
					var current = currentProp?.GetValue(null);
					var clipProp = current?.GetType().GetProperty("Clipboard");
					var clipboard = clipProp?.GetValue(current);
					if (clipboard is not null)
					{
						var setText = clipboard.GetType().GetMethod("SetTextAsync", new[] { typeof(string) });
						if (setText is not null)
						{
							var task = (Task)setText.Invoke(clipboard, new object[] { text })!;
							await task.ConfigureAwait(false);
							return true;
						}
					}
				}
			}
			catch { }

			// Try AvaloniaLocator / GetService(IClipboard)
			try
			{
				var locatorType = Type.GetType("Avalonia.AvaloniaLocator, Avalonia") ?? Type.GetType("AvaloniaLocator, Avalonia");
				if (locatorType is not null)
				{
					var currentProp = locatorType.GetProperty("Current", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
					var current = currentProp?.GetValue(null);
					var getService = locatorType.GetMethod("GetService", new[] { typeof(Type) });
					if (getService is not null && current is not null)
					{
						var clipboardType = Type.GetType("Avalonia.Input.IClipboard, Avalonia.Input") ?? Type.GetType("Avalonia.Input.IClipboard, Avalonia");
						if (clipboardType is not null)
						{
							var clipboard = getService.Invoke(current, new object[] { clipboardType });
							if (clipboard is not null)
							{
								var setText = clipboard.GetType().GetMethod("SetTextAsync", new[] { typeof(string) });
								if (setText is not null)
								{
									var task = (Task)setText.Invoke(clipboard, new object[] { text })!;
									await task.ConfigureAwait(false);
									return true;
								}
							}
						}
					}
				}
			}
			catch { }

			return false;
		}
		public async Task<byte[]?> GetImageAsync()
		{
			try
			{
				// 1) Try Avalonia clipboard via reflection
				var appType = Type.GetType("Avalonia.Application, Avalonia");
				if (appType is not null)
				{
					var currentProp = appType.GetProperty("Current", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
					var current = currentProp?.GetValue(null);
					var clipProp = current?.GetType().GetProperty("Clipboard");
					var clipboard = clipProp?.GetValue(current);
					if (clipboard is not null)
					{
						try { Console.WriteLine($"[DEBUG] Avalonia clipboard runtime type: {clipboard.GetType().FullName}"); } catch { }
						var getImage = clipboard.GetType().GetMethod("GetImageAsync", Type.EmptyTypes) ?? clipboard.GetType().GetMethod("GetDataAsync", new[] { typeof(string) });
						if (getImage is not null)
						{
							try
							{
								var task = getImage.Invoke(clipboard, getImage.GetParameters().Length == 0 ? null : new object[] { "image/png" });
								if (task is Task t)
								{
									await t.ConfigureAwait(false);
									var res = t.GetType().GetProperty("Result")?.GetValue(t);
									if (res is byte[] b) return b;
									if (res is Stream s)
									{
										using var ms = new MemoryStream();
										s.CopyTo(ms);
										return ms.ToArray();
									}
								}
							}
							catch { }
						}
					}
				}
			}
			catch { }

			// 2) STA Win32/COM clipboard reader (supports Explorer FileContents/ISTREAM)
			if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
			{
				var staResult = RunGetImageFromWin32ClipboardOnSta();
				if (staResult is not null && staResult.Length > 0) return staResult;
			}

			return null;
		}

		private static bool IsImageBytes(byte[] data)
		{
			if (data == null || data.Length < 4) return false;
			// PNG
			if (data.Length >= 8 && data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47) return true;
			// JPG
			if (data.Length >= 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF) return true;
			// BMP
			if (data.Length >= 2 && data[0] == 0x42 && data[1] == 0x4D) return true;
			return false;
		}

		// Win32 clipboard helpers

		private static byte[]? GetDibFromClipboard()
		{
			// Enhanced: try PNG registered format, CF_DIBV5, CF_DIB. Enumerate formats for diagnostics.
			const uint CF_DIB = 8;
			const uint CF_DIBV5 = 17;
			const uint CF_BITMAP = 2;
			try
			{
				LogDebug("[DEBUG] GetDibFromClipboard: opening clipboard");
				if (!OpenClipboard(IntPtr.Zero)) { LogDebug($"[DEBUG] OpenClipboard failed: {Marshal.GetLastWin32Error()}"); return null; }
			}
			catch (Exception exAvail) { LogDebug($"[DEBUG] GetDibFromClipboard open error: {exAvail}"); return null; }
			try
			{
				// enumerate available formats
				LogDebug("[DEBUG] Enumerating clipboard formats:");
				uint fmt = 0;
				while ((fmt = EnumClipboardFormats(fmt)) != 0)
				{
					var nameBuf = new System.Text.StringBuilder(128);
					int res = GetClipboardFormatName(fmt, nameBuf, nameBuf.Capacity);
					if (res > 0)
						LogDebug($"[DEBUG]  format id={fmt} name={nameBuf}");
					else
						LogDebug($"[DEBUG]  format id={fmt} (no name)");
				}
				// try PNG registered format first
				uint pngFmt = RegisterClipboardFormat("PNG");
				LogDebug($"[DEBUG] RegisterClipboardFormat(\"PNG\") = {pngFmt}");
				uint[] candidates = new uint[] { pngFmt, CF_DIBV5, CF_DIB, CF_BITMAP };
				foreach (var cand in candidates)
				{
					if (cand == 0) continue;
					bool available = IsClipboardFormatAvailable(cand);
					LogDebug($"[DEBUG] IsClipboardFormatAvailable({cand}) = {available}");
					if (!available) continue;
					LogDebug($"[DEBUG] Attempting to read clipboard format {cand}");
					IntPtr h = GetClipboardData(cand);
					LogDebug($"[DEBUG] GetClipboardData returned: {h}");
					if (h == IntPtr.Zero) continue;
					IntPtr ptr = GlobalLock(h);
					LogDebug($"[DEBUG] GlobalLock returned: {ptr}");
					if (ptr == IntPtr.Zero) { Console.WriteLine($"[DEBUG] GlobalLock failed: {Marshal.GetLastWin32Error()}"); continue; }
					try
					{
						var sizeU = GlobalSize(h);
						int size = sizeU == UIntPtr.Zero ? 0 : (int)sizeU;
						LogDebug($"[DEBUG] GlobalSize for format {cand}: {size}");
						if (size <= 0) { Console.WriteLine($"[DEBUG] size<=0 for format {cand}"); continue; }
						var buf = new byte[size];
						Marshal.Copy(ptr, buf, 0, size);
						LogDebug($"[DEBUG] Copied {buf.Length} bytes for format {cand}");
						return buf;
					}
					finally { GlobalUnlock(h); }
				}
				LogDebug("[DEBUG] No supported clipboard image formats yielded data");
				return null;
			}
			catch (Exception ex)
			{
				LogDebug($"[DEBUG] GetDibFromClipboard exception: {ex}");
				return null;
			}
			finally { try { CloseClipboard(); } catch { } }
		}

		/// <summary>
		/// Public helper: directly attempt to read image from Win32 clipboard (DIB/PNG)
		/// Returns PNG bytes when conversion possible, otherwise BMP-wrapped bytes, or null.
		/// </summary>
		public static byte[]? GetImageFromWin32Clipboard()
		{
			// First try Shell DataObject (FileGroupDescriptorW / FileContents) — Explorer copy/paste
			try
			{
				var shell = GetImageFromShellClipboard();
				if (shell is not null && shell.Length > 0)
				{
					LogDebug($"[DEBUG] GetImageFromWin32Clipboard: Read {shell.Length} bytes from Shell FileContents");
					return shell;
				}
			}
			catch (Exception exShell) { LogDebug($"[DEBUG] GetImageFromWin32Clipboard shell path failed: {exShell}"); }
			try
			{
				var dib = GetDibFromClipboard();
				if (dib is null || dib.Length == 0) return null;
				// Convert DIB to BMP by prefixing BITMAPFILEHEADER
				int infoHeaderSize = BitConverter.ToInt32(dib, 0);
				int bfType = 0x4D42; // 'BM'
				int bfOffBits = 14 + infoHeaderSize;
				int bfSize = bfOffBits + (dib.Length - infoHeaderSize);
				using var ms = new MemoryStream();
				using (var bw = new BinaryWriter(ms))
				{
					bw.Write((ushort)bfType);
					bw.Write(bfSize);
					bw.Write((ushort)0);
					bw.Write((ushort)0);
					bw.Write(bfOffBits);
					bw.Write(dib);
				}
				var bmpBytes = ms.ToArray();
				// Try to convert BMP->PNG via System.Drawing if available
				try
				{
					System.Reflection.Assembly? drawAsm = null;
					try { drawAsm = System.Reflection.Assembly.Load("System.Drawing.Common"); } catch { }
					if (drawAsm is null) { try { drawAsm = System.Reflection.Assembly.Load("System.Drawing"); } catch { } }
					if (drawAsm is not null)
					{
						var imgType = drawAsm.GetType("System.Drawing.Image");
						var fromStream = imgType?.GetMethod("FromStream", new[] { typeof(Stream) });
						if (fromStream is not null)
						{
							using var bmpStream = new MemoryStream(bmpBytes);
							var imgObj = fromStream.Invoke(null, new object[] { bmpStream });
							if (imgObj is not null)
							{
								var imgFmtType = drawAsm.GetType("System.Drawing.Imaging.ImageFormat");
								var pngProp = imgFmtType?.GetProperty("Png", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
								var pngFmt = pngProp?.GetValue(null);
								using var outMs = new MemoryStream();
								var saveMethod = imgObj.GetType().GetMethod("Save", new[] { typeof(Stream), pngFmt?.GetType() ?? typeof(object) });
								if (saveMethod is null) saveMethod = imgObj.GetType().GetMethod("Save", new[] { typeof(Stream), typeof(object) });
								if (saveMethod is not null)
								{
									saveMethod.Invoke(imgObj, new object?[] { outMs, pngFmt });
									var pngBytes = outMs.ToArray();
									if (pngBytes.Length > 0)
									{
										LogDebug($"[DEBUG] GetImageFromWin32Clipboard: Converted BMP->PNG length: {pngBytes.Length}");
										return pngBytes;
									}
								}
							}
						}
					}
				}
				catch (Exception exConv) { LogDebug($"[DEBUG] GetImageFromWin32Clipboard conversion failed: {exConv.Message}"); }
				// fallback: return BMP bytes
				LogDebug($"[DEBUG] GetImageFromWin32Clipboard: returning BMP bytes length {bmpBytes.Length}");
				return bmpBytes;
			}
			catch (Exception ex) { LogDebug($"[DEBUG] GetImageFromWin32Clipboard error: {ex}"); return null; }
		}

		[DllImport("user32.dll", SetLastError = true)]
		private static extern bool OpenClipboard(IntPtr hwnd);

		[DllImport("user32.dll", SetLastError = true)]
		private static extern bool CloseClipboard();

		[DllImport("user32.dll", SetLastError = true)]
		private static extern bool IsClipboardFormatAvailable(uint format);

		[DllImport("user32.dll", SetLastError = true)]
		private static extern IntPtr GetClipboardData(uint uFormat);

		[DllImport("user32.dll", SetLastError = true)]
		private static extern uint EnumClipboardFormats(uint format);

		[DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
		private static extern int GetClipboardFormatName(uint format, System.Text.StringBuilder lpszFormatName, int cchMaxCount);

		[DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
		private static extern uint RegisterClipboardFormat(string lpszFormat);

		[DllImport("kernel32.dll", SetLastError = true)]
		private static extern IntPtr GlobalLock(IntPtr hMem);

		[DllImport("kernel32.dll", SetLastError = true)]
		private static extern bool GlobalUnlock(IntPtr hMem);

		[DllImport("kernel32.dll", SetLastError = true)]
		private static extern UIntPtr GlobalSize(IntPtr hMem);

		[DllImport("ole32.dll")]
		private static extern int OleGetClipboard(out IntPtr dataObject);

		/// <summary>
		/// Attempt to read FileGroupDescriptorW / FileContents from clipboard (Explorer copy).
		/// Returns the first file's raw bytes if available.
		/// This is a heuristic using Win32 HGLOBAL reads and may not work for every source.
		/// </summary>
		[DllImport("ole32.dll")]
		private static extern void ReleaseStgMedium(ref System.Runtime.InteropServices.ComTypes.STGMEDIUM pmedium);

		private static byte[]? GetImageFromShellClipboard()
		{
			try
			{
				uint fg = RegisterClipboardFormat("FileGroupDescriptorW");
				uint fc = RegisterClipboardFormat("FileContents");
				LogDebug($"[DEBUG] Shell formats Register: FileGroupDescriptorW={fg}, FileContents={fc}");
				if (fg == 0 || fc == 0) return null;

				// Try robust COM IDataObject path via OleGetClipboard
				IntPtr pDataObj = IntPtr.Zero;
				int hr = OleGetClipboard(out pDataObj);
				if (hr == 0 && pDataObj != IntPtr.Zero)
				{
					try
					{
						var dataObj = (System.Runtime.InteropServices.ComTypes.IDataObject)Marshal.GetObjectForIUnknown(pDataObj);

						// First, try to get FileGroupDescriptorW (to read filename) as HGLOBAL
						try
						{
							var feFg = new System.Runtime.InteropServices.ComTypes.FORMATETC();
							feFg.cfFormat = unchecked((short)fg);
							feFg.ptd = IntPtr.Zero;
							feFg.dwAspect = (System.Runtime.InteropServices.ComTypes.DVASPECT)1; // DVASPECT_CONTENT
							feFg.lindex = -1;
							feFg.tymed = (System.Runtime.InteropServices.ComTypes.TYMED)1; // TYMED_HGLOBAL
							if (dataObj != null)
							{
								System.Runtime.InteropServices.ComTypes.STGMEDIUM mediumFg;
								try
								{
									dataObj.GetData(ref feFg, out mediumFg);
									try
									{
										if (mediumFg.unionmember != IntPtr.Zero)
										{
											IntPtr p = GlobalLock(mediumFg.unionmember);
											if (p != IntPtr.Zero)
											{
												try
												{
													var size = GlobalSize(mediumFg.unionmember);
													int len = size == UIntPtr.Zero ? 0 : (int)size;
													LogDebug($"[DEBUG] FileGroupDescriptorW HGLOBAL size = {len}");
													if (len > 0)
													{
														var buf = new byte[len];
														Marshal.Copy(p, buf, 0, len);
														// parse first unicode filename (best-effort)
														string? fileName = null;
														for (int i = 0; i + 4 < buf.Length; i += 2)
														{
															if (buf[i] != 0 || buf[i + 1] != 0)
															{
																int j = i;
																while (j + 1 < buf.Length && !(buf[j] == 0 && buf[j + 1] == 0)) j += 2;
																int charCount = (j - i) / 2;
																if (charCount > 0 && charCount < 1024)
																{
																	fileName = System.Text.Encoding.Unicode.GetString(buf, i, charCount * 2).Trim('\0');
																	break;
																}
															}
														}
														LogDebug($"[DEBUG] Parsed filename from FileGroupDescriptorW: {fileName ?? "<none>"}");
													}
												}
												finally { GlobalUnlock(mediumFg.unionmember); }
											}
										}
									}
									finally { ReleaseStgMedium(ref mediumFg); }
								}
								catch { }
							}
						}
						catch (Exception exfg) { LogDebug($"[DEBUG] FileGroupDescriptorW via IDataObject failed: {exfg}"); }

						// Try to read FileContents. Prefer HGLOBAL, then ISTREAM.
						var fe = new System.Runtime.InteropServices.ComTypes.FORMATETC();
						fe.cfFormat = unchecked((short)fc);
						fe.ptd = IntPtr.Zero;
						fe.dwAspect = (System.Runtime.InteropServices.ComTypes.DVASPECT)1; // DVASPECT_CONTENT
						fe.lindex = 0;
						// Try HGLOBAL
						fe.tymed = (System.Runtime.InteropServices.ComTypes.TYMED)1; // TYMED_HGLOBAL
						try
						{
							System.Runtime.InteropServices.ComTypes.STGMEDIUM medium;
							dataObj.GetData(ref fe, out medium);
							try
							{
								if (medium.unionmember != IntPtr.Zero)
								{
									IntPtr p = GlobalLock(medium.unionmember);
									if (p != IntPtr.Zero)
									{
										try
										{
											var size = GlobalSize(medium.unionmember);
											int len = size == UIntPtr.Zero ? 0 : (int)size;
											LogDebug($"[DEBUG] FileContents HGLOBAL size = {len}");
											if (len > 0)
											{
												var data = new byte[len];
												Marshal.Copy(p, data, 0, len);
												// heuristic: image magic
												if (IsImageBytes(data)) return data;
												return data;
											}
										}
										finally { GlobalUnlock(medium.unionmember); }
									}
								}
							}
							finally { ReleaseStgMedium(ref medium); }
						}
						catch (Exception exH) { LogDebug($"[DEBUG] FileContents HGLOBAL path failed: {exH.Message}"); }

						// Try ISTREAM
						try
						{
							System.Runtime.InteropServices.ComTypes.FORMATETC feStream = new System.Runtime.InteropServices.ComTypes.FORMATETC();
							feStream.cfFormat = unchecked((short)fc);
							feStream.ptd = IntPtr.Zero;
							feStream.dwAspect = (System.Runtime.InteropServices.ComTypes.DVASPECT)1;
							feStream.lindex = 0;
							feStream.tymed = (System.Runtime.InteropServices.ComTypes.TYMED)4; // TYMED_ISTREAM
							System.Runtime.InteropServices.ComTypes.STGMEDIUM medStream;
							dataObj.GetData(ref feStream, out medStream);
							try
							{
								if (medStream.unionmember != IntPtr.Zero)
								{
									var comStream = (System.Runtime.InteropServices.ComTypes.IStream)Marshal.GetObjectForIUnknown(medStream.unionmember);
									if (comStream != null)
									{
										// Read stream in chunks
										var ms = new MemoryStream();
										const int chunk = 81920;
										var readBuf = new byte[chunk];
										IntPtr pcbRead = Marshal.AllocHGlobal(4);
										try
										{
											while (true)
											{
												// comStream.Read fills readBuf and writes bytes read to pcbRead
												comStream.Read(readBuf, readBuf.Length, pcbRead);
												int read = Marshal.ReadInt32(pcbRead);
												if (read <= 0) break;
												ms.Write(readBuf, 0, read);
											}
											var data = ms.ToArray();
											LogDebug($"[DEBUG] FileContents ISTREAM read length = {data.Length}");
											if (IsImageBytes(data)) return data;
											return data;
										}
										finally { Marshal.FreeHGlobal(pcbRead); }
									}
								}
							}
							finally { ReleaseStgMedium(ref medStream); }
						}
						catch (Exception exS) { LogDebug($"[DEBUG] FileContents ISTREAM path failed: {exS.Message}"); }

						try { Marshal.Release(pDataObj); } catch { }
					}
					catch (Exception exDo) { LogDebug($"[DEBUG] IDataObject processing failed: {exDo}"); }
				}

				// Fallback: attempt legacy HGLOBAL reads (previous heuristic)
				try
				{
					IntPtr hd = GetClipboardData(fg);
					if (hd != IntPtr.Zero)
					{
						IntPtr p = GlobalLock(hd);
						if (p != IntPtr.Zero)
						{
							try
							{
								var size = GlobalSize(hd);
								int len = size == UIntPtr.Zero ? 0 : (int)size;
								LogDebug($"[DEBUG] FileGroupDescriptorW HGLOBAL size = {len}");
							}
							finally { GlobalUnlock(hd); }
						}
					}
				}
				catch { }

				return null;
			}
			catch (Exception ex)
			{
				LogDebug($"[DEBUG] GetImageFromShellClipboard exception: {ex}");
				return null;
			}
		}

		public async Task<string?> GetTextAsync()
		{
			// Try Avalonia first
			try
			{
				var appType = Type.GetType("Avalonia.Application, Avalonia");
				if (appType is not null)
				{
					var currentProp = appType.GetProperty("Current", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
					var current = currentProp?.GetValue(null);
					var clipProp = current?.GetType().GetProperty("Clipboard");
					var clipboard = clipProp?.GetValue(current);
					if (clipboard is not null)
					{
						var getText = clipboard.GetType().GetMethod("GetTextAsync", Type.EmptyTypes) ?? clipboard.GetType().GetMethod("GetText", Type.EmptyTypes);
						if (getText is not null)
						{
							try
							{
								var task = getText.Invoke(clipboard, null);
								if (task is Task t)
								{
									await t.ConfigureAwait(false);
									var res = t.GetType().GetProperty("Result")?.GetValue(t) as string;
									return res;
								}
							}
							catch { }
						}
					}
				}
			}
			catch { }

			// Windows native STA clipboard read (avoid System.Windows.Forms)
			if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
			{
				try
				{
					var thRes = RunGetTextFromWin32ClipboardOnSta();
					return thRes;
				}
				catch { }
			}

			return null;
		}

		private static string? GetTextFromWin32Clipboard()
		{
			try
			{
				const uint CF_UNICODETEXT = 13;
				if (!OpenClipboard(IntPtr.Zero)) return null;
				try
				{
					if (!IsClipboardFormatAvailable(CF_UNICODETEXT)) return null;
					IntPtr h = GetClipboardData(CF_UNICODETEXT);
					if (h == IntPtr.Zero) return null;
					IntPtr ptr = GlobalLock(h);
					if (ptr == IntPtr.Zero) return null;
					try
					{
						var text = Marshal.PtrToStringUni(ptr);
						return text;
					}
					finally { GlobalUnlock(h); }
				}
				finally { try { CloseClipboard(); } catch { } }
			}
			catch { return null; }
		}

		private static string? RunGetTextFromWin32ClipboardOnSta()
		{
			string? result = null;
			var th = new Thread(() =>
			{
				try { result = GetTextFromWin32Clipboard(); } catch { }
			});
			th.SetApartmentState(ApartmentState.STA);
			th.IsBackground = true;
			th.Start();
			if (!th.Join(1500)) { }
			return result;
		}
	}
}
