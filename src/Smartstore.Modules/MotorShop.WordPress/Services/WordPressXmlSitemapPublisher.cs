using System.Runtime.CompilerServices;
using Smartstore.Core.Localization;
using Smartstore.Core.Seo;
using Smartstore.Core.Seo.Routing;

namespace MotorShop.WordPress.Services;

/// <summary>
/// Adds /blog, /rehberler and every published post and page to the Smartstore XML sitemap.
/// Uses <see cref="SeoSettings.XmlSitemapIncludesBlog"/>. Nodes are created for the master language only:
/// WordPress content is not translated.
/// </summary>
/// <remarks>
/// The sitemap is rebuilt by the scheduled task (nightly) or from the WordPress admin page of MOTOR.
/// When WordPress is down during a rebuild, the fallback cache copies are used.
/// </remarks>
public class WordPressXmlSitemapPublisher : IXmlSitemapPublisher
{
    const string EntityName = "WordPressPost";
    const string SectionEntityName = "WordPressSection";

    private readonly WordPressContentService _content;

    public WordPressXmlSitemapPublisher(WordPressContentService content)
    {
        _content = content;
    }

    public XmlSitemapProvider? PublishXmlSitemap(XmlSitemapBuildContext context)
    {
        if (!_content.IsEnabled || !context.LoadSettings<SeoSettings>().XmlSitemapIncludesBlog)
        {
            return null;
        }

        return new Provider(_content);
    }

    private sealed class Provider : XmlSitemapProvider
    {
        private readonly WordPressContentService _content;
        private List<NamedEntity>? _entities;

        public Provider(WordPressContentService content)
        {
            _content = content;
        }

        // After topics (200), before products (int.MaxValue, must stay last).
        public override int Order => 300;

        public override async Task<int> GetTotalCountAsync()
            => (await LoadAsync(CancellationToken.None)).Count;

        public override async IAsyncEnumerable<NamedEntity> EnlistAsync([EnumeratorCancellation] CancellationToken cancelToken = default)
        {
            foreach (var entity in await LoadAsync(cancelToken))
            {
                yield return entity;
            }
        }

        public override XmlSitemapNode? CreateNode(string baseUrl, NamedEntity entity, UrlRecordCollection slugs, Language language, XmlSitemapBuildNodeContext ctx)
        {
            if (language.Id != ctx.DefaultLanguageId || !entity.Slug.HasValue())
            {
                return null;
            }

            return new XmlSitemapNode
            {
                // Same form as the canonical URL of the page (trailing slash setting).
                Loc = baseUrl + RouteHelper.NormalizePathComponent(entity.Slug.TrimStart('/')),
                LastMod = entity.LastMod,
                ChangeFreq = entity.EntityName == SectionEntityName ? ChangeFrequency.Daily : ChangeFrequency.Weekly
            };
        }

        // The generator asks for the count before it enumerates; both must see the same list.
        private async Task<List<NamedEntity>> LoadAsync(CancellationToken cancelToken)
        {
            if (_entities != null)
            {
                return _entities;
            }

            var posts = new List<BlogPost>();
            await foreach (var post in _content.EnumerateAllAsync(cancelToken))
            {
                posts.Add(post);
            }

            var entities = new List<NamedEntity>();
            if (posts.Count > 0)
            {
                var latest = posts.Max(x => x.ModifiedUtc);
                entities.Add(new NamedEntity { EntityName = SectionEntityName, Id = 1, Slug = BlogPaths.Blog, LastMod = latest });

                var guides = posts.Where(x => x.Section == BlogSection.Guides).ToList();
                if (guides.Count > 0)
                {
                    entities.Add(new NamedEntity { EntityName = SectionEntityName, Id = 2, Slug = BlogPaths.Guides, LastMod = guides.Max(x => x.ModifiedUtc) });
                }
            }

            entities.AddRange(posts
                .GroupBy(x => x.Id).Select(g => g.First())
                .Select(x => new NamedEntity { EntityName = EntityName, Id = x.Id, Slug = BlogPaths.ForPost(x), LastMod = x.ModifiedUtc }));

            return _entities = entities;
        }
    }
}
