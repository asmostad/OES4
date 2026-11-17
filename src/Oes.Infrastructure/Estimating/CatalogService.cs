using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Oes.Application.Abstractions;
using Oes.Application.Security;
using Oes.Domain.Estimating;
using Oes.Infrastructure.Crm;
using Oes.Infrastructure.Persistence;

namespace Oes.Infrastructure.Estimating;

public sealed record PriceListRow(PriceList List, int ItemCount, int EstimateCount);

public sealed record PriceItemRow(Guid PriceListItemId, Guid CatalogItemId, string CategoryCode, string Code, string Description,
    string Unit, string Activity, decimal UnitPrice, decimal? MaterialPrice, decimal? LaborPrice);

public sealed class PriceListInput
{
    public string Name { get; set; } = "";
    public string? RegionCode { get; set; }
    public DateOnly EffectiveDate { get; set; } = DateOnly.FromDateTime(DateTime.UtcNow);
    public string Status { get; set; } = PriceListStatuses.Draft;
    public string? Notes { get; set; }
}

public sealed record ImportResult(int Rows, int ItemsCreated, int PricesCreated, int PricesUpdated, List<string> Errors);

public sealed class ProductInput
{
    public string Sku { get; set; } = "";
    public string Name { get; set; } = "";
    public string Unit { get; set; } = "EA";
    public Guid? TradeTypeId { get; set; }
    public decimal? DefaultCost { get; set; }
    public decimal DefaultPrice { get; set; }
    public bool IsMaterial { get; set; } = true;
    public string? Supplier { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// Catalog (what items are) and price lists (what they cost), kept separate so a tenant can keep
/// many dated lists, import new ones, and never change an existing estimate (decision 4).
/// OES4 ships no price data: tenants import or build their own (licensing, deferred).
/// </summary>
public sealed class CatalogService(OesDbContext db, RecordScope scope, IAuditLog audit)
{
    public const int MaxImportRows = 25_000;

    public async Task<List<PriceListRow>> PriceListsAsync(bool includeArchived = false, CancellationToken ct = default)
    {
        var lists = db.PriceLists.AsQueryable();
        if (!includeArchived) lists = lists.Where(l => l.Status != PriceListStatuses.Archived);
        var rows = await lists.OrderByDescending(l => l.Status == PriceListStatuses.Active).ThenByDescending(l => l.EffectiveDate)
            .Select(l => new { l, Items = db.PriceListItems.Count(i => i.PriceListId == l.Id), Estimates = db.Estimates.Count(e => e.PriceListId == l.Id) })
            .ToListAsync(ct);
        return rows.Select(r => new PriceListRow(r.l, r.Items, r.Estimates)).ToList();
    }

    public async Task<PriceList> GetListAsync(Guid id, CancellationToken ct = default) =>
        await db.PriceLists.FirstOrDefaultAsync(l => l.Id == id, ct) ?? throw new RecordNotFoundException("Price list");

    public async Task<Guid> SaveListAsync(Guid? id, PriceListInput input, CancellationToken ct = default)
    {
        await scope.EnsurePermissionAsync(Permissions.PriceListsManage);
        if (string.IsNullOrWhiteSpace(input.Name)) throw new CrmRuleException("Price list name is required.");
        if (!PriceListStatuses.All.Contains(input.Status)) throw new CrmRuleException("Unknown status.");
        var list = id is { } lid ? await GetListAsync(lid, ct) : new PriceList();
        if (id is null) db.PriceLists.Add(list);
        list.Name = input.Name.Trim();
        list.RegionCode = ContactService.Clean(input.RegionCode);
        list.EffectiveDate = input.EffectiveDate;
        list.Status = input.Status;
        list.Notes = ContactService.Clean(input.Notes);
        audit.Record(id is null ? "price_list.created" : "price_list.updated", "PriceList", list.Id, $"Price list {list.Name}");
        await db.SaveChangesAsync(ct);
        return list.Id;
    }

    /// <summary>Starts next year's list from this one: a draft copy of every price.</summary>
    public async Task<Guid> CopyListAsync(Guid sourceId, string name, DateOnly effectiveDate, CancellationToken ct = default)
    {
        await scope.EnsurePermissionAsync(Permissions.PriceListsManage);
        var source = await GetListAsync(sourceId, ct);
        var copy = new PriceList { Name = name.Trim(), RegionCode = source.RegionCode, EffectiveDate = effectiveDate, Source = "copy", Notes = $"Copied from {source.Name}" };
        db.PriceLists.Add(copy);
        var items = await db.PriceListItems.AsNoTracking().Where(i => i.PriceListId == sourceId).ToListAsync(ct);
        db.PriceListItems.AddRange(items.Select(i => new PriceListItem
        {
            PriceListId = copy.Id, CatalogItemId = i.CatalogItemId, Activity = i.Activity,
            UnitPrice = i.UnitPrice, MaterialPrice = i.MaterialPrice, LaborPrice = i.LaborPrice,
        }));
        audit.Record("price_list.copied", "PriceList", copy.Id, $"{copy.Name} copied from {source.Name}");
        await db.SaveChangesAsync(ct);
        return copy.Id;
    }

    public async Task<PagedResult<PriceItemRow>> ItemsAsync(Guid priceListId, string? q, int page = 1, int pageSize = 50, CancellationToken ct = default)
    {
        var rows = ItemQuery(priceListId, q);
        var total = await rows.CountAsync(ct);
        page = Math.Max(1, page);
        var items = await rows.OrderBy(r => r.CategoryCode).ThenBy(r => r.Code).ThenBy(r => r.Activity)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new PagedResult<PriceItemRow>(items.Select(ToRow).ToList(), total, page, pageSize);
    }

    /// <summary>Typeahead for adding price-book lines: matches code or description words.</summary>
    public async Task<List<PriceItemRow>> SearchItemsAsync(Guid priceListId, string q, int take = 25, CancellationToken ct = default) =>
        (await ItemQuery(priceListId, q).OrderBy(r => r.Code.StartsWith(q) ? 0 : 1).ThenBy(r => r.Code).Take(take).ToListAsync(ct))
        .Select(ToRow).ToList();

    // Filtering happens on this flat shape; the record is built after the query runs (EF can't filter through a constructor).
    private sealed class ItemFlat
    {
        public Guid PriceListItemId { get; init; }
        public Guid CatalogItemId { get; init; }
        public string CategoryCode { get; init; } = "";
        public string Code { get; init; } = "";
        public string Description { get; init; } = "";
        public string Unit { get; init; } = "";
        public string Activity { get; init; } = "";
        public decimal UnitPrice { get; init; }
        public decimal? MaterialPrice { get; init; }
        public decimal? LaborPrice { get; init; }
    }

    private static PriceItemRow ToRow(ItemFlat r) =>
        new(r.PriceListItemId, r.CatalogItemId, r.CategoryCode, r.Code, r.Description, r.Unit, r.Activity, r.UnitPrice, r.MaterialPrice, r.LaborPrice);

    private IQueryable<ItemFlat> ItemQuery(Guid priceListId, string? q)
    {
        var query = from p in db.PriceListItems
                    join i in db.CatalogItems on p.CatalogItemId equals i.Id
                    join c in db.CatalogCategories on i.CategoryId equals c.Id
                    where p.PriceListId == priceListId && i.IsActive
                    select new ItemFlat
                    {
                        PriceListItemId = p.Id, CatalogItemId = i.Id, CategoryCode = c.Code, Code = i.Code, Description = i.Description,
                        Unit = i.Unit, Activity = p.Activity, UnitPrice = p.UnitPrice, MaterialPrice = p.MaterialPrice, LaborPrice = p.LaborPrice,
                    };
        if (!string.IsNullOrWhiteSpace(q))
        {
            foreach (var word in q.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(5))
                query = query.Where(r => r.Code.Contains(word) || r.Description.Contains(word) || r.CategoryCode == word);
        }
        return query;
    }

    public async Task SetPriceAsync(Guid priceListItemId, decimal unitPrice, CancellationToken ct = default)
    {
        await scope.EnsurePermissionAsync(Permissions.PriceListsManage);
        if (unitPrice < 0) throw new CrmRuleException("Prices can't be negative.");
        var item = await db.PriceListItems.FirstOrDefaultAsync(i => i.Id == priceListItemId, ct) ?? throw new RecordNotFoundException("Price");
        item.UnitPrice = unitPrice;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Imports a CSV into a price list. Columns (header names, any order, case-insensitive):
    /// category, code, description, unit, activity, unit_price; optional material, labor.
    /// Catalog items are matched by category + code and created if new; prices are upserted.
    /// </summary>
    public async Task<ImportResult> ImportCsvAsync(Guid priceListId, TextReader csv, CancellationToken ct = default)
    {
        await scope.EnsurePermissionAsync(Permissions.PriceListsManage);
        var list = await GetListAsync(priceListId, ct);
        var errors = new List<string>();
        var rows = Csv.Read(csv).ToList();
        if (rows.Count < 2) throw new CrmRuleException("The file has no rows after the header.");
        if (rows.Count - 1 > MaxImportRows) throw new CrmRuleException($"The file has more than {MaxImportRows:N0} rows. Split it into smaller files.");

        var header = rows[0].Select(h => h.Trim().ToLowerInvariant().Replace(" ", "_")).ToList();
        int Col(params string[] names) => header.FindIndex(h => names.Contains(h));
        var cCat = Col("category", "cat", "category_code");
        var cCode = Col("code", "selector", "item_code", "sel");
        var cDesc = Col("description", "desc");
        var cUnit = Col("unit", "uom", "units");
        var cAct = Col("activity", "act");
        var cPrice = Col("unit_price", "price", "unitprice", "total");
        var cMat = Col("material", "material_price", "mat");
        var cLab = Col("labor", "labor_price", "lab");
        if (cCat < 0 || cCode < 0 || cDesc < 0 || cPrice < 0)
            throw new CrmRuleException("The header needs at least: category, code, description, unit_price.");

        var categories = await db.CatalogCategories.ToDictionaryAsync(c => c.Code, StringComparer.OrdinalIgnoreCase, ct);
        var items = await db.CatalogItems.ToListAsync(ct);
        var itemIndex = items.ToDictionary(i => (i.CategoryId, i.Code.ToUpperInvariant()));
        var prices = await db.PriceListItems.Where(p => p.PriceListId == priceListId).ToListAsync(ct);
        var priceIndex = prices.ToDictionary(p => (p.CatalogItemId, p.Activity));
        int created = 0, priceNew = 0, priceUpd = 0;

        for (var r = 1; r < rows.Count; r++)
        {
            var row = rows[r];
            string Get(int i) => i >= 0 && i < row.Count ? row[i].Trim() : "";
            var lineNo = r + 1;
            var catCode = Get(cCat).ToUpperInvariant();
            var code = Get(cCode).ToUpperInvariant();
            var desc = Get(cDesc);
            if (catCode.Length == 0 || code.Length == 0 || desc.Length == 0) { Err($"Line {lineNo}: category, code and description are required."); continue; }
            if (catCode.Length > 20 || code.Length > 40 || desc.Length > 500) { Err($"Line {lineNo}: a value is too long."); continue; }
            if (!TryMoney(Get(cPrice), out var price)) { Err($"Line {lineNo}: unit price \"{Get(cPrice)}\" isn't a number."); continue; }
            decimal? mat = TryMoney(Get(cMat), out var m) ? m : null;
            decimal? lab = TryMoney(Get(cLab), out var l) ? l : null;
            var activity = NormalizeActivity(Get(cAct));
            if (activity is null) { Err($"Line {lineNo}: unknown activity \"{Get(cAct)}\"."); continue; }
            var unit = Get(cUnit).ToUpperInvariant();
            if (unit.Length == 0) unit = "EA";
            if (unit.Length > 10) { Err($"Line {lineNo}: unit \"{unit}\" is too long."); continue; }

            if (!categories.TryGetValue(catCode, out var cat))
            {
                cat = new CatalogCategory { Code = catCode, Name = catCode };
                db.CatalogCategories.Add(cat);
                categories[catCode] = cat;
            }
            if (!itemIndex.TryGetValue((cat.Id, code), out var item))
            {
                item = new CatalogItem { CategoryId = cat.Id, Code = code, Description = desc, Unit = unit };
                db.CatalogItems.Add(item);
                itemIndex[(cat.Id, code)] = item;
                created++;
            }
            if (priceIndex.TryGetValue((item.Id, activity), out var existing))
            {
                existing.UnitPrice = price; existing.MaterialPrice = mat; existing.LaborPrice = lab;
                priceUpd++;
            }
            else
            {
                var p = new PriceListItem { PriceListId = priceListId, CatalogItemId = item.Id, Activity = activity, UnitPrice = price, MaterialPrice = mat, LaborPrice = lab };
                db.PriceListItems.Add(p);
                priceIndex[(item.Id, activity)] = p;
                priceNew++;
            }
        }

        list.Source = "import";
        audit.Record("price_list.imported", "PriceList", list.Id, $"{list.Name}: {priceNew} new, {priceUpd} updated prices",
            new { rows = rows.Count - 1, created, priceNew, priceUpd, errors = errors.Count });
        await db.SaveChangesAsync(ct);
        return new ImportResult(rows.Count - 1, created, priceNew, priceUpd, errors);

        void Err(string message) { if (errors.Count < 200) errors.Add(message); }
    }

    private static bool TryMoney(string s, out decimal value) =>
        decimal.TryParse(s.Replace("$", "").Replace(",", "").Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out value) && value >= 0;

    private static string? NormalizeActivity(string s) => s.Trim().ToLowerInvariant() switch
    {
        "" or "-" => Activities.None,
        "+" or "replace" or "rpl" => Activities.Replace,
        "-r" or "remove" or "rem" or "tear off" => Activities.Remove,
        "&" or "r&r" or "r_and_r" or "remove and replace" => Activities.RemoveReplace,
        "i" or "install" or "inst" => Activities.Install,
        "d&r" or "detach_reset" or "detach & reset" => Activities.Detach,
        _ => null,
    };

    // ---------------- Products (line-item method) ----------------

    public async Task<List<Product>> ProductsAsync(string? q, bool includeInactive = false, CancellationToken ct = default)
    {
        var products = db.Products.AsQueryable();
        if (!includeInactive) products = products.Where(p => p.IsActive);
        if (!string.IsNullOrWhiteSpace(q)) products = products.Where(p => p.Sku.Contains(q) || p.Name.Contains(q));
        return await products.OrderBy(p => p.Name).Take(500).ToListAsync(ct);
    }

    public async Task<Guid> SaveProductAsync(Guid? id, ProductInput input, CancellationToken ct = default)
    {
        await scope.EnsurePermissionAsync(Permissions.ProductsManage);
        if (string.IsNullOrWhiteSpace(input.Sku) || string.IsNullOrWhiteSpace(input.Name)) throw new CrmRuleException("SKU and name are required.");
        if (input.DefaultPrice < 0 || input.DefaultCost < 0) throw new CrmRuleException("Prices can't be negative.");
        var sku = input.Sku.Trim().ToUpperInvariant();
        if (await db.Products.AnyAsync(p => p.Sku == sku && p.Id != id, ct)) throw new CrmRuleException("Another product already uses that SKU.");
        var product = id is { } pid ? await db.Products.FirstOrDefaultAsync(p => p.Id == pid, ct) ?? throw new RecordNotFoundException("Product") : new Product();
        if (id is null) db.Products.Add(product);
        product.Sku = sku;
        product.Name = input.Name.Trim();
        product.Unit = string.IsNullOrWhiteSpace(input.Unit) ? "EA" : input.Unit.Trim().ToUpperInvariant();
        product.TradeTypeId = input.TradeTypeId;
        product.DefaultCost = input.DefaultCost;
        product.DefaultPrice = input.DefaultPrice;
        product.IsMaterial = input.IsMaterial;
        product.Supplier = ContactService.Clean(input.Supplier);
        product.IsActive = input.IsActive;
        await db.SaveChangesAsync(ct);
        return product.Id;
    }
}
