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

/**
 * Remembers that the AudioBook Bay pagination notice was declined.
 *
 * Stored per browser rather than server-side, matching the no-auth security banner: the notice is
 * advisory, and re-prompting on every load trains people to dismiss it unread. Applying the patch
 * changes the indexer itself, so the notice stops appearing on its own once it succeeds.
 */
export const AUDIOBOOKBAY_PATCH_PREF_KEY = 'bookmarkarr.hideAudiobookBayPatchNotice'

export function getAudiobookBayPatchNoticeDismissed(): boolean {
  try {
    return window.localStorage.getItem(AUDIOBOOKBAY_PATCH_PREF_KEY) === 'true'
  } catch {
    return false
  }
}

export function setAudiobookBayPatchNoticeDismissed(dismissed: boolean): void {
  try {
    window.localStorage.setItem(AUDIOBOOKBAY_PATCH_PREF_KEY, dismissed ? 'true' : 'false')
  } catch {
    // Ignore storage failures (private mode, disabled storage, quota, etc.)
  }
}
