# WordPress as headless CMS

WordPress is the editor for MOTOR's editorial content. MOTOR fetches the published content over the
WordPress REST API and renders it itself, server side, in the MotorDark theme under its own domain.
WordPress is **not** a shop: catalog, stock, customers, cart, checkout, iyzico payments, orders,
invoices, shipping and the MOTOR admin stay in MOTOR, which remains the only source for them.

Module: `src/Smartstore.Modules/MotorShop.WordPress` · tests: `test/MotorShop.WordPress.Tests` ·
WordPress stack: `deploy/wordpress`.

## 1. Decision

| Option | Verdict |
| --- | --- |
| Smartstore built-in CMS only | Topics (CMS pages), menus and the Page Builder cover legal and info pages. This repository has **no blog or news module** (the open-source edition ships without them), so articles, categories, tags, authors, editorial workflow and media library would have to be built. Topics stay the place for legal texts (MotorShop.Legal). |
| WooCommerce | Rejected. A second shop with its own products, stock and orders would duplicate the data MOTOR owns and break iyzico, invoicing and the admin modules. |
| WordPress as frontend (MOTOR as backend) | Rejected. Two templates for one site, the cart and checkout would need a rewrite, SEO value split across two systems. |
| **WordPress headless, MOTOR renders** | **Chosen.** Editors get a mature editor and media library; visitors and search engines only see MOTOR URLs. A WordPress outage affects the blog only. |

Operational cost to accept: one more server (PHP + MariaDB) to patch and back up. If the owner does not want
to run it, the integration simply stays disabled (`Enabled=false`), which is the default.

Responsibilities:

| WordPress | MOTOR |
| --- | --- |
| Blog posts, maintenance guides, technical articles, campaign and brand texts, SEO info pages, editorial images | Products, stock, prices, customers, cart, checkout, iyzico, orders, invoices, shipping, MOTOR admin modules |
| Editors and their logins | Shop admins and their logins (no shared login, no SSO) |
| MariaDB of its own | Supabase PostgreSQL (WordPress never connects to it) |

## 2. What MOTOR shows

| URL | Content |
| --- | --- |
| `/blog` | Published posts except guides, category chips, search (`?q=`), paging (`?page=`) |
| `/blog/{slug}` | Post: cover image, title, date, reading time, category, sanitized content, tags, linked products, related posts |
| `/blog/kategori/{slug}` | Posts of a category |
| `/blog/etiket/{slug}` | Posts of a tag |
| `/rehberler`, `/rehberler/{slug}` | Posts in the WordPress category `rehberler` (setting `GuideCategorySlug`) |
| `/blog/sayfa/{slug}` | WordPress pages |
| `POST /wordpress/webhook/purge` | Cache purge called by WordPress (shared secret) |

- Only `status=publish` content is requested, and MOTOR rejects anything else that comes back (drafts,
  private, scheduled, password protected).
- One URL per article: a guide under `/blog/...` or a post under `/rehberler/...` is redirected (301), as are
  upper case slugs and WordPress pages under `/blog/{slug}`.
- `blog` and `rehberler` are reserved: Smartstore will not give these slugs to new products or categories.
  The admin page warns if an existing entity already uses one.

### Product links in articles

Tag a post in WordPress with `sku-<product sku>` (e.g. `sku-869-01780`). MOTOR shows those products under
"Yazıda geçen ürünler" with the normal product cards, prices and visibility rules (unpublished, hidden or
other-store products never appear). SKU tags are not shown as tags. Links typed into the text work too.

### SEO

- Server side rendered pages, title, meta description (Yoast SEO title/description are used when present),
  canonical URL on the store domain, Open Graph and Twitter tags, `BlogPosting` JSON-LD, breadcrumb with
  `BreadcrumbList` JSON-LD.
- Search result pages are `noindex, follow`; unknown slugs, categories and pages past the end return 404.
- XML sitemap: `/blog`, `/rehberler`, every post and page are added to the Smartstore sitemap
  (setting "XML sitemap includes blog", on by default). The sitemap is rebuilt nightly or with
  "Site haritasını yenile" on the admin page.
