/**
 * Search Service for SPT Launcher
 * Handles API calls to various SPT content sources
 */

// Import configuration
import {
  getApiEndpoint,
  isDemoMode,
  getSearchSetting,
  getFilterSetting,
  getErrorHandlingSetting,
} from "../config/searchConfig";

// Import scraper for real data
import { scrapeMods, getTrendingMods } from "./sptHubScraper";

// Cache for API responses
const cache = new Map();
const CACHE_DURATION = getSearchSetting("CACHE_DURATION") || 5 * 60 * 1000; // 5 minutes

/**
 * Generic API fetch with error handling and caching
 */
async function fetchWithCache(url, options = {}) {
  const cacheKey = `${url}-${JSON.stringify(options)}`;
  const cached = cache.get(cacheKey);

  if (cached && Date.now() - cached.timestamp < CACHE_DURATION) {
    console.log(`Using cached data for: ${url}`);
    return cached.data;
  }

  try {
    console.log(`Fetching from: ${url}`);
    const response = await fetch(url, {
      ...options,
      headers: {
        "Content-Type": "application/json",
        ...options.headers,
      },
    });

    if (!response.ok) {
      console.warn(
        `HTTP ${response.status} for ${url}: ${response.statusText}`
      );
      throw new Error(`HTTP ${response.status}: ${response.statusText}`);
    }

    const data = await response.json();
    console.log(`Successfully fetched data from ${url}:`, data);

    // Cache the successful response
    cache.set(cacheKey, {
      data,
      timestamp: Date.now(),
    });

    return data;
  } catch (error) {
    console.warn(`Failed to fetch from ${url}:`, error);
    return null;
  }
}

/**
 * Search SPT addons from various sources
 */
export async function searchAddons(query, filters = {}) {
  try {
    // Try SPT-AKI Hub scraper first for real data
    console.log(
      `Attempting to scrape SPT-AKI Hub for real mod data: "${query}"...`
    );

    // Special handling for specific searches like "SPT Battlepass"
    if (query && query.toLowerCase().includes("battlepass")) {
      console.log(
        "Detected Battlepass search - will scrape more pages to find it"
      );
    }

    const scrapedResults = await scrapeMods(
      query || "",
      filters.category || "all",
      1,
      5 // Scrape up to 5 pages for more comprehensive results
    );

    if (scrapedResults.success && scrapedResults.data.length > 0) {
      console.log(
        `Successfully scraped ${scrapedResults.data.length} mods from SPT-AKI Hub`
      );

      // Check if we found the specific mod we're looking for
      if (query && query.toLowerCase().includes("battlepass")) {
        const foundBattlepass = scrapedResults.data.some((mod) =>
          mod.name.toLowerCase().includes("battlepass")
        );
        if (foundBattlepass) {
          console.log("Found Battlepass mod in search results!");
        } else {
          console.log(
            "Battlepass mod not found in initial search, trying broader search..."
          );
          // Try a broader search with just "Battlepass"
          const broaderResults = await scrapeMods("Battlepass", "all", 1, 5);
          if (broaderResults.success && broaderResults.data.length > 0) {
            console.log(
              `Found ${broaderResults.data.length} results with broader search`
            );
            // Combine results, prioritizing the original query
            const combinedResults = [
              ...scrapedResults.data,
              ...broaderResults.data,
            ];
            const uniqueResults = combinedResults.filter(
              (mod, index, self) =>
                index === self.findIndex((m) => m.name === mod.name)
            );
            return {
              success: true,
              data: uniqueResults,
              source: "spt-hub-scraped",
              totalResults: uniqueResults.length,
              hasMore: uniqueResults.length >= 20 * 5,
            };
          }
        }
      }

      // Ensure we're returning the actual data array, not a nested structure
      const modData = Array.isArray(scrapedResults.data)
        ? scrapedResults.data
        : [scrapedResults.data];

      console.log(
        `Returning ${modData.length} mods from searchAddons:`,
        modData.map((m) => ({ name: m.name, id: m.id, source: m.source }))
      );

      return {
        success: true,
        data: modData,
        source: "spt-hub-scraped",
        totalResults: modData.length,
        hasMore: modData.length >= 20 * 5,
      };
    }

    // Check if we were blocked by CSRF protection
    if (scrapedResults.source === "csrf-blocked") {
      console.log("CSRF protection blocked scraping, showing helpful message");
      return {
        success: false,
        error: scrapedResults.error,
        data: [],
        source: "csrf-blocked",
        message: scrapedResults.message,
        action: scrapedResults.action,
        fallbackData: getMockAddons(query, filters),
      };
    }

    // Fallback to mock data if scraping fails for other reasons
    console.log("Scraping failed, using mock data for addons");
    return {
      success: true,
      data: getMockAddons(query, filters),
      source: "mock-data",
    };
  } catch (error) {
    console.error("Search addons failed:", error);
    console.log("Falling back to mock data due to error");
    return {
      success: false,
      error: error.message,
      data: getMockAddons(query, filters),
      source: "mock-data",
    };
  }
}

