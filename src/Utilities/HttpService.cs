using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace YSMInstaller {
    public static class HttpService {
        private static readonly HttpClient Client = CreateClient();
        private const int UnknownSizeProgressStepBytes = 256 * 1024;

        public readonly struct DownloadProgressInfo {
            public DownloadProgressInfo(long bytesReceived, long? totalBytes) {
                BytesReceived = bytesReceived;
                TotalBytes = totalBytes;
            }

            public long BytesReceived { get; }
            public long? TotalBytes { get; }
        }

        public static async Task<string> GetStringAsync(string url, string? acceptHeader = null) {
            using (var request = new HttpRequestMessage(HttpMethod.Get, url)) {
                if (!string.IsNullOrWhiteSpace(acceptHeader)) {
                    request.Headers.Accept.ParseAdd(acceptHeader);
                }

                using (var response = await Client.SendAsync(request)) {
                    response.EnsureSuccessStatusCode();
                    return await response.Content.ReadAsStringAsync();
                }
            }
        }

        public static async Task<long?> TryGetRemoteFileSizeAsync(string url) {
            url = GoogleDriveLinks.Normalize(url);
            try {
                using (var headRequest = new HttpRequestMessage(HttpMethod.Head, url))
                using (var headResponse = await Client.SendAsync(headRequest)) {
                    if (headResponse.IsSuccessStatusCode) {
                        return headResponse.Content.Headers.ContentLength;
                    }
                }
            }
            catch {
                // Ignore and fallback to GET headers probe.
            }

            try {
                using (
                    var response = await Client.GetAsync(
                        url,
                        HttpCompletionOption.ResponseHeadersRead
                    )
                ) {
                    if (!response.IsSuccessStatusCode) {
                        return null;
                    }

                    return response.Content.Headers.ContentLength;
                }
            }
            catch {
                return null;
            }
        }

        public static async Task DownloadFileAsync(
            string url,
            string destinationPath,
            IProgress<int>? progress = null,
            IProgress<DownloadProgressInfo>? detailedProgress = null,
            CancellationToken cancellationToken = default
        ) {
            url = GoogleDriveLinks.Normalize(url);
            using (
                var response = await Client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            ) {
                response.EnsureSuccessStatusCode();

                var totalBytes = response.Content.Headers.ContentLength ?? -1L;
                var canReportProgress = totalBytes != -1 && progress != null;

                using (var contentStream = await response.Content.ReadAsStreamAsync())
                using (
                    var fileStream = new FileStream(
                        destinationPath,
                        FileMode.Create,
                        FileAccess.Write,
                        FileShare.None,
                        8192,
                        true
                    )
                ) {
                    var buffer = new byte[8192];
                    long totalRead = 0;
                    int read;
                    int lastReported = -1;
                    long lastReportedBytes = -UnknownSizeProgressStepBytes;

                    while ((read = await contentStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0) {
                        await fileStream.WriteAsync(buffer, 0, read, cancellationToken);
                        totalRead += read;

                        if (
                            detailedProgress != null
                            && (
                                canReportProgress
                                || totalRead - lastReportedBytes >= UnknownSizeProgressStepBytes
                            )
                        ) {
                            detailedProgress.Report(
                                new DownloadProgressInfo(
                                    totalRead,
                                    totalBytes == -1 ? (long?)null : totalBytes
                                )
                            );
                            lastReportedBytes = totalRead;
                        }

                        if (canReportProgress) {
                            int percent = (int)((totalRead * 100L) / totalBytes);
                            if (percent != lastReported) {
                                progress?.Report(percent);
                                lastReported = percent;
                            }
                        }
                    }

                    detailedProgress?.Report(
                        new DownloadProgressInfo(
                            totalRead,
                            totalBytes == -1 ? (long?)null : totalBytes
                        )
                    );
                }
            }
        }

        private static HttpClient CreateClient() {
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };

            client.DefaultRequestHeaders.UserAgent.ParseAdd("YSMInstaller/1.0");
            return client;
        }
    }
}
