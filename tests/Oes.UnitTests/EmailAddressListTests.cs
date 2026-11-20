using Oes.Infrastructure.Communication;
using Oes.Infrastructure.Crm;

namespace Oes.UnitTests;

public class EmailAddressListTests
{
    [Fact]
    public void Addresses_are_split_checked_lowercased_and_deduplicated()
    {
        Assert.Equal(["a@example.com", "b@example.org"], MessagingService.Addresses(" A@Example.com; b@example.org,a@example.com ", "CC"));
        Assert.Empty(MessagingService.Addresses(null, "CC"));
        Assert.Empty(MessagingService.Addresses(" ; , ", "CC"));
        Assert.Contains("(BCC)", Assert.Throws<CrmRuleException>(() => MessagingService.Addresses("Bob <bob@example.com>", "BCC")).Message); // bare addresses only
        Assert.Throws<CrmRuleException>(() => MessagingService.Addresses("bob@localhost", "CC"));
        Assert.Throws<CrmRuleException>(() => MessagingService.Addresses(string.Join(",", Enumerable.Range(1, 11).Select(i => $"p{i}@example.com")), "CC"));
    }
}
