using System.Text;
using Microsoft.EntityFrameworkCore;
using Oes.Domain.Crm;
using Oes.Infrastructure.Ai;
using Oes.Infrastructure.Crm;
using Oes.Infrastructure.Identity;
using Oes.Infrastructure.Measurements;
using Oes.Infrastructure.Persistence;

namespace Oes.Integration.Tests;

[Collection(DbCollection.Name)]
public sealed class MeasurementExtractionTests(MySqlFixture fx)
{
    private sealed record World(Guid Tenant, AppUser Admin, Guid Property, Guid Structure, Guid Roofing);

    private async Task<World> WorldAsync()
    {
        var (t, admin) = await fx.CreateTenantAsync();
        await using var s = fx.ScopeAs(t.Id, admin);
        var property = await s.Get<PropertyService>().CreateAsync(new PropertyInput { Line1 = "4 Report Rd", City = "Lincoln", State = "NE", PostalCode = "68502" });
        var structure = (await s.Get<PropertyService>().GetAsync(property)).Structures.Single().Id;
        var roofing = (await s.Get<MeasurementService>().TradesAsync()).Single(x => x.Code == "roofing").Id;
        return new World(t.Id, admin, property, structure, roofing);
    }

    private static MemoryStream Pdf() => new(Encoding.ASCII.GetBytes("%PDF-1.4\n% fake EagleView report\n"));

    [Fact]
    public async Task The_ai_reading_only_prefills_and_the_saved_version_keeps_the_report_and_answer()
    {
        var w = await WorldAsync();
        fx.Ai.Reset();
        fx.Ai.NextExtraction = ("""
            {"source":"eagleview","measured_on":"2026-10-02","notes":"The report also covers a detached garage; I used the main house.",
             "total_area":2550.4,"eaves":120,"ridges":-5,"hips":null,"bogus":7}
            """, "end_turn");
        MeasurementExtraction x;
        await using (var s = fx.ScopeAs(w.Tenant, w.Admin))
        {
            x = await s.Get<MeasurementExtractor>().ExtractAsync(w.Property, w.Structure, w.Roofing, Pdf(), "eagleview.pdf");
            // Nothing saved yet but the report.
            Assert.False(await s.Get<OesDbContext>().MeasurementSets.AnyAsync(m => m.PropertyId == w.Property));
            Assert.True(await s.Get<OesDbContext>().Documents.AnyAsync(d => d.Id == x.DocumentId && d.EntityId == w.Property && d.CategoryKey == "measurement_report"));
        }
        Assert.Equal(new Dictionary<string, decimal> { ["|total_area"] = 2550.4m, ["|eaves"] = 120m }, x.Values); // negative and unknown dropped
        Assert.Equal(("eagleview", new DateOnly(2026, 10, 2)), (x.Source, x.MeasuredOn));
        Assert.Contains("detached garage", x.Notes);

        var request = fx.Ai.Extractions.Single();
        Assert.StartsWith("%PDF", Encoding.ASCII.GetString(request.Pdf));
        Assert.True(request.JsonSchema.GetProperty("properties").TryGetProperty("total_area", out _));
        Assert.False(request.JsonSchema.GetProperty("additionalProperties").GetBoolean());
        Assert.Contains("Total roof area (sq ft)", request.Instructions);

        await using (var s = fx.ScopeAs(w.Tenant, w.Admin))
        {
            Assert.Equal(0.09m, (await s.Get<OesDbContext>().AiUsageDaily.SingleAsync(u => u.UserId == w.Admin.Id)).CostUsd); // counts toward the budget

            // A person checks, fixes one number and saves.
            var input = new MeasurementInput
            {
                Source = x.Source!, MeasuredOn = x.MeasuredOn, ReportDocumentId = x.DocumentId, ExtractionJson = x.RawJson,
                Values = new() { ["|total_area"] = 2551m, ["|eaves"] = 120m },
            };
            var setId = await s.Get<MeasurementService>().SaveVersionAsync(w.Property, w.Structure, w.Roofing, input);
            var set = await s.Get<OesDbContext>().MeasurementSets.Include(m => m.Values).SingleAsync(m => m.Id == setId);
            Assert.Equal((x.DocumentId, "eagleview"), (set.SourceDocumentId!.Value, set.Source));
            Assert.Contains("detached garage", set.ExtractionJson);
            Assert.Equal(2551m, set.Values.Single(v => v.FieldKey == "total_area").Value);
        }
    }

    [Fact]
    public async Task Refusals_bad_files_and_foreign_reports_are_handled()
    {
        var w = await WorldAsync();
        var other = await WorldAsync();
        await using var s = fx.ScopeAs(w.Tenant, w.Admin);
        var extractor = s.Get<MeasurementExtractor>();

        fx.Ai.Reset();
        fx.Ai.NextExtraction = (null, "refusal");
        var refused = await Assert.ThrowsAsync<CrmRuleException>(() => extractor.ExtractAsync(w.Property, w.Structure, w.Roofing, Pdf(), "report.pdf"));
        Assert.Contains("by hand", refused.Message);
        Assert.True(await s.Get<OesDbContext>().Documents.AnyAsync(d => d.EntityId == w.Property && d.FileName == "report.pdf")); // kept anyway

        fx.Ai.Reset();
        await Assert.ThrowsAsync<CrmRuleException>(() => extractor.ExtractAsync(w.Property, w.Structure, w.Roofing, new MemoryStream(Encoding.ASCII.GetBytes("not a pdf")), "x.pdf"));
        await Assert.ThrowsAsync<CrmRuleException>(() => extractor.ExtractAsync(w.Property, w.Structure, w.Roofing, Pdf(), "photo.jpg"));
        Assert.Empty(fx.Ai.Extractions); // never sent

        fx.Ai.IsConfigured = false;
        Assert.Contains("isn't connected", (await Assert.ThrowsAsync<CrmRuleException>(() => extractor.ExtractAsync(w.Property, w.Structure, w.Roofing, Pdf(), "r.pdf"))).Message);
        fx.Ai.IsConfigured = true;

        // A report stored on a different property can't be attached to this one.
        Guid foreignDoc;
        await using (var o = fx.ScopeAs(other.Tenant, other.Admin))
            foreignDoc = await o.Get<RecordItems>().UploadAsync(EntityTypes.Property, other.Property, Pdf(), "theirs.pdf", "application/pdf", "measurement_report");
        await Assert.ThrowsAsync<CrmRuleException>(() => s.Get<MeasurementService>().SaveVersionAsync(w.Property, w.Structure, w.Roofing,
            new MeasurementInput { ReportDocumentId = foreignDoc, Values = new() { ["|total_area"] = 100m } }));
    }
}