/**
 * Search SPT servers
 */
export async function searchServers(query, filters = {}) {
  try {
    // Since we don't have a scraper for servers yet, use mock data
    // In the future, we could implement server scraping from community sites
    console.log(
      "Using mock data for server search (no scraper implemented yet)"
    );

    return {
      success: true,
      data: getMockServers(query, filters),
      source: "mock-data",
    };
  } catch (error) {
    console.error("Search servers failed:", error);
    return {
      success: false,
      error: error.message,
      data: getMockServers(query, filters),
      source: "mock-data",
    };
  }
}

/**
 * Search community resources
 */
export async function searchCommunity(query, filters = {}) {
  try {
    // Since we don't have a scraper for community resources yet, use mock data
    // In the future, we could implement community scraping from Discord, Reddit, etc.
    console.log(
      "Using mock data for community search (no scraper implemented yet)"
    );

    return {
      success: true,
      data: getMockCommunity(query, filters),
      source: "mock-data",
    };
  } catch (error) {
    console.error("Search community failed:", error);
    return {
      success: false,
      error: error.message,
      data: getMockCommunity(query, filters),
      source: "mock-data",
    };
  }
}

/**
 * Get addon details by ID
 */
export async function getAddonDetails(addonId, source = "auto") {
  try {
    // For scraped mods, we need to handle them differently since they don't have real IDs
    if (addonId.startsWith("scraped-")) {
      console.log(`Getting details for scraped mod: ${addonId}`);

      // Extract the index from the scraped ID (e.g., "scraped-0" -> 0)
      const index = parseInt(addonId.replace("scraped-", ""));

      // Try to get the actual scraped mod data from the cache or re-scrape
      try {
        const { scrapeMods } = await import("./sptHubScraper");

        // Re-scrape to get fresh data (or use cached data)
        const scrapedResults = await scrapeMods("", "all", 1);

        if (scrapedResults.success && scrapedResults.data[index]) {
          const actualMod = scrapedResults.data[index];
          console.log(`Found actual scraped mod data:`, actualMod);

          return {
            success: true,
            data: actualMod,
            source: "spt-hub-scraped",
          };
        }
      } catch (scrapeError) {
        console.warn("Failed to get actual scraped mod data:", scrapeError);
      }

      // Fallback to generic response if we can't get the actual data
      return {
        success: true,
        data: {
          id: addonId,
          name: "SPT-AKI Mod",
          type: "addon",
          description: "Mod details available from SPT-AKI Hub",
          source: "spt-hub-scraped",
          message:
            "This mod was found via web scraping. Visit the SPT-AKI Hub for full details.",
        },
        source: "spt-hub-scraped",
      };
    }

    // Try API endpoints for non-scraped mods
    let results;

    if (source === "auto" || source === "spt-api") {
      const sptEndpoint = getApiEndpoint("SPT_ADDONS");
      if (sptEndpoint) {
        results = await fetchWithCache(`${sptEndpoint}/${addonId}`);
        if (results && results.success) {
          return results;
        }
      }
    }

    if (source === "auto" || source === "spt-wiki") {
      const wikiEndpoint = getApiEndpoint("SPT_WIKI");
      if (wikiEndpoint) {
        results = await fetchWithCache(`${wikiEndpoint}/${addonId}`);
        if (results && results.success) {
          return results;
        }
      }
    }

    // Fallback to mock data
    const mockAddons = getMockAddons();
    const addon = mockAddons.find((a) => a.id === addonId);

    return {
      success: true,
      data: addon,
      source: "mock-data",
    };
  } catch (error) {
    console.error("Get addon details failed:", error);
    return {
      success: false,
      error: error.message,
    };
  }
}

