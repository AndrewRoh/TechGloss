using Microsoft.EntityFrameworkCore;
using TechGloss.Core.Models;

namespace TechGloss.GlossaryApi.Data;

public sealed class GlossaryDbContext : DbContext
{
    public GlossaryDbContext(DbContextOptions<GlossaryDbContext> options) : base(options) { }

    public DbSet<GlossaryEntry> Entries => Set<GlossaryEntry>();
    public DbSet<GlossaryCategory> Categories => Set<GlossaryCategory>();

    protected override void OnModelCreating(ModelBuilder m)
    {
        m.Entity<GlossaryEntry>(e =>
        {
            e.ToTable("glossary_entry");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasConversion<string>();
            e.Property(x => x.CategoryId).HasConversion<string?>();
            e.Property(x => x.Status).HasDefaultValue("draft");
        });

        m.Entity<GlossaryCategory>(c =>
        {
            c.ToTable("glossary_category");
            c.HasKey(x => x.Id);
            c.Property(x => x.Id).HasConversion<string>();
        });
    }
}
