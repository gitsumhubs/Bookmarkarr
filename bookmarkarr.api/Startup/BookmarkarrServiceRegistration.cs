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

using System.Text.Json.Serialization;
using Asp.Versioning;
using Bookmarkarr.Api.Filters;
using Microsoft.AspNetCore.Mvc;

namespace Bookmarkarr.Api.Startup;

public static class BookmarkarrServiceRegistration
{
    public static WebApplicationBuilder AddBookmarkarrApiServices(
        this WebApplicationBuilder builder,
        IFileSystem fileSystem)
    {
        if (builder.Environment.IsEnvironment("Test"))
        {
            Program.ApplyTestHostPatches(builder);
        }

        builder.Services.AddControllers(options =>
            {
                options.Filters.Add<ServerErrorProblemDetailsFilter>();
            })
            .AddJsonOptions(options =>
            {
                options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
                options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
            });

        builder.Services.AddProblemDetails();
        builder.Services.AddExceptionHandler<BookmarkarrExceptionHandler>();
        builder.Services.AddBookmarkarrApiVersioning();
        builder.Services.AddAuthorization();
        builder.Services.AddBookmarkarrReverseProxyHeaders();
        builder.Services.AddBookmarkarrRealtime();
        builder.Services.AddBookmarkarrDomainWorkflows();
        builder.Services.AddBookmarkarrDevelopmentCors(builder.Environment);
        builder.Services.AddBookmarkarrSwagger(fileSystem);
        builder.Services.AddBookmarkarrSecurity(builder.Configuration, builder.Environment, fileSystem);

        return builder;
    }

    private static IServiceCollection AddBookmarkarrApiVersioning(this IServiceCollection services)
    {
        var defaultApiVersion = new ApiVersion(1, 0);
        services
            .AddApiVersioning(options =>
            {
                options.DefaultApiVersion = defaultApiVersion;
                options.AssumeDefaultVersionWhenUnspecified = true;
                options.ReportApiVersions = true;
                options.ApiVersionReader = new UrlSegmentApiVersionReader();
            })
            .AddMvc(options =>
            {
                foreach (var controllerType in typeof(Program).Assembly.GetTypes()
                             .Where(t =>
                                 !t.IsAbstract
                                 && t.Name.EndsWith("Controller", StringComparison.OrdinalIgnoreCase)
                                 && typeof(ControllerBase).IsAssignableFrom(t)))
                {
                    options.Conventions.Controller(controllerType).HasApiVersion(defaultApiVersion);
                }
            })
            .AddApiExplorer(options =>
            {
                options.GroupNameFormat = "'v'VVV";
                options.SubstituteApiVersionInUrl = true;
            });

        return services;
    }
}
