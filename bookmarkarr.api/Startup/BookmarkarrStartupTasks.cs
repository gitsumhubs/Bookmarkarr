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

using Serilog;

namespace Bookmarkarr.Api.Startup;

public static class BookmarkarrStartupTasks
{
    public static async Task RunBookmarkarrStartupTasksAsync(this WebApplication app)
    {
        await app.WarnIfAuthenticationDisabledAsync();
    }

    private static async Task WarnIfAuthenticationDisabledAsync(this WebApplication app)
    {
        try
        {
            using var authWarningScope = app.Services.CreateScope();
            var configurationService = authWarningScope.ServiceProvider.GetService<IConfigurationService>();
            var startupCfg = configurationService != null ? await configurationService.GetStartupConfigAsync() : null;
            var authRaw = startupCfg?.AuthenticationRequired;
            var authEnabled = authRaw?.Trim().ToLowerInvariant() is "true" or "yes" or "1" or "enabled";
            if (!authEnabled)
            {
                Log.Logger.Warning(
                    "[Startup] Authentication is DISABLED. Bookmarkarr should only be exposed on a trusted LAN/VPN in this mode. If exposed to the internet, enable Bookmarkarr authentication or enforce authentication at your reverse proxy.");
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException)
        {
            Log.Logger.Debug(ex, "[Startup] Failed to evaluate authentication-enabled startup warning");
        }
    }
}
