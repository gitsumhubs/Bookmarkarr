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

import type { Directive, DirectiveBinding } from 'vue'

/**
 * `v-tooltip` — a tooltip anchored to the element rather than to the pointer.
 *
 * The native `title` attribute renders wherever the cursor happens to be, at a size and offset
 * the browser owns. Anyone using an enlarged pointer — a common accessibility setting — has the
 * first line of every tooltip sitting underneath their own cursor, and no amount of CSS can move
 * it. Anchoring to the element's box instead makes pointer size irrelevant, because the tooltip
 * never renders where the pointer is.
 *
 * Two things come along for free: it appears on keyboard focus, which `title` never does, and it
 * inherits the app's theme tokens instead of the OS chrome.
 *
 * Usage:
 *   v-tooltip="'Plain string'"
 *   v-tooltip="{ content: someRef, placement: 'bottom' }"
 *
 * Never pair it with `title` on the same element — the browser would draw its own tooltip on top
 * of this one and the result is worse than either alone.
 */

type Placement = 'top' | 'bottom'

interface TooltipOptions {
  content?: string | null
  placement?: Placement
  /** Set false to leave the element's own aria-label/`aria-describedby` wiring alone. */
  describe?: boolean
}

interface TooltipState {
  content: string
  placement: Placement
  describe: boolean
  showTimer?: number
}

/** Gap between the anchor and the tooltip, and the minimum breathing room at a viewport edge. */
const ANCHOR_GAP = 8
const VIEWPORT_MARGIN = 8
const SHOW_DELAY_MS = 250
const TOOLTIP_ID = 'bmk-tooltip'

const states = new WeakMap<HTMLElement, TooltipState>()

let tooltipEl: HTMLElement | null = null
let activeAnchor: HTMLElement | null = null

function normalize(value: TooltipOptions | string | null | undefined): TooltipOptions {
  if (typeof value === 'string') return { content: value }
  return value ?? {}
}

function ensureTooltipElement(): HTMLElement {
  if (tooltipEl?.isConnected) return tooltipEl

  const el = document.createElement('div')
  el.id = TOOLTIP_ID
  el.className = 'bmk-tooltip'
  el.setAttribute('role', 'tooltip')
  // Hidden from the a11y tree until shown; anchors point at it via aria-describedby.
  el.setAttribute('aria-hidden', 'true')
  document.body.appendChild(el)
  tooltipEl = el
  return el
}

function position(anchor: HTMLElement, tip: HTMLElement, preferred: Placement) {
  const anchorBox = anchor.getBoundingClientRect()
  const tipBox = tip.getBoundingClientRect()

  // Flip when the preferred side cannot fit, rather than letting the tooltip run off-screen.
  const fitsAbove = anchorBox.top - tipBox.height - ANCHOR_GAP >= VIEWPORT_MARGIN
  const fitsBelow =
    anchorBox.bottom + tipBox.height + ANCHOR_GAP <= window.innerHeight - VIEWPORT_MARGIN
  let placement = preferred
  if (placement === 'top' && !fitsAbove && fitsBelow) placement = 'bottom'
  else if (placement === 'bottom' && !fitsBelow && fitsAbove) placement = 'top'

  const top =
    placement === 'top'
      ? anchorBox.top - tipBox.height - ANCHOR_GAP
      : anchorBox.bottom + ANCHOR_GAP

  // Centre on the anchor, then clamp so a control near either edge stays fully readable.
  const idealLeft = anchorBox.left + anchorBox.width / 2 - tipBox.width / 2
  const maxLeft = window.innerWidth - tipBox.width - VIEWPORT_MARGIN
  const left = Math.max(VIEWPORT_MARGIN, Math.min(idealLeft, Math.max(VIEWPORT_MARGIN, maxLeft)))

  tip.style.top = `${Math.round(top)}px`
  tip.style.left = `${Math.round(left)}px`
  tip.dataset.placement = placement
}

