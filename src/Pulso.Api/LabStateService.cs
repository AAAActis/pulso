using System.Collections.Concurrent;

namespace Pulso.Api;

public sealed class LabStateService
{
    private readonly ConcurrentQueue<int> _latencyRingBuffer = new();
    private readonly object _bufferLock = new();
    private const int RingBufferSize = 30;

    private long _cacheHits;
    private long _cacheMisses;
    public long CacheHits => _cacheHits;
    public long CacheMisses => _cacheMisses;
    public bool StaleWhileRevalidateActive { get; private set; }

    private DateTimeOffset? _simulateFailureUntil;
    private readonly object _simulationLock = new();

    public DateTimeOffset? SimulateFailureUntil => _simulateFailureUntil;
    public bool IsCircuitBreakerOpen { get; private set; }

    public void RecordLatency(int latencyMs)
    {
        lock (_bufferLock)
        {
            _latencyRingBuffer.Enqueue(latencyMs);
            while (_latencyRingBuffer.Count > RingBufferSize)
            {
                _latencyRingBuffer.TryDequeue(out _);
            }
        }
    }

    public (int P50, int P95) GetLatencyPercentiles()
    {
        lock (_bufferLock)
        {
            if (_latencyRingBuffer.IsEmpty)
                return (12, 45);

            var array = _latencyRingBuffer.ToArray();
            Array.Sort(array);

            var p50Index = array.Length / 2;
            var p95Index = (int)Math.Ceiling(array.Length * 0.95) - 1;

            return (array[p50Index], array[Math.Max(0, p95Index)]);
        }
    }

    public void IncrementCacheHits() => Interlocked.Increment(ref _cacheHits);
    public void IncrementCacheMisses() => Interlocked.Increment(ref _cacheMisses);

    public double HitRatioPercent
    {
        get
        {
            var hits = _cacheHits;
            var misses = _cacheMisses;
            var total = hits + misses;
            return total == 0 ? 0.0 : Math.Round((double)hits / total * 100, 1);
        }
    }

    public void SetStaleWhileRevalidate(bool active) => StaleWhileRevalidateActive = active;

    public SimulateFailureResponse StartFailureSimulation(int durationSeconds = 45)
    {
        lock (_simulationLock)
        {
            var until = DateTimeOffset.UtcNow.AddSeconds(durationSeconds);
            _simulateFailureUntil = until;
            IsCircuitBreakerOpen = true;
            return new SimulateFailureResponse(
                Success: true,
                Message: $"Simulación de caída activada por {durationSeconds}s. Circuit Breaker: OPEN.",
                DurationSeconds: durationSeconds
            );
        }
    }

    public bool IsFailureSimulated()
    {
        lock (_simulationLock)
        {
            if (_simulateFailureUntil.HasValue && DateTimeOffset.UtcNow >= _simulateFailureUntil.Value)
            {
                _simulateFailureUntil = null;
                IsCircuitBreakerOpen = false;
                StaleWhileRevalidateActive = false;
                return false;
            }
            return _simulateFailureUntil.HasValue;
        }
    }

    public void ResetSimulation()
    {
        lock (_simulationLock)
        {
            _simulateFailureUntil = null;
            IsCircuitBreakerOpen = false;
            StaleWhileRevalidateActive = false;
        }
    }
}