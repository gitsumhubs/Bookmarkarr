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
using Microsoft.Extensions.Logging;

namespace Bookmarkarr.Application.Downloads.Common
{
    /// <summary>
    /// Diagnostics for the two ways a download client can answer a poll perfectly and still leave
    /// every tracked download frozen: by reporting nothing, and by reporting paths this process
    /// cannot open. Both are silent by construction, which is what makes them worth a warning.
    /// </summary>
    public partial class DownloadClientGateway
    {
        /// <summary>
        /// An id-filtered poll asks about downloads we already know exist, so a short answer is not
        /// an ordinary empty queue. Nothing else reports it: an empty JSON array is a valid,
        /// successful response, so no exception is thrown and the update loop simply never runs for
        /// the downloads left out. A partial answer is the more dangerous shape of the two, because
        /// the poll as a whole still looks healthy.
        /// </summary>
        private void WarnOnUnreportedDownloads(
            DownloadClientConfiguration client,
            List<string> ids,
            HashSet<string> matchedIds,
            int returnedItemCount)
        {
            var clientName = client.Name ?? client.Id ?? client.Type;

            if (returnedItemCount == 0)
            {
                logger.LogWarning(
                    "Download client {ClientName} returned no queue items for {TrackedCount} tracked download(s). " +
                    "They may have been removed from the client, or the client may not be honouring the id filter.",
                    clientName,
                    ids.Count);
                return;
            }

            var unmatched = ids.Where(id => !matchedIds.Contains(id)).ToList();
            if (unmatched.Count == 0)
            {
                return;
            }

            logger.LogWarning(
                "Download client {ClientName} did not report {UnmatchedCount} of {TrackedCount} tracked download(s); " +
                "their state will not advance until the client reports them again. Unreported ids: {UnmatchedIds}",
                clientName,
                unmatched.Count,
                ids.Count,
                string.Join(", ", unmatched));
        }

        /// <summary>
        /// Returns a description of the path problem, or null when there is nothing to report —
        /// including when the client's queue is empty, because an empty queue is no evidence either
        /// way and a test must not invent a failure it cannot see.
        /// </summary>
        private async Task<string?> DescribeUnreachableClientPathsAsync(
            DownloadClientConfiguration client,
            CancellationToken ct)
        {
            List<QueueItem> items;
            try
            {
                items = await GetQueueAsync(client, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException)
            {
                // The connection itself already tested clean, so a queue read that fails here is not
                // worth failing the test over. Say nothing rather than report a phantom path fault.
                logger.LogDebug(ex, "Could not read the queue while verifying paths for client {ClientId}", LogRedaction.SanitizeText(client.Id));
                return null;
            }

            var directories = items
                .Select(item => item.ContentPath ?? item.LocalPath)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(path => path!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (directories.Count == 0)
            {
                return null;
            }

            // Only report when every path is unreachable. A mixed result means the paths do resolve
            // and the gaps belong to individual downloads, not to the client's configuration.
            var unreachable = directories
                .Where(path => !fileSystem.DirectoryExists(path) && !fileSystem.FileExists(path))
                .ToList();

            if (unreachable.Count < directories.Count)
            {
                return null;
            }

            return $"However, none of the {directories.Count} path(s) it reports exist inside Bookmarkarr " +
                $"(for example '{unreachable[0]}'), so imports from this client will fail. " +
                "Add a Remote Path Mapping for this client, or configure the client to report the same paths Bookmarkarr sees.";
        }
    }
}
