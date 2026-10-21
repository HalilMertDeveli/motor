# Google login (customers)

Customers register and sign in with their Google account through the built-in module
`Smartstore.Google.Auth` and Smartstore Identity. There is no second user table: a Google
account is an `ExternalAuthenticationRecord` (provider `Smartstore.GoogleAuth`, Google `sub`)
of a normal customer. No credentials belong in this file or anywhere else in the repository.

## Behavior

| Case | Result |
|---|---|
| New Google account, verified email, registration open | Customer is created (username = email, first/last name from Google, no password), gets the **Registered** role only, the guest cart moves over |
| Google account already linked | Sign-in to the linked customer; orders, addresses and cart stay |
| Email already used by a local account | **No automatic linking.** Message: sign in with the password, then link Google under *My account > Customer info* |
| Google reports `email_verified` other than `true` | Rejected, no account |
| Customer cancels at Google | "Sign-in was cancelled" message |
| Invalid/expired OAuth state, replayed callback, timeout | "Session expired" message, nothing is created |
| Linked customer is inactive or blocked | Rejected |
| Registration disabled (`UserRegistrationType = Disabled`) | New Google accounts rejected, linked ones still sign in |
| Module inactive or Client ID/secret missing | No button for customers; administrators see a notice with a link to the configuration |

Linking (customer info page) needs a signed-in customer, an antiforgery token and the
customer id in the protected OAuth state. It is not offered while an admin impersonates
a customer. `RegisterCustomerRoleId` (the additional registration role) is never given to
Google registrations, so dealer or admin roles are only assigned by an administrator.

## Google Cloud Console (manual, by the store owner)

1. Create or select a project at <https://console.cloud.google.com/>.
2. **Google Auth Platform > Branding**: app name "Prosev", support email, logo (optional),
   authorized domain (the store's domain, e.g. `prosev.com.tr` or `fly.dev` while on
   `prosev.fly.dev`), privacy policy and terms of service URLs of the store.
3. **Audience**: *External*. While in *Testing*, only listed test users can sign in;
   choose **Publish app** (*In production*) when the store goes live. The basic scopes
   below do not require Google verification.
4. **Data Access**: only `openid`, `.../auth/userinfo.email`, `.../auth/userinfo.profile`.
5. **Clients > Create client > Web application**, name e.g. "Prosev production":
   * Authorized JavaScript origins: not needed.
   * Authorized redirect URIs: `https://<store domain>/signin-google`, exactly, one per
     public host name (e.g. `https://prosev.fly.dev/signin-google` and later
     `https://www.<domain>/signin-google`).
6. Create a **separate** client for development with
   `http://localhost:<port>/signin-google`. Never reuse the production secret locally.
7. Copy Client ID and Client secret directly into the admin form (below). Do not send
   them by chat or email and do not put them into the repository, logs or a PR.

## Store admin

1. *Admin > Configuration > External authentication*: activate **Google ile giriş**.
2. *Configure*: enter Client ID and Client secret, save. The page shows the redirect URI
   built from the store URL and warns if the current request host differs.
3. *Configuration > Stores*: the store URL must be the public `https://` URL.

## Reverse proxy

The handler builds the redirect URI from the request. Behind Fly.io (or Caddy on the VPS)
`Smartstore:ReverseProxy` must be enabled with the proxy networks, so `X-Forwarded-Proto: https`
and the host are trusted (`deploy/fly/fly.toml` already does this), and `AllowedHosts` must
contain every public host name. Otherwise Google rejects the login with
`redirect_uri_mismatch`. The correlation cookie (`.AspNetCore.Correlation.*`, `HttpOnly`,
path `/signin-google`, 15 minutes) must come back on the cross-site redirect from Google, so
check after go-live that it is sent with `Secure` over HTTPS (local QA ran over plain HTTP).

## Verify after go-live

* Log in on desktop and phone with a real Google account, log out, log in again.
* Check *Admin > Customers*: one customer, role *Registered*, external record "Google".
* Try a Google account whose email already has a local account: it must not be linked.
* Server log: failures appear as `Google sign-in failed (<code>)` without codes or tokens.
