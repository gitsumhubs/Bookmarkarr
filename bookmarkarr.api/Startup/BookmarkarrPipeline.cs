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

using Asp.Versioning.ApiExplorer;
using Bookmarkarr.Api.Middleware;
namespace Bookmarkarr.Api.Startup;

public static class BookmarkarrPipeline
{
    public static WebApplication UseBookmarkarrRequestPipeline(
        this WebApplication app,
        Action<IEndpointRouteBuilder> mapRealtimeHubs)
    {
        app.UseBookmarkarrSwaggerUi();
        app.UseMiddleware<RequestTelemetryMiddleware>();
        app.UseBookmarkarrExceptionHandler();
        app.UseForwardedHeaders();
        app.MapBookmarkarrStaticAssets();
        app.UseRouting();
        app.UseMiddleware<RequestBodyLoggingMiddleware>();
        app.UseBookmarkarrDevelopmentCors();
        app.UseBookmarkarrSecurityMiddleware();
        app.UseAuthorization();
        app.MapControllers();
        mapRealtimeHubs(app);
        app.MapFallbackToFile("index.html");

        return app;
    }

    private static void UseBookmarkarrSwaggerUi(this WebApplication app)
    {
        if (!app.Environment.IsDevelopment())
        {
            return;
        }

        var apiVersionDescriptionProvider = app.Services.GetRequiredService<IApiVersionDescriptionProvider>();
        app.UseSwagger();
        app.UseSwaggerUI(options =>
        {
            foreach (var description in apiVersionDescriptionProvider.ApiVersionDescriptions)
            {
                options.SwaggerEndpoint(
                    $"/swagger/{description.GroupName}/swagger.json",
                    $"Bookmarkarr API {description.GroupName.ToUpperInvariant()}");
            }
        });
    }

    private static void UseBookmarkarrExceptionHandler(this WebApplication app)
    {
        app.UseExceptionHandler();
    }

    private static void UseBookmarkarrDevelopmentCors(this WebApplication app)
    {
        if (app.Environment.IsDevelopment())
        {
            app.UseCors("DevOnly");
        }
    }
}
