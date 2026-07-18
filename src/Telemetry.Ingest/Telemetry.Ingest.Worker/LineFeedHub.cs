using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using Confluent.Kafka;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Platform.Contracts;
using Telemetry.Ingest.Domain.LineFeed;

namespace Telemetry.Ingest.Worker;

/// <summary>
/// Hub do painel ao vivo (/v1/linha/ws): o primeiro frame carrega o token
/// (browser não manda header no upgrade — ver LineFeedProtocol); o hub valida
/// assinatura, issuer/audience e papel (operador/admin) antes de transmitir
/// qualquer byte de telemetria. Token ruim = socket fechado com PolicyViolation.
/// </summary>
public sealed partial class LineFeedHub(IConfiguration config, ILogger<LineFeedHub> log)
{
    private static readonly TimeSpan CredentialTimeout = TimeSpan.FromSeconds(10);

    private readonly ConcurrentDictionary<Guid, WebSocket> _sockets = new();
    private readonly TokenValidationParameters _validation = new()
    {
        ValidIssuer = "identity",
        ValidAudience = "plataforma-linha",
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(
            config["Jwt:SigningKey"] ?? throw new InvalidOperationException(
                "Jwt:SigningKey não configurado — o host resolve a chave no boot via PlatformSecrets.JwtSigningKey."))),
        RoleClaimType = "role",
        ClockSkew = TimeSpan.FromSeconds(30),
    };

    public int ConnectedCount => _sockets.Count;

    public async Task HandleAsync(HttpContext context)
    {
        using var socket = await context.WebSockets.AcceptWebSocketAsync();
        var buffer = new byte[4 * 1024];

        using var credentialWindow = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
        credentialWindow.CancelAfter(CredentialTimeout);

        string? token = null;
        try
        {
            var first = await socket.ReceiveAsync(buffer, credentialWindow.Token);
            if (first.MessageType == WebSocketMessageType.Text)
                token = LineFeedProtocol.BearerTokenFrom(Encoding.UTF8.GetString(buffer, 0, first.Count));
        }
        catch (OperationCanceledException) { /* credencial não chegou na janela */ }
        catch (WebSocketException) { return; }

        if (!await IsAuthorizedAsync(token))
        {
            LogRejected();
            await CloseQuietlyAsync(socket, WebSocketCloseStatus.PolicyViolation, "credencial inválida");
            return;
        }

        var id = Guid.NewGuid();
        _sockets[id] = socket;
        LogConnected(_sockets.Count);
        try
        {
            // Segura a conexão até o cliente fechar; frames extras do cliente são ignorados.
            while (socket.State == WebSocketState.Open && !context.RequestAborted.IsCancellationRequested)
            {
                var frame = await socket.ReceiveAsync(buffer, context.RequestAborted);
                if (frame.MessageType == WebSocketMessageType.Close)
                    break;
            }
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException) { }
        finally
        {
            _sockets.TryRemove(id, out _);
            LogDisconnected(_sockets.Count);
        }

        await CloseQuietlyAsync(socket, WebSocketCloseStatus.NormalClosure, "fim");
    }

    /// <summary>Espelha um frame pra todos os conectados; socket morto sai da lista, nunca derruba o loop.</summary>
    public async Task BroadcastAsync(string json, CancellationToken ct)
    {
        if (_sockets.IsEmpty)
            return;

        var bytes = Encoding.UTF8.GetBytes(json);
        foreach (var (id, socket) in _sockets)
        {
            try
            {
                await socket.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, ct);
            }
            catch (Exception e) when (e is WebSocketException or ObjectDisposedException or InvalidOperationException)
            {
                _sockets.TryRemove(id, out _);
            }
        }
    }

    private async Task<bool> IsAuthorizedAsync(string? token)
    {
        if (token is null)
            return false;

        var result = await new JsonWebTokenHandler().ValidateTokenAsync(token, _validation);
        if (!result.IsValid)
            return false;

        // Mesma exigência da rota autenticada: papel de linha, não qualquer token válido.
        return result.ClaimsIdentity.FindAll("role").Any(c => c.Value is "operador" or "admin");
    }

    private static async Task CloseQuietlyAsync(WebSocket socket, WebSocketCloseStatus status, string reason)
    {
        try
        {
            if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
                await socket.CloseAsync(status, reason, CancellationToken.None);
        }
        catch (WebSocketException) { }
        catch (ObjectDisposedException) { }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Conexão do painel recusada: credencial ausente ou inválida no primeiro frame")]
    private partial void LogRejected();

    [LoggerMessage(Level = LogLevel.Information, Message = "Painel conectado ({Count} ao vivo)")]
    private partial void LogConnected(int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "Painel desconectado ({Count} ao vivo)")]
    private partial void LogDisconnected(int count);
}

/// <summary>
/// Espelha linha.telemetria.v1 pro hub. Grupo de consumo próprio e efêmero
/// (Latest, auto-commit): o feed é fire-and-forget — perder frame num restart é
/// aceitável; atrasar o quality gate do ingest, não. Por isso NÃO compartilha o
/// consumer do IngestConsumer.
/// </summary>
public sealed partial class LineFeedBroadcaster(
    LineFeedHub hub,
    IngestOptions options,
    ILogger<LineFeedBroadcaster> log) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.Run(() => PumpAsync(stoppingToken), stoppingToken);

    private async Task PumpAsync(CancellationToken ct)
    {
        using var consumer = new ConsumerBuilder<string, byte[]>(new ConsumerConfig
        {
            BootstrapServers = options.KafkaBootstrap,
            GroupId = $"linha-feed-{Environment.MachineName}-{Guid.NewGuid():N}",
            AutoOffsetReset = AutoOffsetReset.Latest,
            EnableAutoCommit = true,
        }).Build();
        consumer.Subscribe(options.TelemetryTopic);
        LogStarted(options.TelemetryTopic);

        while (!ct.IsCancellationRequested)
        {
            ConsumeResult<string, byte[]> result;
            try
            {
                result = consumer.Consume(ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ConsumeException)
            {
                continue; // feed é best-effort; o caminho durável é o IngestConsumer
            }

            SensorReadingRecord reading;
            try
            {
                reading = SensorReadingCodec.Decode(result.Message.Value);
            }
            catch (FormatException)
            {
                continue; // payload ruim já vira quarentena no caminho durável
            }

            await hub.BroadcastAsync(
                LineFeedProtocol.ReadingFrame(reading.SensorId, reading.Value, reading.MeasuredAt), ct);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Feed do painel espelhando {Topic}")]
    private partial void LogStarted(string topic);
}
