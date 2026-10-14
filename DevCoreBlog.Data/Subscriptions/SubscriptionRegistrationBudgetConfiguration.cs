using DevCoreBlog.Core.Subscriptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DevCoreBlog.Data.Subscriptions;

/// <summary>A single locked row bounds public verification admissions independently of process memory.</summary>
public sealed class SubscriptionRegistrationBudgetConfiguration : IEntityTypeConfiguration<SubscriptionRegistrationBudget>
{
    public void Configure(EntityTypeBuilder<SubscriptionRegistrationBudget> entity)
    {
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).ValueGeneratedNever();
        entity.ToTable("SubscriptionRegistrationBudgets", table => table.HasCheckConstraint("CK_SubscriptionRegistrationBudgets_Valid", """
            "Id"=1 AND "Count" BETWEEN 0 AND 20
            AND isfinite("WindowStartUtc") AND isfinite("UpdatedUtc")
            AND "WindowStartUtc" = date_trunc('day', "WindowStartUtc" AT TIME ZONE 'UTC') AT TIME ZONE 'UTC'
            AND "UpdatedUtc" >= "WindowStartUtc" AND "UpdatedUtc" < "WindowStartUtc" + interval '1 day'
            """));
    }
}
