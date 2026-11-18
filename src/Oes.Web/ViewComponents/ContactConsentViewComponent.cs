using Microsoft.AspNetCore.Mvc;
using Oes.Infrastructure.Communication;

namespace Oes.Web.ViewComponents;

public sealed record ConsentModel(Guid ContactId, List<ConsentState> States, List<SuppressionRow> Suppressions);

/// <summary>Messaging consent and suppressions for a contact.</summary>
public sealed class ContactConsentViewComponent(ConsentService consent) : ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync(Guid contactId) =>
        View("/Pages/Shared/_ContactConsent.cshtml",
            new ConsentModel(contactId, await consent.CurrentAsync(contactId), await consent.SuppressionsForContactAsync(contactId)));
}
