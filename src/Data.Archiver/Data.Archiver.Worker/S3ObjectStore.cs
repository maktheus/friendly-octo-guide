using System.IO.Compression;
using System.Text;
using Amazon.S3;
using Amazon.S3.Model;

namespace Data.Archiver.Worker;

/// <summary>
/// Escrita no data lake (MinIO ou qualquer S3): JSONL comprimido com gzip.
/// PutObject é atômico no S3 — ou o objeto inteiro aparece, ou nada aparece;
/// não existe objeto meio-escrito pra engine analítica tropeçar.
/// </summary>
public sealed class S3ObjectStore(IAmazonS3 s3, string bucket)
{
    public async Task EnsureBucketAsync(CancellationToken ct)
    {
        var buckets = await s3.ListBucketsAsync(ct);
        if (buckets.Buckets?.Any(b => b.BucketName == bucket) != true)
            await s3.PutBucketAsync(bucket, ct);
    }

    /// <summary>
    /// Comprime e grava o lote; devolve os bytes escritos (pra métrica). Com
    /// <paramref name="wormRetention"/> &gt; 0 aplica object lock (WORM): o objeto
    /// não pode ser sobrescrito nem apagado até a data de retenção — imutabilidade
    /// exigida pela governança do Big Data Pool. Exige bucket com Object Lock ligado.
    /// </summary>
    public async Task<long> PutGzipJsonLinesAsync(
        string key, IReadOnlyList<string> lines, TimeSpan wormRetention, CancellationToken ct)
    {
        using var payload = new MemoryStream();
        using (var gzip = new GZipStream(payload, CompressionLevel.Fastest, leaveOpen: true))
        using (var writer = new StreamWriter(gzip, Encoding.UTF8))
        {
            foreach (var line in lines)
                await writer.WriteLineAsync(line.AsMemory(), ct);
        }

        payload.Position = 0;
        var writtenBytes = payload.Length; // ANTES do Put: o SDK descarta o InputStream (AutoCloseStream)
        var request = new PutObjectRequest
        {
            BucketName = bucket,
            Key = key,
            InputStream = payload,
            ContentType = "application/gzip",
        };
        if (wormRetention > TimeSpan.Zero)
        {
            request.ObjectLockMode = ObjectLockMode.Compliance; // nem o admin apaga antes da data
            request.ObjectLockRetainUntilDate = DateTime.UtcNow.Add(wormRetention);
        }
        await s3.PutObjectAsync(request, ct);
        return writtenBytes;
    }
}
