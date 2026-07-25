namespace Gateway.Domain;

/// <summary>
/// Rate limiting por token bucket: capacidade define o burst, taxa define o regime.
/// Determinístico — o relógio entra por parâmetro. O host mantém um bucket por
/// usuário/IP; na fase 1 o estado migra pro Valkey pra valer entre réplicas.
/// </summary>
public sealed class TokenBucket
{
    private readonly object _lock = new();
    private readonly double _capacity;
    private readonly double _refillPerSecond;
    private double _tokens;
    private DateTimeOffset _lastRefill = DateTimeOffset.MinValue;

    public TokenBucket(double capacity, double refillPerSecond)
    {
        _capacity = capacity;
        _refillPerSecond = refillPerSecond;
        _tokens = capacity;
    }

    public bool TryTake(DateTimeOffset now)
    {
        lock (_lock)
        {
            if (_lastRefill == DateTimeOffset.MinValue)
                _lastRefill = now;

            var elapsed = (now - _lastRefill).TotalSeconds;
            if (elapsed > 0)
            {
                _tokens = Math.Min(_capacity, _tokens + elapsed * _refillPerSecond);
                _lastRefill = now;
            }

            if (_tokens < 1)
                return false;

            _tokens -= 1;
            return true;
        }
    }
}
