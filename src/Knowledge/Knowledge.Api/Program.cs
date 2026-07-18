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

var connectionString = builder.Configuration.GetConnectionString("Postgres")
    ?? "Host=localhost;Username=plataforma;Password=plataforma;Database=knowledge";
builder.Services.AddSingleton(new KnowledgeStore(connectionString, dimensions));

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

app.Run();
