#!/bin/bash
set -e

# Install wkhtmltopdf dependencies for Debian Trixie (.NET 10)
echo "deb [trusted=yes] http://deb.debian.org/debian bookworm main" > /etc/apt/sources.list.d/bookworm.list
apt-get update
apt-get -y install --no-install-recommends \
    wget \
    ca-certificates \
    libjpeg62-turbo \
    libxrender1 \
    libfontconfig1 \
    libx11-6 \
    libxext6 \
    libssl3t64 \
    fonts-liberation \
    xfonts-75dpi \
    xfonts-base

# Download and install wkhtmltopdf for the image's architecture (amd64 or arm64)
pkg="wkhtmltox_0.12.6.1-3.bookworm_$(dpkg --print-architecture).deb"
wget "https://github.com/wkhtmltopdf/packaging/releases/download/0.12.6.1-3/$pkg"
dpkg --force-depends -i "./$pkg"
apt-get -y --fix-broken install

# Cleanup
rm "./$pkg"
rm /etc/apt/sources.list.d/bookworm.list
apt-get update
apt-get clean
rm -rf /var/lib/apt/lists/*