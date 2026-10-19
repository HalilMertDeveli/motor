# -----------------------------------------------------------
# Creates a Docker image by building and publishing
# the source within the container
# -----------------------------------------------------------

# The SDK must match global.json: newer SDKs ship a Razor
# compiler that rejects some Admin views (RZ2009).
ARG SDK_TAG=10.0.202
ARG ASPNET_TAG=10.0

FROM mcr.microsoft.com/dotnet/sdk:${SDK_TAG} AS build

# Copy solution and source
ARG SOLUTION=Smartstore.sln
WORKDIR /app
COPY $SOLUTION global.json nuget.config Directory.Build.props ./
COPY src/ ./src
COPY test/ ./test

# Create Modules dir if missing
RUN mkdir /app/src/Smartstore.Web/Modules -p -v

# Build
RUN dotnet build $SOLUTION -c Release

# Publish
WORKDIR /app/src/Smartstore.Web
RUN dotnet publish Smartstore.Web.csproj -c Release -o /app/release/publish \
	--no-self-contained \
	--no-restore

# Build Docker image
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
WORKDIR /app
COPY --from=build /app/release/publish .
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
