using System.ComponentModel.DataAnnotations;

namespace MotorShop.AdminPanel.Models;

public class CategoryTreeModel
{
    public string Q { get; set; }
    public List<CategoryRowModel> Rows { get; set; } = [];
    public int TotalCategories { get; set; }
}

public class CategoryRowModel
{
    public int Id { get; set; }
    public int? ParentId { get; set; }
    public string Name { get; set; }
    public int Depth { get; set; }
    public bool Published { get; set; }
    public int DisplayOrder { get; set; }
    public int ProductCount { get; set; }
    public int ChildCount { get; set; }
    public string ImageUrl { get; set; }
    public bool IsFirst { get; set; }
    public bool IsLast { get; set; }

    /// <summary>
    /// Matches the search itself (ancestors of matches are shown for context).
    /// </summary>
    public bool IsMatch { get; set; } = true;
}

public class CategoryEditorModel
{
    public int Id { get; set; }
    public long Version { get; set; }

    [Required, StringLength(400)]
    public string Name { get; set; }

    public int ParentId { get; set; }
    public bool Published { get; set; } = true;
    public int DisplayOrder { get; set; }
    public string Description { get; set; }

    [StringLength(400)]
    public string MetaTitle { get; set; }

    [StringLength(4000)]
    public string MetaDescription { get; set; }

    [StringLength(400)]
    public string SeName { get; set; }

    public int? MediaFileId { get; set; }
    public bool RemoveImage { get; set; }

    // Display only.
    public string ImageUrl { get; set; }
    public List<CategoryOption> ParentOptions { get; set; } = [];
}

public class CategoryDeleteInfo
{
    public int Id { get; set; }
    public string Name { get; set; }
    public string ParentName { get; set; }
    public int ChildCount { get; set; }
    public int DescendantCount { get; set; }
    public int ProductCount { get; set; }

    /// <summary>
    /// Products that would be left without any category.
    /// </summary>
    public int OrphanedProductCount { get; set; }
}
