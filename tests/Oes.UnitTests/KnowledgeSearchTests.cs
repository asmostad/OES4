using Oes.Infrastructure.Ai;

namespace Oes.UnitTests;

public class KnowledgeSearchTests
{
    [Fact]
    public void Articles_split_at_headings_and_pack_paragraphs()
    {
        var body = "Intro line about us.\n\n# Workmanship warranty\n\nTen years on labor.\n\nTransferable once.\n\nShingle warranty:\nThe maker's warranty applies.\n\n## Claims\n\nCall the office.";
        var p = KnowledgeSearch.Split(body);
        Assert.Equal([(null, "Intro line about us."), ("Workmanship warranty", "Ten years on labor.\n\nTransferable once."),
            ("Shingle warranty", "The maker's warranty applies."), ("Claims", "Call the office.")], p.Select(x => (x.Heading, x.Text)));
        Assert.Equal([0, 1, 2, 3], p.Select(x => x.Seq));
    }

    [Fact]
    public void Long_text_is_cut_near_the_target_size_at_sentence_ends()
    {
        var sentence = "This sentence is about forty-five characters. ";
        var body = string.Concat(Enumerable.Repeat(sentence, 80)) + "\n\nShort tail.";
        var p = KnowledgeSearch.Split(body);
        Assert.True(p.Count >= 3);
        Assert.All(p, x => Assert.True(x.Text.Length <= KnowledgeSearch.TargetChars));
        Assert.All(p.SkipLast(1), x => Assert.EndsWith(".", x.Text));
        Assert.EndsWith("Short tail.", p[^1].Text);
        Assert.Single(KnowledgeSearch.Split(new string('x', 3000)), x => x.Text.Length < KnowledgeSearch.TargetChars); // no sentence ends: hard cut
    }

    [Fact]
    public void Query_words_drop_common_words_and_plurals()
    {
        Assert.Equal(["long", "workmanship", "warrant"], KnowledgeSearch.QueryWords("How long is our workmanship warranty?"));
        Assert.Equal(["warrant", "shingle", "gutter"], KnowledgeSearch.QueryWords("warranties for shingles and gutters"));
        Assert.Equal(["glass"], KnowledgeSearch.QueryWords("glass")); // not "glas"
        Assert.Equal(8, KnowledgeSearch.QueryWords("one two three four five six seven eight nine ten").Count);
    }

    [Fact]
    public void Rare_words_and_title_words_rank_higher_and_one_article_cant_fill_the_results()
    {
        KnowledgeCandidate C(Guid article, string title, string text) => new(Guid.NewGuid(), article, title, KnowledgeSearch.SearchTextFor(title, null, text));
        var warranty = Guid.NewGuid();
        var gutters = Guid.NewGuid();
        var other = Guid.NewGuid();
        var candidates = new[]
        {
            C(warranty, "Workmanship warranty", "We cover labor for ten years."),
            C(warranty, "Workmanship warranty", "Labor claims go to the office."),
            C(warranty, "Workmanship warranty", "Labor, labor, labor."),
            C(gutters, "Gutters", "Seamless gutters, labor included."),
            C(other, "Office hours", "Open weekdays."),
        };
        var ranked = KnowledgeSearch.Rank(KnowledgeSearch.QueryWords("warranty on labor"), candidates, 5);
        Assert.Equal(3, ranked.Count); // two from the warranty article at most, plus gutters; office hours shares nothing
        Assert.Equal(warranty, ranked[0].Candidate.ArticleId);
        Assert.Equal(2, ranked.Count(r => r.Candidate.ArticleId == warranty));
        Assert.Equal(gutters, ranked[^1].Candidate.ArticleId); // labor only, no title match
        Assert.Empty(KnowledgeSearch.Rank([], candidates, 5));
    }
}
