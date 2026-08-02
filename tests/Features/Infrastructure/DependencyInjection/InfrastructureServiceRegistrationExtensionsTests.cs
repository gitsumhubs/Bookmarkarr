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
using Bookmarkarr.Infrastructure.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Options;

namespace Bookmarkarr.Tests.Features.Infrastructure.DependencyInjection
{
    [Trait("Name", "InfrastructureServiceRegistrationExtensionsTests")]
    [Trait("Category", "ServiceCollection")]
    public class InfrastructureServiceRegistrationExtensionsTests
    {
        [Fact]
        public void AddBookmarkarrInfrastructure_RegistersInfrastructureServices()
        {
            var services = new ServiceCollection();

            services.AddBookmarkarrInfrastructure();

            Assert.Contains(
                services,
                descriptor =>
                    descriptor.ServiceType == typeof(IImageCacheService) &&
                    descriptor.ImplementationType == typeof(ImageCacheService) &&
                    descriptor.Lifetime == ServiceLifetime.Singleton);

            Assert.Contains(
                services,
                descriptor =>
                    descriptor.ServiceType == typeof(IApplicationVersionService) &&
                    descriptor.ImplementationType == typeof(ApplicationVersionService) &&
                    descriptor.Lifetime == ServiceLifetime.Scoped);

            using var serviceProvider = services.BuildServiceProvider();
            var httpClientOptions = serviceProvider
                .GetRequiredService<IOptionsMonitor<HttpClientFactoryOptions>>()
                .Get(typeof(ImageCacheService).Name);

            Assert.NotEmpty(httpClientOptions.HttpMessageHandlerBuilderActions);
        }

        [Fact]
        public void AddBookmarkarrAppServices_RegistersMyAnonamouseConnectionTester()
        {
            var services = new ServiceCollection();

            services.AddBookmarkarrAppServices(new ConfigurationManager());

            Assert.Contains(
                services,
                descriptor =>
                    descriptor.ServiceType == typeof(IMyAnonamouseConnectionTester) &&
                    descriptor.ImplementationType == typeof(MyAnonamouseConnectionTester) &&
                    descriptor.Lifetime == ServiceLifetime.Scoped);
        }

        [Fact]
        public void AddBookmarkarrAppServices_RegistersTimeProviderForDownloadProcessingServices()
        {
            var services = new ServiceCollection();

            services.AddBookmarkarrAppServices(new ConfigurationManager());

            Assert.Contains(
                services,
                descriptor => descriptor.ServiceType == typeof(TimeProvider));

            Assert.Contains(
                services,
                descriptor =>
                    descriptor.ServiceType == typeof(IDownloadProcessingJobService) &&
                    descriptor.ImplementationType == typeof(DownloadProcessingJobService) &&
                    descriptor.Lifetime == ServiceLifetime.Scoped);
        }

        [Fact]
        public void DownloadProcessingJobService_ResolvesWithoutAdaptersOrHostedWorkers()
        {
            var services = new ServiceCollection();
            services.AddLogging();

            services.AddBookmarkarrInfrastructure(options =>
                options.UseInMemoryDatabase(Guid.NewGuid().ToString()));
            services.AddBookmarkarrAppServices(new ConfigurationManager());

            using var provider = services.BuildServiceProvider(validateScopes: true);
            using var scope = provider.CreateScope();

            var service = scope.ServiceProvider.GetRequiredService<IDownloadProcessingJobService>();

            Assert.NotNull(service);
        }
    }
}
