namespace Data.Archiver.Domain.Lineage;

/// <summary>
/// Evento OpenLineage de um objeto arquivado no data lake — a linhagem que torna o
/// histórico auditável: de qual tópico/partição veio (input), pra qual objeto S3 foi
/// (output), com que versão de schema e por qual serviço. Modelo no formato do spec
/// OpenLineage (RunEvent) pra o Marquez consumir sem tradução.
/// </summary>
public sealed record OpenLineageEvent(
    string EventType,
    string EventTime,
    RunNode Run,
    JobNode Job,
    IReadOnlyList<Dataset> Inputs,
    IReadOnlyList<Dataset> Outputs,
    string Producer);

public sealed record RunNode(string RunId);
public sealed record JobNode(string Namespace, string Name);
public sealed record Dataset(string Namespace, string Name, DatasetFacets Facets);
public sealed record DatasetFacets(SchemaFacet? Schema = null, DataSourceFacet? DataSource = null, CountFacet? RecordCount = null);

/// <summary>
/// Todo facet OpenLineage EXIGE _producer e _schemaURL (BaseFacet do spec) — sem
/// eles o Marquez recusa o RunEvent inteiro com 422. Descoberto validando de
/// verdade contra a API; não é opcional.
/// </summary>
public abstract record FacetBase
{
    [System.Text.Json.Serialization.JsonPropertyName("_producer")]
    public string FacetProducer { get; init; } = LineageBuilder.ProducerUri;

    [System.Text.Json.Serialization.JsonPropertyName("_schemaURL")]
    public string FacetSchemaUrl { get; init; } = LineageBuilder.BaseFacetSchemaUrl;
}

public sealed record SchemaFacet(string Version) : FacetBase;

/// <summary>
/// DataSourceDatasetFacet do spec: name + uri. O Marquez faz URI.parse(uri) sem
/// null-check — sem o campo, o POST inteiro morre com 500 (NPE no OpenLineageDao).
/// </summary>
public sealed record DataSourceFacet(string Name, string Uri) : FacetBase;
public sealed record CountFacet(long Rows) : FacetBase;

/// <summary>
/// Constrói o evento de linhagem de um objeto. Puro e determinístico (tempo e runId
/// entram por parâmetro): mesmo objeto arquivado → mesma linhagem.
/// </summary>
public static class LineageBuilder
{
    public const string Namespace = "plataforma-linha";
    public const string JobName = "data-archiver";
    public const string ProducerUri = "https://github.com/maktheus/friendly-octo-guide/tree/main/src/Data.Archiver";
    public const string BaseFacetSchemaUrl = "https://openlineage.io/spec/2-0-2/OpenLineage.json#/$defs/BaseFacet";

    public static OpenLineageEvent ForArchivedObject(
        string sourceTopic, int partition, long firstOffset,
        string bucket, string objectKey,
        long recordCount, string schemaVersion, string producerService,
        Guid runId, DateTimeOffset eventTime)
    {
        var input = new Dataset(
            Namespace: $"kafka://{sourceTopic}",
            Name: $"{sourceTopic}/partition={partition}/offset={firstOffset}",
            Facets: new DatasetFacets(DataSource: new DataSourceFacet(producerService, $"kafka://{sourceTopic}")));

        var output = new Dataset(
            Namespace: $"s3://{bucket}",
            Name: objectKey,
            Facets: new DatasetFacets(
                Schema: new SchemaFacet(schemaVersion),
                DataSource: new DataSourceFacet(producerService, $"s3://{bucket}"),
                RecordCount: new CountFacet(recordCount)));

        return new OpenLineageEvent(
            EventType: "COMPLETE",
            EventTime: eventTime.ToUniversalTime().ToString("O"),
            Run: new RunNode(runId.ToString()),
            Job: new JobNode(Namespace, JobName),
            Inputs: [input],
            Outputs: [output],
            Producer: ProducerUri);
    }
}
