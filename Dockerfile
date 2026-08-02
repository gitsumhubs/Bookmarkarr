# Bookmarkarr Monorepo Dockerfile
# Builds both backend (.NET API) and frontend (Vue.js) into a single container

# Build gosu with a modern Go toolchain to avoid golang/stdlib CVEs present in
# the Debian-packaged version (compiled with Go 1.19.x). Use Go 1.26 (current
# stable) to pick up all 2026 stdlib security patches.
FROM golang:1.26.2-alpine AS gosu-builder
ARG GOSU_VERSION=1.19
RUN CGO_ENABLED=0 go install github.com/tianon/gosu@${GOSU_VERSION}

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 4545
ENV ASPNETCORE_URLS=http://*:4545
ENV DOCKER_ENV=true

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["Directory.Build.props", "./"]
COPY ["Directory.Packages.props", "./"]
COPY ["bookmarkarr.api/Bookmarkarr.Api.csproj", "bookmarkarr.api/"]
COPY ["bookmarkarr.domain/Bookmarkarr.Domain.csproj", "bookmarkarr.domain/"]
COPY ["bookmarkarr.application/Bookmarkarr.Application.csproj", "bookmarkarr.application/"]
COPY ["bookmarkarr.infrastructure/Bookmarkarr.Infrastructure.csproj", "bookmarkarr.infrastructure/"]
RUN dotnet restore "bookmarkarr.api/Bookmarkarr.Api.csproj"
WORKDIR "/src/bookmarkarr.api"
# Ensure Node.js is available in the build image so MSBuild targets that run
# the frontend (npm/vite) can execute during `dotnet publish`.
# Use NodeSource to install Node 24 (Active LTS as of 2026; Node 20/22 are EOL).
RUN apt-get update \
	&& apt-get install -y --no-install-recommends curl ca-certificates gnupg \
	&& curl -fsSL https://deb.nodesource.com/setup_24.x | bash - \
	&& apt-get install -y --no-install-recommends nodejs \
	&& node --version \
	&& npm --version \
	&& apt-get clean \
	&& rm -rf /var/lib/apt/lists/*
COPY . /src
RUN dotnet build "Bookmarkarr.Api.csproj" -c Release -o /app/build \
	&& dotnet publish "Bookmarkarr.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY docker/runtime/ /tmp/bookmarkarr-runtime/

# Use the gosu binary built above instead of the apt package.
COPY --from=gosu-builder /go/bin/gosu /usr/local/bin/gosu
RUN chmod +x /usr/local/bin/gosu

RUN sh /tmp/bookmarkarr-runtime/create-bookmarkarr-user.sh

COPY --from=build /app/publish .

# Install Node.js only for the Discord bot runtime. npm is used for the install
# and then removed from the final filesystem; the bot only needs node.
RUN sh /tmp/bookmarkarr-runtime/install-discord-bot-runtime.sh

RUN sh /tmp/bookmarkarr-runtime/finalize-app.sh

# Copy entrypoint script for PUID/PGID/UMASK support
COPY docker-entrypoint.sh /docker-entrypoint.sh
RUN sh /tmp/bookmarkarr-runtime/prepare-entrypoint.sh \
	&& rm -rf /tmp/bookmarkarr-runtime

ENTRYPOINT ["/docker-entrypoint.sh"]
HEALTHCHECK --interval=30s --timeout=5s --start-period=30s --retries=5 \
  CMD curl --fail --silent --show-error http://127.0.0.1:4545/api/v1/system/ready || exit 1
