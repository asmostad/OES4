using Oes.Domain.Crm;

namespace Oes.UnitTests;

public class AddressKeyTests
{
    [Theory]
    [InlineData("123 North Main Street", null, "68502")]
    [InlineData("123 n. main st", null, "68502-1234")]
    [InlineData("  123  N MAIN   ST ", null, "68502")]
    public void Variants_of_the_same_address_get_the_same_key(string line1, string? line2, string zip) =>
        Assert.Equal("123 N MAIN ST|68502", AddressKey.For(line1, line2, "Lincoln", "NE", zip));

    [Theory]
    [InlineData("4410 Oak Ridge Dr", "Apt 4")]
    [InlineData("4410 Oak Ridge Drive", "#4")]
    [InlineData("4410 oak ridge dr", "unit 4")]
    [InlineData("4410 Oak Ridge Dr", "Apt #4")]
    public void Unit_designators_are_equivalent(string line1, string line2) =>
        Assert.Equal("4410 OAK RDG DR #4|68516", AddressKey.For(line1, line2, "Lincoln", "NE", "68516"));

    [Fact]
    public void Different_units_are_different_properties() =>
        Assert.NotEqual(AddressKey.For("88 Pine Ct", "Apt 1", "Omaha", "NE", "68104"), AddressKey.For("88 Pine Ct", "Apt 2", "Omaha", "NE", "68104"));

    [Fact]
    public void Falls_back_to_city_and_state_without_a_zip() =>
        Assert.Equal("10 ELM ST|OMAHA|NE", AddressKey.For("10 Elm Street", null, "Omaha", "ne", ""));
}

public class ContactValueTests
{
    [Theory]
    [InlineData("(402) 555-0134", "+14025550134")]
    [InlineData("402.555.0134", "+14025550134")]
    [InlineData("1-402-555-0134", "+14025550134")]
    [InlineData("+44 20 7946 0958", "+442079460958")]
    public void Phones_normalize_to_e164(string input, string expected) =>
        Assert.Equal(expected, ContactValue.Normalize(ContactMethodKinds.Mobile, input));

    [Fact]
    public void Emails_are_lower_cased_and_trimmed() =>
        Assert.Equal("jane@example.com", ContactValue.Normalize(ContactMethodKinds.Email, "  Jane@Example.COM "));

    [Fact]
    public void Us_numbers_format_for_display() =>
        Assert.Equal("(402) 555-0134", ContactValue.FormatPhone("+14025550134", "x"));

    [Fact]
    public void Display_name_follows_type()
    {
        var person = new Contact { FirstName = "Jane", LastName = "Smith" };
        person.RefreshDisplayName();
        var org = new Contact { Type = ContactTypes.Organization, OrganizationName = " Acme PM " };
        org.RefreshDisplayName();
        Assert.Equal("Jane Smith", person.DisplayName);
        Assert.Equal("Acme PM", org.DisplayName);
    }
}
