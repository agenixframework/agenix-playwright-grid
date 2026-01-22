window.copyText = function (text) {
  return navigator.clipboard.writeText(text ?? '')
    .then(() => {
      console.log('Text copied to clipboard');
      return true;
    })
    .catch(err => {
      console.error('Failed to copy text: ', err);
      return false;
    });
};

// Accessibility helpers
window.focusFirstInvalid = function () {
  try {
    const el = document.querySelector('[aria-invalid="true"], .is-invalid, .input-validation-error');
    if (el && typeof el.focus === 'function') {
      el.focus();
      if (el.select) {
        try {
          el.select();
        } catch {
        }
      }
      return true;
    }
  } catch (e) {
    console.warn('focusFirstInvalid failed', e);
  }
  return false;
};

window.announceLive = function (message, politeness) {
  try {
    const div = document.createElement('div');
    div.setAttribute('aria-live', politeness || 'polite');
    div.setAttribute('class', 'visually-hidden');
    div.textContent = message || '';
    document.body.appendChild(div);
    setTimeout(() => document.body.removeChild(div), 500);
    return true;
  } catch (e) {
    return false;
  }
};

// Enhanced form validation for login
window.enhanceLoginForm = function () {
  try {
    const form = document.querySelector('.login-form');
    const inputs = form?.querySelectorAll('.form-control-enhanced');

    if (!form || !inputs) return false;

    inputs.forEach(input => {
      // Real-time validation feedback
      input.addEventListener('input', function () {
        validateField(this);
      });

      input.addEventListener('blur', function () {
        validateField(this);
      });

      // Enhanced focus effects
      input.addEventListener('focus', function () {
        this.closest('.input-container')?.classList.add('focused');
      });

      input.addEventListener('blur', function () {
        this.closest('.input-container')?.classList.remove('focused');
      });
    });

    return true;
  } catch (e) {
    console.warn('enhanceLoginForm failed', e);
    return false;
  }
};

function validateField(field) {
  const container = field.closest('.form-group');
  if (!container) return;

  // Remove existing validation classes
  field.classList.remove('is-valid', 'is-invalid');

  const isValid = field.checkValidity() && field.value.trim().length > 0;

  if (field.value.trim().length > 0) {
    field.classList.add(isValid ? 'is-valid' : 'is-invalid');
  }

  // Announce validation state for screen readers
  if (!isValid && field.value.trim().length > 0) {
    const fieldName = field.getAttribute('placeholder') || 'Field';
    window.announceLive(`${fieldName} is invalid`, 'assertive');
  }
}

// Initialize login form enhancements when page loads
document.addEventListener('DOMContentLoaded', function () {
  window.enhanceLoginForm();
});

// Reinitialize after Blazor updates
window.addEventListener('blazor:navigated', function () {
  setTimeout(() => window.enhanceLoginForm(), 100);
});

// Best-effort OS name/version detection for display when server does not provide OS
window.detectOS = function () {
  try {
    const nav = navigator || {};
    const ua = nav.userAgent || '';
    const plat = (nav.userAgentData && nav.userAgentData.platform) || nav.platform || '';

    function parseWindowsVersion(uaStr) {
      // Map common Windows NT versions
      const m = uaStr.match(/Windows NT ([0-9]+\.[0-9]+)/i);
      if (!m) return 'Windows';
      const ver = m[1];
      const map = {
        '10.0': 'Windows 10/11',
        '6.3': 'Windows 8.1',
        '6.2': 'Windows 8',
        '6.1': 'Windows 7'
      };
      return map[ver] || 'Windows';
    }

    function parseMacVersion(uaStr) {
      const m = uaStr.match(/Mac OS X ([0-9_\.]+)/i);
      if (!m) return 'macOS';
      return 'macOS ' + m[1].replaceAll('_', '.');
    }

    function parseIOSVersion(uaStr) {
      const m = uaStr.match(/CPU (iPhone )?OS ([0-9_]+)/i);
      if (!m) return 'iOS';
      return 'iOS ' + m[2].replaceAll('_', '.');
    }

    const p = (plat || '').toLowerCase();
    if (p.includes('win')) return parseWindowsVersion(ua);
    if (p.includes('mac')) return parseMacVersion(ua);
    if (p.includes('ios') || /iPhone|iPad|iPod/.test(ua)) return parseIOSVersion(ua);
    if (p.includes('linux')) return 'Linux';
    if (p.includes('android') || /Android/.test(ua)) {
      const m = ua.match(/Android\s([0-9.]+)/i);
      return 'Android' + (m ? (' ' + m[1]) : '');
    }
    // Fallback: return platform or UA snippet
    if (plat) return plat;
    if (ua) return ua.substring(0, 64);
    return '-';
  } catch (e) {
    return '-';
  }
};

