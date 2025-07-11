using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
    
namespace YSMInstaller {
    public static class HttpService {
        private static readonly HttpClient _client = new HttpClient();

        public static async Task<string> GetStringAsync(string url) {
            var response = await _client.GetAsync(url);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync();
        }

        public static async Task<byte[]> DownloadBytesAsync(string url) {
            var response = await _client.GetAsync(url);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsByteArrayAsync();
        }

        public static async Task DownloadFileAsync(string url, string destinationPath, IProgress<int> progress = null) {
            using (var response = await _client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead)) {
                response.EnsureSuccessStatusCode();

                var totalBytes = response.Content.Headers.ContentLength ?? -1L;
                var canReportProgress = totalBytes != -1 && progress != null;

                using (var contentStream = await response.Content.ReadAsStreamAsync())
                using (var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true)) {
                    var buffer = new byte[8192];
                    long totalRead = 0;
                    int read;

                    int lastReported = -1;

                    while ((read = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0) {
                        await fileStream.WriteAsync(buffer, 0, read);
                        totalRead += read;

                        if (canReportProgress) {
                            int percent = (int)((totalRead * 100L) / totalBytes);
                            if (percent != lastReported) {
                                progress.Report(percent);
                                lastReported = percent;
                            }
                        }
                    }
                }
            }
        }
    }
}
