using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using MotorShop.AdminPanel.Models;
using Smartstore.Core;
using Smartstore.Core.Catalog.Categories;
using Smartstore.Core.Content.Media;
using Smartstore.Core.Data;
using Smartstore.Core.Logging;
using Smartstore.Core.Seo;

namespace MotorShop.AdminPanel.Services;

/// <summary>
/// Category screens. Reads the categories directly (not the cached storefront tree) so the admin sees
/// unpublished categories and its own changes at once; writes go through the tracked context and
/// <see cref="ICategoryService"/>, whose hooks refresh the storefront tree.
/// </summary>
public class CategoryEditService
{
    public const long MaxImageBytes = 5 * 1024 * 1024;
    static readonly string[] ImageExtensions = [".jpg", ".jpeg", ".png", ".webp", ".gif"];

    private readonly SmartDbContext _db;
    private readonly ICommonServices _services;
    private readonly ICategoryService _categoryService;
    private readonly IMediaService _mediaService;
    private readonly IUrlService _urlService;

    public CategoryEditService(
        SmartDbContext db,
        ICommonServices services,
        ICategoryService categoryService,
        IMediaService mediaService,
        IUrlService urlService)
    {
        _db = db;
        _services = services;
        _categoryService = categoryService;
        _mediaService = mediaService;
        _urlService = urlService;
    }

    public Localizer T { get; set; } = NullLocalizer.Instance;

    private string R(string key, params object[] args)
        => T("Plugins.MotorShop.AdminPanel." + key, args);

    record Node(int Id, int? ParentId, string Name, bool Published, int DisplayOrder, int? MediaFileId);

    private async Task<List<Node>> LoadNodesAsync()
    {
        return await _db.Categories
            .AsNoTracking()
            .Where(x => !x.Deleted)
            .Select(x => new Node(x.Id, x.ParentId, x.Name, x.Published, x.DisplayOrder, x.MediaFileId))
            .ToListAsync();
    }

    public async Task<CategoryTreeModel> GetTreeAsync(string q)
    {
        var nodes = await LoadNodesAsync();
        var children = nodes.ToLookup(x => x.ParentId ?? 0);
        var counts = await _db.ProductCategories
            .AsNoTracking()
            .Where(x => !x.Product.Deleted && !x.Product.IsSystemProduct)
            .GroupBy(x => x.CategoryId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count);

        q = q?.Trim();
        var model = new CategoryTreeModel { Q = q, TotalCategories = nodes.Count };

        // Matches plus their ancestors, so a hit is shown in its place in the tree.
        HashSet<int> visible = null;
        HashSet<int> matches = null;
        if (q.HasValue())
        {
            var byId = nodes.ToDictionary(x => x.Id);
            matches = [.. nodes.Where(x => x.Name.Contains(q, StringComparison.CurrentCultureIgnoreCase)).Select(x => x.Id)];
            visible = [];
            foreach (var id in matches)
            {
                for (int? current = id; current is int c && byId.TryGetValue(c, out var n) && visible.Add(c); current = n.ParentId)
                {
                }
            }
        }

        var files = (await _mediaService.GetFilesByIdsAsync([.. nodes.Where(x => x.MediaFileId > 0).Select(x => x.MediaFileId.Value).Distinct()]))
            .ToDictionarySafe(x => x.Id);

        void Walk(int parentId, int depth, HashSet<int> path)
        {
            var siblings = children[parentId].OrderBy(x => x.DisplayOrder).ThenBy(x => x.Name).ToList();
            for (var i = 0; i < siblings.Count; i++)
            {
                var node = siblings[i];

                // Guards against a corrupt parent chain in old data.
                if (!path.Add(node.Id))
                {
                    continue;
                }

                if (visible == null || visible.Contains(node.Id))
                {
                    model.Rows.Add(new CategoryRowModel
                    {
                        Id = node.Id,
                        ParentId = node.ParentId,
                        Name = node.Name,
                        Depth = depth,
                        Published = node.Published,
                        DisplayOrder = node.DisplayOrder,
                        ProductCount = counts.GetValueOrDefault(node.Id),
                        ChildCount = children[node.Id].Count(),
                        ImageUrl = node.MediaFileId > 0 && files.TryGetValue(node.MediaFileId.Value, out var f) ? _mediaService.GetUrl(f, 64) : null,
                        IsFirst = i == 0,
                        IsLast = i == siblings.Count - 1,
                        IsMatch = matches == null || matches.Contains(node.Id)
                    });
                }

                Walk(node.Id, depth + 1, path);
            }
        }

        Walk(0, 0, []);

        // Categories whose parent no longer exists are shown at the root.
        var known = nodes.Select(x => x.Id).ToHashSet();
        foreach (var orphan in nodes.Where(x => x.ParentId > 0 && !known.Contains(x.ParentId.Value)))
        {
            if (visible == null || visible.Contains(orphan.Id))
            {
                model.Rows.Add(new CategoryRowModel { Id = orphan.Id, Name = orphan.Name, Published = orphan.Published, ProductCount = counts.GetValueOrDefault(orphan.Id), IsFirst = true, IsLast = true });
            }
        }

        return model;
    }