/**
 * Download addon (placeholder for actual download logic)
 */
export async function downloadAddon(addonId, version = "latest") {
  try {
    // For scraped mods, we can't download directly since we don't have real download URLs
    if (addonId.startsWith("scraped-")) {
      console.log(
        `Cannot download scraped mod: ${addonId} - no direct download URL available`
      );

      return {
        success: false,
        error: "Direct download not available for scraped mods",
        message:
          "This mod was found via web scraping. Visit the SPT-AKI Hub website to download it manually.",
        source: "spt-hub-scraped",
        action: "Visit SPT-AKI Hub to download manually",
      };
    }

    // Get download URL from SPT-AKI Hub API for non-scraped mods
    const downloadsEndpoint = getApiEndpoint("SPT_DOWNLOADS");
    if (downloadsEndpoint) {
      const downloadInfo = await fetchWithCache(
        `${downloadsEndpoint}/${addonId}?version=${version}`
      );

      if (downloadInfo && downloadInfo.download_url) {
        console.log(
          `Downloading addon ${addonId} version ${version} from SPT-AKI Hub`
        );

        // In a real implementation, this would:
        // 1. Download the file from the provided URL
        // 2. Verify checksum
        // 3. Extract to appropriate directory
        // 4. Update local addon registry

        // Simulate download process
        await new Promise((resolve) => setTimeout(resolve, 2000));

        return {
          success: true,
          message: "Addon downloaded successfully from SPT-AKI Hub",
          localPath: `/addons/${addonId}-${version}`,
          source: "spt-hub-downloads",
          downloadUrl: downloadInfo.download_url,
          fileSize: downloadInfo.file_size,
        };
      }
    }

    // Fallback to mock download
    console.log(`Downloading addon ${addonId} version ${version} (mock)`);
    await new Promise((resolve) => setTimeout(resolve, 2000));

    return {
      success: true,
      message: "Addon downloaded successfully (mock)",
      localPath: `/addons/${addonId}-${version}`,
      source: "mock-data",
    };
  } catch (error) {
    console.error("Download addon failed:", error);
    return {
      success: false,
      error: error.message,
    };
  }
}

/**
 * Check server status
 */
export async function checkServerStatus(serverId) {
  try {
    // Since we don't have real server status checking yet, use mock data
    // In the future, we could implement real-time server status checking
    console.log(
      "Using mock data for server status (no real-time checking implemented yet)"
    );

    return {
      success: true,
      data: {
        online: Math.random() > 0.1, // 90% chance of being online
        players: Math.floor(Math.random() * 200),
        uptime: Math.floor(Math.random() * 100),
        lastCheck: new Date().toISOString(),
      },
      source: "mock-data",
    };
  } catch (error) {
    console.error("Check server status failed:", error);
    return {
      success: false,
      error: error.message,
    };
  }
}

/**
 * Get trending/popular content
 */
