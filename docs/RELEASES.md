# Releases and GHCR

## Continuous Integration

`.github/workflows/ci.yml` restores locked .NET dependencies, installs Node 24 dependencies, type-checks and unit-tests the frontend, runs backend tests, runs migration tests, validates Compose, and checks the Dockerfile build definition.

## Version Releases

Pushing a semantic version tag such as `v1.3.0` to GitHub triggers `.github/workflows/publish-ghcr.yml`. It builds and publishes `linux/amd64` and `linux/arm64` through Buildx.

Generated GHCR tags are:

- Exact semantic version, such as `1.3.0`
- Minor line, such as `1.3`
- Major line, such as `1`
- `latest`

The image name is `ghcr.io/<repository-owner>/bookmarkarr`. GitHub’s workflow token receives only `contents: read` and `packages: write`.

## Public Repository

The public source repository is `https://github.com/gitsumhubs/Bookmarkarr`. Push a semantic version tag to publish the matching GHCR image, then configure package visibility in GitHub if anonymous pulls are intended.

## Local Release Validation

```bash
docker build -t bookmarkarr:release-candidate .
docker run --rm --entrypoint dotnet bookmarkarr:release-candidate --info
```

Also run all commands in the README development/testing section and verify a clean application startup against a fresh config directory before tagging.
