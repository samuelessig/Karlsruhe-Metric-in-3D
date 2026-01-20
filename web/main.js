/* global temml, history, location, IntersectionObserver */

function renderMath () {
  document.querySelectorAll('.math').forEach(el => {
    temml.renderMathInElement(el, { fences: '$+' }, { displayMode: false })
  })
}
function initSlideshow (imgElement, images, intervalMs = 3000) {
  let currentIndex = imgElement.dataset.currentIndex ? parseInt(imgElement.dataset.currentIndex) : 0
  function showNextImage () {
    currentIndex = imgElement.dataset.currentIndex ? parseInt(imgElement.dataset.currentIndex) : 0
    currentIndex = (currentIndex + 1) % images.length
    imgElement.dataset.currentIndex = currentIndex
    imgElement.src = images[currentIndex]
  }

  // advance every X seconds
  let timer = setInterval(showNextImage, intervalMs)

  // advance on click
  imgElement.addEventListener('click', () => {
    showNextImage()
    clearInterval(timer)
    timer = setInterval(showNextImage, intervalMs)
  })
}

function makeImagesZoomable () {
  const zoomables = () => Array.from(document.querySelectorAll('img.zoomable'))

  const overlay = document.createElement('div')
  overlay.className = 'zoom-overlay'
  overlay.innerHTML = `
    <div class="zoom-overlay__panel" role="dialog">
      <button class="zoom-overlay__close" type="button" title="Close">×</button>
      <img class="zoom-overlay__img" alt="">
      <div class="zoom-overlay__caption" hidden></div>
    </div>
  `
  document.body.appendChild(overlay)

  const panel = overlay.querySelector('.zoom-overlay__panel')
  const imgEl = overlay.querySelector('.zoom-overlay__img')
  const capEl = overlay.querySelector('.zoom-overlay__caption')
  const closeBtn = overlay.querySelector('.zoom-overlay__close')

  let currentIndex = -1

  function getCaptionFor (img) {
    const next = img.nextElementSibling
    if (next && next.matches('span.caption')) {
      if (next.title && next.title.trim().length) {
        return next.title.trim()
      }
      const text = next.textContent.trim()
      return text.length ? text : null
    }
    return null
  }

  function openAt (index) {
    const imgs = zoomables()
    if (!imgs.length) return

    currentIndex = (index + imgs.length) % imgs.length
    const img = imgs[currentIndex]

    const fullSrc = img.currentSrc || img.src
    imgEl.src = fullSrc
    imgEl.alt = img.alt || ''

    const cap = getCaptionFor(img)
    if (cap) {
      capEl.textContent = cap
      capEl.hidden = false
    } else {
      capEl.textContent = ''
      capEl.hidden = true
    }

    overlay.classList.add('is-open')
    document.documentElement.style.overflow = 'hidden' // prevent background scroll
    closeBtn.focus({ preventScroll: true })
  }

  function close () {
    overlay.classList.remove('is-open')
    document.documentElement.style.overflow = ''
    imgEl.src = ''
    currentIndex = -1
  }

  function next (delta) {
    if (currentIndex === -1) return
    openAt(currentIndex + delta)
  }

  document.addEventListener('click', (e) => {
    const img = e.target.closest('img.zoomable')
    if (!img) return

    const imgs = zoomables()
    const idx = imgs.indexOf(img)
    if (idx !== -1) openAt(idx)
  })

  closeBtn.addEventListener('click', close)

  // click outside
  overlay.addEventListener('click', (e) => {
    if (!panel.contains(e.target)) close()
  })

  // Keyboard navigation
  document.addEventListener('keydown', (e) => {
    if (!overlay.classList.contains('is-open')) return

    if (e.key === 'Escape') {
      e.preventDefault()
      close()
    } else if (e.key === 'ArrowRight') {
      e.preventDefault()
      next(+1)
    } else if (e.key === 'ArrowLeft') {
      e.preventDefault()
      next(-1)
    }
  })
}

function createNavigation () {
  const contentRoot = document.querySelector('#content') || document.body
  const tocLinksRoot = document.querySelector('#toc-links')
  if (!tocLinksRoot) return

  // Collect headings in order
  const headings = Array.from(
    contentRoot.querySelectorAll('h2, h3')
  ).filter(h => h.textContent.trim().length > 0)

  if (headings.length === 0) return

  // Ensure every heading has an id
  const slugify = (s) =>
    s.toLowerCase()
      .trim()
      .replace(/[\s]+/g, '-')
      .replace(/[^\w-]+/g, '')
      .replace(/-+/g, '-')

  const used = new Map()
  function uniqueId (base) {
    const n = (used.get(base) || 0) + 1
    used.set(base, n)
    return n === 1 ? base : `${base}-${n}`
  }

  headings.forEach(h => {
    if (!h.id) {
      const base = slugify(h.textContent) || 'section'
      h.id = uniqueId(base)
    }
  })

  const frag = document.createDocumentFragment()
  const linkById = new Map()

  headings.forEach(h => {
    const level = Number(h.tagName.substring(1)) // 1..3
    const a = document.createElement('a')
    a.href = `#${h.id}`
    a.textContent = h.textContent.trim()
    a.dataset.level = String(level)
    a.addEventListener('click', (e) => {
      e.preventDefault()
      document.getElementById(h.id).scrollIntoView({ behavior: 'smooth', block: 'start' })
      history.replaceState(null, '', `#${h.id}`)
    })
    frag.appendChild(a)
    linkById.set(h.id, a)
  })

  tocLinksRoot.innerHTML = ''
  tocLinksRoot.appendChild(frag)

  let activeId = null

  function setActive (id) {
    if (activeId === id) return
    if (activeId && linkById.get(activeId)) linkById.get(activeId).classList.remove('active')
    activeId = id
    const link = linkById.get(id)
    if (link) {
      link.classList.add('active')
      link.scrollIntoView({ block: 'nearest' })
    }
  }

  const visible = new Map()

  const observer = new IntersectionObserver((entries) => {
    for (const entry of entries) {
      const id = entry.target.id
      if (entry.isIntersecting) {
        visible.set(id, entry.boundingClientRect.top)
      } else {
        visible.delete(id)
      }
    }

    if (visible.size === 0) return

    let bestId = null
    let bestScore = Infinity

    for (const [id, top] of visible.entries()) {
      const score = top >= 0 ? top : Math.abs(top) + 10000
      if (score < bestScore) {
        bestScore = score
        bestId = id
      }
    }

    if (bestId) setActive(bestId)
  }, {
    root: null,
    rootMargin: '-15% 0px -70% 0px',
    threshold: [0, 1.0]
  })

  headings.forEach(h => observer.observe(h))

  // Initial highlight (in case you load with a hash)
  if (location.hash) {
    const initial = location.hash ? location.hash.slice(1) : headings[0].id
    if (initial && linkById.has(initial)) setActive(initial)
    else setActive(headings[0].id)
  }
}

document.addEventListener('DOMContentLoaded', () => {
  renderMath()
  initSlideshow(document.getElementById('unitslideshow'), [
    'images/unit0.png',
    'images/unit1.png',
    'images/unit2.png',
    'images/unit3.png'
  ], 3000)

  initSlideshow(document.getElementById('pathslideshow'), [
    'images/path0.png',
    'images/path1.png',
    'images/path2.png',
    'images/path3.png'
  ], 2000)

  makeImagesZoomable()
  createNavigation()
})
