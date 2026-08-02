/*
 * Bookmarkarr - Audiobook Management System
 * Copyright (C) 2024-2026 Bookmarkarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */
using Bookmarkarr.Infrastructure.Persistence;
using Bookmarkarr.Infrastructure.Persistence.Repositories;
using Microsoft.Extensions.DependencyInjection;

namespace Bookmarkarr.Infrastructure.DependencyInjection.SystemDiagnostics;

internal static class SystemDiagnosticRegistrationExtensions
{
    public static IServiceCollection AddSystemDiagnosticServices(this IServiceCollection services)
    {
        services.AddSingleton<IAppMetricsService, MeterAppMetricsService>();
        services.AddSingleton<IProcessRunner, SystemProcessRunner>();
        services.AddScoped<IProcessExecutionStore, ProcessExecutionStore>();
        services.AddSingleton<IDiskSpaceProbe, DiskSpaceProbe>();
        services.AddScoped<ISystemService, SystemService>();
        services.AddScoped<ISystemReadinessService, SystemReadinessService>();
        return services;
    }

    public static IServiceCollection AddSystemDiagnosticInfrastructure(
        this IServiceCollection services,
        string? contentRootPath)
    {
        services.AddScoped<IProcessExecutionLogRepository, EfProcessExecutionLogRepository>();
        services.AddSingleton<IApplicationPathService>(_ => new ApplicationPathService(contentRootPath));
        services.AddScoped<IApplicationVersionService, ApplicationVersionService>();
        return services;
    }
}