export async function getTrendingContent(category = "all", limit = 10) {
  try {
    // Try SPT-AKI Hub scraper first for real trending data
    console.log("Attempting to scrape SPT-AKI Hub for trending mods...");

    const scrapedResults = await getTrendingMods(limit, 5); // Scrape up to 5 pages for trending content

    if (scrapedResults.success && scrapedResults.data.length > 0) {
      console.log(
        `Successfully scraped ${scrapedResults.data.length} trending mods from SPT-AKI Hub`
      );
      return {
        success: true,
        data: scrapedResults.data,
        source: "spt-hub-scraped",
        totalResults: scrapedResults.totalResults,
        hasMore: scrapedResults.hasMore,
      };
    }

    // Check if we were blocked by CSRF protection
    if (scrapedResults.source === "csrf-blocked") {
      console.log("CSRF protection blocked trending scraping, using mock data");
      return {
        success: true,
        data: getMockTrending(category, limit),
        source: "mock-data",
        message: "Using demo data due to website security protection",
      };
    }

    // Fallback to mock trending data if scraping fails for other reasons
    console.log("Scraping failed, using mock trending data");
    return {
      success: true,
      data: getMockTrending(category, limit),
      source: "mock-data",
    };
  } catch (error) {
    console.error("Get trending content failed:", error);
    return {
      success: false,
      error: error.message,
      data: getMockTrending(category, limit),
      source: "mock-data",
    };
  }
}

// Mock data functions (fallbacks)
function getMockAddons(query = "", filters = {}) {
  const mockAddons = [
    {
      id: 1,
      name: "SPT Realism",
      type: "addon",
      description:
        "Enhanced realism mod for SPT-AKI with advanced ballistics, medical system, and AI improvements",
      rating: 4.8,
      downloads: 15420,
      author: "SPT Community",
      version: "2.1.0",
      category: "Gameplay",
      tags: ["realism", "ballistics", "medical", "ai"],
      lastUpdated: "2024-01-15",
      compatibility: "SPT 3.7+",
      downloadUrl: "https://github.com/spt-aki/realism/releases/latest",
      repository: "https://github.com/spt-aki/realism",
      issues: "https://github.com/spt-aki/realism/issues",
      documentation: "https://spt-aki.com/docs/realism",
      dependencies: [],
      size: "15.2 MB",
      checksum: "sha256:abc123...",
    },
    {
      id: 2,
      name: "Fika Co-op",
      type: "addon",
      description:
        "Multiplayer co-op support for SPT with seamless integration and enhanced features",
      rating: 4.9,
      downloads: 28940,
      author: "Fika Team",
      version: "1.5.2",
      category: "Multiplayer",
      tags: ["co-op", "multiplayer", "fika", "integration"],
      lastUpdated: "2024-01-20",
      compatibility: "SPT 3.6+",
      downloadUrl: "https://github.com/fika-gg/coop/releases/latest",
      repository: "https://github.com/fika-gg/coop",
      issues: "https://github.com/fika-gg/coop/issues",
      documentation: "https://fika.gg/docs/coop",
      dependencies: ["SPT Core"],
      size: "8.7 MB",
      checksum: "sha256:def456...",
    },
    {
      id: 3,
      name: "SPT Questing",
      type: "addon",
      description:
        "Advanced quest management system with dynamic objectives and reward tracking",
      rating: 4.7,
      downloads: 12340,
      author: "Quest Master",
      version: "3.0.1",
      category: "Questing",
      tags: ["quests", "objectives", "rewards", "tracking"],
      lastUpdated: "2024-01-10",
      compatibility: "SPT 3.5+",
      downloadUrl: "https://github.com/spt-aki/questing/releases/latest",
      repository: "https://github.com/spt-aki/questing",
      issues: "https://github.com/spt-aki/questing/issues",
      documentation: "https://spt-aki.com/docs/questing",
      dependencies: [],
      size: "12.1 MB",
      checksum: "sha256:ghi789...",
    },
    {
      id: 4,
      name: "SPT Graphics",
      type: "addon",
      description: "Enhanced graphics and visual effects for better immersion",
      rating: 4.6,
      downloads: 8900,
      author: "Graphics Team",
      version: "1.2.0",
      category: "Visual",
      tags: ["graphics", "visual", "effects", "immersion"],
      lastUpdated: "2024-01-05",
      compatibility: "SPT 3.7+",
      downloadUrl: "https://github.com/spt-aki/graphics/releases/latest",
      repository: "https://github.com/spt-aki/graphics",
      issues: "https://github.com/spt-aki/graphics/issues",
      documentation: "https://spt-aki.com/docs/graphics",
      dependencies: [],
      size: "45.8 MB",
      checksum: "sha256:jkl012...",
    },
  ];

  // Filter by query if provided
  if (query) {
    const searchTerm = query.toLowerCase();
    return mockAddons.filter(
      (addon) =>
        addon.name.toLowerCase().includes(searchTerm) ||
        addon.description.toLowerCase().includes(searchTerm) ||
        addon.tags.some((tag) => tag.toLowerCase().includes(searchTerm))
    );
  }

  // Apply filters
  let filtered = mockAddons;

  if (filters.rating > 0) {
    filtered = filtered.filter((addon) => addon.rating >= filters.rating);
  }

  if (filters.downloads > 0) {
    filtered = filtered.filter((addon) => addon.downloads >= filters.downloads);
  }

  return filtered;
}

