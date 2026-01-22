/**
 * Artifact Cache Manager
 * Client-side caching for artifact pre-signed URLs using SessionStorage.
 * Reduces redundant API calls to fetch pre-signed URLs from hub.
 */
window.ArtifactCache = {
  /**
   * Cache a pre-signed URL for an artifact.
   * @param {string} artifactId - Artifact GUID
   * @param {string} url - Pre-signed URL
   * @param {string} expiresAt - ISO 8601 expiration timestamp
   */
  cacheUrl: function (artifactId, url, expiresAt) {
    try {
      const cacheEntry = {
        url: url,
        expiresAt: expiresAt,
        cachedAt: new Date().toISOString()
      };
      sessionStorage.setItem(`artifact:${artifactId}`, JSON.stringify(cacheEntry));
      console.debug(`[ArtifactCache] Cached URL for ${artifactId}, expires ${expiresAt}`);
    } catch (e) {
      console.warn('[ArtifactCache] Failed to cache URL:', e);
    }
  },

  /**
   * Get cached pre-signed URL if not expired.
   * Returns null if not cached or expired (within 5-minute safety margin).
   * @param {string} artifactId - Artifact GUID
   * @returns {string|null} - Cached URL or null
   */
  getCachedUrl: function (artifactId) {
    try {
      const cached = sessionStorage.getItem(`artifact:${artifactId}`);
      if (!cached) {
        console.debug(`[ArtifactCache] MISS for ${artifactId} (not cached)`);
        return null;
      }

      const entry = JSON.parse(cached);
      const expiresAt = new Date(entry.expiresAt);
      const now = new Date();

      // Check if expires more than 5 minutes from now (safety margin)
      const marginMs = 5 * 60 * 1000;
      if (expiresAt > new Date(now.getTime() + marginMs)) {
        console.debug(`[ArtifactCache] HIT for ${artifactId} (expires in ${Math.floor((expiresAt - now) / 1000)}s)`);
        return entry.url;
      }

      // Expired or expiring soon - remove from cache
      sessionStorage.removeItem(`artifact:${artifactId}`);
      console.debug(`[ArtifactCache] EXPIRED for ${artifactId} (removed)`);
      return null;
    } catch (e) {
      console.warn('[ArtifactCache] Failed to get cached URL:', e);
      return null;
    }
  },

  /**
   * Clear all expired artifact URLs from SessionStorage.
   * Automatically called on page load.
   */
  clearExpired: function () {
    try {
      const keys = [];
      for (let i = 0; i < sessionStorage.length; i++) {
        const key = sessionStorage.key(i);
        if (key && key.startsWith('artifact:')) {
          keys.push(key);
        }
      }

      const now = new Date();
      let clearedCount = 0;

      keys.forEach(key => {
        try {
          const cached = sessionStorage.getItem(key);
          if (cached) {
            const entry = JSON.parse(cached);
            if (new Date(entry.expiresAt) <= now) {
              sessionStorage.removeItem(key);
              clearedCount++;
            }
          }
        } catch (e) {
          // Invalid entry - remove it
          sessionStorage.removeItem(key);
          clearedCount++;
        }
      });

      if (clearedCount > 0) {
        console.debug(`[ArtifactCache] Cleared ${clearedCount} expired URLs on page load`);
      }
    } catch (e) {
      console.warn('[ArtifactCache] Failed to clear expired URLs:', e);
    }
  },

  /**
   * Clear all cached artifact URLs (for debugging/troubleshooting).
   */
  clearAll: function () {
    try {
      const keys = [];
      for (let i = 0; i < sessionStorage.length; i++) {
        const key = sessionStorage.key(i);
        if (key && key.startsWith('artifact:')) {
          keys.push(key);
        }
      }

      keys.forEach(key => sessionStorage.removeItem(key));
      console.log(`[ArtifactCache] Cleared ${keys.length} cached URLs`);
    } catch (e) {
      console.warn('[ArtifactCache] Failed to clear cache:', e);
    }
  },

  /**
   * Get cache statistics (for debugging).
   * @returns {object} - {totalCached, expiredCount, validCount}
   */
  getStats: function () {
    try {
      const keys = [];
      for (let i = 0; i < sessionStorage.length; i++) {
        const key = sessionStorage.key(i);
        if (key && key.startsWith('artifact:')) {
          keys.push(key);
        }
      }

      const now = new Date();
      let validCount = 0;
      let expiredCount = 0;

      keys.forEach(key => {
        try {
          const cached = sessionStorage.getItem(key);
          if (cached) {
            const entry = JSON.parse(cached);
            if (new Date(entry.expiresAt) > now) {
              validCount++;
            } else {
              expiredCount++;
            }
          }
        } catch (e) {
          expiredCount++;
        }
      });

      return {
        totalCached: keys.length,
        validCount: validCount,
        expiredCount: expiredCount
      };
    } catch (e) {
      console.warn('[ArtifactCache] Failed to get stats:', e);
      return {totalCached: 0, validCount: 0, expiredCount: 0};
    }
  }
};

// Auto-cleanup expired URLs on page load
if (document.readyState === 'loading') {
  document.addEventListener('DOMContentLoaded', function () {
    window.ArtifactCache.clearExpired();
  });
} else {
  // DOM already loaded
  window.ArtifactCache.clearExpired();
}

// Expose to console for debugging
console.debug('[ArtifactCache] Initialized. Use ArtifactCache.getStats() for cache info.');
