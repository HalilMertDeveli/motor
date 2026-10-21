using MotorShop.WordPress.Services;
using Smartstore.Web.Models.Catalog;
using Smartstore.Web.Models.Common;

namespace MotorShop.WordPress.Models;

public class BlogListModel
{
    public BlogSection Section { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Intro { get; set; }
    public BlogPostList Result { get; set; } = BlogPostList.Empty(1, 12, true);
    public IReadOnlyList<BlogTerm> Categories { get; set; } = [];
    public string? ActiveCategorySlug { get; set; }
    public string? Search { get; set; }
    public string CanonicalUrl { get; set; } = string.Empty;

    /// <summary>
    /// Route name and values of this list, used for the pager links.
    /// </summary>
    public string RouteName { get; set; } = string.Empty;
    public Dictionary<string, object?> RouteValues { get; set; } = [];
}

public class BlogPostModel
{
    public required BlogPost Post { get; init; }
    public string CanonicalUrl { get; set; } = string.Empty;
    public string SectionTitle { get; set; } = string.Empty;
    public string SectionUrl { get; set; } = string.Empty;
    public IReadOnlyList<BlogPost> Related { get; set; } = [];
    public ProductSummaryModel? Products { get; set; }
    public MetaPropertiesModel MetaProperties { get; set; } = new();
}
