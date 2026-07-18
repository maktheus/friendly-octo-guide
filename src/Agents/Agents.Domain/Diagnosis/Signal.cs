namespace Agents.Domain.Diagnosis;

public enum SignalKind { Alert, Trace, Telemetry, Log, MesEvento }

public enum Severity { Info, Warning, Error, Critical }

/// <summary>
/// Um sinal observado pela espinha de observabilidade, normalizado pro agente
/// correlacionar: alerta, span de trace, leitura de telemetria ou log. `Resource`
/// é a chave de correlação (ex.: "linha-2", "prensa-3") — é por ela que sinais
/// dispersos viram um incidente só.
/// </summary>
public sealed record Signal(
    SignalKind Kind,
    string Resource,
    Severity Severity,
    DateTimeOffset At,
    string Message,
    string? TraceId = null);
