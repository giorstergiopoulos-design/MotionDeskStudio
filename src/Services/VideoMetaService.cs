using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace MotionDesk.Services
{
    // Thumbnail + duration for the wallpaper video list.
    //  - Thumbnail: the Windows Shell thumbnail provider (IShellItemImageFactory) — the same picture Explorer shows, no extra dependency.
    //  - Duration: parsed from "ffmpeg -i" output when FFmpeg is available (already used for .wmv conversion); otherwise no duration is shown.
    // Both are computed on background threads and cached per path, so rebuilding the page never blocks the UI.
    public static class VideoMetaService
    {
        private static readonly ConcurrentDictionary<string, Task<Bitmap?>> Thumbs = new(StringComparer.OrdinalIgnoreCase);
        private static readonly ConcurrentDictionary<string, Task<string?>> Durations = new(StringComparer.OrdinalIgnoreCase);
        private static readonly SemaphoreSlim DurationGate = new(2);

        public static Task<Bitmap?> GetThumbnailAsync(string path) => Thumbs.GetOrAdd(path, p => Task.Run(() => LoadThumbnail(p)));
        public static Task<string?> GetDurationAsync(string path) => Durations.GetOrAdd(path, p => Task.Run(() => LoadDurationAsync(p)));

        // ---------------------------------------------------------------- thumbnail (Shell)
        [StructLayout(LayoutKind.Sequential)] private struct SIZE { public int cx, cy; }
        [Flags] private enum SIIGBF { ResizeToFit = 0, BiggerSizeOk = 1, ThumbnailOnly = 8 }

        [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItemImageFactory
        {
            [PreserveSig] int GetImage(SIZE size, SIIGBF flags, out IntPtr phbm);
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        private static extern void SHCreateItemFromParsingName(string pszPath, IntPtr pbc, [In] ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory ppv);

        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr hObject);

        private static Bitmap? LoadThumbnail(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                var iid = new Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b");
                SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out var factory);
                try
                {
                    int hr = factory.GetImage(new SIZE { cx = 128, cy = 72 }, SIIGBF.ThumbnailOnly | SIIGBF.BiggerSizeOk, out var hbm);
                    if (hr != 0 || hbm == IntPtr.Zero) return null;
                    try
                    {
                        using var raw = Image.FromHbitmap(hbm);
                        return new Bitmap(raw);   // detach from the GDI handle
                    }
                    finally { DeleteObject(hbm); }
                }
                finally { Marshal.ReleaseComObject(factory); }
            }
            catch (Exception) { return null; }   // no thumbnail provider for this codec, COM failure, … -> placeholder
        }

        // ---------------------------------------------------------------- duration (ffmpeg)
        private static readonly Regex DurationRegex = new(@"Duration:\s*(\d+):(\d+):(\d+)", RegexOptions.Compiled);

        private static async Task<string?> LoadDurationAsync(string path)
        {
            await DurationGate.WaitAsync();
            try
            {
                var ffmpeg = WmvConversionService.FindFfmpeg();
                if (ffmpeg == null || !File.Exists(path)) return null;
                var psi = new ProcessStartInfo(ffmpeg) { RedirectStandardError = true, RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
                psi.ArgumentList.Add("-hide_banner"); psi.ArgumentList.Add("-i"); psi.ArgumentList.Add(path);
                using var proc = Process.Start(psi);
                if (proc == null) return null;
                var err = proc.StandardError.ReadToEndAsync();      // drain both pipes (a full pipe would block ffmpeg)
                var outp = proc.StandardOutput.ReadToEndAsync();
                if (!proc.WaitForExit(8000)) { try { proc.Kill(true); } catch (Exception) { } return null; }
                var text = await err; await outp;
                var m = DurationRegex.Match(text);
                if (!m.Success) return null;
                int h = int.Parse(m.Groups[1].Value), mi = int.Parse(m.Groups[2].Value), s = int.Parse(m.Groups[3].Value);
                return h > 0 ? $"{h}:{mi:00}:{s:00}" : $"{mi}:{s:00}";
            }
            catch (Exception) { return null; }
            finally { DurationGate.Release(); }
        }
    }
}
