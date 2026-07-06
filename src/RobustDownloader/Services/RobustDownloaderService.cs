using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace RobustDownloader.Services;

public sealed class RobustDownloaderService
{
    private const int MaxRetries = 50;
    private const int UiUpdateRateMs = 500;
    private const int SpeedWindowSec = 3;
    private const int StallTimeoutMinutes = 1;
    private const int ReadTimeoutSeconds = 30;
    private const long FlushThreshold = 32 * 1024 * 1024;

    private int _maxBufferCount;
    private int _blockSizeBytes;
    private string _savePath = "";
    private string _downloadingPath = "";
    private string _configPath = "";
    private long _totalFileSize;
    private string? _basicAuthHeader;
    private List<KeyValuePair<string, string>> _customHeaders = [];
    private DateTime? _serverLastModifiedUtc;
    private long _nextWriteOffset;
    private readonly ConcurrentDictionary<long, byte[]> _buffer = new();
    private SemaphoreSlim _downloadSlots = new(1, 1);
    private SemaphoreSlim _bufferSlots = new(1, 1);
    private readonly object _configLock = new();
    private long _totalBytesWritten;
    private long _totalNetworkBytes;
    private Stopwatch _globalStopwatch = new();
    private readonly Queue<(double Time, long Bytes)> _speedSamples = new();
    private string _diagStatus = "Initializing";
    private DownloadManager? _downloadManager;
    private IProgress<DownloadProgress>? _progress;
    private string _mode = "Range";
    private bool _updateFileTimestamp = true;
    private bool _useSystemProxy = true;
    private string _proxyAddress = "";

