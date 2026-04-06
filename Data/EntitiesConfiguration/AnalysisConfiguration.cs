using CVAnalyzerAPI.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CVAnalyzerAPI.Data.EntitiesConfiguration;

public class AnalysisConfiguration : IEntityTypeConfiguration<Analysis>
{
    public void Configure(EntityTypeBuilder<Analysis> builder)
    {
        builder.ToTable(t =>
        t.HasCheckConstraint("CK_Analysis_Score", "[Score] > 0 AND [Score] <= 100"));

    }
}
