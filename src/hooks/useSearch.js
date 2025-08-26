import { useState, useCallback, useRef, useEffect } from "react";
import {
  searchAddons,
  searchServers,
  searchCommunity,
  downloadAddon,
  checkServerStatus,
  getTrendingContent,
} from "../services/searchService";

/**
 * Custom hook for managing search functionality
 */
export function useSearch() {
  const [searchQuery, setSearchQuery] = useState("");
  const [searchResults, setSearchResults] = useState([]);
  const [isSearching, setIsSearching] = useState(false);
  const [searchCategory, setSearchCategory] = useState("all");
  const [recentSearches, setRecentSearches] = useState([]);
  const [showFilters, setShowFilters] = useState(false);
  const [sortBy, setSortBy] = useState("relevance");
  const [sortOrder, setSortOrder] = useState("desc");
  const [filters, setFilters] = useState({
    rating: 0,
    downloads: 0,
    players: 0,
    uptime: 0,
  });
  const [dataSource, setDataSource] = useState("mock-data");
  const [error, setError] = useState(null);
  const [trendingContent, setTrendingContent] = useState([]);
  const [isLoadingTrending, setIsLoadingTrending] = useState(false);

  // Refs for debouncing
  const searchTimeoutRef = useRef(null);
  const abortControllerRef = useRef(null);

  // Load trending content on mount
  useEffect(() => {
    loadTrendingContent();
  }, []);

  // Load trending content
  const loadTrendingContent = useCallback(async () => {
    setIsLoadingTrending(true);
    try {
      const result = await getTrendingContent("all", 8);
      if (result.success) {
        setTrendingContent(result.data);
        setDataSource(result.source);
      }
    } catch (err) {
      console.error("Failed to load trending content:", err);
    } finally {
      setIsLoadingTrending(false);
    }
  }, []);

  // Perform search with debouncing
  const performSearch = useCallback(
    async (query, category, sortBy, sortOrder, filters) => {
      if (!query.trim()) {
        setSearchResults([]);
        return;
      }

      // Clear previous timeout
      if (searchTimeoutRef.current) {
        clearTimeout(searchTimeoutRef.current);
      }

      // Abort previous request
      if (abortControllerRef.current) {
        abortControllerRef.current.abort();
      }

      // Create new abort controller
      abortControllerRef.current = new AbortController();

      // Debounce search
      searchTimeoutRef.current = setTimeout(async () => {
        setIsSearching(true);
        setError(null);

        try {
          let results = [];
          let sources = [];

          // Search based on category
          if (category === "all" || category === "addons") {
            console.log(`Searching addons for query: "${query}"`);
            const addonResults = await searchAddons(query, filters);
            console.log(`Addon search results:`, {
              success: addonResults.success,
              dataLength: addonResults.data?.length || 0,
              source: addonResults.source,
              firstResult: addonResults.data?.[0],
            });

            if (addonResults.success && addonResults.data) {
              const addonData = Array.isArray(addonResults.data)
                ? addonResults.data
                : [addonResults.data];
              console.log(
                `Adding ${addonData.length} addon results to search results`
              );
              results.push(...addonData);
              sources.push(addonResults.source);
            }
          }

          if (category === "all" || category === "servers") {
            const serverResults = await searchServers(query, filters);
            if (serverResults.success) {
              results.push(...serverResults.data);
              sources.push(serverResults.source);
            }
          }

          if (category === "all" || category === "community") {
            const communityResults = await searchCommunity(query, filters);
            if (communityResults.success) {
              results.push(...communityResults.data);
              sources.push(communityResults.source);
            }
          }

          // Determine the primary data source
          let source = "mock-data";
          if (sources.includes("spt-hub-scraped")) {
            source = "spt-hub-scraped";
          } else if (sources.includes("spt-api")) {
            source = "spt-api";
          } else if (sources.length > 0) {
            source = sources[0]; // Use the first source if no preferred ones
          }

          // Sort results
          const sortedResults = sortResults(results, sortBy, sortOrder, query);

          console.log("=== SEARCH DEBUG INFO ===");
          console.log("Query:", query);
          console.log("Category:", category);
          console.log("Total results found:", results.length);
          console.log("Sources:", sources);
          console.log("Primary source:", source);

          if (results.length > 0) {
            console.log("First result:", JSON.stringify(results[0], null, 2));
            console.log(
              "First 3 results:",
              results
                .slice(0, 3)
                .map((r) => ({ id: r.id, name: r.name, type: r.type }))
            );
          }

          console.log("Sorted results count:", sortedResults.length);
          console.log("=== END DEBUG INFO ===");

          console.log("About to set search results:", {
            resultsLength: sortedResults.length,
            firstResult: sortedResults[0],
            allResults: sortedResults.map((r) => ({
              name: r.name,
              id: r.id,
              type: r.type,
            })),
          });

          setSearchResults(sortedResults);
          setDataSource(source);

          console.log("State updated:", {
            searchResultsLength: sortedResults.length,
            dataSource: source,
            timestamp: new Date().toISOString(),
          });

          // Add to recent searches
          if (!recentSearches.includes(query)) {
            setRecentSearches((prev) => [query, ...prev.slice(0, 4)]);
          }
        } catch (err) {
          if (err.name !== "AbortError") {
            setError(err.message);
            console.error("Search failed:", err);
          }
        } finally {
          setIsSearching(false);
        }
      }, 300); // 300ms debounce
    },
    [recentSearches, filters]
  );

  // Sort results based on criteria
  const sortResults = useCallback((results, sortBy, sortOrder, query) => {
    const sorted = [...results].sort((a, b) => {
      let aValue, bValue;

      switch (sortBy) {
        case "rating":
          aValue = a.rating || 0;
          bValue = b.rating || 0;
          break;
        case "downloads":
          aValue = a.downloads || 0;
          bValue = b.downloads || 0;
          break;
        case "players":
          aValue = a.players || 0;
          bValue = b.players || 0;
          break;
        case "uptime":
          aValue = a.uptime || 0;
          bValue = b.uptime || 0;
          break;
        case "name":
          aValue = a.name.toLowerCase();
          bValue = b.name.toLowerCase();
          break;
        case "relevance":
        default:
          aValue = getRelevanceScore(a, query);
          bValue = getRelevanceScore(b, query);
          break;
      }

      if (sortOrder === "asc") {
        return aValue > bValue ? 1 : -1;
      } else {
        return aValue < bValue ? 1 : -1;
      }
    });

    return sorted;
  }, []);

  // Calculate relevance score for search results
  const getRelevanceScore = useCallback((item, searchTerm) => {
    if (!searchTerm) return 0;

    let score = 0;
    const term = searchTerm.toLowerCase();

    if (item.name.toLowerCase().includes(term)) score += 10;
    if (item.description.toLowerCase().includes(term)) score += 5;
    if (item.category.toLowerCase().includes(term)) score += 3;
    if (item.tags && item.tags.some((tag) => tag.toLowerCase().includes(term)))
      score += 2;
    if (
      item.features &&
      item.features.some((feature) => feature.toLowerCase().includes(term))
    )
      score += 1;

    return score;
  }, []);

  // Handle search submission
  const handleSearch = useCallback(
    (e) => {
      e?.preventDefault();
      if (!searchQuery.trim()) return;

      performSearch(searchQuery, searchCategory, sortBy, sortOrder, filters);
    },
    [searchQuery, searchCategory, sortBy, sortOrder, filters, performSearch]
  );

  // Handle search query change with auto-search
  const handleSearchQueryChange = useCallback(
    (newQuery) => {
      setSearchQuery(newQuery);

      if (newQuery.trim()) {
        performSearch(newQuery, searchCategory, sortBy, sortOrder, filters);
      } else {
        setSearchResults([]);
      }
    },
    [searchCategory, sortBy, sortOrder, filters, performSearch]
  );

  // Handle category change
  const handleCategoryChange = useCallback(
    (category) => {
      setSearchCategory(category);
      if (searchQuery.trim()) {
        performSearch(searchQuery, category, sortBy, sortOrder, filters);
      }
    },
    [searchQuery, sortBy, sortOrder, filters, performSearch]
  );

  // Handle sort change
  const handleSortChange = useCallback(
    (newSortBy) => {
      setSortBy(newSortBy);
      if (searchResults.length > 0) {
        const sorted = sortResults(
          searchResults,
          newSortBy,
          sortOrder,
          searchQuery
        );
        setSearchResults(sorted);
      }
    },
    [searchResults, sortOrder, searchQuery, sortResults]
  );

  // Handle sort order change
  const handleSortOrderChange = useCallback(() => {
    const newOrder = sortOrder === "asc" ? "desc" : "asc";
    setSortOrder(newOrder);
    if (searchResults.length > 0) {
      const sorted = sortResults(searchResults, sortBy, newOrder, searchQuery);
      setSearchResults(sorted);
    }
  }, [searchResults, sortBy, sortOrder, searchQuery, sortResults]);

  // Handle filter change
  const handleFilterChange = useCallback((filterName, value) => {
    setFilters((prev) => ({ ...prev, [filterName]: value }));
  }, []);

  // Apply filters
  const applyFilters = useCallback(() => {
    if (searchQuery.trim()) {
      performSearch(searchQuery, searchCategory, sortBy, sortOrder, filters);
    }
  }, [searchQuery, searchCategory, sortBy, sortOrder, filters, performSearch]);

  // Reset filters
  const resetFilters = useCallback(() => {
    setFilters({
      rating: 0,
      downloads: 0,
      players: 0,
      uptime: 0,
    });
  }, []);

  // Clear search
  const clearSearch = useCallback(() => {
    setSearchQuery("");
    setSearchResults([]);
    setShowFilters(false);
    setError(null);
    resetFilters();
  }, [resetFilters]);

  // Download addon
  const handleDownloadAddon = useCallback(
    async (addonId, version = "latest") => {
      try {
        const result = await downloadAddon(addonId, version);
        if (result.success) {
          // Update local state or show success message
          console.log("Download successful:", result.message);
          return result;
        } else {
          throw new Error(result.error);
        }
      } catch (err) {
        console.error("Download failed:", err);
        throw err;
      }
    },
    []
  );

  // Check server status
  const handleCheckServerStatus = useCallback(async (serverId) => {
    try {
      const result = await checkServerStatus(serverId);
      if (result.success) {
        // Update server status in results
        setSearchResults((prev) =>
          prev.map((item) =>
            item.id === serverId && item.type === "server"
              ? { ...item, ...result.data }
              : item
          )
        );
        return result;
      } else {
        throw new Error(result.error);
      }
    } catch (err) {
      console.error("Status check failed:", err);
      throw err;
    }
  }, []);

  // Quick search from recent searches
  const quickSearch = useCallback(
    (query) => {
      setSearchQuery(query);
      performSearch(query, searchCategory, sortBy, sortOrder, filters);
    },
    [searchCategory, sortBy, sortOrder, filters, performSearch]
  );

  // Refresh search results
  const refreshSearch = useCallback(() => {
    if (searchQuery.trim()) {
      performSearch(searchQuery, searchCategory, sortBy, sortOrder, filters);
    }
  }, [searchQuery, searchCategory, sortBy, sortOrder, filters, performSearch]);

  // Cleanup on unmount
  useEffect(() => {
    return () => {
      if (searchTimeoutRef.current) {
        clearTimeout(searchTimeoutRef.current);
      }
      if (abortControllerRef.current) {
        abortControllerRef.current.abort();
      }
    };
  }, []);

  return {
    // State
    searchQuery,
    searchResults,
    isSearching,
    searchCategory,
    recentSearches,
    showFilters,
    sortBy,
    sortOrder,
    filters,
    dataSource,
    error,
    trendingContent,
    isLoadingTrending,

    // Actions
    setSearchQuery: handleSearchQueryChange,
    setSearchCategory: handleCategoryChange,
    setShowFilters,
    setSortBy: handleSortChange,
    setSortOrder: handleSortOrderChange,
    setFilters: handleFilterChange,

    // Functions
    handleSearch,
    performSearch,
    applyFilters,
    resetFilters,
    clearSearch,
    handleDownloadAddon,
    handleCheckServerStatus,
    quickSearch,
    refreshSearch,
    loadTrendingContent,
  };
}
