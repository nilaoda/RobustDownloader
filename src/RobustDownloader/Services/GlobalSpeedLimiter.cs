using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace RobustDownloader.Services;

public static class GlobalSpeedLimiter
{
    private const int MinimumLimitedReadSize = 4 * 1024;
    private const int MaximumLimitedReadSize = 128 * 1024;
    private const int MinimumLimitedRequestSize = 16 * 1024;
    private const int MaximumLimitedRequestSize = 256 * 1024;
    private const int MinimumInFlightBytes = 64 * 1024;
    private const int MaximumInFlightBytes = 2 * 1024 * 1024;
    private const double CapacitySeconds = 0.05;
    private const double ReadSliceSeconds = 0.02;
    private const double RequestSliceSeconds = 0.02;
    private const double InFlightSeconds = 0.1;

    private static readonly object SyncRoot = new();
    private static long _limitBytesPerSecond;
    private static double _availableBytes;
    private static long _inFlightBytes;
    private static long _lastRefillTimestamp;
    private static TaskCompletionSource<object?> _changeSignal = CreateSignal();

    public static long LimitBytesPerSecond => Volatile.Read(ref _limitBytesPerSecond);
    public static bool IsEnabled => LimitBytesPerSecond > 0;

    public static void Configure(bool isEnabled, long bytesPerSecond)
    {
        var limit = isEnabled ? Math.Max(1, bytesPerSecond) : 0;

        lock (SyncRoot)
        {
            var previousLimit = Volatile.Read(ref _limitBytesPerSecond);
            if (previousLimit > 0)
                Refill(previousLimit);

            Volatile.Write(ref _limitBytesPerSecond, limit);
            _lastRefillTimestamp = Stopwatch.GetTimestamp();

            if (limit <= 0)
            {
                _availableBytes = 0;
                _inFlightBytes = 0;
            }
            else if (previousLimit <= 0)
            {
                _availableBytes = 0;
            }
            else
            {
                _availableBytes = Math.Min(_availableBytes, GetCapacityBytes(limit));
            }

            PulseWaitersLocked();
        }
    }

    public static int GetRangeRequestSize(long requestedBytes)
    {
        if (requestedBytes <= 0) return 0;

        var limit = LimitBytesPerSecond;
        if (limit <= 0)
            return checked((int)Math.Min(requestedBytes, int.MaxValue));

        var preferred = GetPreferredRequestSize(limit);
        return checked((int)Math.Clamp(requestedBytes, 1, preferred));
    }

    public static async ValueTask<GlobalSpeedLimitLease> AcquireReadLeaseAsync(int requestedBytes, CancellationToken token)
    {
        if (requestedBytes <= 0)
            return GlobalSpeedLimitLease.Unlimited(0);

        var limit = LimitBytesPerSecond;
        if (limit <= 0)
            return GlobalSpeedLimitLease.Unlimited(requestedBytes);

        while (true)
        {
            Task signalTask;
            double waitSeconds;

            lock (SyncRoot)
            {
                limit = LimitBytesPerSecond;
                if (limit <= 0)
                    return GlobalSpeedLimitLease.Unlimited(requestedBytes);

                Refill(limit);
                var targetReadSize = Math.Min(requestedBytes, GetPreferredReadSize(limit));
                if (_availableBytes >= targetReadSize)
                {
                    _availableBytes -= targetReadSize;
                    return GlobalSpeedLimitLease.Tracked(targetReadSize, tracksInFlight: false);
                }

                var minimumUsefulRead = Math.Min(targetReadSize, MinimumLimitedReadSize);
                if (_availableBytes >= minimumUsefulRead)
                {
                    var allowed = Math.Max(1, (int)Math.Min(requestedBytes, Math.Floor(_availableBytes)));
                    _availableBytes -= allowed;
                    return GlobalSpeedLimitLease.Tracked(allowed, tracksInFlight: false);
                }

                waitSeconds = Math.Max(0.001, (minimumUsefulRead - _availableBytes) / limit);
                signalTask = _changeSignal.Task;
            }

            await WaitForBudgetAsync(signalTask, waitSeconds, token);
        }
    }

