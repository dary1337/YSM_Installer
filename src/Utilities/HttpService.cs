using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace YSMInstaller {
    public static class HttpService {
        private static readonly HttpClient Client = CreateClient();
        private const int UnknownSizeProgressStepBytes = 256 * 1024;

        // Per-chunk retry budget for multi-part downloads. Covers transient hiccups (5xx, brief
        // network drop, server-side reset) without disguising deterministic failures — the
        // backoff is short enough that real outages bubble up within ~10 s.
        private const int MaxChunkAttempts = 3;
        private static readonly TimeSpan[] ChunkRetryBackoff = {
            TimeSpan.FromSeconds(2),
            TimeSpan.FromSeconds(5),
        };

        // Caps detailed-progress reports to ~5/sec so the "X / Y MB" label doesn't flicker on
        // fast pipes (10+ MB/s would otherwise update 10+ times per second). Final state is
        // always reported once after the loop, so the visible MB count lands on the true total.
        private static readonly TimeSpan DetailedReportMinInterval = TimeSpan.FromMilliseconds(200);

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

        public static async Task<long?> TryGetRemoteFileSizeAsync(
            string url,
            CancellationToken cancellationToken = default
        ) {
            // Manual / synthetic mod entries have no DownloadUrl; skip the probe instead of
            // letting HttpClient throw InvalidOperationException on a missing absolute URI.
            if (string.IsNullOrWhiteSpace(url)) {
                return null;
            }
            url = GoogleDriveLinks.Normalize(url);
            try {
                using (var headRequest = new HttpRequestMessage(HttpMethod.Head, url))
                using (var headResponse = await Client.SendAsync(headRequest, cancellationToken)) {
                    if (headResponse.IsSuccessStatusCode) {
                        return headResponse.Content.Headers.ContentLength;
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
                throw;
            }
            catch (Exception exception) {
                AppLogger.Critical($"HEAD size probe failed for {url}; falling back to GET headers.", exception);
            }

            try {
                using (
                    var response = await Client.GetAsync(
                        url,
                        HttpCompletionOption.ResponseHeadersRead,
                        cancellationToken
                    )
                ) {
                    if (!response.IsSuccessStatusCode) {
                        return null;
                    }

                    return response.Content.Headers.ContentLength;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
                throw;
            }
            catch (Exception exception) {
                AppLogger.Critical($"Failed to determine remote file size for {url}.", exception);
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
            // Stream into a sibling .tmp first and atomically rename on success — a crash /
            // cancel / network drop mid-write never leaves a half-written file at destinationPath.
            string tempPath = destinationPath + ".tmp";
            bool moved = false;
            try {
            using (
                var response = await Client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            ) {
                response.EnsureSuccessStatusCode();

                // Google Drive (and similar) return 200 OK with text/html when the file is
                // throttled / quota-exceeded / behind an interstitial; downloading that as a
                // "mod archive" would only fail later with a cryptic SharpCompress error.
                string? mediaType = response.Content.Headers.ContentType?.MediaType;
                if (!string.IsNullOrEmpty(mediaType)
                    && mediaType!.StartsWith("text/html", StringComparison.OrdinalIgnoreCase)) {
                    throw new RemoteHtmlResponseException(
                        $"Server returned HTML instead of a file for {url} — the host likely " +
                        "rate-limited or quota-blocked this download (common with public Google " +
                        "Drive files). Try again later, or ask the mod publisher to refresh the link."
                    );
                }

                var totalBytes = response.Content.Headers.ContentLength ?? -1L;
                var canReportProgress = totalBytes != -1 && progress != null;

                using (var contentStream = await response.Content.ReadAsStreamAsync())
                using (
                    var fileStream = new FileStream(
                        tempPath,
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
                    DateTime lastDetailedReportAt = DateTime.MinValue;

                    while ((read = await contentStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0) {
                        await fileStream.WriteAsync(buffer, 0, read, cancellationToken);
                        totalRead += read;

                        if (
                            detailedProgress != null
                            && (
                                canReportProgress
                                || totalRead - lastReportedBytes >= UnknownSizeProgressStepBytes
                            )
                            && DateTime.UtcNow - lastDetailedReportAt >= DetailedReportMinInterval
                        ) {
                            lastDetailedReportAt = DateTime.UtcNow;
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
            if (File.Exists(destinationPath)) {
                File.Delete(destinationPath);
            }
            File.Move(tempPath, destinationPath);
            moved = true;
            }
            finally {
                if (!moved) {
                    TryDeleteFile(tempPath);
                }
            }
        }

        // Sums HEAD-reported sizes from all parts. Used for the pre-download disk-space estimate
        // when a mod ships as a split archive. Returns null on the first part with unknown size
        // so we don't compute a misleading partial total.
        public static async Task<long?> TryGetTotalSizeAsync(
            IReadOnlyList<string> urls,
            CancellationToken cancellationToken = default
        ) {
            if (urls == null || urls.Count == 0) {
                return null;
            }
            long total = 0;
            foreach (string url in urls) {
                long? part = await TryGetRemoteFileSizeAsync(url, cancellationToken);
                if (!part.HasValue || part.Value <= 0) {
                    return null;
                }
                total = checked(total + part.Value);
            }
            return total;
        }

        // Streams multiple URLs into a single destination file, byte-concatenated. Works for
        // archives produced by `7z -v<size>` or `split -b <size>` — the result is a valid
        // single archive that SharpCompress / ZipFile can open with no awareness of parts.
        public static async Task DownloadFilePartsAsync(
            IReadOnlyList<string> urls,
            string destinationPath,
            long? knownTotalBytes = null,
            IProgress<int>? progress = null,
            IProgress<DownloadProgressInfo>? detailedProgress = null,
            IProgress<string>? retryStatus = null,
            CancellationToken cancellationToken = default
        ) {
            if (urls == null || urls.Count == 0) {
                throw new ArgumentException("At least one URL is required.", nameof(urls));
            }

            long totalBytes = knownTotalBytes ?? -1L;
            bool canReportPercent = totalBytes > 0 && progress != null;

            // Same atomic-rename invariant as the single-URL path — multi-GB concatenations
            // are even more painful if a mid-stream failure leaves garbage at the final path.
            string tempPath = destinationPath + ".tmp";
            bool moved = false;
            try {
            using (
                var fileStream = new FileStream(
                    tempPath,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None,
                    8192,
                    true
                )
            ) {
                long writtenSoFar = 0;
                int lastPercent = -1;
                long lastReportedBytes = -UnknownSizeProgressStepBytes;
                DateTime lastDetailedReportAt = DateTime.MinValue;
                var buffer = new byte[8192];

                for (int i = 0; i < urls.Count; i++) {
                    string url = GoogleDriveLinks.Normalize(urls[i]);

                    // Snapshot the per-chunk start so a mid-stream failure can be retried by
                    // truncating partial bytes for this chunk only and rewinding counters —
                    // bytes from earlier successful chunks stay on disk.
                    long chunkStartPosition = fileStream.Position;
                    long chunkStartBytes = writtenSoFar;
                    long chunkStartReported = lastReportedBytes;
                    int chunkStartPercent = lastPercent;

                    int attempt = 0;
                    while (true) {
                        attempt++;
                        try {
#if DEBUG
                            // Dev injection point for the Test menu to exercise the retry loop
                            // without a real network failure. Compiled out of Release entirely.
                            if (DevWarnoMocks.TryConsumeChunkFailure(out string mockReason)) {
                                throw new IOException($"[mock] {mockReason}");
                            }
#endif
                            using (
                                var response = await Client.GetAsync(
                                    url,
                                    HttpCompletionOption.ResponseHeadersRead,
                                    cancellationToken
                                )
                            ) {
                                response.EnsureSuccessStatusCode();
                                string? mediaType = response.Content.Headers.ContentType?.MediaType;
                                if (!string.IsNullOrEmpty(mediaType)
                                    && mediaType!.StartsWith("text/html", StringComparison.OrdinalIgnoreCase)) {
                                    throw new RemoteHtmlResponseException(
                                        $"Server returned HTML instead of a file for {url} — the host likely " +
                                        "rate-limited or quota-blocked this download. Try again later, or " +
                                        "ask the mod publisher to refresh the link."
                                    );
                                }

                                using (var contentStream = await response.Content.ReadAsStreamAsync()) {
                                    int read;
                                    while ((read = await contentStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0) {
                                        await fileStream.WriteAsync(buffer, 0, read, cancellationToken);
                                        writtenSoFar += read;

                                        if (
                                            detailedProgress != null
                                            && (
                                                canReportPercent
                                                || writtenSoFar - lastReportedBytes >= UnknownSizeProgressStepBytes
                                            )
                                            && DateTime.UtcNow - lastDetailedReportAt >= DetailedReportMinInterval
                                        ) {
                                            lastDetailedReportAt = DateTime.UtcNow;
                                            detailedProgress.Report(
                                                new DownloadProgressInfo(
                                                    writtenSoFar,
                                                    totalBytes > 0 ? totalBytes : (long?)null
                                                )
                                            );
                                            lastReportedBytes = writtenSoFar;
                                        }

                                        if (canReportPercent) {
                                            int percent = (int)((writtenSoFar * 100L) / totalBytes);
                                            if (percent != lastPercent) {
                                                progress!.Report(percent);
                                                lastPercent = percent;
                                            }
                                        }
                                    }
                                }
                            }
                            break;
                        }
                        catch (RemoteHtmlResponseException) {
                            // Quota / interstitial — won't clear in seconds, retry is pointless.
                            throw;
                        }
                        catch (TaskCanceledException exception) when (
                            !cancellationToken.IsCancellationRequested
                            && attempt < MaxChunkAttempts
                        ) {
                            // HttpClient timeout (not user cancel — that case is filtered out and
                            // handled by the OperationCanceledException catch below).
                            await ResetChunkAndBackoffAsync(exception, attempt, i + 1, urls.Count);
                        }
                        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
                            throw;
                        }
                        catch (HttpRequestException exception) when (attempt < MaxChunkAttempts) {
                            await ResetChunkAndBackoffAsync(exception, attempt, i + 1, urls.Count);
                        }
                        catch (IOException exception) when (attempt < MaxChunkAttempts) {
                            await ResetChunkAndBackoffAsync(exception, attempt, i + 1, urls.Count);
                        }
                    }

                    async Task ResetChunkAndBackoffAsync(Exception exception, int attemptNo, int partNo, int partTotal) {
                        AppLogger.Info(
                            $"Chunk {partNo}/{partTotal} attempt {attemptNo} failed ({exception.GetType().Name}: {exception.Message}); retrying."
                        );
                        // Stage-text format is "Downloading..." so the UI's step-checklist stays
                        // on the Downloading row instead of bouncing between matched prefixes.
                        // Clamp the index so growing MaxChunkAttempts without extending the
                        // backoff array can't crash — extra retries just reuse the last delay.
                        int backoffIndex = Math.Min(attemptNo - 1, ChunkRetryBackoff.Length - 1);
                        TimeSpan delay = ChunkRetryBackoff[backoffIndex];
                        retryStatus?.Report(
                            $"Downloading... retrying part {partNo}/{partTotal} (attempt {attemptNo + 1}) in {delay.TotalSeconds:0}s"
                        );
                        fileStream.SetLength(chunkStartPosition);
                        fileStream.Position = chunkStartPosition;
                        writtenSoFar = chunkStartBytes;
                        lastReportedBytes = chunkStartReported;
                        lastPercent = chunkStartPercent;
                        await Task.Delay(delay, cancellationToken);
                    }
                }

                detailedProgress?.Report(
                    new DownloadProgressInfo(
                        writtenSoFar,
                        totalBytes > 0 ? totalBytes : (long?)null
                    )
                );
            }
            if (File.Exists(destinationPath)) {
                File.Delete(destinationPath);
            }
            File.Move(tempPath, destinationPath);
            moved = true;
            }
            finally {
                if (!moved) {
                    TryDeleteFile(tempPath);
                }
            }
        }

        // Best-effort cleanup for temp files left behind by failed downloads. Narrow catches so
        // a programming bug surfaces instead of being silently swallowed.
        private static void TryDeleteFile(string path) {
            try {
                if (File.Exists(path)) {
                    File.Delete(path);
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        private static HttpClient CreateClient() {
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };

            client.DefaultRequestHeaders.UserAgent.ParseAdd("YSMInstaller/1.0");
            return client;
        }
    }
}