// Dashboard local login via browser fetch to ensure auth cookie is set client-side
window.dashboardLocalLogin = async function (payload) {
  try {
    const resp = await fetch('/auth/local', {
      method: 'POST',
      headers: {'Content-Type': 'application/json'},
      // include credentials so Set-Cookie from the response is persisted by the browser
      credentials: 'include',
      body: JSON.stringify(payload || {})
    });
    if (resp.ok) {
      try {
        const data = await resp.json();
        const redirect = (data && data.redirect) || (payload && payload.returnUrl) || '/';
        // Force a full navigation so the new cookie is attached to the request and SignalR reconnects under auth
        window.location.assign(redirect);
      } catch (e) {
        const fwd = (payload && payload.returnUrl) || '/';
        window.location.assign(fwd);
      }
      return {ok: true};
    }
    return {ok: false, status: resp.status};
  } catch (e) {
    return {ok: false, error: (e && (e.message || e.toString())) || 'error'};
  }
};

// Toast notification system
window.showToast = function (message, type) {
  try {
    // Remove existing toast if any
    const existingToast = document.querySelector('.toast-notification');
    if (existingToast) {
      existingToast.remove();
    }

    // Create toast element
    const toast = document.createElement('div');
    toast.className = `toast-notification toast-${type || 'info'}`;
    toast.innerHTML = `
      <div class="toast-content">
        <svg class="toast-icon" width="20" height="20" viewBox="0 0 16 16" fill="currentColor">
          ${type === 'success' ? '<path d="M10.97 4.97a.75.75 0 0 1 1.07 1.05l-3.99 4.99a.75.75 0 0 1-1.08.02L4.324 8.384a.75.75 0 1 1 1.06-1.06l2.094 2.093 3.473-4.425a.267.267 0 0 1 .02-.022z"/>' : ''}
          ${type === 'error' ? '<path d="M16 8A8 8 0 1 1 0 8a8 8 0 0 1 16 0zM5.354 4.646a.5.5 0 1 0-.708.708L7.293 8l-2.647 2.646a.5.5 0 0 0 .708.708L8 8.707l2.646 2.647a.5.5 0 0 0 .708-.708L8.707 8l2.647-2.646a.5.5 0 0 0-.708-.708L8 7.293 5.354 4.646z"/>' : ''}
          ${type === 'info' ? '<path d="m8.93 6.588-2.29.287-.082.38.45.083c.294.07.352.176.288.469l-.738 3.468c-.194.897.105 1.319.808 1.319.545 0 1.178-.252 1.465-.598l.088-.416c-.2.176-.492.246-.686.246-.275 0-.375-.193-.304-.533L8.93 6.588zM9 4.5a1 1 0 1 1-2 0 1 1 0 0 1 2 0z"/>' : ''}
        </svg>
        <span class="toast-message">${message || ''}</span>
      </div>
    `;

    // Add to body
    document.body.appendChild(toast);

    // Trigger animation
    setTimeout(() => toast.classList.add('show'), 10);

    // Auto remove after 3 seconds
    setTimeout(() => {
      toast.classList.remove('show');
      setTimeout(() => toast.remove(), 300);
    }, 3000);

    return true;
  } catch (e) {
    console.error('showToast failed', e);
    return false;
  }
};

// Time toggle on hover - switches between relative time and full timestamp
// Use event delegation to handle dynamically added elements
document.addEventListener('mouseover', function (e) {
  if (e.target.classList.contains('time-toggle')) {
    const fullTime = e.target.getAttribute('data-full');
    if (fullTime && !e.target.hasAttribute('data-showing-full')) {
      e.target.textContent = fullTime;
      e.target.setAttribute('data-showing-full', 'true');
    }
  }
});

