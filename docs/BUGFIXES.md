# Bookmarkarr Bug-Fix Ledger

This append-only ledger tracks inherited and production regressions. Resolved entries are not removed; newly discovered production bugs must be added with their root cause, implementation, regression coverage, and validation evidence.

## BF-001 — Extension-shaped single-file download directory

- Status: Resolved and validated
- Root cause: qBittorrent-compatible clients can report a single-file torrent as a directory whose name includes the media extension. The translated directory was passed to the processor and discarded by `File.Exists`, so the import exhausted its retries with `No importable files found`.
- Implementation: `bookmarkarr.application/Downloads/Common/DownloadClientGateway.cs` resolves only the exact same-named nested audio or ebook file after remote-path translation. Resolution is constrained to the mapped directory, rejects rooted/parent-relative escape and reparse points, and never recursively selects unrelated siblings from an extension-shaped directory.
- Regression tests: `DownloadClientGatewayTests.GetQueueItemAsync_ExtensionShapedSourceDirectory_ResolvesExactNestedAudiobookOnly` uses a generic `Example Book.m4b/Example Book.m4b` layout; `GetQueueItemAsync_ExtensionShapedContentDirectory_ResolvesExactNestedEbookOnly` covers ebook content paths and unrelated siblings.
- Validation: 2026-08-02, containment-focused run passed 3/3 and full backend suite passed 1,235/1,235.

## BF-002 — Stable download-client identifiers

- Status: Resolved and validated
- Root cause: title/history reconciliation could replace a durable client-specific identifier after submission, causing polling to follow the wrong repeated-title item.
- Implementation: `bookmarkarr.application/Downloads/Submission/DownloadService.cs` requires and persists the adapter submission ID; `Downloads/Common/DownloadClientMetadataUpdater.cs` stores the client ID and torrent hash; queue reconciliation preserves an already established external ID.
- Regression tests: `DownloadServiceTests` covers submission ID persistence and blank-ID rejection; `DownloadQueueServiceReconciliationTests.GetQueueAsync_RepeatedTitleHistory_DoesNotRebindEstablishedExternalId` covers repeated-title polling stability.
- Validation: 2026-08-02, focused inherited-regression group and full backend suite passed.

## BF-003 — RDT/qBittorrent `pausedUP` completion

- Status: Resolved and validated
- Root cause: fully downloaded torrents in qBittorrent-compatible `pausedUP` state were mapped as paused instead of import-ready.
- Implementation: `bookmarkarr.infrastructure/DownloadClients/Qbittorrent/QbittorrentResponseMapper.cs` maps upload-side paused/stopped states to completed when progress is 100%; `QbittorrentTorrentLookupBuilder.cs` preserves import lookup behavior.
- Regression tests: `QbittorrentAdapterTests.CompletedTorrentStates_MapToCompleted` includes `pausedUP` for both item and queue mappings.
- Validation: 2026-08-02, focused inherited-regression group and full backend suite passed.

## BF-004 — Missing non-primary companion files

- Status: Resolved and validated
- Root cause: a client-reported cover, cue, metadata, or other sidecar that had disappeared caused an otherwise valid media import to be rejected.
- Implementation: `bookmarkarr.infrastructure/Downloads/Processing/DownloadProcessingJobProcessor.cs` distinguishes missing primary media/archive paths from non-primary companions according to the requested edition.
- Regression tests: `DownloadProcessingJobProcessorTests.Import_MissingNonAudioCompanions_ImportsAvailableAudio` and `Import_MissingNonEbookCompanions_ImportsAvailableEbook`; `Import_MissingAudioFile_Retries` proves a missing primary still blocks and retries.
- Validation: 2026-08-02, focused audio/ebook sidecar tests and full backend suite passed.

## BF-005 — Bounded unavailable-file retry and manual recovery

- Status: Resolved and validated
- Root cause: completed client entries could become visible before their files were locally available, while exhausted jobs lacked a safe operator recovery path.
- Implementation: `bookmarkarr.domain/Downloads/DownloadProcessingJob.cs` provides bounded exponential backoff and attempt diagnostics; `DownloadProcessingJobProcessor.cs` records precise availability failures; `bookmarkarr.api/Features/Downloads/DownloadsController.cs` exposes authenticated `POST /api/v1/downloads/{id}/retry-import` for blocked imports.
- Regression tests: `DownloadProcessingJobProcessorTests.CompletedDownload_With_MissingSource`, `Import_DirectDownloadMissingStagedFile_RetriesWithoutExternalClientRecovery`, and `RetryJob_IsNotProcessedBeforeTheRetryTimerExpires`; `DownloadsControllerTests.RetryBlockedImport_ImportBlocked_TransitionsToImportPending` and the non-blocked rejection test cover manual retry.
- Validation: 2026-08-02, focused bounded/manual retry tests and full backend suite passed.

## BF-006 — AudiobookBay magnets through Prowlarr

