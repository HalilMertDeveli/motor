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
EXPOSE 80
ENV ASPNETCORE_URLS="http://+:80"

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

ENTRYPOINT ["./Smartstore.Web", "--urls", "http://0.0.0.0:80"]
