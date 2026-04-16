using Microsoft.EntityFrameworkCore;
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

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<GlossaryDbContext>();
    db.Database.EnsureCreated();
}

app.MapGet("/health", () => Results.Ok(new { status = "ok", time = DateTime.UtcNow }));
app.MapLookup();
app.MapSearch();
app.MapUpsertAndPublish();
app.MapExtractTerms();

app.Run();

public partial class Program { }