document.addEventListener('mouseout', function (e) {
  if (e.target.classList.contains('time-toggle')) {
    const relativeTime = e.target.getAttribute('data-relative');
    if (relativeTime && e.target.hasAttribute('data-showing-full')) {
      e.target.textContent = relativeTime;
      e.target.removeAttribute('data-showing-full');
    }
  }
});

// Close launch menu when clicking outside
document.addEventListener('click', function (e) {
  // Check if click is outside launch menu
  const clickedMenu = e.target.closest('.launch-menu-wrapper');

  if (!clickedMenu) {
    // Close any open menu by triggering a click on the close button
    const openMenus = document.querySelectorAll('.launch-menu-dropdown');
    openMenus.forEach(menu => {
      const menuButton = menu.closest('.launch-menu-wrapper')?.querySelector('.launch-menu-btn');
      if (menuButton) {
        menuButton.click();
      }
    });
  }
});

// Set checkbox indeterminate state
window.setCheckboxIndeterminate = function (element, indeterminate) {
  try {
    if (element) {
      element.indeterminate = indeterminate;
      if (indeterminate) {
        element.classList.add('indeterminate');
      } else {
        element.classList.remove('indeterminate');
      }
      return true;
    }
    return false;
  } catch (e) {
    console.error('setCheckboxIndeterminate failed', e);
    return false;
  }
};

// Get text selection from textarea/input
window.getTextSelection = function (element) {
  try {
    if (element && (element.tagName === 'TEXTAREA' || element.tagName === 'INPUT')) {
      return {
        start: element.selectionStart || 0,
        end: element.selectionEnd || 0,
        selectedText: element.value.substring(element.selectionStart || 0, element.selectionEnd || 0)
      };
    }
    return null;
  } catch (e) {
    console.error('getTextSelection failed', e);
    return null;
  }
};

// Set text selection (cursor position) in textarea/input
window.setTextSelection = function (element, start, end) {
  try {
    if (element && (element.tagName === 'TEXTAREA' || element.tagName === 'INPUT')) {
      element.focus();
      element.setSelectionRange(start, end);
      return true;
    }
    return false;
  } catch (e) {
    console.error('setTextSelection failed', e);
    return false;
  }
};

// C3.js Chart for Launch Comparison
window.renderComparisonChart = function (elementId, launchesData) {
  try {
    console.log('=== renderComparisonChart START ===');
    console.log('elementId:', elementId);
    console.log('launchesData:', JSON.stringify(launchesData, null, 2));
    console.log('c3 available:', !!window.c3);
    console.log('d3 available:', !!window.d3);
    console.log('d3 version:', window.d3 && window.d3.version);

    const element = document.getElementById(elementId);
    console.log('DOM element found:', !!element);
    if (!element) {
      console.warn('Element #' + elementId + ' NOT FOUND in DOM, will retry...');
      return false;
    }
    console.log('Element HTML:', element.outerHTML);

    if (!window.c3 || !window.d3) {
      console.error('C3 or D3 not loaded');
      return false;
    }

    if (!launchesData || launchesData.length === 0) {
      console.error('No launches data provided');
      return false;
    }

    // Prepare categories (X-axis labels)
    const categories = launchesData.map((l, idx) => `#${idx + 1}`);

    // Prepare columns - one row per test status type across all launches
    const passedData = ['Passed'].concat(launchesData.map(l => l.passedPercentage || 0));
    const failedData = ['Failed'].concat(launchesData.map(l => l.failedPercentage || 0));
    const skippedData = ['Skipped'].concat(launchesData.map(l => l.skippedPercentage || 0));

    console.log('Chart columns:', [passedData, failedData, skippedData]);

    // Format start time helper
    const formatStartTime = (isoString) => {
      const date = new Date(isoString);
      return date.toLocaleString('en-US', {
        year: 'numeric',
        month: 'short',
        day: 'numeric',
        hour: '2-digit',
        minute: '2-digit'
      });
    };

    // Render chart
    const chart = c3.generate({
      bindto: `#${elementId}`,
      data: {
        columns: [
          passedData,
          failedData,
          skippedData
        ],
        type: 'bar',
        colors: {
          'Passed': '#10B981',
          'Failed': '#EF4444',
          'Skipped': '#F59E0B'
        }
      },
      axis: {
        x: {
          type: 'category',
          categories: categories
        },
        y: {
          label: {
            text: '% of tests',
            position: 'outer-middle'
          },
          min: 0,
          max: 100,
          padding: {top: 0, bottom: 0}
        }
      },
      bar: {
        width: {
          ratio: 0.6
        }
      },
      legend: {
        show: true,
        position: 'bottom'
      },
      tooltip: {
        format: {
          title: function (x) {
            // x is the index (0, 1, 2...)
            const launch = launchesData[x];
            return `${launch.launchName} #${launch.launchNumber}\nStart: ${formatStartTime(launch.startTime)}`;
          },
          value: function (value, ratio, id) {
            return value.toFixed(1) + '%';
          }
        }
      },
      size: {
        height: 400
      }
    });

    console.log('Chart rendered successfully');
    return true;
  } catch (e) {
    console.error('renderComparisonChart failed', e);
    return false;
  }
};

