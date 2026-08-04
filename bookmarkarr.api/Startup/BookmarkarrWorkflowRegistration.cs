/*
 * Bookmarkarr - Audiobook Management System
 * Copyright (C) 2024-2026 Bookmarkarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 * GNU Affero General Public License for more details.
 *
 * You should have received a copy of the GNU Affero General Public License
 * along with this program. If not, see <https://www.gnu.org/licenses/>.
 */

using Bookmarkarr.Application.Common;
using Bookmarkarr.Application.Search.Filters;
using Bookmarkarr.Application.Search.Strategies;
using Bookmarkarr.Api.Features.CatalogImports;

namespace Bookmarkarr.Api.Startup;

public static class BookmarkarrWorkflowRegistration
{
    public static IServiceCollection AddBookmarkarrDomainWorkflows(this IServiceCollection services)
    {
        services.AddScoped<IRootFolderService, RootFolderService>();
        services.AddScoped<ILegacyOutputPathMigrator, LegacyOutputPathMigrator>();
        services.AddMemoryCache();

        services.AddBookmarkarrMetadataWorkflows();
        services.AddBookmarkarrSearchWorkflows();
        services.AddBookmarkarrControllerWorkflows();

        return services;
    }

    private static IServiceCollection AddBookmarkarrMetadataWorkflows(this IServiceCollection services)
    {
        services.AddScoped<MetadataConverters>();
        services.AddScoped<MetadataMerger>();
        services.AddScoped<MetadataSourceCatalog>();
        services.AddScoped<IMetadataStrategy, AudibleMetadataStrategy>();
        services.AddScoped<IMetadataStrategy, AudnexusStrategy>();
        services.AddScoped<MetadataStrategyCoordinator>();
        services.AddScoped<AudibleAuthorPageCollector>();
        services.AddScoped<AudibleSimpleLookupWorkflow>();
        services.AddScoped<AudibleAuthorSearchWorkflow>();
        return services;
    }

    private static IServiceCollection AddBookmarkarrSearchWorkflows(this IServiceCollection services)
    {
        services.AddScoped<SearchProgressReporter>();
        services.AddScoped<IndexerAdditionalSettingsParser>();
        services.AddScoped<IndexerSearchWorkflow>();
        services.AddScoped<ISearchResultFilter, KindleEditionFilter>();
        services.AddScoped<ISearchResultFilter, AudiobookOnlyFilter>();
        services.AddScoped<ISearchResultFilter, PromotionalTitleFilter>();
        services.AddScoped<ISearchResultFilter, ProductLikeTitleFilter>();
        services.AddScoped<ISearchResultFilter, MissingInformationFilter>();
        services.AddScoped<SearchResultFilterPipeline>();
        services.AddScoped<AsinCandidateCollector>();
        services.AddScoped<AsinEnricher>();
        services.AddScoped<SearchResultScorerService>();
        services.AddScoped<SearchResultSortingService>();
        services.AddScoped<SearchFinalDispositionLogger>();
        services.AddScoped<AsinSearchHandler>();
        return services;
    }

    private static IServiceCollection AddBookmarkarrControllerWorkflows(this IServiceCollection services)
    {
        services.AddScoped<LibraryMetadataRescanWorkflow>();
        services.AddScoped<LibraryScanPathResolver>();
        services.AddScoped<LibraryScanQueueWorkflow>();
        services.AddScoped<LibraryAddWorkflow>();
        services.AddScoped<LibraryManualScanWorkflow>();
        services.AddScoped<LibraryBulkEditWorkflow>();
        services.AddScoped<LibraryMoveWorkflow>();
        services.AddScoped<LibraryDeleteWorkflow>();
        services.AddScoped<LibraryUpdateWorkflow>();
        services.AddScoped<LibraryIdentifierWorkflow>();
        services.AddScoped<LibraryPreviewPathWorkflow>();
        services.AddScoped<LibraryQueryWorkflow>();
        services.AddScoped<LibraryRenameWorkflow>();
        services.AddScoped<LibraryStatusReconciliationWorkflow>();
        services.AddScoped<SearchResponseMapper>();
        services.AddScoped<ImagePlaceholderResolver>();
        services.AddScoped<IndexerTestWorkflow>();
        services.AddScoped<ProwlarrIndexerImportWorkflow>();
        services.AddScoped<ProwlarrIndexerNotificationWorkflow>();
        services.AddScoped<ProwlarrIndexerUpsertWorkflow>();
        services.AddScoped<StructuredSearchWorkflow>();
        services.AddScoped<SearchByTitleWorkflow>();
        services.AddScoped<Bookmarkarr.Api.Features.Library.EbookLibraryImportWorkflow>();
        services.AddScoped<ManualImportPathPlanner>();
        services.AddScoped<ManualImportCompanionImporter>();
        // Singleton: it outlives the request that schedules it and creates its own scopes.
        services.AddSingleton<GoodreadsCatalogImportAutoDownloadWorkflow>();
        return services;
    }
}
