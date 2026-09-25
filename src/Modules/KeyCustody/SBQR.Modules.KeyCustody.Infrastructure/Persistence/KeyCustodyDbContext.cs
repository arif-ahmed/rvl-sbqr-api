using Microsoft.EntityFrameworkCore;
using SBQR.Modules.KeyCustody.Domain.Aggregates;

namespace SBQR.Modules.KeyCustody.Infrastructure.Persistence;

/// <summary>
/// EF Core context over the <c>crypto_keys</c> table. KeyCustody owns both
/// the read side (key resolution at signing time, the cross-module
/// <c>GetSigningKeyQuery</c>) and the full write-side lifecycle (generate,
/// adopt, rotate, suspend, reinstate, retire). Schema is owned by
/// db/migrations/*.sql — this context NEVER calls Database.Migrate()
/// (PERSISTENCE_DECISIONS.md §3).
/// </summary>
public sealed class KeyCustodyDbContext : DbContext
{
    public KeyCustodyDbContext(DbContextOptions<KeyCustodyDbContext> options)
        : base(options)
    {
    }

    public DbSet<CryptoKey> CryptoKeys => Set<CryptoKey>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(KeyCustodyDbContext).Assembly);
    }
}
