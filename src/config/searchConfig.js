/**
 * Search Configuration for SPT Launcher
 * This file allows easy configuration of search behavior and API endpoints
 */

export const SEARCH_CONFIG = {
  // Enable/disable demo mode (when true, uses mock data instead of real APIs)
  DEMO_MODE: false,

  // API endpoints for different content sources
  // NOTE: These endpoints are currently returning 404 errors
  // The SPT-AKI Hub API structure may be different than expected
  // When the correct API endpoints are identified, set DEMO_MODE to false
  API_ENDPOINTS: {
    SPT_ADDONS: "https://hub.sp-tarkov.com/api/search",
    SPT_SERVERS: "https://hub.sp-tarkov.com/api/search",
    SPT_COMMUNITY: "https://hub.sp-tarkov.com/api/search",
    SPT_WIKI: "https://hub.sp-tarkov.com/api/search",
    SPT_DOWNLOADS: "https://hub.sp-tarkov.com/api/search",
    SPT_MODS: "https://hub.sp-tarkov.com/api/search",
  },

  // Search behavior settings
  SEARCH: {
    // Debounce delay for search input (milliseconds)
    DEBOUNCE_DELAY: 300,

    // Maximum number of recent searches to remember
    MAX_RECENT_SEARCHES: 10,

    // Number of trending items to show
    TRENDING_ITEMS_LIMIT: 8,

    // Enable real-time search suggestions
    ENABLE_SUGGESTIONS: true,

    // Enable auto-search on input change
    ENABLE_AUTO_SEARCH: true,
  },

  // Cache settings
  CACHE: {
    // Cache duration for API responses (milliseconds)
    DURATION: 5 * 60 * 1000, // 5 minutes

    // Enable/disable caching
    ENABLED: true,
  },

  // Filter defaults
  FILTERS: {
    DEFAULT_RATING: 0,
    DEFAULT_DOWNLOADS: 0,
    DEFAULT_PLAYERS: 0,
    DEFAULT_UPTIME: 0,

    // Filter ranges
    RATING_RANGE: { min: 0, max: 5, step: 0.1 },
    DOWNLOADS_RANGE: { min: 0, max: 50, step: 1 },
    PLAYERS_RANGE: { min: 0, max: 200, step: 5 },
    UPTIME_RANGE: { min: 0, max: 100, step: 1 },
  },

  // Sort options
  SORT_OPTIONS: [
    { value: "relevance", label: "Relevance" },
    { value: "rating", label: "Rating" },
    { value: "downloads", label: "Downloads" },
    { value: "players", label: "Players" },
    { value: "uptime", label: "Uptime" },
    { value: "name", label: "Name" },
  ],

  // Category options
  CATEGORIES: [
    { value: "all", label: "All", icon: "Search" },
    { value: "client mods", label: "Client Mods", icon: "Puzzle" },
    { value: "server mods", label: "Server Mods", icon: "Server" },
    { value: "tools", label: "Tools", icon: "Wrench" },
    { value: "overhauls", label: "Overhauls", icon: "RefreshCw" },
    { value: "releases", label: "Releases", icon: "Package" },
  ],

  // Feature flags
  FEATURES: {
    // Enable addon downloads
    ENABLE_DOWNLOADS: true,

    // Enable server status checking
    ENABLE_SERVER_STATUS: true,

    // Enable external link handling
    ENABLE_EXTERNAL_LINKS: true,

    // Enable trending content
    ENABLE_TRENDING: true,

    // Enable search suggestions
    ENABLE_SUGGESTIONS: true,

    // Enable advanced filtering
    ENABLE_ADVANCED_FILTERS: true,
  },

  // UI settings
  UI: {
    // Show data source indicator
    SHOW_DATA_SOURCE: true,

    // Show refresh buttons
    SHOW_REFRESH_BUTTONS: true,

    // Enable hover effects
    ENABLE_HOVER_EFFECTS: true,

    // Animation duration (milliseconds)
    ANIMATION_DURATION: 200,
  },

  // Error handling
  ERROR_HANDLING: {
    // Show error messages to user
    SHOW_ERRORS: true,

    // Retry failed requests
    ENABLE_RETRY: true,

    // Maximum retry attempts
    MAX_RETRIES: 3,

    // Retry delay (milliseconds)
    RETRY_DELAY: 1000,
  },
};

// Helper function to check if demo mode is enabled
export const isDemoMode = () => SEARCH_CONFIG.DEMO_MODE;

// Helper function to get API endpoint
export const getApiEndpoint = (endpointName) => {
  if (isDemoMode()) {
    return null; // Return null in demo mode to trigger fallback
  }
  return SEARCH_CONFIG.API_ENDPOINTS[endpointName];
};

// Helper function to check if feature is enabled
export const isFeatureEnabled = (featureName) => {
  return SEARCH_CONFIG.FEATURES[featureName] || false;
};

// Helper function to get search setting
export const getSearchSetting = (settingName) => {
  return SEARCH_CONFIG.SEARCH[settingName];
};

// Helper function to get filter setting
export const getFilterSetting = (settingName) => {
  return SEARCH_CONFIG.FILTERS[settingName];
};

// Helper function to get UI setting
export const getUISetting = (settingName) => {
  return SEARCH_CONFIG.UI[settingName];
};

// Helper function to get error handling setting
export const getErrorHandlingSetting = (settingName) => {
  return SEARCH_CONFIG.ERROR_HANDLING[settingName];
};

export default SEARCH_CONFIG;
