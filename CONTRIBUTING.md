# Contributing to Bookmarkarr

## Before You Start

Discuss large model, API, or database changes with the repository owner before implementation. Keep audiobook and ebook behavior edition-aware, preserve Listenarr attribution and AGPL licensing, and avoid changes that silently widen search results or cross media roots.

## Development Setup

Follow [RECREATE.md](RECREATE.md). Use .NET 10, Node.js 24, Python 3.11+, and Docker. Never commit `.env`, API credentials, runtime databases, media, downloads, build output, or generated migration reports.

## Required Checks

```bash
dotnet test bookmarkarr.slnx -c Release -p:SkipFrontendBuild=true
npm ci --prefix fe
npm run --prefix fe type-check
npm run --prefix fe test:unit
python3 -m unittest discover -s tools/bookmarkarr-migrate -p 'test_*.py'
docker compose -f docker-compose.yml -f docker-compose.dev.yml config --quiet
docker build -t bookmarkarr:local .
```

Add regression tests for each bug fix. Integration adapters must use mocks or legal fixtures and must not download copyrighted production content.

## Database Changes

Generate EF Core migrations from the actual model, include the designer and updated snapshot, and test both a fresh database and upgrade from the released schema. Migrations must preserve edition identity and avoid rewriting existing per-edition settings.

## Pull Requests and Commits

Keep commits focused with concise descriptive messages. Explain user-visible behavior, migration impact, verification performed, and any deferred external validation. Do not add assistant attribution or co-author trailers.
