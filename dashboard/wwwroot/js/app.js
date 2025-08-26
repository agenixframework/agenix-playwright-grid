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