    public async Task<DownloadResult> RunAsync(DownloadRequest request, IProgress<DownloadProgress> progress, CancellationToken token)
    {
        ResetState();
        _progress = progress;

        if (request.CrcOnly && request.SkipCrc)
        {
            return Fail("--crc-only cannot be used with --skip-crc.");
        }

        if (string.IsNullOrWhiteSpace(request.Url) || string.IsNullOrWhiteSpace(request.SavePath))
        {
            return Fail("URL and save path are required.");
        }

        var uri = new Uri(request.Url);
        var url = uri.GetLeftPart(UriPartial.Authority) + uri.AbsolutePath + uri.Query;
        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            var encoded = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(uri.UserInfo));
            _basicAuthHeader = $"Basic {encoded}";
        }
        else if (!string.IsNullOrWhiteSpace(request.BasicAuthUsername))
        {
            var userInfo = $"{request.BasicAuthUsername}:{request.BasicAuthPassword}";
            var encoded = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(userInfo));
            _basicAuthHeader = $"Basic {encoded}";
        }

        _savePath = request.SavePath;
        _downloadingPath = _savePath + ".downloading";
        _configPath = _savePath + ".cfg";
        _updateFileTimestamp = request.UpdateFileTimestamp;
        _useSystemProxy = request.UseSystemProxy;
        _proxyAddress = request.ProxyAddress.Trim();
        _blockSizeBytes = Math.Max(32 * 1024, checked((int)(request.BlockSizeMb * 1024 * 1024)));
        var threadCount = Math.Clamp(request.ThreadCount, 1, 256);
        _maxBufferCount = Math.Max(threadCount * 2, 32);
        _customHeaders = ParseCustomHeaders(request.HeaderText);
        _downloadSlots = new SemaphoreSlim(threadCount, threadCount);
        _bufferSlots = new SemaphoreSlim(_maxBufferCount, _maxBufferCount);

        if (!request.CrcOnly && File.Exists(_savePath))
        {
            ReportMessage($"Target file already exists, skipping download: {_savePath}");
            return new DownloadResult { Kind = DownloadResultKind.Skipped, MessageKey = "Download.SkippedExisting" };
        }

        Directory.CreateDirectory(Path.GetDirectoryName(_savePath) ?? AppContext.BaseDirectory);

        using var metadataClient = CreateHttpClient(threadCount);
        try
        {
            ReportMessage("Connecting to server...");
            var supportsRange = await InitializeDownloadAsync(metadataClient, url, request.CrcOnly, request.SkipCrc, token);

            if (request.CrcOnly)
            {
                return new DownloadResult { Kind = DownloadResultKind.CrcOnlyCompleted, MessageKey = "Download.CrcDone" };
            }

            if (supportsRange)
            {
                _mode = "Range";
                return await RunRangeDownloadAsync(url, threadCount, token);
            }

            _mode = "Single";
            Report(new DownloadProgress
            {
                Message = "Download.RangeFallback",
                Diagnostic = "No Range support",
                Mode = _mode,
                IsWarning = true
            });
            return await RunSingleStreamDownloadAsync(url, threadCount, token);
        }
        catch (OperationCanceledException)
        {
            ReportMessage("Download.CanceledWithResume");
            return new DownloadResult { Kind = DownloadResultKind.Canceled, MessageKey = "Download.Canceled" };
        }
        catch (Exception ex)
        {
            return Fail(ex.Message);
        }
    }

    private void ResetState()
    {
        _maxBufferCount = 0;
        _blockSizeBytes = 0;
        _savePath = "";
        _downloadingPath = "";
        _configPath = "";
        _totalFileSize = 0;
        _basicAuthHeader = null;
        _customHeaders = [];
        _serverLastModifiedUtc = null;
        _nextWriteOffset = 0;
        _buffer.Clear();
        _downloadSlots = new SemaphoreSlim(1, 1);
        _bufferSlots = new SemaphoreSlim(1, 1);
        _totalBytesWritten = 0;
        _totalNetworkBytes = 0;
        _globalStopwatch = new Stopwatch();
        _speedSamples.Clear();
        _diagStatus = "Initializing";
        _downloadManager = null;
        _mode = "Range";
        _updateFileTimestamp = true;
    }

    private async Task<DownloadResult> RunRangeDownloadAsync(string url, int threadCount, CancellationToken token)
    {
        LoadResumeOffset();
        PrepareDiskSpace();

        var sessionStartOffset = _nextWriteOffset;
        _totalNetworkBytes = _nextWriteOffset;

        ReportMessage($"Total Size: {FormatSize(_totalFileSize)}");
        ReportMessage($"Resuming From: {FormatSize(_nextWriteOffset)} ({(_totalFileSize == 0 ? 0 : _nextWriteOffset / (double)_totalFileSize):P1})");
        ReportMessage($"Threads: {threadCount}; Mode: Adaptive Fragmentation Enabled");

        _globalStopwatch = Stopwatch.StartNew();
        _downloadManager = new DownloadManager(this, url, threadCount, token);

        var writerTask = Task.Run(() => WriterLoop(token), CancellationToken.None);
        var uiTask = Task.Run(() => UILoop(token), CancellationToken.None);

        await _downloadManager.StartAsync();
        await writerTask;
        await uiTask;

        if (_totalBytesWritten == _totalFileSize)
        {
            CompleteFile();
            var elapsedSeconds = Math.Max(_globalStopwatch.Elapsed.TotalSeconds, 0.001);
            var bytesDownloadedThisSession = _totalBytesWritten - sessionStartOffset;
            var avgSpeed = (long)(bytesDownloadedThisSession / elapsedSeconds);
            return new DownloadResult
            {
                Kind = DownloadResultKind.Completed,
                MessageKey = "Download.CompletedAverageSpeed",
                MessageArgs = [FormatSize(avgSpeed)]
            };
        }

        return Fail($"Size mismatch. Written: {_totalBytesWritten}, Expected: {_totalFileSize}");
    }

    private async Task<DownloadResult> RunSingleStreamDownloadAsync(string url, int threadCount, CancellationToken token)
    {
        if (File.Exists(_configPath)) File.Delete(_configPath);
        _nextWriteOffset = 0;
        _totalBytesWritten = 0;
        _totalNetworkBytes = 0;
        _globalStopwatch = Stopwatch.StartNew();

        using var client = CreateHttpClient(threadCount);
        using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();

        if (response.Content.Headers.ContentLength.HasValue)
            _totalFileSize = response.Content.Headers.ContentLength.Value;
        if (response.Content.Headers.LastModified.HasValue)
            _serverLastModifiedUtc = response.Content.Headers.LastModified.Value.UtcDateTime;

        await using var source = await response.Content.ReadAsStreamAsync(token);
        await using var fs = new FileStream(_downloadingPath, FileMode.Create, FileAccess.Write, FileShare.Read, 1024 * 1024);

        var buffer = new byte[1024 * 1024];
        long unflushedBytes = 0;
        var lastUi = Stopwatch.StartNew();

        while (true)
        {
            using var ctsRead = CancellationTokenSource.CreateLinkedTokenSource(token);
            ctsRead.CancelAfter(TimeSpan.FromSeconds(ReadTimeoutSeconds));

            int read;
            try
            {
                read = await source.ReadAsync(buffer, ctsRead.Token);
            }
            catch (OperationCanceledException)
            {
                if (token.IsCancellationRequested) throw;
                throw new IOException($"Read timeout after {ReadTimeoutSeconds}s");
            }

            if (read == 0) break;

            await fs.WriteAsync(buffer.AsMemory(0, read), token);
            _totalBytesWritten += read;
            _totalNetworkBytes += read;
            unflushedBytes += read;

            if (unflushedBytes >= FlushThreshold)
            {
                await fs.FlushAsync(token);
                unflushedBytes = 0;
            }

            if (lastUi.ElapsedMilliseconds >= UiUpdateRateMs)
            {
                UpdateProgress("Single stream");
                lastUi.Restart();
            }
        }

        await fs.FlushAsync(token);

        if (_totalFileSize > 0 && _totalBytesWritten != _totalFileSize)
            return Fail($"Size mismatch. Written: {_totalBytesWritten}, Expected: {_totalFileSize}");

        CompleteFile();
        UpdateProgress("Completed");
        return new DownloadResult { Kind = DownloadResultKind.Completed, MessageKey = "Download.SingleThreadCompleted" };
    }

    private async Task<bool> InitializeDownloadAsync(HttpClient client, string url, bool crcOnly, bool skipCrc, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();

        if (response.Content.Headers.LastModified.HasValue)
            _serverLastModifiedUtc = response.Content.Headers.LastModified.Value.UtcDateTime;

        if (!skipCrc && TryGetCrcHeader(response, out var crcValues))
        {
            var crcValue = crcValues.FirstOrDefault();
            if (!string.IsNullOrEmpty(crcValue))
            {
                var crcFileName = _savePath + ".crc64";
                var fileName = Path.GetFileName(_savePath);
                var content = $"{crcValue} *{fileName}";
                if (!File.Exists(crcFileName) || File.ReadAllText(crcFileName) != content)
                {
                    await File.WriteAllTextAsync(crcFileName, content, token);
                    ReportMessage($"CRC64 saved to: {Path.GetFileName(crcFileName)}");
                }
            }
        }

        if (response.Content.Headers.ContentLength.HasValue)
            _totalFileSize = response.Content.Headers.ContentLength.Value;

        if (crcOnly) return false;

        try
        {
            using var rangeRequest = new HttpRequestMessage(HttpMethod.Get, url);
            rangeRequest.Headers.Range = new RangeHeaderValue(0, 1);
            using var rangeResponse = await client.SendAsync(rangeRequest, HttpCompletionOption.ResponseHeadersRead, token);
            return rangeResponse.StatusCode == HttpStatusCode.PartialContent;
        }
        catch
        {
            return false;
        }
    }

    private async Task DownloadChunkWithRetry(HttpClient client, string url, Chunk chunk, CancellationToken token)
    {
        var retry = 0;
        var totalSize = chunk.End - chunk.Start + 1;
        var data = new byte[checked((int)totalSize)];
        var bytesReceivedTotal = 0;

        while (retry < MaxRetries && bytesReceivedTotal < totalSize)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                var requestStart = chunk.Start + bytesReceivedTotal;
                var requestEnd = chunk.End;
                var remaining = requestEnd - requestStart + 1;
                var currentRequestLimit = remaining;

                if (retry > 2) currentRequestLimit = 1 * 1024 * 1024;
                if (retry > 5) currentRequestLimit = 64 * 1024;
                if (retry > 8) currentRequestLimit = 32 * 1024;

                if (currentRequestLimit < remaining)
                    requestEnd = requestStart + currentRequestLimit - 1;

                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Range = new RangeHeaderValue(requestStart, requestEnd);

                using var ctsSend = CancellationTokenSource.CreateLinkedTokenSource(token);
                ctsSend.CancelAfter(TimeSpan.FromSeconds(20));

                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ctsSend.Token);
                if (!response.IsSuccessStatusCode)
                {
                    if ((int)response.StatusCode is 429 or 503)
                    {
                        retry++;
                        await Task.Delay(2000 * retry, token);
                        continue;
                    }
                    response.EnsureSuccessStatusCode();
                }

                await using var stream = await response.Content.ReadAsStreamAsync(token);
                var expectedForThisRequest = requestEnd - requestStart + 1;
                var bytesReadForThisRequest = 0;

                while (bytesReadForThisRequest < expectedForThisRequest)
                {
                    using var ctsRead = CancellationTokenSource.CreateLinkedTokenSource(token);
                    ctsRead.CancelAfter(TimeSpan.FromSeconds(ReadTimeoutSeconds));

                    try
                    {
                        var read = await stream.ReadAsync(data, bytesReceivedTotal, checked((int)(expectedForThisRequest - bytesReadForThisRequest)), ctsRead.Token);
                        if (read == 0) break;

                        bytesReceivedTotal += read;
                        bytesReadForThisRequest += read;
                        Interlocked.Add(ref _totalNetworkBytes, read);
                    }
                    catch (OperationCanceledException)
                    {
                        if (token.IsCancellationRequested) throw;
                        throw new IOException($"Read timeout after {ReadTimeoutSeconds}s");
                    }
                }
            }
            catch
            {
                retry++;
                if (retry >= MaxRetries) throw;
                try { await Task.Delay(500 + retry * 200, token); } catch { }
            }
        }

        if (bytesReceivedTotal != totalSize)
            throw new IOException($"Chunk failed after {MaxRetries} retries. Got {bytesReceivedTotal}/{totalSize}");

        _buffer.TryAdd(chunk.Start, data);
    }

    private void WriterLoop(CancellationToken token)
    {
        using var fs = new FileStream(_downloadingPath, FileMode.Open, FileAccess.Write, FileShare.Read);
        fs.Seek(_nextWriteOffset, SeekOrigin.Begin);

        long unflushedBytes = 0;
        while (_totalBytesWritten < _totalFileSize && !token.IsCancellationRequested)
        {
            if (_buffer.TryGetValue(_nextWriteOffset, out var data))
            {
                fs.Write(data, 0, data.Length);
                _nextWriteOffset += data.Length;
                _totalBytesWritten += data.Length;
                unflushedBytes += data.Length;

                _buffer.TryRemove(_nextWriteOffset - data.Length, out _);
                _bufferSlots.Release();

                if (unflushedBytes >= FlushThreshold || _totalBytesWritten == _totalFileSize)
                {
                    fs.Flush(true);
                    unflushedBytes = 0;
                    UpdateConfigFile(_nextWriteOffset);
                }
            }
            else
            {
                Thread.Sleep(20);
            }
        }

        fs.Flush(true);
        UpdateConfigFile(_nextWriteOffset);
    }

    private async Task UILoop(CancellationToken token)
    {
        long lastNetworkBytes = 0;
        var lastActivityTime = DateTime.Now;

        while (_totalBytesWritten < _totalFileSize && !token.IsCancellationRequested)
        {
            var currentBytes = Interlocked.Read(ref _totalNetworkBytes);
            var isBufferFull = _bufferSlots.CurrentCount == 0;
            var areThreadsBusy = _downloadSlots.CurrentCount == 0;
            var hasWriterBlock = _buffer.ContainsKey(_nextWriteOffset);

            if (currentBytes > lastNetworkBytes)
            {
                _diagStatus = "Running";
            }
            else
            {
                if (hasWriterBlock) _diagStatus = "Disk I/O Bottleneck";
                else if (isBufferFull) _diagStatus = "DEADLOCK: Buffer Full & Missing Next Block";
                else if (areThreadsBusy) _diagStatus = "Network Hang / Fragmenting";
                else _diagStatus = "Idle / Queue Empty";
            }

            if (currentBytes > lastNetworkBytes)
            {
                lastNetworkBytes = currentBytes;
                lastActivityTime = DateTime.Now;
            }
            else
            {
                var stalledDuration = DateTime.Now - lastActivityTime;
                if (stalledDuration.TotalSeconds > 15)
                {
                    if (_diagStatus.Contains("DEADLOCK", StringComparison.Ordinal))
                    {
                        ResolveDeadlock();
                        lastActivityTime = DateTime.Now;
                    }
                    else if (stalledDuration.TotalMinutes >= StallTimeoutMinutes)
                    {
                        ReportMessage($"STALL DETECTED! Duration: {stalledDuration.TotalMinutes:F1} min. Reason: {_diagStatus}");
                        _downloadManager?.SoftRestart();
                        lastActivityTime = DateTime.Now;
                    }
                }
            }

            UpdateProgress((DateTime.Now - lastActivityTime).TotalSeconds > 5 ? _diagStatus : "Running");
            await Task.Delay(UiUpdateRateMs, token).ContinueWith(_ => { }, CancellationToken.None);
        }

        if (!token.IsCancellationRequested)
            UpdateProgress("Completed");
    }

    private void LoadResumeOffset()
    {
        if (File.Exists(_configPath) && File.Exists(_downloadingPath))
        {
            try
            {
                var lines = File.ReadAllLines(_configPath);
                if (lines.Length > 0 && long.TryParse(lines[0], out var savedOffset))
                {
                    if (savedOffset <= _totalFileSize && new FileInfo(_downloadingPath).Length >= savedOffset)
                    {
                        _nextWriteOffset = savedOffset;
                        _totalBytesWritten = savedOffset;
                    }
                }
            }
            catch { }
        }
    }

    private void PrepareDiskSpace()
    {
        if (!File.Exists(_downloadingPath) || new FileInfo(_downloadingPath).Length != _totalFileSize)
        {
            ReportMessage("Allocating disk space...");
            using var fs = new FileStream(_downloadingPath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None);
            fs.SetLength(_totalFileSize);
        }
    }

    private void UpdateConfigFile(long offset)
    {
        lock (_configLock)
            File.WriteAllText(_configPath, offset.ToString(CultureInfo.InvariantCulture));
    }

    private void ResolveDeadlock()
    {
        if (_buffer.IsEmpty) return;
        var furthestOffset = _buffer.Keys.Max();
        if (furthestOffset == _nextWriteOffset) return;
        if (_buffer.TryRemove(furthestOffset, out _))
        {
            _bufferSlots.Release();
            ReportMessage($"Deadlock breaker evicted block {furthestOffset}.");
        }
    }

    private ConcurrentQueue<Chunk> GenerateChunksQueue(long startOffset)
    {
        var queue = new ConcurrentQueue<Chunk>();
        var current = startOffset;
        var count = 0;
        var existingKeys = new HashSet<long>(_buffer.Keys);

        while (current < _totalFileSize && count < _maxBufferCount)
        {
            if (!existingKeys.Contains(current))
            {
                var end = Math.Min(current + _blockSizeBytes - 1, _totalFileSize - 1);
                queue.Enqueue(new Chunk(current, end));
            }
            current += _blockSizeBytes;
            count++;
        }

        return queue;
    }

    private void UpdateProgress(string message)
    {
        var nowSeconds = _globalStopwatch.Elapsed.TotalSeconds;
        var currentNetworkBytes = Interlocked.Read(ref _totalNetworkBytes);

        lock (_speedSamples)
        {
            _speedSamples.Enqueue((nowSeconds, currentNetworkBytes));
            while (_speedSamples.Count > 0 && nowSeconds - _speedSamples.Peek().Time > SpeedWindowSec)
                _speedSamples.Dequeue();
        }

        double speed = 0;
        lock (_speedSamples)
        {
            if (_speedSamples.Count >= 2)
            {
                var first = _speedSamples.Peek();
                var last = _speedSamples.Last();
                if (last.Time - first.Time > 0.1)
                    speed = (last.Bytes - first.Bytes) / (last.Time - first.Time);
            }
        }

        var total = _totalFileSize;
        var remainingBytes = total > 0 ? Math.Max(0, total - _totalBytesWritten) : 0;
        var progressPct = total > 0 ? _totalBytesWritten / (double)total * 100 : 0;
        var eta = "--:--:--";
        if (speed > 0 && remainingBytes > 0)
        {
            try
            {
                var etaSpan = TimeSpan.FromSeconds(remainingBytes / speed);
                eta = etaSpan.TotalDays >= 1
                    ? $"{etaSpan.Days}d {etaSpan.Hours:D2}:{etaSpan.Minutes:D2}:{etaSpan.Seconds:D2}"
                    : $"{etaSpan:hh\\:mm\\:ss}";
            }
            catch { }
        }

        Report(new DownloadProgress
        {
            ProgressPercent = progressPct,
            BytesWritten = _totalBytesWritten,
            TotalBytes = total,
            NetworkBytes = currentNetworkBytes,
            SpeedBytesPerSecond = (long)speed,
            Eta = eta,
            Message = message,
            Diagnostic = _diagStatus,
            Mode = _mode
        });
    }

    private void CompleteFile()
    {
        if (File.Exists(_configPath)) File.Delete(_configPath);
        if (File.Exists(_savePath)) File.Delete(_savePath);
        File.Move(_downloadingPath, _savePath);

        if (_updateFileTimestamp && _serverLastModifiedUtc.HasValue)
        {
            File.SetCreationTimeUtc(_savePath, _serverLastModifiedUtc.Value);
            File.SetLastWriteTimeUtc(_savePath, _serverLastModifiedUtc.Value);
        }
    }

    private HttpClient CreateHttpClient(int threadCount)
    {
        var handler = new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            MaxConnectionsPerServer = threadCount + 20,
            ConnectTimeout = TimeSpan.FromSeconds(10),
            UseProxy = _useSystemProxy || !string.IsNullOrWhiteSpace(_proxyAddress)
        };

        if (!string.IsNullOrWhiteSpace(_proxyAddress))
            handler.Proxy = new WebProxy(_proxyAddress);

        var client = new HttpClient(handler) { Timeout = TimeSpan.FromHours(24) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/143.0.0.0 Safari/537.36");
        ApplyCustomHeaders(client);

        if (_basicAuthHeader != null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", _basicAuthHeader[6..]);
        }

        return client;
    }

    private static bool TryGetCrcHeader(HttpResponseMessage response, out IEnumerable<string> crcValues)
    {
        if (response.Headers.TryGetValues("x-cos-hash-crc64ecma", out var cosValues))
        {
            crcValues = cosValues;
            return true;
        }
        if (response.Headers.TryGetValues("x-oss-hash-crc64ecma", out var ossValues))
        {
            crcValues = ossValues;
            return true;
        }
        crcValues = [];
        return false;
    }

    private static string FormatSize(long bytes)
    {
        string[] sizes = ["B", "KB", "MB", "GB", "TB"];
        double len = bytes;
        var order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len /= 1024;
        }
        return $"{len:0.00} {sizes[order]}";
    }

    private static List<KeyValuePair<string, string>> ParseCustomHeaders(string text)
    {
        var headers = new List<KeyValuePair<string, string>>();
        foreach (var raw in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var value = raw.Trim();
            if (string.IsNullOrWhiteSpace(value)) continue;
            var idx = value.IndexOf(':', StringComparison.Ordinal);
            if (idx <= 0) continue;
            var name = value[..idx].Trim();
            var headerValue = value[(idx + 1)..].Trim();
            if (!string.IsNullOrEmpty(name))
                headers.Add(new KeyValuePair<string, string>(name, headerValue));
        }
        return headers;
    }

    private void ApplyCustomHeaders(HttpClient client)
    {
        foreach (var header in _customHeaders)
        {
            if (string.Equals(header.Key, "User-Agent", StringComparison.OrdinalIgnoreCase))
            {
                client.DefaultRequestHeaders.UserAgent.Clear();
                if (!string.IsNullOrWhiteSpace(header.Value))
                    client.DefaultRequestHeaders.UserAgent.ParseAdd(header.Value);
                continue;
            }
            if (client.DefaultRequestHeaders.Contains(header.Key))
                client.DefaultRequestHeaders.Remove(header.Key);
            client.DefaultRequestHeaders.TryAddWithoutValidation(header.Key, header.Value);
        }
    }

    private void ReportMessage(string message)
    {
        Report(new DownloadProgress
        {
            ProgressPercent = _totalFileSize > 0 ? _totalBytesWritten / (double)_totalFileSize * 100 : 0,
            BytesWritten = _totalBytesWritten,
            TotalBytes = _totalFileSize,
            NetworkBytes = Interlocked.Read(ref _totalNetworkBytes),
            Message = message,
            Diagnostic = _diagStatus,
            Mode = _mode
        });
    }

    private void Report(DownloadProgress progress)
    {
        _progress?.Report(progress);
    }

    private DownloadResult Fail(string message)
    {
        Report(new DownloadProgress { Message = message, Diagnostic = "Error", Mode = _mode, IsWarning = true });
        return new DownloadResult { Kind = DownloadResultKind.Failed, Message = message };
    }

    private readonly record struct Chunk(long Start, long End);

    private sealed class DownloadManager
    {
        private readonly RobustDownloaderService _owner;
        private readonly string _url;
        private readonly int _threadCount;
        private readonly CancellationToken _externalToken;
        private CancellationTokenSource _cts;
        private HttpClient _client;
        private volatile bool _isRestarting;

        public DownloadManager(RobustDownloaderService owner, string url, int threadCount, CancellationToken externalToken)
        {
            _owner = owner;
            _url = url;
            _threadCount = threadCount;
            _externalToken = externalToken;
            _cts = CancellationTokenSource.CreateLinkedTokenSource(externalToken);
            _client = _owner.CreateHttpClient(threadCount);
        }

        public async Task StartAsync()
        {
            while (_owner._totalBytesWritten < _owner._totalFileSize)
            {
                _externalToken.ThrowIfCancellationRequested();
                _isRestarting = false;
                var chunksQueue = _owner.GenerateChunksQueue(_owner._nextWriteOffset);
                if (chunksQueue.IsEmpty)
                {
                    await Task.Delay(500, _externalToken);
                    continue;
                }

                var activeTasks = new List<Task>();
                _owner.ReportMessage($"Download loop started. Pending chunks: {chunksQueue.Count}");

                try
                {
                    while (!chunksQueue.IsEmpty && !_isRestarting)
                    {
                        var acquiredBuffer = false;
                        var acquiredThread = false;
                        try
                        {
                            await _owner._bufferSlots.WaitAsync(_cts.Token);
                            acquiredBuffer = true;

                            await _owner._downloadSlots.WaitAsync(_cts.Token);
                            acquiredThread = true;

                            if (chunksQueue.TryDequeue(out var chunk))
                            {
                                activeTasks.Add(Task.Run(async () =>
                                {
                                    var success = false;
                                    try
                                    {
                                        await _owner.DownloadChunkWithRetry(_client, _url, chunk, _cts.Token);
                                        success = true;
                                    }
                                    catch (OperationCanceledException) { }
                                    catch (Exception ex)
                                    {
                                        _owner.ReportMessage($"Chunk {chunk.Start} failed: {ex.Message}");
                                    }
                                    finally
                                    {
                                        _owner._downloadSlots.Release();
                                        if (!success) _owner._bufferSlots.Release();
                                    }
                                }, CancellationToken.None));
                            }
                            else
                            {
                                _owner._downloadSlots.Release();
                                _owner._bufferSlots.Release();
                            }
                        }
                        catch (OperationCanceledException)
                        {
                            if (acquiredThread) _owner._downloadSlots.Release();
                            if (acquiredBuffer) _owner._bufferSlots.Release();
                            throw;
                        }

                        activeTasks.RemoveAll(t => t.IsCompleted);
                    }

                    await Task.WhenAll(activeTasks);
                }
                catch (OperationCanceledException)
                {
                    if (_externalToken.IsCancellationRequested)
                    {
                        try { await Task.WhenAll(activeTasks); } catch { }
                        throw;
                    }

                    _owner.ReportMessage("DownloadManager is resetting connection pool...");
                    try { await Task.WhenAll(activeTasks); } catch { }
                    _cts.Dispose();
                    _cts = CancellationTokenSource.CreateLinkedTokenSource(_externalToken);
                    _client.Dispose();
                    _client = _owner.CreateHttpClient(_threadCount);
                    _owner.ReportMessage("Reset complete. Resuming download.");
                }
            }
        }

        public void SoftRestart()
        {
            if (_isRestarting) return;
            _isRestarting = true;
            _cts.Cancel();
        }
    }
}