- Duplicate content: the `motor-headless` mu-plugin redirects every WordPress front-end URL (posts, pages,
  categories, tags, home) with 301 to the MOTOR URL and sends `noindex`. Only `/wp-admin`, `/wp-login.php`,
  the REST API and media files stay on the WordPress host.
- Links inside content that point to WordPress permalinks are rewritten to MOTOR URLs.

## 3. Security

- Configuration only from environment variables or `Config/usersettings.*.json`, never from the database or
  the admin UI. The application password and webhook secret are never shown or logged.
- Reading uses the public REST API without credentials. If the WordPress site requires authentication for the
  REST API, use a **separate low privilege user** (role "Subscriber") with an **application password**.
  Never the administrator password.
- SSRF: the base URL must be https, without credentials, query or private address. Every connection is checked
  against the resolved IP addresses (no loopback, private, link local / cloud metadata, CGNAT, multicast),
  which also stops DNS rebinding. Redirects are not followed. Responses are limited to 4 MB.
- Timeouts: 5 s per attempt, one retry for timeouts / 5xx / 429, at most ~11 s in total; a circuit breaker
  pauses calls for 30 s after repeated failures; at most 8 concurrent calls.
- Rate limiting: per visitor IP, 20 uncached WordPress requests in a burst, then 1 every 2 s; cached pages are
  not limited. The webhook is limited the same way and answers 401 to a wrong secret.
- HTML from WordPress is sanitized with an allow list (Ganss HtmlSanitizer): no scripts, event handlers, forms,
  inline styles, objects, `javascript:`/`data:` URLs; classes only `wp-*`/`has-*`/`align*`...; iframes only
  from YouTube (nocookie) and Vimeo over https, sandboxed; `target=_blank` gets `rel="noopener noreferrer"`.
  Titles and excerpts are plain text and HTML encoded by Razor.
- Admin: own permissions `wordpress.read` (status page) and `wordpress.update` (cache, sitemap), granted to
  Administrators. The "WordPress yönetimini aç" button is only a link; the editor logs in to WordPress with
  their own WordPress account. SSO would need its own design and approval.
- Availability: when WordPress is down, cached pages are served (fallback copy up to 48 h, with a notice),
  otherwise the blog answers 503 with `Retry-After`. Product search, cart, checkout and orders never call
  WordPress.

Dependencies: no new NuGet package. `HtmlSanitizer` (MIT) and `Polly` (BSD-3-Clause) are already referenced by
Smartstore; rate limiting uses `System.Threading.RateLimiting` from .NET. WordPress (GPLv2+), MariaDB (GPLv2)
and the official Docker images run as separate services and are not linked into MOTOR (AGPL).

## 4. Configuration (MOTOR)

Section `MotorShop:WordPress`, as environment variables:

| Variable | Default | Notes |
| --- | --- | --- |
| `MotorShop__WordPress__Enabled` | `false` | Off: blog routes return 404, nothing is sent to WordPress |
| `MotorShop__WordPress__BaseUrl` | – | e.g. `https://cms.example.com` (REST API at `/wp-json/`) |
| `MotorShop__WordPress__AdminUrl` | `{BaseUrl}/wp-admin/` | Link on the admin page |
| `MotorShop__WordPress__UserName` | – | Only if the REST API needs authentication |
| `MotorShop__WordPress__ApplicationPassword` | – | **Secret.** WordPress application password of that user |
| `MotorShop__WordPress__WebhookSecret` | – | **Secret.** ≥ 32 characters; empty disables the webhook |
| `MotorShop__WordPress__CacheMinutes` | `10` | Fresh cache |
| `MotorShop__WordPress__StaleCacheHours` | `48` | Fallback copy while WordPress is down |
| `MotorShop__WordPress__TimeoutSeconds` | `5` | Per attempt (restart to apply) |
| `MotorShop__WordPress__PageSize` | `12` | Posts per page |
| `MotorShop__WordPress__GuideCategorySlug` | `rehberler` | Category shown under `/rehberler` |
| `MotorShop__WordPress__ProductTagPrefix` | `sku-` | Tag prefix for product links |
| `MotorShop__WordPress__AllowedEmbedHosts__0..n` | YouTube, Vimeo | iframe hosts |
| `MotorShop__WordPress__AllowInsecureHttp` / `AllowPrivateNetwork` | `false` | **Development only** (local Docker). The admin page warns if they are on in production |

