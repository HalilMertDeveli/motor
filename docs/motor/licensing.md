# MOTOR licensing (AGPL v3)

This is an engineering summary, not legal advice. Decisions marked
**OWNER DECISION** need the owner's approval, ideally after asking a lawyer.

## What applies

- Smartstore Community Edition is licensed under the GNU AGPL v3 (`LICENSE`).
- MOTOR is a modified version of it. The MotorDark theme, MotorShop modules
  (Fitment, Security, ...) and the build files run in the same application and
  are part of the modified work.
- AGPL section 13: when users interact with the modified program over a
  network (a public store), they must be offered the **Corresponding Source** of
  the version that runs, free of charge, from a network server.
- Sections 4-6: handing the program or an image to someone else (customer,
  hosting partner, public image registry) is "conveying" and carries the same
  source obligation.

## What the repository and image already do

- `LICENSE` stays in the repository and is copied to `/app/LICENSE` in every
  image (`Dockerfile`, `NoBuild.Dockerfile`, `Nano.Dockerfile`).
- The image is labelled `org.opencontainers.image.licenses=AGPL-3.0-only`.
- Smartstore copyright headers, `README.md` and the storefront footer
  ("Smartstore" hint and copyright notice) are unchanged. Do not remove them.
- No secrets are part of the source or the image: `.env`, `Config/usersettings*`,
  `Config/connections*`, `App_Data/Tenants` and key files are excluded by
  `.gitignore` and the Docker ignore files.

## What is Corresponding Source

Included: the full source of the deployed commit, including MotorDark,
MotorShop modules, `Dockerfile`, Compose files and build scripts.

Not included (and must never be published): `.env` files, `Settings.txt`,
database contents, customer and order data, media uploads, data protection
keys, server configuration with host names or credentials.

## Ways to offer the source

| Option | How | Notes |
| --- | --- | --- |
| A. Public source repository | A separate public repository (mirror) that receives the deployed commits or tags. Link it in the storefront footer, e.g. "Source code". | Simplest to keep correct. The private working repository stays private. |
| B. Source archive from the store | Each release publishes `git archive <commit>` as a download on the store (or a static host) and links it in the footer. | No public repository needed; must be regenerated for every deployment. |
| C. Written offer only | "Ask us for the source." | Not enough for section 13 network use on its own. |

Whichever option: tag every deployment with its commit
(`MOTOR_IMAGE_TAG=$(git rev-parse --short HEAD)`), so the offered source matches
what runs.

## OWNER DECISION points

1. **Option A or B**, and where the "Source code" link goes in the storefront.
   Needs to be live before the store is public. The private repository is not
   made public by this work.
2. **History or snapshot**: publish the full git history, or only release
   snapshots. Before publishing anything, scan the history for secrets
   (for example with gitleaks) and for anything that should not be public.
3. **Third-party assets**: product and hero photography, fonts and icons in
   MotorDark become public with the source. Confirm their licences allow
   redistribution, or replace them, or move them out of the source tree (for
   example uploaded as media at runtime).
4. **MotorShop modules**: modules loaded into the application are treated here
   as part of the AGPL work. Keeping any of them closed would need a legal
   opinion first.
5. **Image distribution**: no image is pushed to any registry today. Publishing
   a MOTOR image (public or to third parties) also triggers the source offer.
