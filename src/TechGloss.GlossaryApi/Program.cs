using Microsoft.EntityFrameworkCore;
using Qdrant.Client;
using TechGloss.GlossaryApi.Data;
using TechGloss.GlossaryApi.Endpoints;
using TechGloss.GlossaryApi.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<GlossaryDbContext>(opt =>
    opt.UseSqlite(builder.Configuration.GetConnectionString("Glossary")
        ?? "Data Source=glossary.db"));

// 임베딩 및 용어 추출 서비스 — Ollama HttpClient 공유
builder.Services.AddHttpClient<EmbeddingService>();
builder.Services.AddHttpClient<TermExtractionService>();

// Phase D: TechGloss:Qdrant:Host 설정 시 Qdrant 활성화 — 미설정 시 SQL LIKE fallback
var qdrantHost = builder.Configuration["TechGloss:Qdrant:Host"];
if (!string.IsNullOrWhiteSpace(qdrantHost))
{
    var qdrantPort = builder.Configuration.GetValue<int>("TechGloss:Qdrant:GrpcPort", 6334);
    builder.Services.AddSingleton(new QdrantClient(qdrantHost, qdrantPort));
    builder.Services.AddSingleton<QdrantService>();
}

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<GlossaryDbContext>();
    db.Database.EnsureCreated();

    // Phase D: Qdrant 컬렉션 초기화 (등록된 경우에만)
    var qdrantSvc = scope.ServiceProvider.GetService<QdrantService>();
    if (qdrantSvc is not null)
        await qdrantSvc.EnsureCollectionAsync();
}

app.MapGet("/health", () => Results.Ok(new { status = "ok", time = DateTime.UtcNow }));
app.MapLookup();
app.MapSearch();
app.MapUpsertAndPublish();
app.MapExtractTerms();

app.Run();

public partial class Program { }
