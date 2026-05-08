using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using TechGloss.GlossaryApi.Data;
using TechGloss.GlossaryApi.Endpoints;
using TechGloss.GlossaryApi.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<GlossaryDbContext>(opt =>
    opt.UseSqlite(builder.Configuration.GetConnectionString("Glossary")
        ?? "Data Source=glossary.db"));

// 임베딩 서비스 — Ollama HttpClient
builder.Services.AddHttpClient<EmbeddingService>(c =>
    c.Timeout = TimeSpan.FromSeconds(60));

// TermExtractionService: AddHttpClient이 Transient으로 등록 (DbContext는 파라미터로 주입)
builder.Services.AddHttpClient<TermExtractionService>(c =>
    c.Timeout = TimeSpan.FromSeconds(60));

// Phase D: Qdrant REST API 클라이언트 — Qdrant:Endpoint 설정 시 활성화
var qdrantEndpoint = builder.Configuration["Qdrant:Endpoint"];
if (!string.IsNullOrWhiteSpace(qdrantEndpoint))
{
    builder.Services.AddHttpClient<QdrantService>(c =>
    {
        c.BaseAddress = new Uri(qdrantEndpoint);
        c.Timeout = TimeSpan.FromSeconds(30);
    });
}

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<GlossaryDbContext>();

    // 001_initial.sql 실행 — FTS5 가상 테이블·트리거·UNIQUE 인덱스 생성 (IF NOT EXISTS 안전)
    var connStr = builder.Configuration.GetConnectionString("Glossary") ?? "Data Source=glossary.db";
    await ApplyMigrationSqlAsync(connStr, app.Logger);

    // EF Core: 기본 테이블 보장 (위 SQL과 중복이지만 EF 매핑 테이블 검증용)
    db.Database.EnsureCreated();

    // Phase D: Qdrant 컬렉션 초기화 (등록된 경우에만)
    var qdrantSvc = scope.ServiceProvider.GetService<QdrantService>();
    if (qdrantSvc is not null)
    {
        try { await qdrantSvc.EnsureCollectionAsync(); }
        catch (Exception ex)
        {
            app.Logger.LogWarning(ex, "Qdrant 연결 실패 — MVP 모드(SQL LIKE)로 폴백");
        }
    }
}

app.MapGet("/health", () => Results.Ok(new { status = "ok", time = DateTime.UtcNow }));
app.MapLookup();
app.MapSearch();
app.MapUpsertAndPublish();
app.MapExtractTerms();

app.Run();

// 001_initial.sql을 SQLite 연결로 직접 실행 — EF가 모르는 FTS5/트리거/추가 인덱스 생성
static async Task ApplyMigrationSqlAsync(string connStr, ILogger logger)
{
    var sqlPath = Path.Combine(AppContext.BaseDirectory, "Data", "Migrations", "001_initial.sql");
    if (!File.Exists(sqlPath))
    {
        logger.LogWarning("마이그레이션 파일 없음: {Path}", sqlPath);
        return;
    }

    var sql = await File.ReadAllTextAsync(sqlPath);
    // 세미콜론 구분 실행 (CREATE TABLE/INDEX/TRIGGER/VIRTUAL TABLE 각각)
    var statements = sql
        .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Where(s => !string.IsNullOrWhiteSpace(s));

    await using var conn = new SqliteConnection(connStr);
    await conn.OpenAsync();
    foreach (var stmt in statements)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = stmt;
        try { await cmd.ExecuteNonQueryAsync(); }
        catch (Exception ex) when (ex.Message.Contains("already exists"))
        { /* IF NOT EXISTS로 처리했지만 일부 SQLite 버전 예외 허용 */ }
    }
}

public partial class Program { }