    /// <summary>
    /// Ids of <paramref name="categoryId"/> and all its descendants.
    /// </summary>
    internal static HashSet<int> Subtree(int categoryId, IEnumerable<(int Id, int? ParentId)> nodes)
    {
        var children = nodes.ToLookup(x => x.ParentId ?? 0, x => x.Id);
        var result = new HashSet<int>();
        var stack = new Stack<int>();
        stack.Push(categoryId);

        while (stack.Count > 0)
        {
            var id = stack.Pop();
            if (result.Add(id))
            {
                foreach (var child in children[id])
                {
                    stack.Push(child);
                }
            }
        }

        return result;
    }

    /// <summary>
    /// A category cannot become its own parent or a child of one of its descendants.
    /// </summary>
    internal static bool IsValidParent(int categoryId, int parentId, IEnumerable<(int Id, int? ParentId)> nodes)
        => parentId == 0 || categoryId == 0 || !Subtree(categoryId, nodes).Contains(parentId);

    static readonly System.Globalization.CultureInfo Turkish = System.Globalization.CultureInfo.GetCultureInfo("tr-TR");

    /// <summary>
    /// Trimmed, inner whitespace collapsed: "  Açık   Kask " = "Açık Kask".
    /// </summary>
    internal static string NormalizeName(string name)
        => string.Join(' ', (name ?? string.Empty).Split((char[])null, StringSplitOptions.RemoveEmptyEntries));

    /// <summary>
    /// Same name in Turkish, ignoring case ("kask" = "KASK", "ışık" = "IŞIK").
    /// </summary>
    internal static bool SameName(string a, string b)
        => string.Compare(NormalizeName(a), NormalizeName(b), Turkish, System.Globalization.CompareOptions.IgnoreCase) == 0;

    /// <summary>
    /// Another category with this name under the same parent.
    /// </summary>
    internal static int? FindSibling(string name, int parentId, int exceptId, IEnumerable<(int Id, int? ParentId, string Name)> nodes)
        => nodes.FirstOrDefault(x => x.Id != exceptId && (x.ParentId ?? 0) == parentId && SameName(x.Name, name)) is var hit && hit.Id > 0 ? hit.Id : null;

    /// <summary>
    /// Every category with its full path ("Kask › Açık Kask"), sorted by path. Read directly from the database
    /// (not the cached storefront tree), so a category created a moment ago is listed at once.
    /// </summary>
    public async Task<List<CategoryPickerOption>> GetPickerOptionsAsync()
    {
        var nodes = await LoadNodesAsync();
        return BuildPickerOptions(nodes.Select(x => (x.Id, x.ParentId, x.Name, x.Published)));
    }