function getMockServers(query = "", filters = {}) {
  const mockServers = [
    {
      id: 5,
      name: "SPT Official",
      type: "server",
      description:
        "Official SPT-AKI community server with active moderation and regular events",
      players: 156,
      maxPlayers: 200,
      location: "US East",
      uptime: 99.9,
      category: "Official",
      features: ["moderation", "events", "support", "community"],
      lastRestart: "2024-01-22",
      discord: "discord.gg/spt",
      website: "https://spt-aki.com",
      rules: "https://spt-aki.com/rules",
      whitelist: false,
      password: false,
      version: "SPT 3.7.1",
      mods: ["SPT Realism", "Fika Co-op"],
    },
    {
      id: 6,
      name: "Fika Co-op Hub",
      type: "server",
      description:
        "Dedicated Fika co-op server with optimized performance and dedicated staff",
      players: 89,
      maxPlayers: 150,
      location: "EU West",
      uptime: 98.5,
      category: "Co-op",
      features: ["fika", "co-op", "dedicated", "staff"],
      lastRestart: "2024-01-21",
      discord: "discord.gg/fika",
      website: "https://fika.gg",
      rules: "https://fika.gg/rules",
      whitelist: true,
      password: false,
      version: "SPT 3.6.2",
      mods: ["Fika Co-op", "SPT Graphics"],
    },
    {
      id: 7,
      name: "SPT Hardcore",
      type: "server",
      description:
        "Hardcore difficulty server with enhanced AI and realistic settings",
      players: 45,
      maxPlayers: 100,
      location: "US West",
      uptime: 97.2,
      category: "Hardcore",
      features: ["hardcore", "ai", "realistic", "challenge"],
      lastRestart: "2024-01-20",
      discord: "discord.gg/hardcore",
      website: "https://hardcore.spt-aki.com",
      rules: "https://hardcore.spt-aki.com/rules",
      whitelist: true,
      password: true,
      version: "SPT 3.7.0",
      mods: ["SPT Realism", "SPT Questing"],
    },
    {
      id: 8,
      name: "SPT Casual",
      type: "server",
      description:
        "Casual gaming server perfect for new players and relaxed gameplay",
      players: 78,
      maxPlayers: 120,
      location: "US Central",
      uptime: 99.1,
      category: "Casual",
      features: ["casual", "newbie-friendly", "relaxed", "helpful"],
      lastRestart: "2024-01-19",
      discord: "discord.gg/casual",
      website: "https://casual.spt-aki.com",
      rules: "https://casual.spt-aki.com/rules",
      whitelist: false,
      password: false,
      version: "SPT 3.6.1",
      mods: ["SPT Graphics"],
    },
  ];

  // Filter by query if provided
  if (query) {
    const searchTerm = query.toLowerCase();
    return mockServers.filter(
      (server) =>
        server.name.toLowerCase().includes(searchTerm) ||
        server.description.toLowerCase().includes(searchTerm) ||
        server.features.some((feature) =>
          feature.toLowerCase().includes(searchTerm)
        )
    );
  }

  // Apply filters
  let filtered = mockServers;

  if (filters.players > 0) {
    filtered = filtered.filter((server) => server.players >= filters.players);
  }

  if (filters.uptime > 0) {
    filtered = filtered.filter((server) => server.uptime >= filters.uptime);
  }

  return filtered;
}

