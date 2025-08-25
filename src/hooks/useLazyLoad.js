import { useState, useEffect, useCallback, useRef } from "react";

/**
 * Custom hook for lazy loading components with intersection observer
 * @param {Object} options - Configuration options
 * @returns {Object} Lazy loading state and controls
 */
export function useLazyLoad(options = {}) {
  const {
    threshold = 0.1,
    rootMargin = "50px",
    enabled = true,
    delay = 0,
  } = options;

  const [isVisible, setIsVisible] = useState(false);
  const [isLoaded, setIsLoaded] = useState(false);
  const elementRef = useRef(null);
  const observerRef = useRef(null);

  // Intersection observer callback
  const handleIntersection = useCallback(
    (entries) => {
      const [entry] = entries;
      if (entry.isIntersecting) {
        setIsVisible(true);

        // Optional delay before loading
        if (delay > 0) {
          setTimeout(() => setIsLoaded(true), delay);
        } else {
          setIsLoaded(true);
        }

        // Disconnect observer after first intersection
        if (observerRef.current) {
          observerRef.current.disconnect();
        }
      }
    },
    [delay]
  );

  // Set up intersection observer
  useEffect(() => {
    if (!enabled || !elementRef.current) return;

    const observer = new IntersectionObserver(handleIntersection, {
      threshold,
      rootMargin,
    });

    observer.observe(elementRef.current);
    observerRef.current = observer;

    return () => {
      if (observer) {
        observer.disconnect();
      }
    };
  }, [enabled, threshold, rootMargin, handleIntersection]);

  // Manual load trigger
  const triggerLoad = useCallback(() => {
    setIsVisible(true);
    setIsLoaded(true);
  }, []);

  // Reset loading state
  const reset = useCallback(() => {
    setIsVisible(false);
    setIsLoaded(false);
  }, []);

  return {
    elementRef,
    isVisible,
    isLoaded,
    triggerLoad,
    reset,
  };
}

/**
 * Custom hook for lazy loading data with pagination
 * @param {Function} fetchFunction - Function to fetch data
 * @param {Object} options - Configuration options
 * @returns {Object} Lazy loading data state and controls
 */
export function useLazyData(fetchFunction, options = {}) {
  const {
    pageSize = 20,
    initialPage = 1,
    autoLoad = true,
    threshold = 0.1,
  } = options;

  const [data, setData] = useState([]);
  const [loading, setLoading] = useState(false);
  const [hasMore, setHasMore] = useState(true);
  const [currentPage, setCurrentPage] = useState(initialPage);
  const [error, setError] = useState(null);
  const loadingRef = useRef(false);

  // Fetch data for a specific page
  const fetchPage = useCallback(
    async (page) => {
      if (loadingRef.current || !hasMore) return;

      try {
        loadingRef.current = true;
        setLoading(true);
        setError(null);

        const result = await fetchFunction(page, pageSize);

        if (result.success) {
          const newData = result.data || [];

          if (page === 1) {
            setData(newData);
          } else {
            setData((prev) => [...prev, ...newData]);
          }

          setHasMore(newData.length === pageSize);
          setCurrentPage(page);
        } else {
          throw new Error(result.error || "Failed to fetch data");
        }
      } catch (err) {
        setError(err.message);
        console.error("Lazy data fetch error:", err);
      } finally {
        setLoading(false);
        loadingRef.current = false;
      }
    },
    [fetchFunction, pageSize, hasMore]
  );

  // Load next page
  const loadNextPage = useCallback(() => {
    if (!loading && hasMore) {
      fetchPage(currentPage + 1);
    }
  }, [loading, hasMore, currentPage, fetchPage]);

  // Refresh data
  const refresh = useCallback(() => {
    setData([]);
    setHasMore(true);
    setCurrentPage(initialPage);
    setError(null);
    fetchPage(1);
  }, [fetchPage, initialPage]);

  // Initial load
  useEffect(() => {
    if (autoLoad) {
      fetchPage(1);
    }
  }, [autoLoad, fetchPage]);

  return {
    data,
    loading,
    hasMore,
    currentPage,
    error,
    loadNextPage,
    refresh,
    fetchPage,
  };
}

/**
 * Custom hook for lazy loading images
 * @param {string} src - Image source URL
 * @param {Object} options - Configuration options
 * @returns {Object} Image loading state
 */
export function useLazyImage(src, options = {}) {
  const {
    placeholder = "data:image/svg+xml;base64,PHN2ZyB3aWR0aD0iMTAwIiBoZWlnaHQ9IjEwMCIgeG1sbnM9Imh0dHA6Ly93d3cudzMub3JnLzIwMDAvc3ZnIj48cmVjdCB3aWR0aD0iMTAwIiBoZWlnaHQ9IjEwMCIgZmlsbD0iI2YzZjRmNiIvPjwvc3ZnPg==",
    threshold = 0.1,
  } = options;

  const [imageSrc, setImageSrc] = useState(placeholder);
  const [isLoaded, setIsLoaded] = useState(false);
  const [error, setError] = useState(null);
  const imgRef = useRef(null);

  useEffect(() => {
    if (!src) return;

    const img = new Image();
    imgRef.current = img;

    img.onload = () => {
      setImageSrc(src);
      setIsLoaded(true);
      setError(null);
    };

    img.onerror = () => {
      setError("Failed to load image");
      setIsLoaded(false);
    };

    img.src = src;

    return () => {
      if (imgRef.current) {
        imgRef.current.onload = null;
        imgRef.current.onerror = null;
      }
    };
  }, [src]);

  return {
    imageSrc,
    isLoaded,
    error,
    imgRef,
  };
}
