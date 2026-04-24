using CVAnalyzerAPI.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System.Text.Json;

namespace CVAnalyzerAPI.Data.EntitiesConfiguration;

public class AnalysisConfiguration : IEntityTypeConfiguration<Analysis>
{
    public void Configure(EntityTypeBuilder<Analysis> builder)
    {
        builder.ToTable(t =>
        t.HasCheckConstraint("CK_Analysis_Score", "\"Score\" > 0 AND \"Score\" <= 100"));

        builder.Property(a => a.Strengths)
        .HasConversion(
            v => JsonSerializer.Serialize(v, (JsonSerializerOptions)null!),
            v => JsonSerializer.Deserialize<List<AnalysisStrength>>(v, (JsonSerializerOptions)null!) ?? new List<AnalysisStrength>()
        );
        builder.Property(a => a.Weaknesses)
        .HasConversion(
            v => JsonSerializer.Serialize(v, (JsonSerializerOptions)null!),
            v => JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions)null!) ?? new List<string>()
        );
        builder.Property(a => a.Suggestions)
        .HasConversion(
            v => JsonSerializer.Serialize(v, (JsonSerializerOptions)null!),
            v => JsonSerializer.Deserialize<List<AnalysisSuggestion>>(v, (JsonSerializerOptions)null!) ?? new List<AnalysisSuggestion>()
        );
    }
}
