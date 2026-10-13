using CVAnalyzerAPI.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CVAnalyzerAPI.Data.EntitiesConfiguration;

public class CVConfiguration : IEntityTypeConfiguration<CV>
{
    public void Configure(EntityTypeBuilder<CV> builder)
    {
            builder.HasKey(c => c.Id);
            builder.Property(c => c.FileName).IsRequired().HasMaxLength(255);
            builder.Property(c => c.FilePath).IsRequired().HasMaxLength(500);
            builder.Property(c => c.UploadedAt).IsRequired();
            builder.Property(c => c.ShareToken).IsRequired();

            builder.HasIndex(c=> c.ShareToken).IsUnique();

            builder.HasOne(c => c.User)
                    .WithMany(u => u.CVs)
                    .HasForeignKey(c => c.UserId)
                    .OnDelete(DeleteBehavior.Cascade);
    
            builder.HasMany(c => c.Analyses)
                    .WithOne(a => a.CV)
                    .HasForeignKey(a => a.CVId)
                    .OnDelete(DeleteBehavior.Cascade);
    }
}