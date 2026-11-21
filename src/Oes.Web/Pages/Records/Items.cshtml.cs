using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;
using Oes.Infrastructure.Crm;
using Oes.Infrastructure.Files;
using Oes.Web.Infrastructure;

namespace Oes.Web.Pages.Records;

/// <summary>Shared handlers for the timeline partial: notes, uploads and downloads on any record.</summary>
[RequestSizeLimit(60 * 1024 * 1024)]
public class ItemsModel(RecordItems items) : CrmPageModel
{
    private static readonly FileExtensionContentTypeProvider Types = new();

    public IActionResult OnGet() => NotFound();

    public Task<IActionResult> OnPostNoteAsync(string entityType, Guid entityId, string body, string? returnUrl) =>
        Run(returnUrl, async () => { await items.AddNoteAsync(entityType, entityId, body); Flash("Note added."); });

    public Task<IActionResult> OnPostPinAsync(Guid id, string? returnUrl) =>
        Run(returnUrl, () => items.TogglePinAsync(id));

    public Task<IActionResult> OnPostDeleteNoteAsync(Guid id, string? returnUrl) =>
        Run(returnUrl, async () => { await items.DeleteNoteAsync(id); Flash("Note deleted."); });

    public Task<IActionResult> OnPostArchiveDocumentAsync(Guid id, string? returnUrl) =>
        Run(returnUrl, async () => { await items.ArchiveDocumentAsync(id); Flash("File removed."); });

    public Task<IActionResult> OnPostUploadAsync(string entityType, Guid entityId, IFormFile? file, string? categoryKey, string? returnUrl) =>
        Run(returnUrl, async () =>
        {
            if (file is null || file.Length == 0) throw new CrmRuleException("Choose a file to upload.");
            // The content type is derived from the extension, never trusted from the browser.
            var contentType = Types.TryGetContentType(file.FileName, out var t) ? t : "application/octet-stream";
            await using var stream = file.OpenReadStream();
            try
            {
                await items.UploadAsync(entityType, entityId, stream, file.FileName, contentType, string.IsNullOrWhiteSpace(categoryKey) ? null : categoryKey);
            }
            catch (FileRejectedException ex) { throw new CrmRuleException(ex.Message); }
            Flash($"Uploaded {file.FileName}.");
        });

    public async Task<IActionResult> OnGetDownloadAsync(Guid id)
    {
        try
        {
            var (doc, content) = await items.OpenDocumentAsync(id);
            // Images and PDFs open in the browser; everything else downloads. nosniff is set globally.
            var inline = doc.ContentType.StartsWith("image/") || doc.ContentType == "application/pdf";
            return inline ? File(content, doc.ContentType) : File(content, doc.ContentType, doc.FileName);
        }
        catch (RecordNotFoundException) { return NotFound(); }
    }

    private async Task<IActionResult> Run(string? returnUrl, Func<Task> action)
    {
        try { await action(); }
        catch (RecordNotFoundException) { return NotFound(); }
        catch (Exception ex) when (ex is CrmRuleException or RecordForbiddenException) { Flash(ex.Message, "danger"); }
        return BackTo(returnUrl, "/");
    }
}