// Test item navigation keyboard shortcuts
window.setupTestNavigationKeyboard = (dotNetRef) => {
  // Store reference and cleanup function
  if (window._testNavKeyboardCleanup) {
    window._testNavKeyboardCleanup();
  }

  const handler = (e) => {
    // Don't interfere with text input
    if (e.target.tagName === 'INPUT' ||
      e.target.tagName === 'TEXTAREA' ||
      e.target.isContentEditable) {
      return;
    }

    if (e.key === 'ArrowLeft' || e.key === 'ArrowRight') {
      e.preventDefault();
      dotNetRef.invokeMethodAsync('HandleKeyPress', e.key);
    }
  };

  document.addEventListener('keydown', handler);

  // Store cleanup function for when component unmounts
  window._testNavKeyboardCleanup = () => {
    document.removeEventListener('keydown', handler);
    window._testNavKeyboardCleanup = null;
  };
};

// Export comparison chart as PNG with tooltips
window.exportComparisonChartAsPNG = async function (launchesData) {
  try {
    console.log('=== exportComparisonChartAsPNG START ===');

    if (!window.html2canvas) {
      console.error('html2canvas not loaded');
      alert('Export library not loaded. Please refresh the page and try again.');
      return false;
    }

    const chartElement = document.getElementById('compare-chart');
    if (!chartElement) {
      console.error('Chart element not found');
      alert('Chart not found. Please try again.');
      return false;
    }

    // Create a container with chart and tooltips info
    const exportContainer = document.createElement('div');
    exportContainer.style.position = 'absolute';
    exportContainer.style.left = '-9999px';
    exportContainer.style.background = 'white';
    exportContainer.style.padding = '30px';
    exportContainer.style.width = '1200px';
    document.body.appendChild(exportContainer);

    // Add title
    const title = document.createElement('h3');
    title.textContent = 'Launch Comparison';
    title.style.textAlign = 'center';
    title.style.marginBottom = '20px';
    title.style.color = '#1f2937';
    exportContainer.appendChild(title);

    // Clone the chart
    const chartClone = chartElement.cloneNode(true);
    exportContainer.appendChild(chartClone);

    // Add launch details table below chart
    const detailsTable = document.createElement('table');
    detailsTable.style.width = '100%';
    detailsTable.style.marginTop = '30px';
    detailsTable.style.borderCollapse = 'collapse';
    detailsTable.style.fontSize = '14px';

    // Table header
    const thead = document.createElement('thead');
    thead.innerHTML = `
      <tr style="background: linear-gradient(135deg, #667eea 0%, #764ba2 100%); color: white;">
        <th style="padding: 12px; text-align: left; border: 1px solid #ddd;">#</th>
        <th style="padding: 12px; text-align: left; border: 1px solid #ddd;">Launch Name</th>
        <th style="padding: 12px; text-align: left; border: 1px solid #ddd;">Owner</th>
        <th style="padding: 12px; text-align: left; border: 1px solid #ddd;">Start Time</th>
        <th style="padding: 12px; text-align: center; border: 1px solid #ddd;">Passed</th>
        <th style="padding: 12px; text-align: center; border: 1px solid #ddd;">Failed</th>
        <th style="padding: 12px; text-align: center; border: 1px solid #ddd;">Skipped</th>
      </tr>
    `;
    detailsTable.appendChild(thead);

    // Table body with data
    const tbody = document.createElement('tbody');
    launchesData.forEach((launch, idx) => {
      const row = document.createElement('tr');
      row.style.backgroundColor = idx % 2 === 0 ? '#f9fafb' : 'white';

      const formatStartTime = (isoString) => {
        const date = new Date(isoString);
        return date.toLocaleString('en-US', {
          year: 'numeric',
          month: 'short',
          day: 'numeric',
          hour: '2-digit',
          minute: '2-digit'
        });
      };

      row.innerHTML = `
        <td style="padding: 10px; border: 1px solid #ddd; font-weight: 600;">#${idx + 1}</td>
        <td style="padding: 10px; border: 1px solid #ddd;">${launch.launchName} #${launch.launchNumber}</td>
        <td style="padding: 10px; border: 1px solid #ddd;">${launch.ownerUsername ?? 'Unknown'}</td>
        <td style="padding: 10px; border: 1px solid #ddd;">${formatStartTime(launch.startTime)}</td>
        <td style="padding: 10px; border: 1px solid #ddd; text-align: center; color: #4CAF50; font-weight: 600;">${(launch.passedPercentage ?? 0).toFixed(1)}%</td>
        <td style="padding: 10px; border: 1px solid #ddd; text-align: center; color: #F44336; font-weight: 600;">${(launch.failedPercentage ?? 0).toFixed(1)}%</td>
        <td style="padding: 10px; border: 1px solid #ddd; text-align: center; color: #FFA000; font-weight: 600;">${(launch.skippedPercentage ?? 0).toFixed(1)}%</td>
      `;
      tbody.appendChild(row);
    });
    detailsTable.appendChild(tbody);
    exportContainer.appendChild(detailsTable);

    // Capture with html2canvas
    const canvas = await html2canvas(exportContainer, {
      backgroundColor: '#ffffff',
      scale: 2,
      logging: false,
      useCORS: true
    });

    // Remove temporary container
    document.body.removeChild(exportContainer);

    // Convert to blob and download
    canvas.toBlob((blob) => {
      const url = URL.createObjectURL(blob);
      const link = document.createElement('a');
      const timestamp = new Date().toISOString().replace(/[:.]/g, '-').slice(0, 19);
      link.download = `launch-comparison-${timestamp}.png`;
      link.href = url;
      link.click();
      URL.revokeObjectURL(url);
      console.log('Export successful');
    }, 'image/png');

    return true;
  } catch (e) {
    console.error('exportComparisonChartAsPNG failed', e);
    alert('Export failed: ' + e.message);
    return false;
  }
};

