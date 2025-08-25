import { useState, useEffect, useCallback, useRef, useMemo } from "react";

/**
 * Custom hook for virtual scrolling
 * @param {Array} items - Array of items to render
 * @param {Object} options - Configuration options
 * @returns {Object} Virtual scrolling state and controls
 */
export function useVirtualScroll(items, options = {}) {
  const {
    itemHeight = 50,
    containerHeight = 400,
    overscan = 5,
    scrollThreshold = 100,
  } = options;

  const [scrollTop, setScrollTop] = useState(0);
  const [containerRef, setContainerRef] = useState(null);
  const [isScrolling, setIsScrolling] = useState(false);
  const scrollTimeoutRef = useRef(null);

  // Calculate virtual scroll dimensions
  const virtualScrollData = useMemo(() => {
    if (!items || items.length === 0) {
      return {
        totalHeight: 0,
        visibleStartIndex: 0,
        visibleEndIndex: 0,
        visibleItems: [],
        offsetY: 0,
      };
    }

    const totalHeight = items.length * itemHeight;
    const visibleStartIndex = Math.max(
      0,
      Math.floor(scrollTop / itemHeight) - overscan
    );
    const visibleEndIndex = Math.min(
      items.length - 1,
      Math.ceil((scrollTop + containerHeight) / itemHeight) + overscan
    );

    const visibleItems = items.slice(visibleStartIndex, visibleEndIndex + 1);
    const offsetY = visibleStartIndex * itemHeight;

    return {
      totalHeight,
      visibleStartIndex,
      visibleEndIndex,
      visibleItems,
      offsetY,
    };
  }, [items, scrollTop, containerHeight, itemHeight, overscan]);

  // Handle scroll events
  const handleScroll = useCallback(
    (event) => {
      const newScrollTop = event.target.scrollTop;
      setScrollTop(newScrollTop);
      setIsScrolling(true);

      // Clear existing timeout
      if (scrollTimeoutRef.current) {
        clearTimeout(scrollTimeoutRef.current);
      }

      // Set scrolling to false after threshold
      scrollTimeoutRef.current = setTimeout(() => {
        setIsScrolling(false);
      }, scrollThreshold);
    },
    [scrollThreshold]
  );

  // Scroll to specific item
  const scrollToItem = useCallback(
    (index) => {
      if (!containerRef || index < 0 || index >= items.length) return;

      const targetScrollTop = index * itemHeight;
      containerRef.scrollTo({
        top: targetScrollTop,
        behavior: "smooth",
      });
    },
    [containerRef, items.length, itemHeight]
  );

  // Scroll to top
  const scrollToTop = useCallback(() => {
    if (!containerRef) return;

    containerRef.scrollTo({
      top: 0,
      behavior: "smooth",
    });
  }, [containerRef]);

  // Scroll to bottom
  const scrollToBottom = useCallback(() => {
    if (!containerRef) return;

    const targetScrollTop = (items.length - 1) * itemHeight;
    containerRef.scrollTo({
      top: targetScrollTop,
      behavior: "smooth",
    });
  }, [containerRef, items.length, itemHeight]);

  // Get item position
  const getItemPosition = useCallback(
    (index) => {
      return {
        top: index * itemHeight,
        height: itemHeight,
      };
    },
    [itemHeight]
  );

  // Check if item is visible
  const isItemVisible = useCallback(
    (index) => {
      const { visibleStartIndex, visibleEndIndex } = virtualScrollData;
      return index >= visibleStartIndex && index <= visibleEndIndex;
    },
    [virtualScrollData]
  );

  // Cleanup timeout on unmount
  useEffect(() => {
    return () => {
      if (scrollTimeoutRef.current) {
        clearTimeout(scrollTimeoutRef.current);
      }
    };
  }, []);

  return {
    // State
    scrollTop,
    isScrolling,
    virtualScrollData,

    // Refs
    containerRef: setContainerRef,

    // Event handlers
    handleScroll,

    // Scroll actions
    scrollToItem,
    scrollToTop,
    scrollToBottom,

    // Utilities
    getItemPosition,
    isItemVisible,
  };
}

/**
 * Higher-order component for virtual scrolling
 * @param {React.Component} Component - Component to wrap
 * @param {Object} options - Virtual scroll options
 * @returns {React.Component} Wrapped component with virtual scrolling
 */
export function withVirtualScroll(Component, options = {}) {
  const WrappedComponent = (props) => {
    const virtualScroll = useVirtualScroll(props.items || [], options);

    return <Component {...props} virtualScroll={virtualScroll} />;
  };

  WrappedComponent.displayName = `withVirtualScroll(${
    Component.displayName || Component.name
  })`;

  return WrappedComponent;
}

/**
 * Hook for infinite scroll with virtual scrolling
 * @param {Function} loadMore - Function to load more data
 * @param {Object} options - Configuration options
 * @returns {Object} Infinite scroll state and controls
 */
export function useInfiniteVirtualScroll(loadMore, options = {}) {
  const {
    hasMore = true,
    loading = false,
    threshold = 100,
    itemHeight = 50,
    containerHeight = 400,
  } = options;

  const [items, setItems] = useState([]);
  const [page, setPage] = useState(1);
  const loadingRef = useRef(false);

  // Load more data
  const loadMoreData = useCallback(async () => {
    if (loadingRef.current || !hasMore || loading) return;

    try {
      loadingRef.current = true;
      const newItems = await loadMore(page);

      if (newItems && newItems.length > 0) {
        setItems((prev) => [...prev, ...newItems]);
        setPage((prev) => prev + 1);
      }
    } catch (error) {
      console.error("Failed to load more data:", error);
    } finally {
      loadingRef.current = false;
    }
  }, [loadMore, page, hasMore, loading]);

  // Check if we need to load more
  const checkLoadMore = useCallback(
    (scrollTop, totalHeight) => {
      const remainingHeight = totalHeight - scrollTop - containerHeight;

      if (remainingHeight <= threshold && hasMore && !loading) {
        loadMoreData();
      }
    },
    [containerHeight, threshold, hasMore, loading, loadMoreData]
  );

  // Reset infinite scroll
  const reset = useCallback(() => {
    setItems([]);
    setPage(1);
    loadingRef.current = false;
  }, []);

  return {
    items,
    page,
    loading,
    hasMore,
    loadMoreData,
    checkLoadMore,
    reset,
  };
}
