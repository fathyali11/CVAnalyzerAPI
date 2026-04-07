using CVAnalyzerAPI.Consts;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CVAnalyzerAPI.Data.EntitiesConfiguration;

internal class RolesConfiguration : IEntityTypeConfiguration<IdentityRole>
{
    public void Configure(EntityTypeBuilder<IdentityRole> builder)
    {
        List<IdentityRole> roles = [
            new IdentityRole
            {
                Id="37428717-385c-4bf6-bb31-dcbe09bf6625",
                Name=UserRoles.Admin,
                NormalizedName=UserRoles.Admin.ToUpper()  ,
                ConcurrencyStamp="3781e88b-83b5-401c-8786-a37eb7cd4e67"
            },
            new IdentityRole
            {
                Id="b6366753-89c0-426a-8679-21b8f209b463",
                Name=UserRoles.User,
                NormalizedName=UserRoles.User.ToUpper() ,
                ConcurrencyStamp="7a798498-92ae-4d76-b15a-a5935fbe5261"
            }
            ];

        builder.HasData(roles);

        
    }
}
