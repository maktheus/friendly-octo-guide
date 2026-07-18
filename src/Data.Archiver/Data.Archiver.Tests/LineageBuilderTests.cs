using Data.Archiver.Domain.Lineage;
using Xunit;

namespace Data.Archiver.Tests;

public class LineageBuilderTests
{
    private static readonly DateTimeOffset T = new(2026, 7, 13, 4, 0, 0, TimeSpan.Zero);
    private static readonly Guid Run = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly System.Text.Json.JsonSerializerOptions WebJson =
        new(System.Text.Json.JsonSerializerDefaults.Web);

    private static OpenLineageEvent Build() => LineageBuilder.ForArchivedObject(
        sourceTopic: "linha.telemetria.v1", partition: 2, firstOffset: 12345,
        bucket: "linha-lake", objectKey: "linha.telemetria.v1/dt=2026-07-13/hour=04/part-002-000000012345.jsonl.gz",
        recordCount: 5000, schemaVersion: "sensor-reading.v1", producerService: "telemetry-ingest",
        runId: Run, eventTime: T);

    [Fact]
    public void Evento_liga_input_kafka_a_output_s3()
    {
        var e = Build();

        Assert.Equal("COMPLETE", e.EventType);
        Assert.Equal("data-archiver", e.Job.Name);
        var input = Assert.Single(e.Inputs);
        Assert.Equal("kafka://linha.telemetria.v1", input.Namespace);
        Assert.Contains("offset=12345", input.Name, StringComparison.Ordinal);
        var output = Assert.Single(e.Outputs);
        Assert.Equal("s3://linha-lake", output.Namespace);
    }

    [Fact]
    public void Output_carrega_schema_produtor_e_contagem()
    {
        var output = Build().Outputs[0];
        Assert.Equal("sensor-reading.v1", output.Facets.Schema!.Version);
        Assert.Equal("telemetry-ingest", output.Facets.DataSource!.Name);
        Assert.Equal("s3://linha-lake", output.Facets.DataSource.Uri); // Marquez faz URI.parse sem null-check
        Assert.Equal(5000, output.Facets.RecordCount!.Rows);
    }

    [Fact]
    public void Todo_facet_carrega_producer_e_schemaURL_do_spec()
    {
        // Sem _producer/_schemaURL (BaseFacet), o Marquez recusa o RunEvent com 422 —
        // validado contra a API real. O nome serializado precisa ser EXATO.
        var json = System.Text.Json.JsonSerializer.Serialize(Build(), WebJson);

        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var facets = doc.RootElement.GetProperty("outputs")[0].GetProperty("facets");
        foreach (var facetName in (string[])["schema", "dataSource", "recordCount"])
        {
            var facet = facets.GetProperty(facetName);
            Assert.False(string.IsNullOrEmpty(facet.GetProperty("_producer").GetString()));
            Assert.False(string.IsNullOrEmpty(facet.GetProperty("_schemaURL").GetString()));
        }
    }

    [Fact]
    public void Evento_e_deterministico_dado_run_e_tempo()
    {
        // O que importa é o payload emitido — serializa igual pros mesmos inputs.
        var a = System.Text.Json.JsonSerializer.Serialize(Build());
        var b = System.Text.Json.JsonSerializer.Serialize(Build());
        Assert.Equal(a, b);
    }
}
