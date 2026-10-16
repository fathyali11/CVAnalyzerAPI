using CVAnalyzerAPI.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace CVAnalyzerAPI.Data;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<CV> CVs { get; set; }=default!;
    public DbSet<Analysis> Analyses { get; set; }=default!;
    public DbSet<RefreshToken> RefreshTokens { get; set; } =default!;
    override protected void OnModelCreating(ModelBuilder builder)
    {
        builder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
        base.OnModelCreating(builder);

        // Identity ships a unique index for NormalizedUserName but a *non-unique* one for
        // NormalizedEmail, so two concurrent registrations (or addresses differing only by
        // case) could both be inserted. This closes that race at the database level.
        builder.Entity<ApplicationUser>()
            .HasIndex(u => u.NormalizedEmail)
            .IsUnique();
    }
}
