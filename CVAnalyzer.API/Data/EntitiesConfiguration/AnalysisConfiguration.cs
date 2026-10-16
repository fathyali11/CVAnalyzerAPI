using CVAnalyzerAPI.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
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
        )
        .Metadata.SetValueComparer(ListComparer<AnalysisStrength>(e => e with { }));

        builder.Property(a => a.Weaknesses)
        .HasConversion(
            v => JsonSerializer.Serialize(v, (JsonSerializerOptions)null!),
            v => JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions)null!) ?? new List<string>()
        )
        .Metadata.SetValueComparer(ListComparer<string>(s => s));

        builder.Property(a => a.Suggestions)
        .HasConversion(
            v => JsonSerializer.Serialize(v, (JsonSerializerOptions)null!),
            v => JsonSerializer.Deserialize<List<AnalysisSuggestion>>(v, (JsonSerializerOptions)null!) ?? new List<AnalysisSuggestion>()
        )
        .Metadata.SetValueComparer(ListComparer<AnalysisSuggestion>(e => e with { }));
    }

    private static ValueComparer<List<T>> ListComparer<T>(Func<T, T> cloneElement) where T : class => new(
        (current, original) => current == null
            ? original == null
            : original != null && current.SequenceEqual(original),
        list => list.Aggregate(0, (hash, item) => HashCode.Combine(hash, item == null ? 0 : item.GetHashCode())),
        list => list.Select(item => item == null ? item! : cloneElement(item)).ToList());

}


