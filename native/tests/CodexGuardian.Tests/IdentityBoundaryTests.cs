using CodexGuardian.Core;
using CodexGuardian.Providers;
namespace CodexGuardian.Tests;

public sealed class IdentityBoundaryTests
{
    [Fact] public void PersonalEmailPartitionRequiresExplicitOptInAndSeparatesAccounts()
    {
        const string response = """{"result":{"account":{"email":"one@example.com","workspaceName":null,"planType":"prolite"}}}""";
        Assert.Null(AccountMetadataParser.Parse(response).ScopeKey);
        var optedIn = AccountMetadataParser.Parse(response, true).ScopeKey;
        Assert.NotNull(optedIn);
        Assert.DoesNotContain("one@example.com", optedIn);
        Assert.NotEqual(optedIn, AccountMetadataParser.Parse(response.Replace("one@", "two@", StringComparison.Ordinal), true).ScopeKey);
        Assert.Null(AccountMetadataParser.Parse("""{"result":{"account":{"planType":"prolite"}}}""", true).ScopeKey);
    }
    [Fact] public void ObservedWorkspaceContextPartitionsWithoutPersistingNames()
    {
        const string response = """{"result":{"account":{"email":"private@example.com","workspaceName":"Personal","planType":"pro"}}}""";
        var key = AccountMetadataParser.Parse(response).ScopeKey;
        Assert.NotNull(key);
        Assert.DoesNotContain("private",key);
        Assert.NotEqual(key,AccountMetadataParser.Parse(response.Replace("Personal","Team",StringComparison.Ordinal)).ScopeKey);
    }
    [Fact] public void EmailAndPlanDoNotProveWorkspaceIdentity()
    {
        var parsed = AccountMetadataParser.Parse("""{"result":{"account":{"type":"chatgpt","email":"private@example.com","planType":"pro"}}}""");
        Assert.Equal("pro",parsed.PlanType);
        Assert.Null(parsed.ScopeKey);
    }
    [Fact] public void ExplicitIdentityIsHashedAndPlanChangesPartitionHistory()
    {
        const string response = """{"result":{"account":{"chatgptAccountId":"workspace-secret","planType":"plus"}}}""";
        var first=AccountMetadataParser.Parse(response);
        Assert.NotNull(first.ScopeKey);
        Assert.DoesNotContain("workspace-secret",first.ScopeKey);
        Assert.NotEqual(first.ScopeKey,AccountMetadataParser.Parse(response.Replace("plus","pro",StringComparison.Ordinal)).ScopeKey);
    }
    [Fact] public void DifferentIdentityCannotInheritMissingWindow()
    {
        var now=DateTimeOffset.UtcNow;
        var old=new QuotaSnapshot(now.AddMinutes(-1),"test",new(10,300,now.AddHours(1)),null,DataFreshness.Fresh,ScopeKey:"A");
        var next=new QuotaSnapshot(now,"test",null,new(1,10080,now.AddDays(1)),DataFreshness.Fresh,ScopeKey:"B");
        var merged=CodexSessionQuotaProvider.Merge(old,next,now);
        Assert.Null(merged!.FiveHour);
        Assert.Equal("B",merged.ScopeKey);
    }
    [Fact] public void FutureObservationCannotBeFresh()
        => Assert.Equal(DataFreshness.Unavailable,QuotaSnapshot.GetFreshness(DateTimeOffset.UtcNow.AddHours(1),DateTimeOffset.UtcNow));
}
