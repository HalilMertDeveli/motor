# -----------------------------------------------------------
# Creates a Docker image from an existing build artifact
# -----------------------------------------------------------

ARG ASPNET_TAG=10.0

FROM mcr.microsoft.com/dotnet/aspnet:${ASPNET_TAG}
# Git commit of the source, so a running image maps to its Corresponding Source (AGPL)
ARG MOTOR_REVISION=unknown
LABEL org.opencontainers.image.title="MOTOR" \
      org.opencontainers.image.description="MOTOR store, based on Smartstore Community Edition" \
      org.opencontainers.image.licenses="AGPL-3.0-only" \
      org.opencontainers.image.version="${MOTOR_REVISION}" \
      org.opencontainers.image.revision="${MOTOR_REVISION}"
# Unprivileged port: the app runs as the non-root "app" user of the .NET image (F8).
EXPOSE 8080
ENV ASPNETCORE_URLS="http://+:8080"

# Copy
ARG EDITION=Community
ARG VERSION=6.4.0
ARG RUNTIME=linux-x64
ARG SOURCE=build/artifacts/${EDITION}.${VERSION}.${RUNTIME}

WORKDIR /app
COPY ${SOURCE} ./
COPY LICENSE ./

# Install wkhtmltopdf
COPY install-wkhtmltopdf.sh /tmp/
# (strip CR: a Windows checkout may turn the script into CRLF)
RUN sed -i 's/\r$//' /tmp/install-wkhtmltopdf.sh && \
    chmod +x /tmp/install-wkhtmltopdf.sh && \
    /tmp/install-wkhtmltopdf.sh && \
    rm /tmp/install-wkhtmltopdf.sh

# Run as the non-root "app" user ($APP_UID, 1654) of the .NET base image. Writable are
# only the directories Smartstore writes at runtime and checks during installation
# (Smartstore/IO/FilePermissionChecker.cs): App_Data, Modules, wwwroot/exchange and
# runtimes/<rid>/native. All other application files stay owned by root. Volumes created
# by older root images need a one-time chown, see docs/motor/docker.md ("Non-root image").
RUN rid="linux-$(case "$(uname -m)" in aarch64) echo arm64 ;; *) echo x64 ;; esac)" && \
    mkdir -p /app/App_Data/Tenants /app/App_Data/Logs /app/Modules /app/wwwroot/exchange "/app/runtimes/$rid/native" && \
    chown -R $APP_UID:$APP_UID /app/App_Data /app/Modules /app/wwwroot/exchange "/app/runtimes/$rid/native"
USER $APP_UID

ENTRYPOINT ["./Smartstore.Web", "--urls", "http://0.0.0.0:8080"]