Fly.io (when the app runs there): `fly secrets set MotorShop__WordPress__WebhookSecret=... -a prosev`
(and `MotorShop__WordPress__ApplicationPassword` if used); non-secret values can go into `fly.toml` `[env]`.

Then: **Admin > Plugins > WordPress Blog > Install**, open **Admin > Plugins > WordPress Blog** and check
"Bağlı". Optionally add a "Blog" link under **CMS > Menus** (Main or Footer, link type "route",
route name `MotorWordPress.Blog`, or a plain URL `/blog`).

## 5. WordPress server

`deploy/wordpress/compose.yml`: official `wordpress:6.8-php8.3-apache` + `mariadb:11.4`, own volumes,
WordPress bound to `127.0.0.1:8097`. The `mu-plugins/motor-headless.php` plugin is mounted read only.

```bash
cd deploy/wordpress
cp .env.example .env        # fill in random DB passwords, MOTOR_STORE_URL, webhook secret
docker compose up -d
```

Public setup (needs a domain/subdomain and a server, which the owner provides):

1. DNS `cms.<domain>` → the server; put Caddy (or another HTTPS proxy) in front: `reverse_proxy 127.0.0.1:8097`.
2. Install WordPress at `https://cms.<domain>/wp-admin/install.php`: strong admin password stored in a password
   manager, language Türkçe, time zone Europe/Istanbul.
3. **Settings > Permalinks: "Post name"** (the REST API at `/wp-json/` needs pretty permalinks).
4. **Settings > Reading: "Discourage search engines"** on (belt and braces, the mu-plugin also sends `noindex`).
5. Create the category `rehberler` for guides.
6. Users: editors get the role "Editor" or "Author", never "Administrator". Enable 2FA (e.g. "Two Factor" plugin).
7. Keep plugins minimal; recommended only Yoast SEO (titles/descriptions are used by MOTOR) and a backup plugin
   or a volume backup of `db` and `wordpress`.
8. Set `MOTOR_WEBHOOK_URL=https://<store>/wordpress/webhook/purge` and the same `MOTOR_WEBHOOK_SECRET` in both
   systems, then publish a post and check that it shows on `/blog` at once.

Without the webhook, changes show after `CacheMinutes` or after "Önbelleği yenile" in the MOTOR admin.

## 6. Editor guide (short)

- Publish = visible on MOTOR within seconds (webhook) or 10 minutes. Draft, private, scheduled and password
  protected posts never appear.
- Featured image = cover and card image. Set an alternative text.
- Category `Rehberler` = the post appears under `/rehberler`.
- Tag `sku-<sku>` = product cards under the post.
- YouTube/Vimeo embeds work; other embeds, scripts, forms and custom CSS are removed by MOTOR.
- Unpublishing or deleting a post removes it from MOTOR (404) after the next refresh.

## 7. Tests

`dotnet test test/MotorShop.WordPress.Tests` (125 tests): REST client (parsing, query, auth header,
pagination, 4xx/5xx/redirect/non-JSON/broken JSON, timeout, retry, circuit breaker, cancellation), mapping
(drafts, protected, sections, SKU tags, images), sanitizer/XSS (script vectors, iframes, classes, link
rewrite), SSRF address checks, cache/fallback/deletion/rate limit, routes and slug collisions, admin
permissions, webhook secret.

Local end to end run: `deploy/wordpress` stack + MOTOR QA tenant with
`MotorShop__WordPress__AllowInsecureHttp=true` and `AllowPrivateNetwork=true`.
