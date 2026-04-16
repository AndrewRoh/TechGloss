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
    await SeedIfEmpty(db);
}

app.MapGet("/health", () => Results.Ok(new { status = "ok", time = DateTime.UtcNow }));
app.MapLookup();
app.MapSearch();
app.MapUpsertAndPublish();
app.MapExtractTerms();

app.Run();

static async Task SeedIfEmpty(GlossaryDbContext db)
{
    if (await db.Entries.AnyAsync()) return;
    var seedPath = Path.Combine(AppContext.BaseDirectory, "Data", "seed.json");
    if (!File.Exists(seedPath)) return;
    var json = await File.ReadAllTextAsync(seedPath);
    var entries = System.Text.Json.JsonSerializer.Deserialize<List<TechGloss.Core.Models.GlossaryEntry>>(json,
        new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    if (entries is { Count: > 0 })
    {
        db.Entries.AddRange(entries);
        await db.SaveChangesAsync();
    }
}

public partial class Program { }