// Download file helper for exports (supports both raw content and base64)
window.downloadFile = function (filename, contentOrBase64, contentType) {
  try {
    let blob;

    // Check if content is base64 encoded
    if (typeof contentOrBase64 === 'string' && contentOrBase64.length > 0) {
      // Try to decode as base64
      try {
        const byteCharacters = atob(contentOrBase64);
        const byteNumbers = new Array(byteCharacters.length);
        for (let i = 0; i < byteCharacters.length; i++) {
          byteNumbers[i] = byteCharacters.charCodeAt(i);
        }
        const byteArray = new Uint8Array(byteNumbers);
        blob = new Blob([byteArray], {type: contentType});
      } catch (e) {
        // Not base64, treat as raw content
        blob = new Blob([contentOrBase64], {type: contentType});
      }
    } else {
      blob = new Blob([contentOrBase64], {type: contentType});
    }

    const url = window.URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = filename;
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
    window.URL.revokeObjectURL(url);
    return true;
  } catch (e) {
    console.error('downloadFile failed', e);
    return false;
  }
};

// Tab state persistence using SessionStorage
// Persists tab selection during navigation within same browser tab
// Clears when browser tab closes for fresh start
window.saveTabState = function (pageKey, tabName) {
  try {
    if (!pageKey || !tabName) return false;
    sessionStorage.setItem(`tab_${pageKey}`, tabName);
    return true;
  } catch (e) {
    console.warn('Failed to save tab state:', e);
    return false;
  }
};

window.getTabState = function (pageKey, defaultTab) {
  try {
    if (!pageKey) return defaultTab;
    const savedTab = sessionStorage.getItem(`tab_${pageKey}`);
    return savedTab || defaultTab;
  } catch (e) {
    console.warn('Failed to get tab state:', e);
    return defaultTab;
  }
};
