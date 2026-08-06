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
import { ref, onBeforeUnmount, type Ref } from 'vue'

export interface ColumnResizeOptions<K extends string> {
  /** Local-storage key. Include a version suffix so a later column change can invalidate it. */
  storageKey: string
  defaults: Record<K, number>
  minWidths: Record<K, number>
  /** Below this viewport width the table becomes a card layout and dragging is ignored. */
  mobileBreakpoint: number
}

export interface ColumnResize<K extends string> {
  widths: Ref<Record<K, number>>
  resizingColumn: Ref<K | null>
  startResize: (column: K, event: PointerEvent) => void
  resetWidths: () => void
}

/**
 * Drag-to-resize column widths, persisted per browser.
 *
 * Widths are a layout preference rather than a momentary question, so unlike sort state
 * these survive navigation and reloads — a column widened to read long titles should stay
 * that way.
 */
export function useColumnResize<K extends string>(
  options: ColumnResizeOptions<K>,
): ColumnResize<K> {
  const { storageKey, defaults, minWidths, mobileBreakpoint } = options

  const widths = ref<Record<K, number>>({ ...defaults }) as Ref<Record<K, number>>
  const resizingColumn = ref<K | null>(null) as Ref<K | null>

  let resizeState: { key: K; startX: number; startWidth: number } | null = null

  function load(): void {
    if (typeof window === 'undefined') return

    try {
      const stored = window.localStorage.getItem(storageKey)
      if (!stored) return

      const parsed = JSON.parse(stored) as Partial<Record<K, unknown>>
      for (const key of Object.keys(defaults) as K[]) {
        const value = parsed[key]
        // Only accept values that are still sane. A stored width from an older column set,
        // or a hand-edited entry, must not be able to collapse a column to nothing.
        if (typeof value === 'number' && Number.isFinite(value) && value >= minWidths[key]) {
          widths.value[key] = value
        }
      }
    } catch {
      // Unreadable or malformed storage just means defaults; never block rendering on it.
    }
  }

  function persist(): void {
    if (typeof window === 'undefined') return

    try {
      window.localStorage.setItem(storageKey, JSON.stringify(widths.value))
    } catch {
      // Private-mode or quota failures are not worth surfacing for a column width.
    }
  }

  function isMobileLayout(): boolean {
    return (
      typeof window !== 'undefined' &&
      window.matchMedia(`(max-width: ${mobileBreakpoint}px)`).matches
    )
  }

  function handleResize(event: PointerEvent): void {
    if (!resizeState) return

    const delta = event.clientX - resizeState.startX
    const key = resizeState.key
    widths.value[key] = Math.max(minWidths[key], resizeState.startWidth + delta)
  }

  function stopResize(): void {
    if (!resizeState) return

    resizeState = null
    resizingColumn.value = null
    window.removeEventListener('pointermove', handleResize)
    window.removeEventListener('pointerup', stopResize)
    document.body.style.cursor = ''
    document.body.style.userSelect = ''
    persist()
  }

  function startResize(column: K, event: PointerEvent): void {
    // The card layout has no columns to drag, so a stray touch must not start a resize.
    if (isMobileLayout()) return

    event.preventDefault()
    event.stopPropagation()

    resizeState = { key: column, startX: event.clientX, startWidth: widths.value[column] }
    resizingColumn.value = column

    window.addEventListener('pointermove', handleResize)
    window.addEventListener('pointerup', stopResize)
    document.body.style.cursor = 'col-resize'
    document.body.style.userSelect = 'none'
  }

  function resetWidths(): void {
    widths.value = { ...defaults }
    persist()
  }

  // A drag interrupted by navigation would otherwise leave the window listeners attached
  // and the body stuck showing a col-resize cursor.
  onBeforeUnmount(stopResize)

  load()

  return { widths, resizingColumn, startResize, resetWidths }
}
