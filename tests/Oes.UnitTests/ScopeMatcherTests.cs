using Oes.Domain.Estimating;
using Oes.Infrastructure.Ai;

namespace Oes.UnitTests;

public class ScopeMatcherTests
{
    private static PriceBookEntry E(string code, string activity, string description, string unit, decimal price) =>
        new(Guid.NewGuid(), "RFG", code, activity, description, unit, price);

    // Made-up price list rows (not real price data).
    private static readonly PriceBookEntry ShingleReplace = E("300S", Activities.Replace, "Laminated - comp. shingle rfg. - w/ felt", "SQ", 285m);
    private static readonly PriceBookEntry ShingleRemove = E("300S", Activities.Remove, "Laminated - comp. shingle rfg. - w/ felt", "SQ", 62.5m);
    private static readonly PriceBookEntry Drip = E("DRIP", Activities.Replace, "Drip edge", "LF", 2.35m);
    private static readonly PriceBookEntry LongOne = E("IWS", Activities.Replace, "Ice & water barrier membrane, self-adhering, high temp", "SF", 1.9m);
    private static readonly PriceBookEntry Vent = E("RVENT", Activities.Replace, "Continuous ridge vent - shingle-over style", "LF", 9m);

    private static readonly ScopeMatcher Matcher = new([ShingleReplace, ShingleRemove, Drip, LongOne, Vent]);

    [Fact]
    public void Item_code_and_activity_match_first()
    {
        var m = Matcher.Match("RFG", "300S", Activities.Remove, "Anything at all", "SQ");
        Assert.Equal((ScopeMatchKinds.Code, ShingleRemove), (m.Kind, m.Match));
        Assert.Equal(ShingleReplace, Matcher.Match(null, "300S", Activities.None, "x", "").Match); // no activity: the first row
    }

    [Fact]
    public void Same_code_at_another_activity_is_taken_and_says_so()
    {
        var m = Matcher.Match("RFG", "DRIP", Activities.Detach, "Drip edge", "LF");
        Assert.Equal((ScopeMatchKinds.Activity, Drip), (m.Kind, m.Match));
        Assert.Equal("Matched to the Replace version of this item", m.Note);
    }

    [Fact]
    public void Wording_matches_with_activity_words_read_from_the_start()
    {
        Assert.Equal((ScopeMatchKinds.Description, ShingleRemove),
            (Matcher.Match(null, null, Activities.None, "Remove Laminated - comp. shingle rfg. - w/ felt", "SQ") is var a ? (a.Kind, a.Match) : default));
        Assert.Equal((ScopeMatchKinds.Description, Drip),
            (Matcher.Match(null, "ZZZ", Activities.None, "  *DRIP   EDGE ", "LF") is var b ? (b.Kind, b.Match) : default)); // asterisk, case, spaces
        var rr = Matcher.Match(null, null, Activities.None, "R&R Drip edge", "LF");
        Assert.Equal((ScopeMatchKinds.Activity, Drip), (rr.Kind, rr.Match));
    }

    [Fact]
    public void A_cut_off_description_matches_the_carriers_longer_one()
    {
        var m = Matcher.Match(null, null, Activities.None, "Ice & water barrier membrane, self-adhering, high temp - 2 layers", "SF");
        Assert.Equal((ScopeMatchKinds.Shortened, LongOne), (m.Kind, m.Match));
        var painted = Matcher.Match(null, null, Activities.None, "Drip edge - painted", "LF"); // short descriptions aren't treated as cut off
        Assert.Equal((null, Drip, 67), (painted.Kind, painted.Suggestion, painted.Similarity));
    }

    [Fact]
    public void Close_wording_is_taken_flagged_and_looser_wording_only_suggested()
    {
        var close = Matcher.Match(null, null, Activities.None, "Continuous ridge vent - shingle over style", "LF"); // only punctuation differs
        Assert.Equal((ScopeMatchKinds.Close, Vent, 100), (close.Kind, close.Match, close.Similarity));
        var near = Matcher.Match(null, null, Activities.None, "Continuous ridge vent shingle-over", "LF");
        Assert.Equal((ScopeMatchKinds.Close, Vent, 83), (near.Kind, near.Match, near.Similarity));
        Assert.Contains("Check it", near.Note);
        var loose = Matcher.Match(null, null, Activities.None, "Ridge vent, continuous", "LF"); // 3 of 6 words
        Assert.Equal((null, null, Vent, 50), (loose.Kind, loose.Match, loose.Suggestion, loose.Similarity));
        Assert.Equal(ScopeMatch.None, Matcher.Match(null, null, Activities.None, "Satellite dish detach", "EA"));
    }

    [Fact]
    public void A_different_unit_is_never_taken()
    {
        var m = Matcher.Match("RFG", "DRIP", Activities.Replace, "Drip edge", "SF");
        Assert.Equal((null, null, Drip), (m.Kind, m.Match, m.Suggestion));
        Assert.Equal("Your item is priced per LF; the carrier's line is in SF", m.Note);
        Assert.Equal(Drip, Matcher.Match("RFG", "DRIP", Activities.Replace, "Drip edge", "").Match); // no unit given: fine
    }
}