    internal static List<CategoryPickerOption> BuildPickerOptions(IEnumerable<(int Id, int? ParentId, string Name, bool Published)> nodes)
    {
        var byId = nodes.ToDictionary(x => x.Id);

        string PathOf(int id)
        {
            var names = new List<string>();
            var seen = new HashSet<int>();
            for (int? current = id; current is int c && byId.TryGetValue(c, out var n) && seen.Add(c); current = n.ParentId)
            {
                names.Insert(0, n.Name);
            }
            return string.Join(" › ", names);
        }

        return [.. byId.Values
            .Select(x => new CategoryPickerOption { Id = x.Id, Name = x.Name, Path = PathOf(x.Id), Published = x.Published })
            .OrderBy(x => x.Path, StringComparer.Create(Turkish, true))];
    }

    /// <summary>
    /// "Yeni kategori oluştur" from the product form: name and optional parent only. An existing category with the
    /// same name under the same parent is returned instead of a twin.
    /// </summary>
    public async Task<QuickCategoryResult> QuickCreateAsync(string name, int parentId)
    {
        name = NormalizeName(name);
        if (name.Length == 0)
        {
            return new() { Error = R("Category.NameRequired") };
        }
        if (name.Length > 400)
        {
            return new() { Error = R("Validation.TooLong") };
        }

        var nodes = await LoadNodesAsync();
        if (parentId > 0 && !nodes.Any(x => x.Id == parentId))
        {
            return new() { Error = R("Category.ParentMissing") };
        }

        CategoryPickerOption Option(int id) => BuildPickerOptions(nodes.Select(x => (x.Id, x.ParentId, x.Name, x.Published))).First(x => x.Id == id);

        var existing = FindSibling(name, parentId, 0, nodes.Select(x => (x.Id, x.ParentId, x.Name)));
        if (existing.HasValue)
        {
            return new() { Success = true, Category = Option(existing.Value), Message = R("Category.Exists") };
        }

        var category = new Category
        {
            Name = name,
            ParentId = parentId > 0 ? parentId : null,
            Published = true,
            DisplayOrder = nodes.Where(x => (x.ParentId ?? 0) == parentId).Select(x => x.DisplayOrder + 1).DefaultIfEmpty(0).Max()
        };

        _db.Categories.Add(category);
        await _db.SaveChangesAsync();
        await _urlService.SaveSlugAsync(category, null, category.Name, true);

        _services.ActivityLogger.LogActivity(KnownActivityLogTypes.AddNewCategory, T("ActivityLog.AddNewCategory", category.Name));
        await _db.SaveChangesAsync();

        nodes.Add(new Node(category.Id, category.ParentId, category.Name, category.Published, category.DisplayOrder, null));
        return new() { Success = true, Created = true, Category = Option(category.Id), Message = R("Category.Created", category.Name) };
    }

    public async Task<CategoryEditorModel> LoadEditorAsync(int id, int parentId = 0)
    {
        var model = new CategoryEditorModel { ParentId = parentId };

        if (id == 0)
        {
            // A new category goes after its siblings, like "Yeni kategori oluştur" in the product form.
            model.DisplayOrder = await _db.Categories
                .Where(x => !x.Deleted && (x.ParentId ?? 0) == parentId)
                .Select(x => (int?)x.DisplayOrder + 1)
                .MaxAsync() ?? 0;
        }

        if (id > 0)
        {
            var category = await _db.Categories.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && !x.Deleted);
            if (category == null)
            {
                return null;
            }

            model.Id = category.Id;
            model.Version = category.UpdatedOnUtc.Ticks;
            model.Name = category.Name;
            model.ParentId = category.ParentId ?? 0;
            model.Published = category.Published;
            model.DisplayOrder = category.DisplayOrder;
            model.Description = category.Description;
            model.MetaTitle = category.MetaTitle;
            model.MetaDescription = category.MetaDescription;
            model.MediaFileId = category.MediaFileId;
            model.SeName = await category.GetActiveSlugAsync(0, true, false);
        }

