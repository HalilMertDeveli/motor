# Fly.io layer on top of the regular MOTOR image (repository root Dockerfile).
#
# Fly mounts volumes owned by root, but the MOTOR image runs as the non-root
# "app" user (1654). This layer starts as root only to hand the volume's root
# directory to "app", then drops privileges with setpriv before the app starts.
# Files the app creates on the volume are owned by "app" already, so only the
# mount point itself needs the chown.
#
# Build (from the repository root), see docs/motor/fly.md:
#   docker build -t motor-web:<sha> --build-arg MOTOR_REVISION=<sha> .
#   docker build -t registry.fly.io/<app>:<sha> --build-arg BASE_IMAGE=motor-web:<sha> deploy/fly

ARG BASE_IMAGE
FROM ${BASE_IMAGE}

USER root
# Keep the app user's home: setpriv changes the uid but not the environment.
ENV HOME=/home/app

# Public CA of Supabase ("Supabase Root 2021 CA", valid until 2031-04-26,
# SHA-256 80:70:25:AD:...:CA:FA) for "SSL Mode=VerifyFull". Not a secret.
COPY supabase-ca.crt /etc/motor/supabase-ca.crt

# Listen on [::] (dual stack): Fly's private network (fly proxy, 6PN) is IPv6-only.
ENTRYPOINT ["/bin/sh", "-c", "chown 1654:1654 /app/App_Data/Tenants && exec setpriv --reuid=1654 --regid=1654 --init-groups ./Smartstore.Web --urls http://[::]:8080"]
