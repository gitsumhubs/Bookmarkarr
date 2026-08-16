import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { defineComponent } from 'vue'
import { vTooltip } from '@/directives/tooltip'

/**
 * The point of this directive is that the tooltip is anchored to the element rather than drawn at
 * the pointer, which is what makes it readable behind an enlarged cursor. These cover the parts
 * that guarantee it: it renders outside the anchor, it appears on keyboard focus, and it leaves no
 * native `title` that the browser would draw a second tooltip from.
 */
describe('v-tooltip', () => {
  const mountAnchor = (content: string | null) =>
    mount(
      defineComponent({
        directives: { tooltip: vTooltip },
        props: { content: { type: String, default: null } },
        template: `<button v-tooltip="content">anchor</button>`,
      }),
      { attachTo: document.body, props: { content } },
    )

  const tooltip = () => document.getElementById('bmk-tooltip')

  beforeEach(() => {
    vi.useFakeTimers()
  })

  afterEach(() => {
    vi.useRealTimers()
    tooltip()?.remove()
  })

  it('renders the tooltip outside the anchor, so pointer size cannot occlude it', async () => {
    const wrapper = mountAnchor('Rename files on import')

    await wrapper.find('button').trigger('mouseenter')
    vi.advanceTimersByTime(500)
    await wrapper.vm.$nextTick()

    const tip = tooltip()
    expect(tip).not.toBeNull()
    expect(tip!.textContent).toBe('Rename files on import')
    expect(tip!.classList.contains('is-visible')).toBe(true)
    // Anchored to the document body, not nested inside the control it describes.
    expect(wrapper.find('button').element.contains(tip)).toBe(false)
    expect(tip!.parentElement).toBe(document.body)

    wrapper.unmount()
  })

  it('appears on keyboard focus, which a native title never does', async () => {
    const wrapper = mountAnchor('Automatic Search')

    await wrapper.find('button').trigger('focus')
    await wrapper.vm.$nextTick()

    expect(tooltip()!.classList.contains('is-visible')).toBe(true)
    expect(tooltip()!.textContent).toBe('Automatic Search')

    wrapper.unmount()
  })

  it('leaves no native title behind, which would draw a second tooltip on top', () => {
    const wrapper = mountAnchor('Unmonitor edition')

    expect(wrapper.find('button').attributes('title')).toBeUndefined()

    wrapper.unmount()
  })

  it('describes the anchor only while the tooltip is shown', async () => {
    const wrapper = mountAnchor('Unmonitor edition')
    const button = wrapper.find('button')

    await button.trigger('focus')
    await wrapper.vm.$nextTick()
    expect(button.attributes('aria-describedby')).toBe('bmk-tooltip')

    await button.trigger('blur')
    await wrapper.vm.$nextTick()
    expect(button.attributes('aria-describedby')).toBeUndefined()
    expect(tooltip()!.classList.contains('is-visible')).toBe(false)

    wrapper.unmount()
  })

  it('stays hidden when there is no content to show', async () => {
    const wrapper = mountAnchor(null)

    await wrapper.find('button').trigger('focus')
    await wrapper.vm.$nextTick()

    expect(tooltip()?.classList.contains('is-visible')).not.toBe(true)

    wrapper.unmount()
  })

  it('follows the bound value when it changes while shown', async () => {
    const wrapper = mountAnchor('Keeping original file names')

    await wrapper.find('button').trigger('focus')
    await wrapper.vm.$nextTick()
    expect(tooltip()!.textContent).toBe('Keeping original file names')

    await wrapper.setProps({ content: 'Renaming files on import' })
    await wrapper.vm.$nextTick()
    expect(tooltip()!.textContent).toBe('Renaming files on import')

    wrapper.unmount()
  })
})
