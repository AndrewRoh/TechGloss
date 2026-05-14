using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Pgvector;
using TechGloss.Core.Models;

namespace TechGloss.GlossaryApi.Data;

public sealed class GlossaryDbContext : DbContext
{
    public GlossaryDbContext(DbContextOptions<GlossaryDbContext> options) : base(options) { }

    public DbSet<GlossaryEntry> Entries => Set<GlossaryEntry>();
    public DbSet<GlossaryCategory> Categories => Set<GlossaryCategory>();

    protected override void OnModelCreating(ModelBuilder m)
    {
        // Npgsql 전용 — InMemory 프로바이더에서는 harmless annotation으로 무시됨
        m.HasPostgresExtension("vector");

        var isNpgsql = Database.ProviderName?.Contains("Npgsql") == true;

        m.Entity<GlossaryEntry>(e =>
        {
            e.ToTable("glossary_entry");
            e.HasKey(x => x.Id);
            e.Property(x => x.Status).HasDefaultValue("draft");

            if (isNpgsql)
            {
                // UseVector()로 등록된 Npgsql 타입 매핑이 Vector → vector(768) 저장을 처리
                var comparer = new ValueComparer<float[]?>(
                    (a, b) => a == null ? b == null : b != null && a.SequenceEqual(b),
                    v => v == null ? 0 : v.Aggregate(0, HashCode.Combine),
                    v => v == null ? null : v.ToArray());

                e.Property(x => x.Embedding)
                    .HasColumnType("vector(768)")
                    .IsRequired(false)
                    .HasConversion(
                        v => v == null ? null : new Vector(v),
                        v => v == null ? null : v.ToArray())
                    .Metadata.SetValueComparer(comparer);
            }
            else
            {
                // InMemory(테스트): Vector 타입 미지원 → 컬럼 제외
                e.Ignore(x => x.Embedding);
            }

            e.HasIndex(x => new { x.TermEnNormalized, x.CategoryId }).IsUnique();
            e.HasIndex(x => new { x.TermKoNormalized, x.CategoryId }).IsUnique();
            e.HasIndex(x => new { x.Status, x.CategoryId });
        });

        m.Entity<GlossaryCategory>(c =>
        {
            c.ToTable("glossary_category");
            c.HasKey(x => x.Id);
            c.HasIndex(x => x.NameEnNormalized).IsUnique();
        });
    }
}
