using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CVAnalyzerAPI.Data.EntitiesConfiguration;

internal class RefreshTokenConfiguration : IEntityTypeConfiguration<Models.RefreshToken>
{
    public void Configure(EntityTypeBuilder<Models.RefreshToken> builder)
    {
        builder.Property(rt => rt.Token)
            .IsRequired()
            .HasMaxLength(500);
        builder.HasOne(rt => rt.User)
            .WithMany(u => u.RefreshTokens)
            .HasForeignKey(rt => rt.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
