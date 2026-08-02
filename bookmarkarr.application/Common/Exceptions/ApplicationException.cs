/*
 * Bookmarkarr - Audiobook Management System
 * Copyright (C) 2024-2026 Bookmarkarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */

namespace Bookmarkarr.Application.Common.Exceptions;

public abstract class BookmarkarrApplicationException : Exception
{
    protected BookmarkarrApplicationException(string code, string safeDetail, Exception? innerException = null)
        : base(safeDetail, innerException)
    {
        Code = code;
        SafeDetail = safeDetail;
    }

    public string Code { get; }

    public string SafeDetail { get; }
}

public sealed class ApplicationValidationException : BookmarkarrApplicationException
{
    public ApplicationValidationException(string code, string safeDetail)
        : base(code, safeDetail)
    {
    }
}

public sealed class ApplicationNotFoundException : BookmarkarrApplicationException
{
    public ApplicationNotFoundException(string code, string safeDetail)
        : base(code, safeDetail)
    {
    }
}

public sealed class ApplicationConflictException : BookmarkarrApplicationException
{
    public ApplicationConflictException(string code, string safeDetail, Exception? innerException = null)
        : base(code, safeDetail, innerException)
    {
    }
}

public sealed class ApplicationForbiddenException : BookmarkarrApplicationException
{
    public ApplicationForbiddenException(string code, string safeDetail)
        : base(code, safeDetail)
    {
    }
}

public sealed class ExternalServiceException : BookmarkarrApplicationException
{
    public ExternalServiceException(string code, string safeDetail, Exception? innerException = null)
        : base(code, safeDetail, innerException)
    {
    }
}