- Status: Resolved and validated
- Root cause: AudiobookBay results supplied by Prowlarr may provide the usable download only in the standard Torznab `magneturl` attribute; losing it forced deployments back through a legacy proxy.
- Implementation: `bookmarkarr.application/Search/Indexers/Torznab/TorznabResponseParser.cs` preserves Torznab/Newznab magnet attributes and magnet links from a directly configured Prowlarr indexer. No proxy service is bundled or required.
- Regression tests: `IndexersNewznabParsingTests.ParseTorznabResponse_Preserves_AudiobookBay_Magnet_From_Prowlarr` uses a Prowlarr-shaped AudiobookBay result and verifies the magnet and indexer identity survive parsing.
- Validation: 2026-08-02, focused Prowlarr/AudiobookBay magnet test and full backend suite passed.

## BF-007 — Strict server-side edition search filtering

- Status: Resolved and validated
- Root cause: result classification was previously permissive enough to expose unrelated or wrong-media releases to callers and the frontend.
- Implementation: `bookmarkarr.application/Search/Filters/SearchContentTypeClassifier.cs` classifies from the requested edition, explicit formats, categories, and title signals; `Search/Indexers/Common/IndexerSearchWorkflow.cs` applies the classifier server-side before results are returned.
- Regression tests: `SearchContentTypeClassifierTests` covers audiobook, ebook, legitimate audiobook bundles, category/title conflicts, unknown books, and unrelated video/software content.
- Validation: 2026-08-02, focused classifier group and full backend suite passed.

## BF-008 — Edition-specific import validation and routing

- Status: Resolved and validated
- Root cause: a unified book can receive mixed downloads, but inherited audiobook import state could register ebook files as audio or route both media types to one library root.
- Implementation: `bookmarkarr.application/Downloads/Import/DownloadImportService.cs` has a strict ebook branch; `DownloadProcessingJobProcessor.cs` validates primary files by requested edition, routes legitimate mixed bundles to separate roots, and persists `EditionFile` ownership only for matching extensions.
- Regression tests: `BookEditionTests.File_registration_is_strictly_media_typed`, `DownloadImportServiceTests.EbookImport_CopiesRecognizedFormatToSeparateLibrary`, `EbookImport_RejectsAudioOnlyPayload`, and `DownloadProcessingJobProcessorTests.Import_MixedBundle_RegistersAudioAndEbookOnlyToMatchingEditions`.
- Validation: 2026-08-02, focused mixed-edition routing test and full backend suite passed.

## Validation Runs

- 2026-08-02: `dotnet test` in `mcr.microsoft.com/dotnet/sdk:10.0`, filter `FullyQualifiedName~DownloadClientGatewayTests.GetQueueItemAsync_ExtensionShaped` — passed 2/2.
- 2026-08-02: focused .NET 10 regression run covering extension-shaped paths, ebook sidecars, mixed-edition routing, and Prowlarr/AudiobookBay magnets — passed 5/5.
- 2026-08-02: focused .NET 10 inherited-regression group covering stable IDs, `pausedUP`, sidecars, bounded/manual retry, strict filtering, ebook import, and edition typing — passed 47/47.
- 2026-08-02: containment-focused extension-shaped directory run, including symlink escape rejection — passed 3/3.
- 2026-08-02: complete backend/API/database/adapter/regression suite in .NET 10 — passed 1,235/1,235.
- 2026-08-02: frontend Vue TypeScript check passed; Vitest passed 395/395 tests across 75 files (1 file intentionally skipped).
- 2026-08-02: Goodreads HTTP API/CSRF/transaction/idempotence/ambiguity integration tests — passed 2/2.
- 2026-08-02: dependency audit after lock refresh — zero runtime or development vulnerabilities.
- 2026-08-02: migration safety suite — passed 4/4, including rollback and source immutability.
- 2026-08-02: final current-source backend/API/database/adapter/regression suite — passed 1,238/1,238.
- 2026-08-02: production Docker image built successfully from current source using Node 24; the production frontend bundle completed and reported zero npm audit findings.
- 2026-08-02: portable one-service Compose configuration validated; the readiness endpoint reported current migrations and database connectivity, the frontend returned HTTP 200, and container health was healthy with zero restarts.
- 2026-08-02: persisted SQLite inspection confirmed `Books`, `BookEditions`, `EditionFiles`, and `CatalogImportBatches`, with `20260802064333_AddUnifiedBookEditionsAndCatalogImports` as the latest migration.
- 2026-08-02: isolated empty-volume startup using the final image completed healthy with zero restarts, no error log entries, and exactly one canonical `ApplicationSettings` row (`Id=1`).
- 2026-08-02: reported download-client/root-folder/add-response/queue-polling regression group passed 60/60 in .NET 10; architecture-inclusive follow-up passed 78/78.
- 2026-08-02: final current-source backend suite passed 1,247/1,247; Vue TypeScript checking passed; Vitest passed 397/397 tests across 76 files (1 file intentionally skipped).

## BF-009 — Fresh-start application-settings initialization race

