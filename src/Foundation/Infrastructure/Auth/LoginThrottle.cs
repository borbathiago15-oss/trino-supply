using System.Collections.Concurrent;

namespace TrinoSupply.Foundation.Infrastructure.Auth;

/// <summary>
/// Freio de força bruta por conta (SEC-001): após <see cref="Threshold"/> falhas na janela de
/// <see cref="Window"/>, o login daquela conta é recusado até a janela expirar — mesmo com a senha
/// correta. Em memória (singleton); num cluster, cada nó tem seu contador (limite efetivo = N × nós),
/// o que ainda inviabiliza força bruta. Sucesso zera o contador.
/// </summary>
public interface ILoginThrottle
{
    bool IsLocked(string key);
    void RegisterFailure(string key);
    void Reset(string key);
}

public sealed class InMemoryLoginThrottle : ILoginThrottle
{
    private const int Threshold = 5;
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(15);

    private sealed record Entry(int Failures, DateTimeOffset WindowStart);
    private readonly ConcurrentDictionary<string, Entry> _entries = new();

    public bool IsLocked(string key)
    {
        if (!_entries.TryGetValue(key, out var e)) return false;
        if (DateTimeOffset.UtcNow - e.WindowStart > Window)
        {
            _entries.TryRemove(key, out _); // janela expirou → limpa (mantém o dicionário pequeno)
            return false;
        }
        return e.Failures >= Threshold;
    }

    public void RegisterFailure(string key) =>
        _entries.AddOrUpdate(
            key,
            _ => new Entry(1, DateTimeOffset.UtcNow),
            (_, e) => DateTimeOffset.UtcNow - e.WindowStart > Window
                ? new Entry(1, DateTimeOffset.UtcNow)
                : e with { Failures = e.Failures + 1 });

    public void Reset(string key) => _entries.TryRemove(key, out _);
}