    public static async ValueTask<GlobalSpeedLimitLease> AcquireRequestLeaseAsync(int bytes, CancellationToken token)
    {
        if (bytes <= 0)
            return GlobalSpeedLimitLease.Unlimited(0);

        var limit = LimitBytesPerSecond;
        if (limit <= 0)
            return GlobalSpeedLimitLease.Unlimited(bytes);

        while (true)
        {
            Task signalTask;
            double waitSeconds;

            lock (SyncRoot)
            {
                limit = LimitBytesPerSecond;
                if (limit <= 0)
                    return GlobalSpeedLimitLease.Unlimited(bytes);

                Refill(limit);
                var maxInFlightBytes = GetMaxInFlightBytes(limit);
                if (_availableBytes >= bytes && _inFlightBytes + bytes <= maxInFlightBytes)
                {
                    _availableBytes -= bytes;
                    _inFlightBytes += bytes;
                    return GlobalSpeedLimitLease.Tracked(bytes, tracksInFlight: true);
                }

                var tokenWaitSeconds = _availableBytes >= bytes ? 0 : (bytes - _availableBytes) / limit;
                var inFlightWaitSeconds = _inFlightBytes + bytes <= maxInFlightBytes ? 0 : 0.005;
                waitSeconds = Math.Max(0.001, Math.Max(tokenWaitSeconds, inFlightWaitSeconds));
                signalTask = _changeSignal.Task;
            }

            await WaitForBudgetAsync(signalTask, waitSeconds, token);
        }
    }

    private static void ReleaseLease(int reservedBytes, int unusedBytes, bool tracksTokens, bool tracksInFlight)
    {
        if (reservedBytes <= 0 && unusedBytes <= 0 && !tracksInFlight)
            return;

        lock (SyncRoot)
        {
            var limit = LimitBytesPerSecond;
            if (limit > 0)
                Refill(limit);

            if (tracksInFlight)
                _inFlightBytes = Math.Max(0, _inFlightBytes - reservedBytes);

            if (tracksTokens && unusedBytes > 0 && limit > 0)
                _availableBytes = Math.Min(GetCapacityBytes(limit), _availableBytes + unusedBytes);

            PulseWaitersLocked();
        }
    }

    private static void Refill(long limit)
    {
        var now = Stopwatch.GetTimestamp();
        if (_lastRefillTimestamp == 0)
            _lastRefillTimestamp = now;

        var elapsed = (now - _lastRefillTimestamp) / (double)Stopwatch.Frequency;
        if (elapsed <= 0) return;

        _availableBytes = Math.Min(GetCapacityBytes(limit), _availableBytes + elapsed * limit);
        _lastRefillTimestamp = now;
    }

    private static int GetPreferredReadSize(long limit)
    {
        return (int)Math.Clamp(limit * ReadSliceSeconds, MinimumLimitedReadSize, MaximumLimitedReadSize);
    }

    private static int GetPreferredRequestSize(long limit)
    {
        return (int)Math.Clamp(limit * RequestSliceSeconds, MinimumLimitedRequestSize, MaximumLimitedRequestSize);
    }

    private static double GetCapacityBytes(long limit)
    {
        return Math.Max(GetPreferredRequestSize(limit), limit * CapacitySeconds);
    }

    private static long GetMaxInFlightBytes(long limit)
    {
        var preferred = Math.Clamp(limit * InFlightSeconds, MinimumInFlightBytes, MaximumInFlightBytes);
        return Math.Max(GetPreferredRequestSize(limit), (long)preferred);
    }

    private static async ValueTask WaitForBudgetAsync(Task signalTask, double waitSeconds, CancellationToken token)
    {
        var delayTask = Task.Delay(TimeSpan.FromSeconds(waitSeconds), token);
        var completed = await Task.WhenAny(signalTask, delayTask);
        if (completed == delayTask)
            await delayTask;
        else
            token.ThrowIfCancellationRequested();
    }

    private static void PulseWaitersLocked()
    {
        var signal = _changeSignal;
        _changeSignal = CreateSignal();
        signal.TrySetResult(null);
    }

    private static TaskCompletionSource<object?> CreateSignal()
    {
        return new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public sealed class GlobalSpeedLimitLease : IDisposable
    {
        private readonly bool _tracksTokens;
        private readonly bool _tracksInFlight;
        private int _usedBytes;
        private int _isDisposed;

        private GlobalSpeedLimitLease(int permittedBytes, bool tracksTokens, bool tracksInFlight)
        {
            PermittedBytes = permittedBytes;
            _tracksTokens = tracksTokens;
            _tracksInFlight = tracksInFlight;
        }

        public int PermittedBytes { get; }

        public void Consume(int bytes)
        {
            if (bytes <= 0) return;
            _usedBytes = Math.Min(PermittedBytes, _usedBytes + bytes);
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _isDisposed, 1) != 0) return;

            var usedBytes = Math.Clamp(_usedBytes, 0, PermittedBytes);
            var unusedBytes = Math.Max(0, PermittedBytes - usedBytes);
            ReleaseLease(PermittedBytes, unusedBytes, _tracksTokens, _tracksInFlight);
        }

        internal static GlobalSpeedLimitLease Unlimited(int permittedBytes)
        {
            return new GlobalSpeedLimitLease(permittedBytes, tracksTokens: false, tracksInFlight: false);
        }

        internal static GlobalSpeedLimitLease Tracked(int permittedBytes, bool tracksInFlight)
        {
            return new GlobalSpeedLimitLease(permittedBytes, tracksTokens: true, tracksInFlight);
        }
    }
}
