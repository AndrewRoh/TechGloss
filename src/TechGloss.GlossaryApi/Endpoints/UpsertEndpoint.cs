using Microsoft.EntityFrameworkCore;
using TechGloss.Core.Models;
using TechGloss.GlossaryApi.Data;

namespace TechGloss.GlossaryApi.Endpoints;

public static class UpsertEndpoint
{
    public static void MapUpsertAndPublish(this WebApplication app)
    {
        app.MapPost("/glossary/upsert", async (
            GlossaryEntry entry, GlossaryDbContext db, CancellationToken ct) =>
        {
            entry.TermKoNormalized = entry.TermKo.Trim().Normalize(
                System.Text.NormalizationForm.FormKC).ToLowerInvariant();
            entry.TermEnNormalized = entry.TermEn.Trim().ToLowerInvariant();
            entry.UpdatedAt = DateTimeOffset.UtcNow;

            var exists = await db.Entries.AnyAsync(e => e.Id == entry.Id, ct);
            if (!exists)
            {
                entry.CreatedAt = entry.UpdatedAt;
                entry.Status = "draft";
                db.Entries.Add(entry);
            }
            else
            {
                db.Entries.Update(entry);
            }
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { entry.Id });
        });

        app.MapPost("/glossary/publish", async (
            PublishRequest req, GlossaryDbContext db, CancellationToken ct) =>
        {
            var entry = await db.Entries.FindAsync(new object[] { req.EntryId }, ct);
            if (entry is null) return Results.NotFound();
            entry.Status = "published";
            entry.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            return Results.Ok();
        });
    }
}

public record PublishRequest(Guid EntryId);
