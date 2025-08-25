import { useEffect, useRef, useCallback } from "react";

/**
 * Custom hook for monitoring component performance
 * @param {string} componentName - Name of the component being monitored
 * @param {boolean} enabled - Whether performance monitoring is enabled
 * @returns {Object} Performance monitoring utilities
 */
export function usePerformanceMonitor(componentName, enabled = true) {
  const renderCount = useRef(0);
  const lastRenderTime = useRef(performance.now());
  const renderTimes = useRef([]);

  // Track render performance
  useEffect(() => {
    if (!enabled) return;

    const currentTime = performance.now();
    const renderTime = currentTime - lastRenderTime.current;

    renderCount.current += 1;
    renderTimes.current.push(renderTime);

    // Keep only last 10 render times for performance
    if (renderTimes.current.length > 10) {
      renderTimes.current.shift();
    }

    lastRenderTime.current = currentTime;

    // Log performance data in development
    if (process.env.NODE_ENV === "development") {
      console.log(
        `[Performance] ${componentName} render #${
          renderCount.current
        }: ${renderTime.toFixed(2)}ms`
      );

      if (renderTime > 16) {
        // 60fps threshold
        console.warn(
          `[Performance] ${componentName} slow render: ${renderTime.toFixed(
            2
          )}ms`
        );
      }
    }
  });

  // Get performance statistics
  const getPerformanceStats = useCallback(() => {
    if (renderTimes.current.length === 0) return null;

    const avgRenderTime =
      renderTimes.current.reduce((a, b) => a + b, 0) /
      renderTimes.current.length;
    const maxRenderTime = Math.max(...renderTimes.current);
    const minRenderTime = Math.min(...renderTimes.current);

    return {
      componentName,
      renderCount: renderCount.current,
      averageRenderTime: avgRenderTime,
      maxRenderTime,
      minRenderTime,
      recentRenderTimes: renderTimes.current,
      isPerformingWell: avgRenderTime < 16, // 60fps threshold
    };
  }, [componentName]);

  // Reset performance tracking
  const resetPerformanceTracking = useCallback(() => {
    renderCount.current = 0;
    renderTimes.current = [];
    lastRenderTime.current = performance.now();
  }, []);

  return {
    renderCount: renderCount.current,
    getPerformanceStats,
    resetPerformanceTracking,
    isEnabled: enabled,
  };
}

/**
 * Higher-order component for performance monitoring
 * @param {React.Component} Component - Component to wrap
 * @param {string} displayName - Display name for the component
 * @returns {React.Component} Wrapped component with performance monitoring
 */
export function withPerformanceMonitoring(Component, displayName) {
  const WrappedComponent = (props) => {
    const performance = usePerformanceMonitor(
      displayName || Component.displayName || Component.name
    );

    return <Component {...props} performance={performance} />;
  };

  WrappedComponent.displayName = `withPerformanceMonitoring(${
    displayName || Component.displayName || Component.name
  })`;

  return WrappedComponent;
}
