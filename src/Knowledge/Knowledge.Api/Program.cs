using Knowledge.Api;
using Knowledge.Domain.Embeddings;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Platform.ServiceDefaults;

// Base de conhecimento não-relacional (JSONB + pgvector) exposta por GraphQL
// (HotChocolate): busca semântica pro RAG do Chatbot e pra consulta direta do
// front, com visibilidade por papel aplicada dentro da query.

var instrumentation = new ServiceInstrumentation("knowledge");

var builder = WebApplication.CreateBuilder(args);
builder.AddPlatformDefaults(instrumentation);

if (await PlatformSecrets.TryGetAsync(builder.Configuration, "platform/jwt", "signingKey") is { } jwtKey)
    builder.Configuration["Jwt:SigningKey"] = jwtKey;

// Defesa em profundidade: o Gateway já validou, mas este serviço revalida o JWT.
// Mesmo contrato dual do Gateway: Keycloak (JWKS) quando configurado, senão chave simétrica.
var keycloakBaseUrl = builder.Configuration["Keycloak:BaseUrl"];
var keycloakRealm = builder.Configuration["Keycloak:Realm"] ?? "plataforma-linha";
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        if (!string.IsNullOrEmpty(keycloakBaseUrl))
        {
            o.Authority = $"{keycloakBaseUrl}/realms/{keycloakRealm}";
            o.RequireHttpsMetadata = false;
            o.TokenValidationParameters = new TokenValidationParameters { ValidAudience = "plataforma-linha" };
        }
        else
        {
            o.TokenValidationParameters = new TokenValidationParameters
            {
                ValidIssuer = "identity",
                ValidAudience = "plataforma-linha",
                IssuerSigningKey = new SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(
                    PlatformSecrets.JwtSigningKey(builder.Configuration, builder.Environment))),
            };
        }
    });
builder.Services.AddAuthorization();

var dimensions = builder.Configuration.GetValue("Embeddings:Dimensions", 384);
var embeddingsBaseUrl = builder.Configuration["Embeddings:BaseUrl"];
if (!string.IsNullOrEmpty(embeddingsBaseUrl))
{
    builder.Services.AddHttpClient<IEmbedder, HttpEmbedder>(c =>
    {
        c.BaseAddress = new Uri(embeddingsBaseUrl);
        c.Timeout = TimeSpan.FromSeconds(30);
    }).AddTypedClient<IEmbedder>((http, sp) => new HttpEmbedder(
        http, builder.Configuration["Embeddings:Model"] ?? "nomic-embed-text", dimensions));
}
else
{
    // Sem modelo configurado o pipeline continua inteiro com o embedder local.
    builder.Services.AddSingleton<IEmbedder>(new HashingEmbedder(dimensions));
}

// Mesmo default dos demais serviços (compose: db linha, dev/dev) — o default antigo
// (plataforma/knowledge) não existia em lugar nenhum e quebrava o boot local.
var connectionString = builder.Configuration.GetConnectionString("Postgres")
    ?? "Host=localhost;Database=linha;Username=dev;Password=dev";
builder.Services.AddSingleton(new KnowledgeStore(connectionString, dimensions));
builder.Services.AddSingleton(new CausaRaizStore(connectionString, dimensions));

// A exigência de auth fica no endpoint (RequireAuthorization no MapGraphQL) —
// cobre o schema inteiro sem precisar do pacote de autorização por campo.
builder.Services
    .AddGraphQLServer()
    .AddQueryType<Query>()
    .AddMutationType<Mutation>();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));
app.MapGraphQL("/v1/knowledge/graphql").RequireAuthorization();

await app.Services.GetRequiredService<KnowledgeStore>().EnsureSchemaAsync(CancellationToken.None);

// Épico #7: a base de causa raiz nasce semeada com os motivos de parada do OEE
// (config Ishikawa:Sintomas, formato "MOTIVO|texto do sintoma"; defaults SMT/SMD).
// Seed é hipótese (confiança 0.3) e NUNCA sobrescreve registro curado — idempotente.
var causaRaizStore = app.Services.GetRequiredService<CausaRaizStore>();
await causaRaizStore.EnsureSchemaAsync(CancellationToken.None);

var seedAtivo = app.Configuration["Ishikawa:AtivoId"] ?? "linha-2";
var sintomasConfig = app.Configuration.GetSection("Ishikawa:Sintomas").Get<string[]>() ??
[
    "OEE-STENCIL|Desgaste de estêncil na impressora de pasta",
    "OEE-NOZZLE|Entupimento de nozzle no pick-and-place",
    "OEE-PASTA|Pasta de solda fora da janela de uso",
    "OEE-SPI|Reprovação em série na medição do SPI",
    "OEE-AOI|Falso-positivo recorrente no AOI",
    "OEE-UMID|Umidade fora da faixa na sala SMT",
    "OEE-SETUP|Setup de receita errado na troca de produto",
    "OEE-TREINO|Operador sem treinamento no posto do turno",
];

var seedEmbedder = app.Services.GetRequiredService<IEmbedder>();
var seed = Knowledge.Domain.Ishikawa.CausaRaizSeed.FromSintomas(
    sintomasConfig.Select(s => s.Split('|', 2) is [var motivo, var texto]
        ? (Sintoma: texto, MotivoCodigo: (string?)motivo)
        : (Sintoma: s, MotivoCodigo: null)),
    seedAtivo, DateTimeOffset.UtcNow, Guid.NewGuid);
foreach (var causa in seed)
{
    var embedding = await seedEmbedder.EmbedAsync(causa.Sintoma, CancellationToken.None);
    await causaRaizStore.UpsertAsync(causa, embedding, curada: false, CancellationToken.None);
}

app.Run();