function getMockCommunity(query = "", filters = {}) {
  const mockCommunity = [
    {
      id: 9,
      name: "SPT Wiki",
      type: "community",
      description:
        "Comprehensive SPT-AKI documentation with guides, tutorials, and troubleshooting",
      visits: 45600,
      lastUpdated: "2 days ago",
      category: "Documentation",
      features: ["documentation", "guides", "tutorials", "help"],
      url: "https://spt-wiki.com",
      contributors: 45,
      languages: ["en", "es", "de", "fr"],
      searchEnabled: true,
      api: "https://spt-wiki.com/api",
    },
    {
      id: 10,
      name: "SPT Discord",
      type: "community",
      description:
        "Official SPT community Discord server with active channels and support",
      members: 12500,
      online: 890,
      category: "Community",
      features: ["discord", "support", "channels", "community"],
      url: "https://discord.gg/spt",
      channels: 25,
      roles: ["Member", "Moderator", "Admin", "Developer"],
      verification: "required",
    },
    {
      id: 11,
      name: "SPT Reddit",
      type: "community",
      description:
        "SPT community discussions, support, and content sharing platform",
      subscribers: 8900,
      posts: 15600,
      category: "Forum",
      features: ["reddit", "discussions", "support", "content"],
      url: "https://reddit.com/r/spt",
      moderators: 12,
      rules: "https://reddit.com/r/spt/about/rules",
      wiki: "https://reddit.com/r/spt/wiki",
    },
    {
      id: 12,
      name: "SPT YouTube",
      type: "community",
      description:
        "Official SPT YouTube channel with tutorials, updates, and community highlights",
      subscribers: 15600,
      videos: 89,
      category: "Video",
      features: ["youtube", "tutorials", "updates", "highlights"],
      url: "https://youtube.com/spt",
      lastVideo: "1 week ago",
      playlists: ["Tutorials", "Updates", "Community", "Highlights"],
      liveStreams: false,
    },
  ];

  // Filter by query if provided
  if (query) {
    const searchTerm = query.toLowerCase();
    return mockCommunity.filter(
      (resource) =>
        resource.name.toLowerCase().includes(searchTerm) ||
        resource.description.toLowerCase().includes(searchTerm) ||
        resource.features.some((feature) =>
          feature.toLowerCase().includes(searchTerm)
        )
    );
  }

  return mockCommunity;
}

function getMockTrending(category = "all", limit = 10) {
  const allContent = [
    ...getMockAddons(),
    ...getMockServers(),
    ...getMockCommunity(),
  ];

  // Sort by popularity (downloads, players, visits, etc.)
  const sorted = allContent.sort((a, b) => {
    const aScore = a.downloads || a.players || a.visits || 0;
    const bScore = b.downloads || b.players || b.visits || 0;
    return bScore - aScore;
  });

  // Filter by category if specified
  if (category !== "all") {
    const filtered = sorted.filter(
      (item) => item.category.toLowerCase() === category.toLowerCase()
    );
    return filtered.slice(0, limit);
  }

  return sorted.slice(0, limit);
}

// Export all functions
export {
  fetchWithCache,
  getMockAddons,
  getMockServers,
  getMockCommunity,
  getMockTrending,
};
