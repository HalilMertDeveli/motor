using Microsoft.AspNetCore.Mvc;
using MotorShop.WordPress.Services;
using static MotorShop.WordPress.Controllers.MotorWordPressController;

namespace MotorShop.WordPress;

public static class BlogUrlExtensions
{
    /// <summary>
    /// Canonical storefront URL of a post, guide or page.
    /// </summary>
    public static string BlogPostUrl(this IUrlHelper url, BlogPost post)
    {
        Guard.NotNull(post);

        var routeName = post.Section switch
        {
            BlogSection.Guides => RouteNames.Guide,
            BlogSection.Page => RouteNames.Page,
            _ => RouteNames.Post
        };

        return url.RouteUrl(routeName, new { slug = post.Slug }) ?? BlogPaths.ForPost(post);
    }

    public static string BlogCategoryUrl(this IUrlHelper url, BlogTerm category)
        => url.RouteUrl(RouteNames.Category, new { slug = category.Slug }) ?? BlogPaths.ForCategory(category.Slug);

    public static string BlogTagUrl(this IUrlHelper url, BlogTerm tag)
        => url.RouteUrl(RouteNames.Tag, new { slug = tag.Slug }) ?? BlogPaths.ForTag(tag.Slug);
}
