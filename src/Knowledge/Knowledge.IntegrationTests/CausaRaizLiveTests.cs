using Knowledge.Api;
using Knowledge.Domain.Embeddings;
using Knowledge.Domain.Ishikawa;
using Xunit;

namespace Knowledge.IntegrationTests;

/// <summary>
/// Integração REAL da base de causa raiz (épico #7) contra Postgres + pgvector:
/// schema, seed idempotente, curadoria vencendo hipótese e busca semântica por
/// sintoma. Mesmo gate dos outros testes live: só roda com KNOWLEDGE_PG.
/// </summary>
public sealed class CausaRaizLiveTests
{
    private static string? Conn => Environment.GetEnvironmentVariable("KNOWLEDGE_PG");
    private const int Dims = 64;
    private static readonly HashingEmbedder Embedder = new(Dims);

    private static async Task<CausaRaizStore> FreshStoreAsync()
    {
        var store = new CausaRaizStore(Conn!, Dims);
        await store.EnsureSchemaAsync(CancellationToken.None);
        await using var conn = new Npgsql.NpgsqlConnection(Conn);
        await conn.OpenAsync();
        await using var cmd = new Npgsql.NpgsqlCommand("TRUNCATE causa_raiz;", conn);
        await cmd.ExecuteNonQueryAsync();
        return store;
    }

    private static RootCause Causa(string sintoma, string causa, double confianca) => new(
        Guid.NewGuid(), "linha-2", IshikawaClassifier.Classify(sintoma),
        sintoma, causa, MotivoCodigo: null, confianca,
        DateTimeOffset.FromUnixTimeSeconds(1_700_000_000));

    private static async Task<Guid> UpsertAsync(CausaRaizStore store, RootCause causa, bool curada) =>
        await store.UpsertAsync(causa, await Embedder.EmbedAsync(causa.Sintoma), curada, CancellationToken.None);

    [SkippableFact]
    public async Task Seed_e_idempotente_e_nao_rebaixa_registro_curado()
    {
        Skip.If(Conn is null, "KNOWLEDGE_PG não definida — pulando teste de integração.");
        var store = await FreshStoreAsync();

        // Operação curou a causa (workshop de Ishikawa): confiança alta.
        var curada = Causa("Desgaste de estêncil na impressora", "Estêncil além da vida útil de ciclos", 0.9);
        var idCurada = await UpsertAsync(store, curada, curada: true);

        // Re-seed do boot chega DEPOIS com o mesmo sintoma como hipótese fraca.
        var hipotese = Causa("Desgaste de estêncil na impressora", "(hipótese) a investigar", 0.3);
        var idAposSeed = await UpsertAsync(store, hipotese, curada: false);

        Assert.Equal(idCurada, idAposSeed); // mesmo registro, não duplicou
        var causas = await store.QueryAsync("linha-2", categoria: null, limit: 10, CancellationToken.None);
        var unica = Assert.Single(causas);
        Assert.Equal(0.9, unica.Confianca); // o seed não rebaixou a curadoria
        Assert.Contains("vida útil", unica.Causa, StringComparison.Ordinal);
    }

    [SkippableFact]
    public async Task Consulta_estruturada_filtra_por_categoria_e_ordena_por_confianca()
    {
        Skip.If(Conn is null, "KNOWLEDGE_PG não definida — pulando teste de integração.");
        var store = await FreshStoreAsync();

        await UpsertAsync(store, Causa("Entupimento de nozzle", "Manutenção vencida", 0.7), curada: true);
        await UpsertAsync(store, Causa("Desgaste de squeegee", "Pressão excessiva", 0.5), curada: true);
        await UpsertAsync(store, Causa("Umidade alta na sala", "Climatização", 0.8), curada: true);

        var maquina = await store.QueryAsync("linha-2", IshikawaCategory.Maquina, 10, CancellationToken.None);

        Assert.Equal(2, maquina.Count); // nozzle + squeegee; umidade é MeioAmbiente
        Assert.Equal(0.7, maquina[0].Confianca); // mais confiável primeiro
    }

    [SkippableFact]
    public async Task Sintoma_novo_encontra_causa_de_sintoma_parecido_por_semantica()
    {
        Skip.If(Conn is null, "KNOWLEDGE_PG não definida — pulando teste de integração.");
        var store = await FreshStoreAsync();

        await UpsertAsync(store, Causa("Entupimento de nozzle no pick-and-place", "Manutenção vencida", 0.7), curada: true);
        await UpsertAsync(store, Causa("Operador sem treinamento no turno", "Onboarding incompleto", 0.6), curada: true);

        var query = await Embedder.EmbedAsync("nozzle entupido no pick and place da linha");
        var hits = await store.SearchAsync(query, ativoId: null, limit: 2, CancellationToken.None);

        Assert.NotEmpty(hits);
        Assert.Contains("nozzle", hits[0].Causa.Sintoma, StringComparison.OrdinalIgnoreCase);
        Assert.True(hits[0].Score > hits[^1].Score || hits.Count == 1);
    }
}
