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
import { ref, type Ref } from 'vue'

export type SortDirection = 'asc' | 'desc'

/** A cell's sortable value. Null and undefined mean "no value" and always sort last. */
export type SortValue = string | number | Date | null | undefined

export type SortAccessors<T, K extends string> = Record<K, (item: T) => SortValue>

export interface TableSort<T, K extends string> {
  sortKey: Ref<K | null>
  sortDirection: Ref<SortDirection>
  /** Ascending on first click of a column, descending on the next, and so on. */
  toggleSort: (key: K) => void
  /** 'asc' | 'desc' for the active column, otherwise null — for header arrow state. */
  directionFor: (key: K) => SortDirection | null
  /** Returns a sorted copy. Unsorted when no column is active, preserving load order. */
  sortItems: (items: readonly T[]) => T[]
}

const collator = new Intl.Collator(undefined, {
  numeric: true,
  sensitivity: 'base',
})

function isMissing(value: SortValue): boolean {
  return value === null || value === undefined || value === ''
}

function compare(a: SortValue, b: SortValue): number {
  if (a instanceof Date || b instanceof Date) {
    return Number(a instanceof Date ? a : new Date(String(a))) -
      Number(b instanceof Date ? b : new Date(String(b)))
  }

  if (typeof a === 'number' && typeof b === 'number') {
    return a === b ? 0 : a > b ? 1 : -1
  }

  return collator.compare(String(a), String(b))
}

/**
 * Click-to-sort state for a table, scoped to the component that creates it.
 *
 * State lives in the composable rather than in storage, so it resets whenever the view
 * is left and re-entered — a sort is a way to answer a question in the moment, not a
 * preference to carry forward.
 */
export function useTableSort<T, K extends string>(
  accessors: SortAccessors<T, K>,
): TableSort<T, K> {
  const sortKey = ref<K | null>(null) as Ref<K | null>
  const sortDirection = ref<SortDirection>('asc')

  function toggleSort(key: K): void {
    if (sortKey.value === key) {
      sortDirection.value = sortDirection.value === 'asc' ? 'desc' : 'asc'
      return
    }

    sortKey.value = key
    sortDirection.value = 'asc'
  }

  function directionFor(key: K): SortDirection | null {
    return sortKey.value === key ? sortDirection.value : null
  }

  function sortItems(items: readonly T[]): T[] {
    const key = sortKey.value
    if (!key) return [...items]

    const accessor = accessors[key]
    if (!accessor) return [...items]

    const factor = sortDirection.value === 'asc' ? 1 : -1

    // Comparing accessor output directly would re-derive every value on each of the
    // O(n log n) comparisons, which is wasteful for the formatted strings these tables use.
    return items
      .map((item, index) => ({ item, index, value: accessor(item) }))
      .sort((a, b) => {
        // Rows with no value stay at the bottom in both directions, so this verdict is
        // returned before the direction factor is applied. Letting it flip would surface a
        // block of blank cells at the top of a descending sort and bury the rows the user
        // actually asked to see.
        const aMissing = isMissing(a.value)
        const bMissing = isMissing(b.value)
        if (aMissing || bMissing) {
          if (aMissing && bMissing) return a.index - b.index
          return aMissing ? 1 : -1
        }

        const result = compare(a.value, b.value)
        // Ties keep their original relative order rather than being reshuffled.
        return result === 0 ? a.index - b.index : result * factor
      })
      .map((entry) => entry.item)
  }

  return { sortKey, sortDirection, toggleSort, directionFor, sortItems }
}
