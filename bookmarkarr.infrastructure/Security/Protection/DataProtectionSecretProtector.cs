/*
 * Bookmarkarr - Audiobook Management System
 * Copyright (C) 2024-2026 Bookmarkarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */

namespace Bookmarkarr.Infrastructure.Security.Protection
{
    public sealed class DataProtectionSecretProtector : ISecretProtector
    {
        private readonly IDataProtector _protector;

        public DataProtectionSecretProtector(IDataProtectionProvider provider)
        {
            _protector = provider.CreateProtector("Bookmarkarr.ConfigurationService.ProwlarrImport");
        }

        public string Protect(string plaintext) => _protector.Protect(plaintext);

        public string Unprotect(string protectedValue) => _protector.Unprotect(protectedValue);
    }
}
