/* Bookmarkarr is licensed under the GNU AGPL v3 or later. */
import { describe, expect, it, vi } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import GoodreadsImportView from '@/views/library/GoodreadsImportView.vue'
import { apiService } from '@/services/api'

describe('GoodreadsImportView', () => {
  it('defaults eligible rows to selected audiobook and ebook editions', async () => {
    vi.mocked(apiService.previewGoodreadsImport).mockResolvedValueOnce({
      batchId: 'batch-1',
      expiresAt: '2099-01-01T00:00:00Z',
      status: 'Preview',
      rowCount: 1,
      eligibleCount: 1,
      ambiguousCount: 0,
      rows: [{
        rowId: 'row-1', rowNumber: 1, goodreadsId: '42', isbn: '9781234567890',
        title: 'A Book', primaryAuthor: 'Jane Doe', eligible: true, selected: true,
        mediaFormats: ['Audiobook', 'Ebook'], matchStatus: 'new', matchCandidates: [],
      }],
    })
    const wrapper = mount(GoodreadsImportView)
    const input = wrapper.get('input[type="file"]')
    Object.defineProperty(input.element, 'files', {
      value: [new File(['Book Id,Title,Author\n42,A Book,Jane Doe'], 'goodreads.csv', { type: 'text/csv' })],
      configurable: true,
    })
    await input.trigger('change')
    await wrapper.get('button.primary').trigger('click')
    await flushPromises()

    const selected = wrapper.findAll('input[type="checkbox"]').filter((item) => (item.element as HTMLInputElement).checked)
    expect(selected).toHaveLength(3)
    expect(wrapper.text()).toContain('1 selected')
    expect(wrapper.text()).toContain('New book')
  })

  it('blocks commit until an ambiguous selected row is resolved', async () => {
    vi.mocked(apiService.previewGoodreadsImport).mockResolvedValueOnce({
      batchId: 'batch-2', expiresAt: '2099-01-01T00:00:00Z', status: 'Preview',
      rowCount: 1, eligibleCount: 1, ambiguousCount: 1,
      rows: [{
        rowId: 'row-2', rowNumber: 1, goodreadsId: '', isbn: '', title: 'Shared Title',
        primaryAuthor: 'Jane Doe', eligible: true, selected: true,
        mediaFormats: ['Audiobook', 'Ebook'], matchStatus: 'ambiguous',
        matchCandidates: [{ bookId: 7, title: 'Shared Title', primaryAuthor: 'Jane Doe', matchMethod: 'titleAuthor' }],
      }],
    })
    const wrapper = mount(GoodreadsImportView)
    const input = wrapper.get('input[type="file"]')
    Object.defineProperty(input.element, 'files', { value: [new File(['csv'], 'goodreads.csv')], configurable: true })
    await input.trigger('change')
    await wrapper.get('button.primary').trigger('click')
    await flushPromises()

    expect(wrapper.text()).toContain('Resolve 1 ambiguous selected row')
    expect(wrapper.findAll('button.primary').at(1)?.attributes('disabled')).toBeDefined()
  })
})
