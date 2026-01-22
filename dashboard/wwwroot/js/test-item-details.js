// Test Item Details - JavaScript Helpers

/**
 * Scrolls the history line to center the active item
 * @param {number} activeIndex - Index of the active history item
 */
window.scrollHistoryToActive = function (activeIndex) {
  const historyScroll = document.querySelector('.history-line-scroll');
  const historyItems = document.querySelectorAll('.history-line-item');

  if (!historyScroll || !historyItems || activeIndex >= historyItems.length) {
    return;
  }

  const activeItem = historyItems[activeIndex];
  const itemLeft = activeItem.offsetLeft;
  const itemWidth = activeItem.offsetWidth;
  const scrollWidth = historyScroll.offsetWidth;

  // Center the item in the scroll container
  const scrollTo = itemLeft - (scrollWidth / 2) + (itemWidth / 2);

  historyScroll.scrollTo({
    left: Math.max(0, scrollTo),
    behavior: 'smooth'
  });
};

/**
 * Sets up keyboard navigation for history line (Arrow Left/Right)
 */
window.setupHistoryKeyboard = function () {
  document.addEventListener('keydown', (e) => {
    // Don't interfere with text input
    if (e.target.tagName === 'INPUT' || e.target.tagName === 'TEXTAREA') {
      return;
    }

    const prevBtn = document.getElementById('prevLaunchBtn');
    const nextBtn = document.getElementById('nextLaunchBtn');

    if (e.key === 'ArrowLeft' && prevBtn && !prevBtn.disabled) {
      e.preventDefault();
      prevBtn.click();
    } else if (e.key === 'ArrowRight' && nextBtn && !nextBtn.disabled) {
      e.preventDefault();
      nextBtn.click();
    }
  });
};

/**
 * Scrolls to a specific element and highlights it persistently
 * @param {string} elementId - ID of the element to scroll to
 */
window.scrollToElement = function (elementId) {
  const element = document.getElementById(elementId);
  if (!element) {
    console.warn(`Element with ID ${elementId} not found`);
    return;
  }

  // Remove any existing persistent highlights
  document.querySelectorAll('.highlight-persistent').forEach(el => {
    el.classList.remove('highlight-persistent');
  });

  // Scroll element into view
  element.scrollIntoView({
    behavior: 'smooth',
    block: 'center'
  });

  // Add persistent highlight (stays until user clicks elsewhere or switches tabs)
  element.classList.add('highlight-persistent');
};

/**
 * Removes all persistent highlights from the page
 */
window.clearHighlights = function () {
  document.querySelectorAll('.highlight-persistent').forEach(el => {
    el.classList.remove('highlight-persistent');
  });
};
