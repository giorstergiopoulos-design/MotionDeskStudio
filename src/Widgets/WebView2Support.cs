using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;

namespace MotionDesk.Widgets
{
    /// <summary>Creates one shared WebView2 environment for all rich-content surfaces.</summary>
    public static class WebView2Support
    {
        private static readonly Lazy<Task<CoreWebView2Environment>> _environment =
            new(CreateEnvironmentAsyncCore);

        public static Task<CoreWebView2Environment> CreateEnvironmentAsync() => _environment.Value;

        private static async Task<CoreWebView2Environment> CreateEnvironmentAsyncCore()
        {
            string userDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MotionDeskStudio", "WebView2");
            Directory.CreateDirectory(userDataFolder);
            return await CoreWebView2Environment.CreateAsync(userDataFolder: userDataFolder).ConfigureAwait(true);
        }
    }
}
