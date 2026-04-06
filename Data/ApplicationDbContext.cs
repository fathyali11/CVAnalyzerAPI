using CVAnalyzerAPI.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace CVAnalyzerAPI.Data;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options):IdentityDbContext(options)
{
    public DbSet<CV> CVs { get; set; }=default!;
    public DbSet<Analysis> Analyses { get; set; }=default!;
    override protected void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
    }
}
