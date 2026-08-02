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
using Bookmarkarr.Tests.Mocks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;

namespace Bookmarkarr.Tests.Features.Infrastructure.Persistence
{
    public class TestDatabaseIsolationTests : IClassFixture<BookmarkarrWebApplicationFactory>
    {
        private readonly BookmarkarrWebApplicationFactory _factory;

        public TestDatabaseIsolationTests(BookmarkarrWebApplicationFactory factory)
        {
            _factory = factory;
        }

        [Fact]
        public void TestHost_UsesIsolatedSqlitePath_NotRepoConfigDatabase()
        {
            using var scope = _factory.Services.CreateScope();
            var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
            var env = scope.ServiceProvider.GetRequiredService<IWebHostEnvironment>();

            var configuredPath = config["Bookmarkarr:SqliteDbPath"];
            Assert.False(string.IsNullOrWhiteSpace(configuredPath));

            var resolvedPath = Path.GetFullPath(
                Path.IsPathRooted(configuredPath!)
                    ? configuredPath!
                    : Path.Join(env.ContentRootPath, configuredPath!));

            var repoDbPath = Path.GetFullPath(Path.Join(env.ContentRootPath, "config", "database", "bookmarkarr.db"));
            Assert.False(
                string.Equals(resolvedPath, repoDbPath, StringComparison.OrdinalIgnoreCase),
                $"Test host resolved sqlite path to repo DB: {resolvedPath}");

            var tempRoot = Path.GetFullPath(Path.GetTempPath());
            Assert.True(
                resolvedPath.StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase),
                $"Expected test sqlite path under temp root '{tempRoot}', got '{resolvedPath}'.");
            Assert.Contains("bookmarkarr-tests", resolvedPath, StringComparison.OrdinalIgnoreCase);
        }
    }
}

