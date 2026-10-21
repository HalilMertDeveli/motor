<?php
/**
 * Plugin Name: MOTOR headless mode
 * Description: WordPress is only the editor for the MOTOR blog. Visitors and crawlers are sent to the MOTOR store,
 *              so every article has a single indexed URL there. Notifies MOTOR when content changes.
 * Version:     1.0.0
 *
 * Configuration (environment variables or constants in wp-config.php):
 *   MOTOR_STORE_URL       https://www.example.com          required for redirects
 *   MOTOR_WEBHOOK_URL     https://www.example.com/wordpress/webhook/purge
 *   MOTOR_WEBHOOK_SECRET  shared secret, at least 32 characters
 *   MOTOR_GUIDE_CATEGORY  slug of the category shown under /rehberler (default "rehberler")
 */

defined('ABSPATH') || exit;

function motor_headless_config(string $name, string $default = ''): string
{
    if (defined($name)) {
        return (string) constant($name);
    }

    $value = getenv($name);
    return $value === false ? $default : (string) $value;
}

/**
 * MOTOR URL of a post or page, mirroring MotorShop.WordPress BlogPaths.
 */
function motor_headless_url(?WP_Post $post): string
{
    $store = rtrim(motor_headless_config('MOTOR_STORE_URL'), '/');
    if ($post === null || $post->post_status !== 'publish') {
        return $store . '/blog';
    }

    $slug = rawurlencode(urldecode($post->post_name));

    if ($post->post_type === 'page') {
        return $store . '/blog/sayfa/' . $slug;
    }

    if ($post->post_type === 'post') {
        $guide = motor_headless_config('MOTOR_GUIDE_CATEGORY', 'rehberler');
        return has_category($guide, $post)
            ? $store . '/rehberler/' . $slug
            : $store . '/blog/' . $slug;
    }

    return $store . '/blog';
}

// 1. Front-end requests go to MOTOR (301). Admin, login, REST API, cron, media files and previews keep working.
add_action('template_redirect', function () {
    if (motor_headless_config('MOTOR_STORE_URL') === '' || is_preview() || is_user_logged_in()) {
        return;
    }

    $target = rtrim(motor_headless_config('MOTOR_STORE_URL'), '/') . '/blog';

    if (is_singular(['post', 'page'])) {
        $target = motor_headless_url(get_queried_object());
    } elseif (is_category()) {
        $term = get_queried_object();
        $guide = motor_headless_config('MOTOR_GUIDE_CATEGORY', 'rehberler');
        $target = rtrim(motor_headless_config('MOTOR_STORE_URL'), '/') .
            ($term->slug === $guide ? '/rehberler' : '/blog/kategori/' . rawurlencode(urldecode($term->slug)));
    } elseif (is_tag()) {
        $term = get_queried_object();
        $target = rtrim(motor_headless_config('MOTOR_STORE_URL'), '/') . '/blog/etiket/' . rawurlencode(urldecode($term->slug));
    }

    wp_redirect($target, 301, 'MOTOR');
    exit;
}, 0);

// 2. Belt and braces: WordPress pages are never indexed, even if the redirect is bypassed.
add_filter('wp_robots', function (array $robots) {
    $robots['noindex'] = true;
    $robots['nofollow'] = true;
    return $robots;
});

// "View post" links in the editor point to the MOTOR URL.
add_filter('post_link', fn($url, $post) => motor_headless_config('MOTOR_STORE_URL') !== '' && $post->post_status === 'publish' ? motor_headless_url($post) : $url, 10, 2);
add_filter('page_link', fn($url, $id) => motor_headless_config('MOTOR_STORE_URL') !== '' && get_post_status($id) === 'publish' ? motor_headless_url(get_post($id)) : $url, 10, 2);

// 3. Smaller attack surface: no XML-RPC, no public user listing (user name enumeration).
add_filter('xmlrpc_enabled', '__return_false');
add_filter('rest_endpoints', function (array $endpoints) {
    if (!is_user_logged_in()) {
        unset($endpoints['/wp/v2/users'], $endpoints['/wp/v2/users/(?P<id>[\d]+)']);
    }
    return $endpoints;
});

// Password protected content is not for the shop: keep it out of public lists and totals.
// (MOTOR also skips it when it is returned.)
foreach (['rest_post_query', 'rest_page_query'] as $hook) {
    add_filter($hook, function (array $args) {
        if (!is_user_logged_in()) {
            $args['has_password'] = false;
        }
        return $args;
    });
}

// 4. Tell MOTOR to refresh its blog cache when published content changes.
function motor_headless_notify(): void
{
    static $sent = false;
    $url = motor_headless_config('MOTOR_WEBHOOK_URL');
    $secret = motor_headless_config('MOTOR_WEBHOOK_SECRET');

    // https only; plain http just for a MOTOR running on the developer machine.
    $local = preg_match('#^http://(localhost|host\.docker\.internal)(:\d+)?/#i', $url) === 1;
    if ($sent || $url === '' || strlen($secret) < 32 || (stripos($url, 'https://') !== 0 && !$local)) {
        return;
    }

    $sent = true;
    wp_remote_post($url, [
        'timeout' => 3,
        'blocking' => false,
        'redirection' => 0,
        'headers' => ['X-Motor-WordPress-Secret' => $secret],
    ]);
}

add_action('transition_post_status', function ($new, $old, $post) {
    if (in_array($post->post_type, ['post', 'page'], true) && ($new === 'publish' || $old === 'publish')) {
        motor_headless_notify();
    }
}, 10, 3);

add_action('post_updated', function ($id, $after, $before) {
    if ($after->post_status === 'publish' && in_array($after->post_type, ['post', 'page'], true)) {
        motor_headless_notify();
    }
}, 10, 3);

add_action('deleted_post', fn() => motor_headless_notify());
add_action('edited_term', fn() => motor_headless_notify());
add_action('delete_term', fn() => motor_headless_notify());