- Status: Resolved and validated
- Root cause: multiple hosted services request the singleton application-settings row during first startup. Independent scopes can all observe row 1 as absent and race to insert it, producing a transient unique-key error even though one insert succeeds.
- Implementation: `bookmarkarr.infrastructure/Persistence/Repositories/EfApplicationSettingsRepository.cs` serializes singleton saves; `BookmarkarrDbContext.cs` preserves EF concurrency exceptions for repository handling; `ConfigurationService.cs` serializes the complete get/create/default-save initialization sequence and promotes real edit payloads to the loaded version. Version-zero partial-update compatibility and optimistic conflict checks remain active for real settings edits.
- Regression tests: `ApplicationSettingsConcurrencyTests.ConcurrentFreshStart_CreatesOneCanonicalSettingsRowWithoutErrors` releases eight independent SQLite contexts simultaneously and requires one row, no caller errors, and a version increment for every serialized write. The stale-update test still requires a stable conflict response.
- Validation: A stale-binary stress run reproduced the prior edge; after rebuilding current source, the focused settings/concurrency/import group passed 42/42 and the full backend suite passed 1,238/1,238. An isolated empty-volume startup of the final image was healthy with zero restarts, no error log entries, and exactly one settings row with `Id=1`.

## BF-010 — Download-client category and update API compatibility

- Status: Resolved and validated
- Root cause: `DownloadClientConfiguration` persisted category only inside `SettingsJson`; a common top-level `category` payload was accepted but silently discarded. Read responses also omitted `DownloadPath`, and the controller exposed only POST upsert, causing conventional `PUT /download-clients/{id}` calls to return 405.
- Implementation: the domain model exposes a non-mapped category compatibility property that normalizes into `SettingsJson` regardless of JSON property order; summary/detail responses include category and download path; the controller accepts route-ID PUT updates while retaining POST upsert compatibility.
- Regression tests: `DownloadClientConfiguration_TopLevelCategory_PersistsInsideSettingsJson`, `UpdateDownloadClientConfiguration_PutRoute_UpsertsRouteIdAndCategory`, and `DownloadClientDetailResponse_IncludesDownloadPathAndCategory`.
- Validation: focused reported-defect regression group passed 60/60 in .NET 10; full backend suite passed 1,247/1,247; frontend type checking and 397/397 Vitest tests passed.

## BF-011 — Unsafe application-directory library fallback

- Status: Resolved and validated
- Root cause: an empty output path with no default root was persisted as `AppContext.BaseDirectory`, which is `/app/` in the container image. Adds could then write media into the ephemeral application layer despite zero configured root folders.
- Implementation: settings initialization never selects the application directory, clears previously persisted application-directory values, and uses a configured default root only. `LibraryAddService` rejects implicit adds without a root, validates selected-root containment, records `RootFolderId` on the edition, and the add dialog disables submission until a configured root or explicit custom destination is selected.
- Regression tests: `GetApplicationSettings_WithoutRootFolder_LeavesOutputPathEmpty`, `GetApplicationSettings_ExistingApplicationDirectoryOutput_IsCleared`, `AddToLibrary_WithoutRootFolderOrExplicitDestination_ReturnsBadRequestWithoutWritingBook`, and `AddToLibrary_DestinationOutsideSelectedRoot_ReturnsBadRequest`.
- Validation: focused reported-defect regression group passed 60/60 in .NET 10; full backend suite passed 1,247/1,247; frontend type checking and 397/397 Vitest tests passed.

## BF-012 — Cyclic library-add response after a committed write

- Status: Resolved and validated
- Root cause: the add endpoint returned the tracked audiobook/entity navigation graph directly. `BookEdition.Book.Editions` recursed beyond the serializer depth after the database write had committed, so callers saw a broken response and could retry a successful add.
- Implementation: every add and duplicate-conflict response now uses `AudiobookDtoFactory`; the DTO includes the unified edition data required by the frontend but contains no back-references.
- Regression tests: `AddToLibrary_ResponseUsesNonCyclicDto_AndEditionTracksDefaultRoot` serializes the response and verifies the selected root survives in its edition DTO.
- Validation: focused reported-defect regression group passed 60/60 in .NET 10; full backend suite passed 1,247/1,247.

## BF-013 — Parallel queue polls shared a scoped EF Core context

- Status: Resolved and validated
- Root cause: `DownloadClientQueuePoller` launched per-client tasks concurrently through one scoped `IDownloadClientGateway`. Path translation beneath that gateway queried one scoped remote-path repository/DbContext from several tasks, producing EF Core's second-operation-on-context exception.
- Implementation: an infrastructure queue fetcher creates and owns an asynchronous dependency scope for each client poll and resolves its own gateway/repositories/DbContext. A timed-out task retains its scope until cancellation completes, while poll concurrency and stale-snapshot behavior remain intact.
- Regression tests: `ScopedDownloadClientQueueFetcherTests.GetQueueAsync_CreatesIndependentScopeForEachConcurrentClientPoll` starts simultaneous clients and requires two distinct scoped gateway instances; queue reconciliation coverage continues to pass.
- Validation: focused reported-defect regression group passed 60/60 and the architecture-inclusive follow-up passed 78/78 in .NET 10; full backend suite passed 1,247/1,247.
