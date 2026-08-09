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

# Build the frontend once, on the machine doing the building, whatever the target architecture.
#
# It used to be built inside the per-architecture .NET stage, which meant Vite and its native
# toolchain ran under QEMU for the arm64 image. That failed two ways in one release: a build that
# printed "rendering chunks..." and hung until GitHub's six-hour job limit killed it, and then
# lightningcss failing to load its native binding outright. Neither has anything to do with the
# code being built. The output is portable JavaScript, so there is nothing to gain by emulating it.
FROM --platform=$BUILDPLATFORM node:24-bookworm AS frontend
WORKDIR /src
# npm ci runs a postinstall of `patch-package && npm run version:sync`, which reaches outside fe/
# for its patches and for the csproj it reads the version from. Copying only the manifests first
# would cache better and fail immediately, so the tree it actually needs comes in together.
COPY fe/ ./fe/
COPY scripts/ ./scripts/
COPY package.json package-lock.json ./
COPY bookmarkarr.api/Bookmarkarr.Api.csproj ./bookmarkarr.api/
WORKDIR /src/fe
RUN npm ci
RUN npm run build

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
# No Node here: the frontend arrives prebuilt from the stage above, so SkipFrontendBuild leaves
# MSBuild with nothing to shell out to. That also spares this stage an apt+NodeSource install
# under emulation, which was itself several minutes of the arm64 build.
COPY . /src
RUN dotnet build "Bookmarkarr.Api.csproj" -c Release -o /app/build /p:SkipFrontendBuild=true \
	&& dotnet publish "Bookmarkarr.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false /p:SkipFrontendBuild=true

# The two MSBuild targets that would have populated wwwroot are skipped with the frontend build,
# so place the compiled assets where they would have landed.
COPY --from=frontend /src/fe/dist /app/publish/wwwroot

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
