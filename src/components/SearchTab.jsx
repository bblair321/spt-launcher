import React, { memo, useEffect, useRef } from "react";
import {
  Search,
  Download,
  Server,
  Puzzle,
  ExternalLink,
  Star,
  Clock,
  Filter,
  SortAsc,
  SortDesc,
  X,
  TrendingUp,
  Users,
  Globe,
  RefreshCw,
  AlertCircle,
  Info,
  TrendingDown,
  Zap,
} from "lucide-react";

// Performance monitoring
import { usePerformanceMonitor } from "../hooks/usePerformanceMonitor";

// Custom search hook
import { useSearch } from "../hooks/useSearch";

// Search service for addon details
import { getAddonDetails } from "../services/searchService";

// Toast context for notifications
import { useToastContext } from "../contexts/ToastContext";

function SearchTab() {
  // Performance monitoring
  const performance = usePerformanceMonitor("SearchTab");

  // Search functionality
  const {
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
    setSearchQuery,
    setSearchCategory,
    setShowFilters,
    setSortBy,
    setSortOrder,
    setFilters,
    handleSearch,
    handleSearchQueryChange,
    handleCategoryChange,
    handleSortChange,
    handleSortOrderChange,
    handleFilterChange,
    applyFilters,
    resetFilters,
    clearSearch,
    handleDownloadAddon,
    handleCheckServerStatus,
    quickSearch,
    refreshSearch,
  } = useSearch();

  // Toast notifications
  const { showSuccess, showError, showInfo } = useToastContext();

  // Debounced search effect
  const searchTimeoutRef = useRef(null);

  useEffect(() => {
    if (searchQuery.trim()) {
      // Clear previous timeout
      if (searchTimeoutRef.current) {
        clearTimeout(searchTimeoutRef.current);
      }

      // Set new timeout for debounced search
      searchTimeoutRef.current = setTimeout(() => {
        handleSearch(null);
      }, 500); // 500ms delay
    } else {
      // Clear results if query is empty
      // This will be handled by the useSearch hook
    }

    // Cleanup timeout on unmount or query change
    return () => {
      if (searchTimeoutRef.current) {
        clearTimeout(searchTimeoutRef.current);
      }
    };
  }, [searchQuery, handleSearch]);

  // Handle download with user feedback
  const handleDownload = async (addonId, addonName, version = "latest") => {
    try {
      showInfo("Download Started", `Downloading ${addonName}...`);
      const result = await handleDownloadAddon(addonId, version);
      showSuccess(
        "Download Complete",
        `${addonName} has been downloaded successfully!`
      );
      return result;
    } catch (err) {
      showError(
        "Download Failed",
        `Failed to download ${addonName}: ${err.message}`
      );
      throw err;
    }
  };

  // Handle server status check
  const handleStatusCheck = async (serverId, serverName) => {
    try {
      showInfo("Checking Status", `Checking ${serverName} status...`);
      const result = await handleCheckServerStatus(serverId);
      showSuccess("Status Updated", `${serverName} status has been refreshed!`);
      return result;
    } catch (err) {
      showError(
        "Status Check Failed",
        `Failed to check ${serverName} status: ${err.message}`
      );
      throw err;
    }
  };

  // Handle external link
  const handleExternalLink = (url, name) => {
    if (window.electronAPI?.openExternal) {
      window.electronAPI.openExternal(url);
      showInfo("Link Opened", `${name} opened in your default browser`);
    } else {
      // Fallback for web environment
      window.open(url, "_blank");
    }
  };

  // Handle view addon details
  const handleView = async (addonId, addonName, result) => {
    try {
      let modUrl = "https://hub.sp-tarkov.com/files/";

      // Try to construct the specific mod URL
      if (result && result.downloadUrl && result.downloadUrl !== "#") {
        // Use the mod's specific download URL if available
        modUrl = result.downloadUrl;
      } else if (result && result.name) {
        // Construct URL from mod name (fallback)
        const modSlug = result.name
          .toLowerCase()
          .replace(/[^a-z0-9]+/g, "-")
          .replace(/^-+|-+$/g, "");
        modUrl = `https://hub.sp-tarkov.com/files/search?search=${encodeURIComponent(
          result.name
        )}`;
      }

      showInfo("Opening Mod Page", `Opening ${addonName} on SPT-AKI Hub...`);

      if (window.electronAPI?.openExternal) {
        window.electronAPI.openExternal(modUrl);
        showSuccess(
          "Mod Page Opened",
          `${addonName} opened on SPT-AKI Hub in your browser`
        );
      } else {
        // Fallback for web environment
        window.open(modUrl, "_blank");
        showSuccess(
          "Mod Page Opened",
          `${addonName} opened on SPT-AKI Hub in a new tab`
        );
      }
    } catch (error) {
      console.error("View addon failed:", error);
      showError("View Failed", `Failed to open mod page`, {
        errorDetails: error.message,
      });
    }
  };

  // Render trending content section
  const renderTrendingSection = () => (
    <div className="bg-gradient-to-r from-blue-50 to-indigo-50 dark:from-gray-800 dark:to-gray-700 p-6 rounded-lg border border-blue-200 dark:border-blue-800 mb-6">
      <div className="flex items-center justify-between mb-4">
        <h2 className="text-xl font-semibold text-gray-900 dark:text-gray-100 flex items-center space-x-2">
          <TrendingUp className="w-5 h-5 text-blue-600" />
          <span>Trending Now</span>
        </h2>
        <div className="flex items-center space-x-2">
          <span className="text-xs text-gray-500 dark:text-gray-400">
            Data from: {dataSource === "mock-data" ? "Demo Mode" : dataSource}
          </span>
          {dataSource === "mock-data" && (
            <span className="text-xs text-blue-600 dark:text-blue-400">
              🔒 Security protection prevents automated access
            </span>
          )}
          <button
            onClick={refreshSearch}
            className="p-2 text-gray-500 hover:text-gray-700 dark:hover:text-gray-300 transition-colors"
            title="Refresh trending content"
          >
            <RefreshCw className="w-4 h-4" />
          </button>
        </div>
      </div>

      {isLoadingTrending ? (
        <div className="flex items-center justify-center py-8">
          <div className="w-6 h-6 border-2 border-blue-600 border-t-transparent rounded-full animate-spin" />
          <span className="ml-2 text-gray-600 dark:text-gray-400">
            Loading trending content...
          </span>
        </div>
      ) : (
        <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
          {trendingContent.slice(0, 8).map((item) => (
            <div
              key={item.id}
              className="bg-white dark:bg-gray-800 p-4 rounded-lg border border-gray-200 dark:border-gray-700 hover:shadow-md transition-all cursor-pointer group"
              onClick={() => quickSearch(item.name)}
            >
              <div className="flex items-center space-x-2 mb-2">
                {item.type === "addon" && (
                  <Puzzle className="w-4 h-4 text-blue-500" />
                )}
                {item.type === "server" && (
                  <Server className="w-4 h-4 text-green-500" />
                )}
                {item.type === "community" && (
                  <ExternalLink className="w-4 h-4 text-purple-500" />
                )}
                <h3 className="font-medium text-gray-900 dark:text-gray-100 text-sm truncate">
                  {item.name}
                </h3>
              </div>

              <p className="text-xs text-gray-600 dark:text-gray-400 line-clamp-2 mb-2">
                {item.description}
              </p>

              <div className="flex items-center justify-between text-xs text-gray-500 dark:text-gray-400">
                <span className="capitalize">{item.category}</span>
                {item.type === "addon" && (
                  <div className="flex items-center space-x-1">
                    <Star className="w-3 h-3 text-yellow-500" />
                    <span>{item.rating}</span>
                  </div>
                )}
                {item.type === "server" && (
                  <span>
                    {item.players}/{item.maxPlayers}
                  </span>
                )}
              </div>
            </div>
          ))}
        </div>
      )}
    </div>
  );

  // Render search suggestions
  const renderSearchSuggestions = () => {
    if (!searchQuery.trim() || searchQuery.length < 2) return null;

    const suggestions = [];

    // Add category suggestions
    if (
      searchQuery.toLowerCase().includes("mod") ||
      searchQuery.toLowerCase().includes("addon")
    ) {
      suggestions.push("SPT Addons");
    }
    if (
      searchQuery.toLowerCase().includes("server") ||
      searchQuery.toLowerCase().includes("multi")
    ) {
      suggestions.push("SPT Servers");
    }
    if (
      searchQuery.toLowerCase().includes("help") ||
      searchQuery.toLowerCase().includes("guide")
    ) {
      suggestions.push("SPT Documentation");
    }

    // Add trending content suggestions
    trendingContent.forEach((item) => {
      if (item.name.toLowerCase().includes(searchQuery.toLowerCase())) {
        suggestions.push(item.name);
      }
    });

    const uniqueSuggestions = [...new Set(suggestions)].slice(0, 5);

    if (uniqueSuggestions.length === 0) return null;

    return (
      <div className="absolute top-full left-0 right-0 mt-1 bg-white dark:bg-gray-800 border border-gray-200 dark:border-gray-700 rounded-lg shadow-lg z-10">
        {uniqueSuggestions.map((suggestion, index) => (
          <button
            key={index}
            type="button"
            onClick={() => quickSearch(suggestion)}
            className="w-full text-left px-4 py-2 hover:bg-gray-50 dark:hover:bg-gray-700 text-gray-700 dark:text-gray-300 flex items-center space-x-2"
          >
            <Search className="w-4 h-4 text-gray-400" />
            <span>{suggestion}</span>
          </button>
        ))}
      </div>
    );
  };

  // Render filter panel
  const renderFilterPanel = () => (
    <div className="bg-white dark:bg-gray-800 p-4 rounded-lg border border-gray-200 dark:border-gray-700 shadow-sm">
      <div className="flex items-center justify-between mb-4">
        <h3 className="text-lg font-semibold text-gray-900 dark:text-gray-100 flex items-center space-x-2">
          <Filter className="w-5 h-5" />
          <span>Filters</span>
        </h3>
        <button
          onClick={resetFilters}
          className="text-sm text-blue-600 dark:text-blue-400 hover:text-blue-700 dark:hover:text-blue-300"
        >
          Reset
        </button>
      </div>

      <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
        <div>
          <label className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-2">
            Minimum Rating
          </label>
          <input
            type="range"
            min="0"
            max="5"
            step="0.1"
            value={filters.rating}
            onChange={(e) =>
              handleFilterChange("rating", parseFloat(e.target.value))
            }
            className="w-full"
          />
          <span className="text-sm text-gray-500 dark:text-gray-400">
            {filters.rating}+
          </span>
        </div>

        <div>
          <label className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-2">
            Min Downloads (K)
          </label>
          <input
            type="range"
            min="0"
            max="50"
            step="1"
            value={filters.downloads}
            onChange={(e) =>
              handleFilterChange("downloads", parseInt(e.target.value))
            }
            className="w-full"
          />
          <span className="text-sm text-gray-500 dark:text-gray-400">
            {filters.downloads}K+
          </span>
        </div>

        <div>
          <label className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-2">
            Min Players
          </label>
          <input
            type="range"
            min="0"
            max="200"
            step="5"
            value={filters.players}
            onChange={(e) =>
              handleFilterChange("players", parseInt(e.target.value))
            }
            className="w-full"
          />
          <span className="text-sm text-gray-500 dark:text-gray-400">
            {filters.players}+
          </span>
        </div>

        <div>
          <label className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-2">
            Min Uptime (%)
          </label>
          <input
            type="range"
            min="0"
            max="100"
            step="1"
            value={filters.uptime}
            onChange={(e) =>
              handleFilterChange("uptime", parseInt(e.target.value))
            }
            className="w-full"
          />
          <span className="text-sm text-gray-500 dark:text-gray-400">
            {filters.uptime}%+
          </span>
        </div>
      </div>

      <button
        onClick={applyFilters}
        className="w-full mt-4 px-4 py-2 bg-blue-600 text-white rounded-md hover:bg-blue-700 transition-colors"
      >
        Apply Filters
      </button>
    </div>
  );

  // Render search result item
  const renderSearchResult = (result) => (
    <div
      key={result.id}
      className="p-4 border border-gray-200 dark:border-gray-700 rounded-lg hover:bg-gray-50 dark:hover:bg-gray-700/50 transition-colors"
    >
      <div className="flex items-start justify-between">
        <div className="flex-1">
          <div className="flex items-center space-x-2 mb-2">
            {result.type === "addon" && (
              <Puzzle className="w-5 h-5 text-blue-500" />
            )}
            {result.type === "server" && (
              <Server className="w-5 h-5 text-green-500" />
            )}
            {result.type === "community" && (
              <ExternalLink className="w-5 h-5 text-purple-500" />
            )}
            <h3 className="text-lg font-semibold text-gray-900 dark:text-gray-100">
              {result.name}
            </h3>
            <span className="px-2 py-1 bg-gray-100 dark:bg-gray-700 text-gray-600 dark:text-gray-400 text-xs rounded-full">
              {result.category}
            </span>
            {result.type === "addon" && result.compatibility && (
              <span className="px-2 py-1 bg-blue-100 dark:bg-blue-900/20 text-blue-800 dark:text-blue-300 text-xs rounded-full">
                {result.compatibility}
              </span>
            )}
          </div>

          <p className="text-gray-600 dark:text-gray-400 mb-3 text-sm sm:text-base">
            {result.description}
          </p>

          {/* Tags */}
          {result.tags && (
            <div className="flex flex-wrap gap-1 mb-3">
              {result.tags.slice(0, 5).map((tag, index) => (
                <span
                  key={index}
                  className="px-2 py-1 bg-gray-100 dark:bg-gray-800 text-gray-600 dark:text-gray-400 text-xs rounded-full"
                >
                  #{tag}
                </span>
              ))}
            </div>
          )}

          <div className="flex flex-wrap items-center gap-4 text-sm text-gray-500 dark:text-gray-400">
            {result.type === "addon" && (
              <>
                <div className="flex items-center space-x-1">
                  <Star className="w-4 h-4 text-yellow-500" />
                  <span>{result.rating}</span>
                </div>
                <div className="flex items-center space-x-1">
                  <Download className="w-4 h-4" />
                  <span>{result.downloads.toLocaleString()}</span>
                </div>
                <span>by {result.author}</span>
                <span>v{result.version}</span>
                {result.lastUpdated && (
                  <span className="flex items-center space-x-1">
                    <Clock className="w-4 h-4" />
                    <span>{result.lastUpdated}</span>
                  </span>
                )}
                {result.size && (
                  <span className="flex items-center space-x-1">
                    <Zap className="w-4 h-4" />
                    <span>{result.size}</span>
                  </span>
                )}
              </>
            )}

            {result.type === "server" && (
              <>
                <span className="flex items-center space-x-1">
                  <Users className="w-4 h-4" />
                  <span>
                    {result.players}/{result.maxPlayers} players
                  </span>
                </span>
                <span className="flex items-center space-x-1">
                  <Globe className="w-4 h-4" />
                  <span>{result.location}</span>
                </span>
                <span className="flex items-center space-x-1">
                  <TrendingUp className="w-4 h-4" />
                  <span>Uptime: {result.uptime}%</span>
                </span>
                {result.lastRestart && (
                  <span className="flex items-center space-x-1">
                    <Clock className="w-4 h-4" />
                    <span>Restart: {result.lastRestart}</span>
                  </span>
                )}
                {result.version && (
                  <span className="flex items-center space-x-1">
                    <Info className="w-4 h-4" />
                    <span>v{result.version}</span>
                  </span>
                )}
              </>
            )}

            {result.type === "community" && (
              <>
                {result.visits && (
                  <div className="flex items-center space-x-1">
                    <Clock className="w-4 h-4" />
                    <span>{result.visits.toLocaleString()} visits</span>
                  </div>
                )}
                {result.members && (
                  <span className="flex items-center space-x-1">
                    <Users className="w-4 h-4" />
                    <span>{result.members.toLocaleString()} members</span>
                  </span>
                )}
                {result.subscribers && (
                  <span className="flex items-center space-x-1">
                    <Users className="w-4 h-4" />
                    <span>
                      {result.subscribers.toLocaleString()} subscribers
                    </span>
                  </span>
                )}
                {result.lastUpdated && (
                  <span className="flex items-center space-x-1">
                    <Clock className="w-4 h-4" />
                    <span>Updated {result.lastUpdated}</span>
                  </span>
                )}
              </>
            )}
          </div>
        </div>

        <div className="flex flex-col space-y-2 ml-4">
          {/* View/Visit Button */}
          {result.type === "community" && result.url ? (
            <button
              onClick={() => handleExternalLink(result.url, result.name)}
              className="px-4 py-2 bg-blue-600 text-white text-sm rounded-md hover:bg-blue-700 transition-colors flex items-center space-x-2"
            >
              <ExternalLink className="w-4 h-4" />
              <span className="hidden sm:inline">Visit</span>
            </button>
          ) : (
            <button
              onClick={() => handleView(result.id, result.name, result)}
              className="px-4 py-2 bg-blue-600 text-white text-sm rounded-md hover:bg-blue-700 transition-colors flex items-center space-x-2"
            >
              <ExternalLink className="w-4 h-4" />
              <span className="hidden sm:inline">View</span>
            </button>
          )}

          {/* Download Button for Addons */}
          {result.type === "addon" && (
            <button
              onClick={() => handleDownload(result.id, result.name)}
              className="px-4 py-2 bg-green-600 text-white text-sm rounded-md hover:bg-green-700 transition-colors flex items-center space-x-2"
            >
              <Download className="w-4 h-4" />
              <span className="hidden sm:inline">Download</span>
            </button>
          )}

          {/* Status Check Button for Servers */}
          {result.type === "server" && (
            <button
              onClick={() => handleStatusCheck(result.id, result.name)}
              className="px-4 py-2 bg-purple-600 text-white text-sm rounded-md hover:bg-purple-700 transition-colors flex items-center space-x-2"
            >
              <RefreshCw className="w-4 h-4" />
              <span className="hidden sm:inline">Check Status</span>
            </button>
          )}
        </div>
      </div>
    </div>
  );

  return (
    <div className="space-y-4 sm:space-y-6">
      {/* Header */}
      <div className="text-center">
        <h1 className="text-2xl sm:text-3xl font-bold text-gray-900 dark:text-gray-100 mb-2">
          Search & Discovery
        </h1>
        <p className="text-sm sm:text-base text-gray-600 dark:text-gray-400 px-2">
          Find SPT addons, servers, and community content
        </p>
      </div>

      <div className="max-w-6xl mx-auto px-2 sm:px-0">
        {/* Trending Content */}
        {renderTrendingSection()}

        {/* Search Form */}
        <form onSubmit={handleSearch} className="mb-6">
          <div className="space-y-4">
            {/* Search Input and Button */}
            <div className="relative">
              <div className="flex space-x-2">
                <div className="relative flex-1">
                  <Search className="absolute left-3 top-1/2 transform -translate-y-1/2 w-5 h-5 text-gray-400" />
                  <input
                    type="text"
                    value={searchQuery}
                    onChange={(e) => setSearchQuery(e.target.value)}
                    onKeyDown={(e) => {
                      if (e.key === "Enter") {
                        e.preventDefault();
                        handleSearch(e);
                      }
                    }}
                    placeholder="Search for addons, servers, or community content..."
                    className="w-full pl-10 pr-4 py-3 border border-gray-300 dark:border-gray-600 rounded-lg bg-white dark:bg-gray-700 text-gray-900 dark:text-gray-100 text-base sm:text-lg"
                  />
                  {searchQuery && (
                    <button
                      type="button"
                      onClick={clearSearch}
                      className="absolute right-3 top-1/2 transform -translate-y-1/2 text-gray-400 hover:text-gray-600 dark:hover:text-gray-300"
                    >
                      <X className="w-4 h-4" />
                    </button>
                  )}
                </div>
                <button
                  type="submit"
                  disabled={isSearching}
                  className="px-6 py-3 bg-blue-600 text-white rounded-lg hover:bg-blue-700 disabled:opacity-50 disabled:cursor-not-allowed transition-colors flex items-center space-x-2"
                >
                  {isSearching ? (
                    <div className="w-5 h-5 border-2 border-white border-t-transparent rounded-full animate-spin" />
                  ) : (
                    <Search className="w-5 h-5" />
                  )}
                  <span className="hidden sm:inline">
                    {isSearching ? "Searching..." : "Search"}
                  </span>
                </button>
              </div>

              {/* Search Suggestions */}
              {renderSearchSuggestions()}
            </div>

            {/* Category Filter and Controls */}
            <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-4">
              <div className="flex flex-wrap gap-2">
                {["all", "addons", "servers", "community"].map((category) => (
                  <button
                    key={category}
                    type="button"
                    onClick={() => handleCategoryChange(category)}
                    className={`px-4 py-2 rounded-lg border transition-colors text-sm ${
                      searchCategory === category
                        ? "bg-blue-600 text-white border-blue-600"
                        : "bg-white dark:bg-gray-700 text-gray-700 dark:text-gray-300 border-gray-300 dark:border-gray-600 hover:bg-gray-50 dark:hover:bg-gray-600"
                    }`}
                  >
                    {category.charAt(0).toUpperCase() + category.slice(1)}
                  </button>
                ))}
              </div>

              <div className="flex items-center space-x-2">
                <button
                  type="button"
                  onClick={() => setShowFilters(!showFilters)}
                  className={`px-3 py-2 rounded-md border transition-colors text-sm flex items-center space-x-2 ${
                    showFilters
                      ? "bg-blue-600 text-white border-blue-600"
                      : "bg-white dark:bg-gray-700 text-gray-700 dark:text-gray-300 border-gray-300 dark:border-gray-600 hover:bg-gray-50 dark:hover:bg-gray-600"
                  }`}
                >
                  <Filter className="w-4 h-4" />
                  <span className="hidden sm:inline">Filters</span>
                </button>

                <select
                  value={sortBy}
                  onChange={(e) => handleSortChange(e.target.value)}
                  className="px-3 py-2 border border-gray-300 dark:border-gray-600 rounded-md bg-white dark:bg-gray-700 text-gray-700 dark:text-gray-300 text-sm"
                >
                  <option value="relevance">Relevance</option>
                  <option value="rating">Rating</option>
                  <option value="downloads">Downloads</option>
                  <option value="players">Players</option>
                  <option value="uptime">Uptime</option>
                  <option value="name">Name</option>
                </select>

                <button
                  type="button"
                  onClick={handleSortOrderChange}
                  className="p-2 border border-gray-300 dark:border-gray-600 rounded-md bg-white dark:bg-gray-700 text-gray-700 dark:text-gray-300 hover:bg-gray-50 dark:hover:bg-gray-600 transition-colors"
                >
                  {sortOrder === "asc" ? (
                    <SortAsc className="w-4 h-4" />
                  ) : (
                    <SortDesc className="w-4 h-4" />
                  )}
                </button>
              </div>
            </div>
          </div>
        </form>

        {/* Filters Panel */}
        {showFilters && renderFilterPanel()}

        {/* Error Display */}
        {error && (
          <div className="mb-6 p-4 bg-red-50 dark:bg-red-900/20 border border-red-200 dark:border-red-800 rounded-lg">
            <div className="flex items-center space-x-2 text-red-800 dark:text-red-200">
              <AlertCircle className="w-5 h-5" />
              <span className="font-medium">Search Error</span>
            </div>
            <p className="mt-1 text-red-700 dark:text-red-300 text-sm">
              {error}
            </p>
            {/* Show CSRF protection message with helpful action */}
            {error.includes("CSRF protection") && (
              <div className="mt-3 p-3 bg-blue-50 dark:bg-blue-900/20 border border-blue-200 dark:border-blue-800 rounded-lg">
                <div className="flex items-center justify-between">
                  <div className="flex-1">
                    <p className="text-blue-800 dark:text-blue-200 text-sm font-medium mb-2">
                      🔒 Security Protection Detected
                    </p>
                    <p className="text-blue-700 dark:text-blue-300 text-xs">
                      SPT-AKI Hub has security measures that prevent automated
                      access. This is normal and protects the website from
                      abuse.
                    </p>
                  </div>
                  <button
                    onClick={() =>
                      handleExternalLink(
                        "https://hub.sp-tarkov.com/files/",
                        "SPT-AKI Hub"
                      )
                    }
                    className="ml-4 px-4 py-2 bg-blue-600 text-white text-sm rounded-md hover:bg-blue-700 transition-colors flex items-center space-x-2"
                  >
                    <ExternalLink className="w-4 h-4" />
                    <span>Visit Hub</span>
                  </button>
                </div>
              </div>
            )}
          </div>
        )}

        {/* Recent Searches */}
        {recentSearches.length > 0 && (
          <div className="mb-6">
            <h3 className="text-sm font-medium text-gray-700 dark:text-gray-300 mb-2 flex items-center space-x-2">
              <Clock className="w-4 h-4" />
              <span>Recent Searches:</span>
            </h3>
            <div className="flex flex-wrap gap-2">
              {recentSearches.map((search, index) => (
                <button
                  key={index}
                  onClick={() => quickSearch(search)}
                  className="px-3 py-1 bg-gray-100 dark:bg-gray-700 text-gray-700 dark:text-gray-300 rounded-full text-sm hover:bg-gray-200 dark:hover:bg-gray-600 transition-colors"
                >
                  {search}
                </button>
              ))}
            </div>
          </div>
        )}

        {/* Search Results */}
        <div className="bg-white dark:bg-gray-800 p-4 sm:p-6 rounded-lg border border-gray-200 dark:border-gray-700 shadow-sm">
          <div className="flex items-center justify-between mb-4">
            <h2 className="text-lg sm:text-xl font-semibold flex items-center space-x-2 text-gray-900 dark:text-gray-100">
              <Search className="w-5 h-5" />
              <span>Search Results</span>
              {searchResults.length > 0 && (
                <span className="text-sm text-gray-500 dark:text-gray-400">
                  ({searchResults.length} results)
                </span>
              )}
            </h2>
            {/* Debug info */}
            <div className="text-xs text-gray-500 dark:text-gray-400">
              Debug: {searchResults.length} results, Source: {dataSource}
              {searchResults.length > 0 && (
                <div className="mt-1">
                  First result: {searchResults[0]?.name || "No name"} (
                  {searchResults[0]?.type || "No type"})
                </div>
              )}
              <div className="mt-1">
                Query: "{searchQuery}", Category: {searchCategory}
              </div>
              <div className="mt-1">
                Is Searching: {isSearching ? "Yes" : "No"}
              </div>
            </div>
            {searchResults.length > 0 && (
              <div className="flex items-center space-x-2 text-xs text-gray-500 dark:text-gray-400">
                <span>
                  Source:{" "}
                  {dataSource === "mock-data" ? "Demo Mode" : dataSource}
                </span>
                {dataSource === "mock-data" && (
                  <span className="text-blue-600 dark:text-blue-400">
                    🔒 Security protection prevents automated access
                  </span>
                )}
                <button
                  onClick={refreshSearch}
                  className="p-1 hover:text-gray-700 dark:hover:text-gray-300 transition-colors"
                  title="Refresh results"
                >
                  <RefreshCw className="w-4 h-4" />
                </button>
              </div>
            )}
          </div>

          {searchResults.length === 0 ? (
            <div className="text-center py-12 text-gray-500 dark:text-gray-400">
              <Search className="w-16 h-16 mx-auto mb-4 opacity-50" />
              <p className="text-lg">
                {searchQuery ? "No results found" : "No search results yet"}
              </p>
              <p className="text-sm">
                {searchQuery
                  ? `No results found for "${searchQuery}" in ${searchCategory} category`
                  : "Enter a search term above to find SPT content"}
              </p>
              {/* Additional debug info when no results */}
              {searchQuery && (
                <div className="mt-4 p-3 bg-gray-100 dark:bg-gray-700 rounded text-xs">
                  <p>Debug: Search query "{searchQuery}" returned 0 results</p>
                  <p>Category: {searchCategory}</p>
                  <p>DataSource: {dataSource}</p>
                  <p>IsSearching: {isSearching}</p>
                </div>
              )}
            </div>
          ) : (
            <div className="space-y-4">
              {/* Debug: Show raw results data */}
              <div className="p-3 bg-blue-50 dark:bg-blue-900/20 rounded text-xs text-blue-800 dark:text-blue-200">
                <p>Debug: Found {searchResults.length} results</p>
                <p>First 3 results:</p>
                <ul className="mt-1 space-y-1">
                  {searchResults.slice(0, 3).map((result, index) => (
                    <li key={index}>
                      {index + 1}. {result.name} ({result.type}) -{" "}
                      {result.source || "no source"}
                    </li>
                  ))}
                </ul>
              </div>
              {searchResults.map(renderSearchResult)}
            </div>
          )}
        </div>
      </div>
    </div>
  );
}

export default memo(SearchTab);
