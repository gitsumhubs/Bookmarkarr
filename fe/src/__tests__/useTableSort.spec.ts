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
import { describe, it, expect } from 'vitest'
import { useTableSort } from '@/composables/useTableSort'

interface Row {
  title: string
  author?: string | null
  progress: number
}

const rows: Row[] = [
  { title: 'Beta', author: 'Towles', progress: 10 },
  { title: 'alpha', author: null, progress: 9 },
  { title: 'Gamma', author: 'Hobb', progress: 100 },
]

function createSort() {
  return useTableSort<Row, 'title' | 'author' | 'progress'>({
    title: (row) => row.title,
    author: (row) => row.author,
    progress: (row) => row.progress,
  })
}

describe('useTableSort', () => {
  it('leaves items in load order until a column is chosen', () => {
    const { sortItems } = createSort()

    expect(sortItems(rows).map((r) => r.title)).toEqual(['Beta', 'alpha', 'Gamma'])
  })

  it('sorts ascending on the first click and descending on the second', () => {
    const { toggleSort, sortItems } = createSort()

    toggleSort('title')
    expect(sortItems(rows).map((r) => r.title)).toEqual(['alpha', 'Beta', 'Gamma'])

    toggleSort('title')
    expect(sortItems(rows).map((r) => r.title)).toEqual(['Gamma', 'Beta', 'alpha'])
  })

  it('starts a newly chosen column ascending rather than inheriting the last direction', () => {
    const { toggleSort, sortDirection } = createSort()

    toggleSort('title')
    toggleSort('title')
    expect(sortDirection.value).toBe('desc')

    toggleSort('author')
    expect(sortDirection.value).toBe('asc')
  })

  it('compares numbers numerically, not as text', () => {
    // '100' sorts before '9' as a string; the point of this is that it must not.
    const { toggleSort, sortItems } = createSort()

    toggleSort('progress')
    expect(sortItems(rows).map((r) => r.progress)).toEqual([9, 10, 100])
  })

  it('keeps rows with no value at the bottom in both directions', () => {
    const { toggleSort, sortItems } = createSort()

    toggleSort('author')
    expect(sortItems(rows).map((r) => r.author)).toEqual(['Hobb', 'Towles', null])

    toggleSort('author')
    expect(sortItems(rows).map((r) => r.author)).toEqual(['Towles', 'Hobb', null])
  })

  it('returns the same array when nothing is sorted', () => {
    // Identity matters: a fresh array on every recompute changes what a `watch` sees, so a
    // virtualised list rebuilds its layout on unrelated updates and loses scroll position.
    const { sortItems } = createSort()

    expect(sortItems(rows)).toBe(rows)
  })

  it('returns a new array once a column is active', () => {
    const { toggleSort, sortItems } = createSort()

    toggleSort('title')

    expect(sortItems(rows)).not.toBe(rows)
  })

  it('does not mutate the source array', () => {
    const { toggleSort, sortItems } = createSort()
    const original = [...rows]

    toggleSort('title')
    sortItems(rows)

    expect(rows).toEqual(original)
  })

  it('reports direction only for the active column', () => {
    const { toggleSort, directionFor } = createSort()

    toggleSort('title')
    expect(directionFor('title')).toBe('asc')
    expect(directionFor('author')).toBeNull()
  })
})
