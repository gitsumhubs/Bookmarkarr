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
import { describe, it, expect, beforeEach, vi } from 'vitest'
import { defineComponent } from 'vue'
import { mount } from '@vue/test-utils'
import { useColumnResize, type ColumnResize } from '@/composables/useColumnResize'

type Key = 'title' | 'status'

const STORAGE_KEY = 'bookmarkarr.test.columnWidths.v1'

function setViewportWidth(matches: boolean) {
  window.matchMedia = vi.fn().mockImplementation((query: string) => ({
    matches,
    media: query,
    addEventListener: vi.fn(),
    removeEventListener: vi.fn(),
  })) as unknown as typeof window.matchMedia
}

/** Mounts a host component so onBeforeUnmount has an instance to attach to. */
function createResize(): ColumnResize<Key> {
  let api!: ColumnResize<Key>

  mount(
    defineComponent({
      setup() {
        api = useColumnResize<Key>({
          storageKey: STORAGE_KEY,
          defaults: { title: 300, status: 140 },
          minWidths: { title: 160, status: 100 },
          mobileBreakpoint: 768,
        })
        return () => null
      },
    }),
  )

  return api
}

function drag(resize: ColumnResize<Key>, column: Key, deltaX: number) {
  resize.startResize(column, {
    clientX: 0,
    preventDefault: vi.fn(),
    stopPropagation: vi.fn(),
  } as unknown as PointerEvent)

  window.dispatchEvent(new MouseEvent('pointermove', { clientX: deltaX }))
  window.dispatchEvent(new MouseEvent('pointerup'))
}

describe('useColumnResize', () => {
  beforeEach(() => {
    window.localStorage.clear()
    setViewportWidth(false)
  })

  it('starts at the supplied defaults', () => {
    const resize = createResize()

    expect(resize.widths.value).toEqual({ title: 300, status: 140 })
  })

  it('widens a column by the drag distance', () => {
    const resize = createResize()

    drag(resize, 'title', 80)

    expect(resize.widths.value.title).toBe(380)
  })

  it('will not drag a column below its minimum', () => {
    // A column dragged past its minimum would otherwise collapse to nothing and become
    // impossible to grab again.
    const resize = createResize()

    drag(resize, 'title', -1000)

    expect(resize.widths.value.title).toBe(160)
  })

  it('persists widths so they survive a remount', () => {
    const first = createResize()
    drag(first, 'status', 60)

    const second = createResize()

    expect(second.widths.value.status).toBe(200)
  })

  it('ignores stored widths that are below the minimum', () => {
    // Guards against a stored value from an older column set collapsing a column on load.
    window.localStorage.setItem(STORAGE_KEY, JSON.stringify({ title: 10, status: 140 }))

    const resize = createResize()

    expect(resize.widths.value.title).toBe(300)
  })

  it('falls back to defaults when storage holds malformed json', () => {
    window.localStorage.setItem(STORAGE_KEY, 'not json')

    const resize = createResize()

    expect(resize.widths.value).toEqual({ title: 300, status: 140 })
  })

  it('does not resize while the card layout is active', () => {
    setViewportWidth(true)
    const resize = createResize()

    drag(resize, 'title', 200)

    expect(resize.widths.value.title).toBe(300)
  })

  it('restores defaults on reset', () => {
    const resize = createResize()
    drag(resize, 'title', 120)

    resize.resetWidths()

    expect(resize.widths.value).toEqual({ title: 300, status: 140 })
  })

  it('clears the resizing column once the pointer is released', () => {
    const resize = createResize()

    drag(resize, 'title', 40)

    expect(resize.resizingColumn.value).toBeNull()
  })
})
