using System.Data.Common;
using Dapper;
using Mes.Connector.Domain;
using Microsoft.Data.SqlClient;
using Npgsql;

namespace Mes.Connector.Worker;

/// <summary>
/// Adapter SQL do MES real (épico #6): executa consulta SQL parametrizada contra visões/tabelas
/// de bancos de dados industriais (SQL Server, PostgreSQL, Oracle/ODBC) para ler o lote
/// de eventos a partir do cursor informado.
/// Falha de conexão propaga exceção: o loop do worker loga e tenta no próximo poll — nunca
/// avança o cursor sem ter lido.
/// </summary>
public sealed class SqlMesAdapter : IMesAdapter
{
    public const string DefaultQuery = """
        SELECT 
            cursor AS Cursor,
            ativo_id AS AtivoId,
            tipo AS Tipo,
            codigo AS Codigo,
            quantidade AS Quantidade,
            texto AS Texto,
            turno AS Turno,
            occurred_at AS OccurredAt
        FROM mes_events_view
        WHERE (@cursor IS NULL OR cursor > @cursor)
        ORDER BY cursor ASC
        LIMIT 500
        """;

    private readonly Func<DbConnection> _connectionFactory;
    private readonly string _query;

    public SqlMesAdapter(Func<DbConnection> connectionFactory, string? query = null)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _query = !string.IsNullOrWhiteSpace(query) ? query : DefaultQuery;
    }

    public SqlMesAdapter(string connectionString, string? providerName = null, string? query = null)
        : this(CreateFactory(connectionString, providerName), query)
    {
    }

    public async Task<IReadOnlyList<RawMesRow>> PollAsync(string? cursor, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory();
        await connection.OpenAsync(cancellationToken);

        var param = new { cursor };
        var commandDefinition = new CommandDefinition(_query, param, cancellationToken: cancellationToken);

        var result = await connection.QueryAsync<RawMesRowDto>(commandDefinition);

        return result.Select(dto => new RawMesRow(
            Cursor: dto.Cursor ?? string.Empty,
            AtivoId: dto.AtivoId ?? string.Empty,
            Tipo: dto.Tipo ?? string.Empty,
            Codigo: dto.Codigo ?? string.Empty,
            Quantidade: dto.Quantidade,
            Texto: dto.Texto,
            Turno: dto.Turno,
            OccurredAt: dto.OccurredAt
        )).ToList();
    }

    private static Func<DbConnection> CreateFactory(string connectionString, string? providerName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        if (string.Equals(providerName, "sqlserver", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(providerName, "mssql", StringComparison.OrdinalIgnoreCase))
        {
            return () => new SqlConnection(connectionString);
        }

        // Padrão PostgreSQL (Npgsql)
        return () => new NpgsqlConnection(connectionString);
    }

    private sealed class RawMesRowDto
    {
        public string? Cursor { get; set; }
        public string? AtivoId { get; set; }
        public string? Tipo { get; set; }
        public string? Codigo { get; set; }
        public string? Quantidade { get; set; }
        public string? Texto { get; set; }
        public string? Turno { get; set; }
        public string? OccurredAt { get; set; }
    }
}
