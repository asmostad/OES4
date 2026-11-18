using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Oes.Application.Security;
using Oes.Domain.Communication;
using Oes.Domain.Production;
using Oes.Domain.Work;
using Oes.Infrastructure.Crm;
using Oes.Infrastructure.Persistence;
using Oes.Infrastructure.Work;
using Oes.Web.Infrastructure;

namespace Oes.Web.Pages.Admin;

[HasPermission(Permissions.AutomationManage)]
public class AutomationModel(AutomationService automation, OesDbContext db, CrmLookups lookups, TenantTime time) : CrmPageModel
{
    public sealed record StageChoice(string Value, string Group, string Label);

    public List<AutomationRuleRow> Rules { get; private set; } = [];
    public List<AutomationRunRow> Runs { get; private set; } = [];
    public Dictionary<int, DateTime> RunLocal { get; } = [];
    public List<StageChoice> Stages { get; private set; } = [];
    public List<Option> Users { get; private set; } = [];
    public List<Sequence> Sequences { get; private set; } = [];

    public static string TriggerLabel(string trigger) => trigger switch
    {
        AutomationTriggers.OpportunityStage => "An opportunity",
        AutomationTriggers.ProjectStage => "A project",
        _ => "A trade",
    };

    public async Task OnGetAsync()
    {
        Rules = await automation.ListAsync();
        Runs = await automation.RecentRunsAsync(30);
        for (var i = 0; i < Runs.Count; i++) RunLocal[i] = await time.ToLocalAsync(Runs[i].At);
        Stages = [
            .. (await db.PipelineStages.OrderBy(s => s.Sort).ToListAsync()).Select(s => new StageChoice($"{AutomationTriggers.OpportunityStage}|{s.Id}", "Opportunity enters", s.Name)),
            .. (await db.WorkflowStages.Where(s => s.Level == WorkflowLevels.Project).OrderBy(s => s.Sort).ToListAsync()).Select(s => new StageChoice($"{AutomationTriggers.ProjectStage}|{s.Id}", "Project enters", s.Name)),
            .. (await db.WorkflowStages.Where(s => s.Level == WorkflowLevels.Trade).OrderBy(s => s.Sort).ToListAsync()).Select(s => new StageChoice($"{AutomationTriggers.TradeStage}|{s.Id}", "Trade enters", s.Name)),
        ];
        Users = await lookups.UsersAsync();
        Sequences = await db.Sequences.Where(s => s.IsActive).OrderBy(s => s.Name).ToListAsync();
    }

    public Task<IActionResult> OnPostAsync(string when, AutomationRuleInput rule) => Guard(async () =>
    {
        var parts = (when ?? "").Split('|');
        if (parts.Length != 2 || !Guid.TryParse(parts[1], out var stageId)) throw new CrmRuleException("Pick what starts the rule.");
        rule.Trigger = parts[0];
        rule.StageId = stageId;
        await automation.SaveAsync(null, rule);
        Flash("Rule added. It applies to moves from now on.");
        return RedirectToPage();
    });

    public Task<IActionResult> OnPostToggleAsync(Guid id, bool active) => Guard(async () =>
    {
        await automation.SetActiveAsync(id, active);
        return RedirectToPage();
    });

    public Task<IActionResult> OnPostDeleteAsync(Guid id) => Guard(async () =>
    {
        await automation.DeleteAsync(id);
        Flash("Rule deleted.");
        return RedirectToPage();
    });
}
