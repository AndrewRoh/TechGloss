using Microsoft.EntityFrameworkCore;
using Npgsql;
using TechGloss.GlossaryApi.Data;
using TechGloss.GlossaryApi.Endpoints;
using TechGloss.GlossaryApi.Services;

var builder = WebApplication.CreateBuilder(args);

var connStr = builder.Configuration.GetConnectionString("Glossary")
    ?? "Host=localhost;Port=5432;Database=techgloss;Username=postgres;Password=postgres";

// NpgsqlDataSource with pgvector 타입 등록
var dataSourceBuilder = new NpgsqlDataSourceBuilder(connStr);
dataSourceBuilder.UseVector();
var dataSource = dataSourceBuilder.Build();

builder.Services.AddDbContext<GlossaryDbContext>(opt =>
    opt.UseNpgsql(dataSource, o => o.UseVector())
       .UseSnakeCaseNamingConvention());

// 임베딩 서비스 — Ollama HttpClient
builder.Services.AddHttpClient<EmbeddingService>(c =>
    c.Timeout = TimeSpan.FromSeconds(60));

// TermExtractionService
builder.Services.AddHttpClient<TermExtractionService>(c =>
    c.Timeout = TimeSpan.FromSeconds(60));

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<GlossaryDbContext>();

    if (db.Database.IsNpgsql())
    {
        // pgvector 확장 활성화 (테이블 생성 전 필수)
        await db.Database.ExecuteSqlRawAsync("CREATE EXTENSION IF NOT EXISTS vector");

        // EF Core: 테이블 생성
        db.Database.EnsureCreated();

        // name_en_normalized 컬럼 추가 (기존 DB 호환)
        await db.Database.ExecuteSqlRawAsync(@"
            ALTER TABLE glossary_category
                ADD COLUMN IF NOT EXISTS name_en_normalized TEXT NOT NULL DEFAULT ''");
        await db.Database.ExecuteSqlRawAsync(@"
            UPDATE glossary_category
               SET name_en_normalized = lower(trim(name))
             WHERE name_en_normalized = ''");
        await db.Database.ExecuteSqlRawAsync(@"
            CREATE UNIQUE INDEX IF NOT EXISTS ix_glossary_category_name_en_normalized
                ON glossary_category (name_en_normalized)");

        // HNSW 코사인 유사도 인덱스 생성 (embedding 컬럼이 있는 경우만)
        try
        {
            await db.Database.ExecuteSqlRawAsync(@"
                CREATE INDEX IF NOT EXISTS idx_entry_embedding_hnsw
                    ON glossary_entry USING hnsw (embedding vector_cosine_ops)
                    WITH (m = 16, ef_construction = 64)");
        }
        catch (Exception ex)
        {
            app.Logger.LogWarning(ex, "HNSW 인덱스 생성 실패 (embedding 컬럼 없을 경우 무시)");
        }

        await SeedCategoriesAsync(db, app.Logger);
        await SeedIfEmptyAsync(db, app.Logger);
    }
    else
    {
        // 테스트 환경 (InMemory 등)
        db.Database.EnsureCreated();
    }
}

app.MapGet("/health", () => Results.Ok(new { status = "ok", time = DateTime.UtcNow }));
app.MapLookup();
app.MapSearch();
app.MapUpsertAndPublish();
app.MapExtractTerms();

app.Run();

static async Task SeedCategoriesAsync(GlossaryDbContext db, ILogger logger)
{
    string[] names =
    [
        "General", "Cloud", "Frontend", "Backend", "Dotnet",
        "Database", "DevOps", "Security", "Network", "AI", "Mobile", "Testing"
    ];

    var existing = await db.Categories.Select(c => c.NameEnNormalized).ToHashSetAsync();
    var toAdd = names
        .Where(n => !existing.Contains(Normalize(n)))
        .Select(n => new TechGloss.Core.Models.GlossaryCategory
        {
            Id               = Guid.NewGuid(),
            Name             = n,
            NameEnNormalized = Normalize(n),
        })
        .ToList();

    if (toAdd.Count == 0) return;

    db.Categories.AddRange(toAdd);
    await db.SaveChangesAsync();
    logger.LogInformation("카테고리 시드 {Count}건 삽입: {Names}",
        toAdd.Count, string.Join(", ", toAdd.Select(c => c.Name)));
}

static async Task SeedIfEmptyAsync(GlossaryDbContext db, ILogger logger)
{
    if (await db.Entries.AnyAsync()) return;

    var seedPath = Path.Combine(AppContext.BaseDirectory, "Data", "seed.json");
    if (!File.Exists(seedPath))
    {
        logger.LogInformation("seed.json 없음 — 시드 건너뜀");
        return;
    }

    try
    {
        var json = await File.ReadAllTextAsync(seedPath);
        var entries = System.Text.Json.JsonSerializer.Deserialize<List<TechGloss.Core.Models.GlossaryEntry>>(json,
            new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (entries is { Count: > 0 })
        {
            db.Entries.AddRange(entries);
            await db.SaveChangesAsync();
            logger.LogInformation("시드 데이터 {Count}건 삽입", entries.Count);
        }
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "시드 삽입 실패 — 무시하고 계속");
    }
}

static string Normalize(string s) =>
    s.Normalize(System.Text.NormalizationForm.FormKC).Trim().ToLowerInvariant();

public partial class Program { }