function show(anchor: HTMLElement) {
  const state = states.get(anchor)
  if (!state?.content) return

  const tip = ensureTooltipElement()
  tip.textContent = state.content
  tip.setAttribute('aria-hidden', 'false')
  tip.classList.add('is-visible')

  // Measure with the final text in place, then position; two frames is one too many here, so
  // read layout synchronously rather than waiting for rAF and showing at 0,0 for a frame.
  tip.style.top = '0px'
  tip.style.left = '0px'
  position(anchor, tip, state.placement)

  if (state.describe) anchor.setAttribute('aria-describedby', TOOLTIP_ID)
  activeAnchor = anchor
}

function hide() {
  if (activeAnchor) {
    activeAnchor.removeAttribute('aria-describedby')
    const state = states.get(activeAnchor)
    if (state?.showTimer) {
      window.clearTimeout(state.showTimer)
      state.showTimer = undefined
    }
  }
  activeAnchor = null

  if (!tooltipEl) return
  tooltipEl.classList.remove('is-visible')
  tooltipEl.setAttribute('aria-hidden', 'true')
}

function scheduleShow(anchor: HTMLElement, immediate: boolean) {
  const state = states.get(anchor)
  if (!state?.content) return

  if (state.showTimer) window.clearTimeout(state.showTimer)

  if (immediate) {
    show(anchor)
    return
  }

  state.showTimer = window.setTimeout(() => {
    state.showTimer = undefined
    show(anchor)
  }, SHOW_DELAY_MS)
}

const onPointerEnter = (event: Event) => scheduleShow(event.currentTarget as HTMLElement, false)
// Keyboard focus is deliberate, so there is nothing to debounce against.
const onFocus = (event: Event) => scheduleShow(event.currentTarget as HTMLElement, true)
const onLeave = () => hide()
const onKeydown = (event: KeyboardEvent) => {
  if (event.key === 'Escape') hide()
}

let globalListenersBound = false

function bindGlobalListeners() {
  if (globalListenersBound) return
  // A tooltip pinned to viewport coordinates goes stale the moment anything moves underneath it.
  window.addEventListener('scroll', onLeave, { passive: true, capture: true })
  window.addEventListener('resize', onLeave, { passive: true })
  document.addEventListener('keydown', onKeydown)
  globalListenersBound = true
}

export const vTooltip: Directive<HTMLElement, TooltipOptions | string | null | undefined> = {
  mounted(el, binding: DirectiveBinding<TooltipOptions | string | null | undefined>) {
    const options = normalize(binding.value)
    states.set(el, {
      content: options.content?.trim() ?? '',
      placement: options.placement ?? 'top',
      describe: options.describe ?? true,
    })

    bindGlobalListeners()
    el.addEventListener('mouseenter', onPointerEnter)
    el.addEventListener('mouseleave', onLeave)
    el.addEventListener('focus', onFocus)
    el.addEventListener('blur', onLeave)
    // A click usually changes what the tooltip was describing, so the stale text must not linger.
    el.addEventListener('click', onLeave)
  },

  updated(el, binding: DirectiveBinding<TooltipOptions | string | null | undefined>) {
    const options = normalize(binding.value)
    const state = states.get(el)
    if (!state) return

    state.content = options.content?.trim() ?? ''
    state.placement = options.placement ?? 'top'
    state.describe = options.describe ?? true

    // Keep a tooltip that is on screen right now in step with the value behind it.
    if (activeAnchor === el) {
      if (!state.content) hide()
      else show(el)
    }
  },

  beforeUnmount(el) {
    if (activeAnchor === el) hide()
    el.removeEventListener('mouseenter', onPointerEnter)
    el.removeEventListener('mouseleave', onLeave)
    el.removeEventListener('focus', onFocus)
    el.removeEventListener('blur', onLeave)
    el.removeEventListener('click', onLeave)
    states.delete(el)
  },
}

export default vTooltip