        await PrepareEditorAsync(model);
        return model;
    }

    public async Task PrepareEditorAsync(CategoryEditorModel model)
    {
        var nodes = await LoadNodesAsync();
        var excluded = model.Id > 0 ? Subtree(model.Id, nodes.Select(x => (x.Id, x.ParentId))) : [];
        var tree = await GetTreeAsync(null);

        model.ParentOptions = [.. tree.Rows
            .Where(x => !excluded.Contains(x.Id))
            .Select(x => new CategoryOption { Id = x.Id, Name = x.Name, Depth = x.Depth })];

        if (model.MediaFileId > 0)
        {
            var file = await _mediaService.GetFileByIdAsync(model.MediaFileId.Value);
            model.ImageUrl = file != null ? _mediaService.GetUrl(file, 256) : null;
        }
    }

    /// <returns>Field name to error; empty on success. <paramref name="model"/>.Id is set for a new category.</returns>
    public async Task<Dictionary<string, string>> SaveAsync(CategoryEditorModel model, IFormFile image)
    {
        var errors = new Dictionary<string, string>();
        var isNew = model.Id == 0;

        Category category;
        if (isNew)
        {
            category = new Category();
        }
        else
        {
            category = await _db.Categories.FirstOrDefaultAsync(x => x.Id == model.Id && !x.Deleted);
            if (category == null)
            {
                errors[""] = R("NotFound");
                return errors;
            }

            if (category.UpdatedOnUtc.Ticks != model.Version)
            {
                errors[""] = R("ConflictEditor");
                return errors;
            }
        }

        var allNodes = await LoadNodesAsync();
        var nodes = allNodes.Select(x => (x.Id, x.ParentId)).ToList();
        if (model.ParentId > 0 && !nodes.Any(x => x.Id == model.ParentId))
        {
            errors[nameof(model.ParentId)] = R("NotFound");
        }
        else if (!IsValidParent(model.Id, model.ParentId, nodes))
        {
            errors[nameof(model.ParentId)] = R("Category.CircularParent");
        }

        model.Name = NormalizeName(model.Name);
        if (model.Name.Length == 0)
        {
            errors[nameof(model.Name)] = R("Category.NameRequired");
        }
        else if (FindSibling(model.Name, model.ParentId, model.Id, allNodes.Select(x => (x.Id, x.ParentId, x.Name))).HasValue)
        {
            errors[nameof(model.Name)] = R("Category.Duplicate");
        }

        if (image != null && image.Length > 0)
        {
            var extension = System.IO.Path.GetExtension(image.FileName)?.ToLowerInvariant();
            if (!ImageExtensions.Contains(extension) || !image.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            {
                errors["Image"] = R("Category.ImageType");
            }
            else if (image.Length > MaxImageBytes)
            {
                errors["Image"] = R("Category.ImageSize");
            }
        }

        if (errors.Count > 0)
        {
            return errors;
        }

        category.Name = model.Name.Trim();
        category.ParentId = model.ParentId > 0 ? model.ParentId : null;
        category.Published = model.Published;
        category.DisplayOrder = model.DisplayOrder;
        category.Description = model.Description;
        category.MetaTitle = model.MetaTitle;
        category.MetaDescription = model.MetaDescription;

        if (image != null && image.Length > 0)
        {
            using var stream = image.OpenReadStream();
            var fileName = $"category-{Guid.NewGuid():N}{System.IO.Path.GetExtension(image.FileName).ToLowerInvariant()}";
            var file = await _mediaService.SaveFileAsync(SystemAlbumProvider.Catalog + "/" + fileName, stream, false, DuplicateFileHandling.Rename);
            category.MediaFileId = file.Id;
        }
        else if (model.RemoveImage)
        {
            category.MediaFileId = null;
        }

        if (isNew)
        {
            _db.Categories.Add(category);
        }

        await _db.SaveChangesAsync();
        model.Id = category.Id;

        var slug = await _urlService.SaveSlugAsync(category, model.SeName, category.Name, true);
        model.SeName = slug.Slug;

        _services.ActivityLogger.LogActivity(
            isNew ? KnownActivityLogTypes.AddNewCategory : KnownActivityLogTypes.EditCategory,
            T(isNew ? "ActivityLog.AddNewCategory" : "ActivityLog.EditCategory", category.Name));
        await _db.SaveChangesAsync();

        return errors;
    }

    /// <summary>
    /// Swaps the position with the previous or next sibling. Sibling orders are renumbered 0..n first,
    /// because imported categories often share the same display order.
    /// </summary>
    public async Task<bool> MoveAsync(int id, bool up)
    {
        var category = await _db.Categories.FirstOrDefaultAsync(x => x.Id == id && !x.Deleted);
        if (category == null)
        {
            return false;
        }

        var siblings = await _db.Categories
            .Where(x => x.ParentId == category.ParentId && !x.Deleted)
            .OrderBy(x => x.DisplayOrder)
            .ThenBy(x => x.Name)
            .ToListAsync();

        var index = siblings.FindIndex(x => x.Id == id);
        var target = up ? index - 1 : index + 1;
        if (target < 0 || target >= siblings.Count)
        {
            return false;
        }

        (siblings[index], siblings[target]) = (siblings[target], siblings[index]);
        for (var i = 0; i < siblings.Count; i++)
        {
            siblings[i].DisplayOrder = i;
        }

        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<CategoryDeleteInfo> GetDeleteInfoAsync(int id)
    {
        var nodes = await LoadNodesAsync();
        var node = nodes.FirstOrDefault(x => x.Id == id);
        if (node == null)
        {
            return null;
        }

        var subtree = Subtree(id, nodes.Select(x => (x.Id, x.ParentId)));
        var productIds = await _db.ProductCategories
            .Where(x => x.CategoryId == id && !x.Product.Deleted)
            .Select(x => x.ProductId)
            .ToListAsync();

        var withOtherCategory = await _db.ProductCategories
            .Where(x => productIds.Contains(x.ProductId) && x.CategoryId != id && !x.Category.Deleted)
            .Select(x => x.ProductId)
            .Distinct()
            .CountAsync();

        return new CategoryDeleteInfo
        {
            Id = id,
            Name = node.Name,
            ParentName = nodes.FirstOrDefault(x => x.Id == node.ParentId)?.Name,
            ChildCount = nodes.Count(x => x.ParentId == id),
            DescendantCount = subtree.Count - 1,
            ProductCount = productIds.Count,
            OrphanedProductCount = productIds.Count - withOtherCategory
        };
    }

    /// <param name="deleteSubCategories">
    /// <c>false</c>: direct children move up to the deleted category's parent (Smartstore alone would move the
    /// whole subtree to the root). <c>true</c>: the whole subtree is deleted.
    /// Products are never deleted; their mapping to a deleted category is no longer shown.
    /// </param>
    public async Task<bool> DeleteAsync(int id, bool deleteSubCategories)
    {
        var category = await _db.Categories.FirstOrDefaultAsync(x => x.Id == id && !x.Deleted);
        if (category == null)
        {
            return false;
        }

        if (deleteSubCategories)
        {
            await _categoryService.DeleteCategoryAsync(category, true);
        }
        else
        {
            // Without children, DeleteCategoryAsync would only set Deleted. Doing it here avoids its
            // (cached) subtree lookup moving the re-parented children to the root.
            var children = await _db.Categories.Where(x => x.ParentId == id && !x.Deleted).ToListAsync();
            foreach (var child in children)
            {
                child.ParentId = category.ParentId;
            }

            category.Deleted = true;
            await _db.SaveChangesAsync();
        }

        _services.ActivityLogger.LogActivity(KnownActivityLogTypes.DeleteCategory, T("ActivityLog.DeleteCategory", category.Name));
        await _db.SaveChangesAsync();

        return true;
    }
}
