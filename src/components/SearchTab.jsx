import React, { useState, useEffect } from "react";
import {
  Search,
  Download,
  Server,
  Puzzle,
  ExternalLink,
  Star,
  Clock,
} from "lucide-react";

function SearchTab() {
  const [searchQuery, setSearchQuery] = useState("");
  const [searchResults, setSearchResults] = useState([]);
  const [isSearching, setIsSearching] = useState(false);
  const [searchCategory, setSearchCategory] = useState("all");
  const [recentSearches, setRecentSearches] = useState([]);

  // Mock data for demonstration - in real app this would come from APIs
  const mockData = {
    addons: [
      {
        id: 1,
        name: "SPT Realism",
        type: "addon",
        description: "Enhanced realism mod for SPT-AKI",
        rating: 4.8,
        downloads: 15420,
        author: "SPT Community",
        version: "2.1.0",
        category: "Gameplay",
      },
      {
        id: 2,
        name: "Fika Co-op",
        type: "addon",
        description: "Multiplayer co-op support for SPT",
        rating: 4.9,
        downloads: 28940,
        author: "Fika Team",
        version: "1.5.2",
        category: "Multiplayer",
      },
      {
        id: 3,
        name: "SPT Questing",
        type: "addon",
        description: "Advanced quest management system",
        rating: 4.7,
        downloads: 12340,
        author: "Quest Master",
        version: "3.0.1",
        category: "Questing",
      },
    ],
    servers: [
      {
        id: 4,
        name: "SPT Official",
        type: "server",
        description: "Official SPT-AKI community server",
        players: 156,
        maxPlayers: 200,
        location: "US East",
        uptime: "99.9%",
        category: "Official",
      },
      {
        id: 5,
        name: "Fika Co-op Hub",
        type: "server",
        description: "Dedicated Fika co-op server",
        players: 89,
        maxPlayers: 150,
        location: "EU West",
        uptime: "98.5%",
        category: "Co-op",
      },
      {
        id: 6,
        name: "SPT Hardcore",
        type: "server",
        description: "Hardcore difficulty server",
        players: 45,
        maxPlayers: 100,
        location: "US West",
        uptime: "97.2%",
        category: "Hardcore",
      },
    ],
    community: [
      {
        id: 7,
        name: "SPT Wiki",
        type: "community",
        description: "Comprehensive SPT-AKI documentation",
        visits: 45600,
        lastUpdated: "2 days ago",
        category: "Documentation",
      },
      {
        id: 8,
        name: "SPT Discord",
        type: "community",
        description: "Official SPT community Discord server",
        members: 12500,
        online: 890,
        category: "Community",
      },
      {
        id: 9,
        name: "SPT Reddit",
        type: "community",
        description: "SPT community discussions and support",
        subscribers: 8900,
        posts: 15600,
        category: "Forum",
      },
    ],
  };

  const handleSearch = (e) => {
    e.preventDefault();
    if (!searchQuery.trim()) return;

    setIsSearching(true);

    // Simulate search delay
    setTimeout(() => {
      const results = performSearch(searchQuery, searchCategory);
      setSearchResults(results);

      // Add to recent searches
      if (!recentSearches.includes(searchQuery)) {
        setRecentSearches((prev) => [searchQuery, ...prev.slice(0, 4)]);
      }

      setIsSearching(false);
    }, 500);
  };

  const performSearch = (query, category) => {
    const results = [];
    const searchTerm = query.toLowerCase();

    if (category === "all" || category === "addons") {
      results.push(
        ...mockData.addons.filter(
          (item) =>
            item.name.toLowerCase().includes(searchTerm) ||
            item.description.toLowerCase().includes(searchTerm) ||
            item.category.toLowerCase().includes(searchTerm)
        )
      );
    }

    if (category === "all" || category === "servers") {
      results.push(
        ...mockData.servers.filter(
          (item) =>
            item.name.toLowerCase().includes(searchTerm) ||
            item.description.toLowerCase().includes(searchTerm) ||
            item.category.toLowerCase().includes(searchTerm)
        )
      );
    }

    if (category === "all" || category === "community") {
      results.push(
        ...mockData.community.filter(
          (item) =>
            item.name.toLowerCase().includes(searchTerm) ||
            item.description.toLowerCase().includes(searchTerm) ||
            item.category.toLowerCase().includes(searchTerm)
        )
      );
    }

    return results;
  };

  return (
    <div className="space-y-6">
      <div className="text-center">
        <h1 className="text-3xl font-bold text-gray-900 dark:text-gray-100 mb-2">
          Search & Discovery
        </h1>
        <p className="text-gray-600 dark:text-gray-400">
          Find SPT addons, servers, and community content
        </p>
      </div>

      <div className="max-w-4xl mx-auto">
        {/* Search Form */}
        <form onSubmit={handleSearch} className="mb-8">
          <div className="space-y-4">
            {/* Search Input and Button */}
            <div className="flex space-x-2">
              <input
                type="text"
                value={searchQuery}
                onChange={(e) => setSearchQuery(e.target.value)}
                placeholder="Search for addons, servers, or community content..."
                className="flex-1 px-4 py-3 border border-gray-300 dark:border-gray-600 rounded-lg bg-white dark:bg-gray-700 text-gray-900 dark:text-gray-100 text-lg"
              />
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
                <span>{isSearching ? "Searching..." : "Search"}</span>
              </button>
            </div>

            {/* Category Filter */}
            <div className="flex space-x-2">
              {["all", "addons", "servers", "community"].map((category) => (
                <button
                  key={category}
                  type="button"
                  onClick={() => setSearchCategory(category)}
                  className={`px-4 py-2 rounded-lg border transition-colors ${
                    searchCategory === category
                      ? "bg-blue-600 text-white border-blue-600"
                      : "bg-white dark:bg-gray-700 text-gray-700 dark:text-gray-300 border-gray-300 dark:border-gray-600 hover:bg-gray-50 dark:hover:bg-gray-600"
                  }`}
                >
                  {category.charAt(0).toUpperCase() + category.slice(1)}
                </button>
              ))}
            </div>
          </div>
        </form>

        {/* Recent Searches */}
        {recentSearches.length > 0 && (
          <div className="mb-6">
            <h3 className="text-sm font-medium text-gray-700 dark:text-gray-300 mb-2">
              Recent Searches:
            </h3>
            <div className="flex flex-wrap gap-2">
              {recentSearches.map((search, index) => (
                <button
                  key={index}
                  onClick={() => {
                    setSearchQuery(search);
                    handleSearch({ preventDefault: () => {} });
                  }}
                  className="px-3 py-1 bg-gray-100 dark:bg-gray-700 text-gray-700 dark:text-gray-300 rounded-full text-sm hover:bg-gray-200 dark:hover:bg-gray-600 transition-colors"
                >
                  {search}
                </button>
              ))}
            </div>
          </div>
        )}

        <div className="bg-white dark:bg-gray-800 p-6 rounded-lg border border-gray-200 dark:border-gray-700 shadow-sm">
          <h2 className="text-xl font-semibold mb-4 flex items-center space-x-2 text-gray-900 dark:text-gray-100">
            <Search className="w-5 h-5" />
            <span>Search Results</span>
          </h2>

          {searchResults.length === 0 ? (
            <div className="text-center py-12 text-gray-500 dark:text-gray-400">
              <Search className="w-16 h-16 mx-auto mb-4 opacity-50" />
              <p className="text-lg">
                {searchQuery ? "No results found" : "No search results yet"}
              </p>
              <p>
                {searchQuery
                  ? `No results found for "${searchQuery}" in ${searchCategory} category`
                  : "Enter a search term above to find SPT content"}
              </p>
            </div>
          ) : (
            <div className="space-y-4">
              {searchResults.map((result) => (
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
                      </div>

                      <p className="text-gray-600 dark:text-gray-400 mb-3">
                        {result.description}
                      </p>

                      <div className="flex items-center space-x-4 text-sm text-gray-500 dark:text-gray-400">
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
                          </>
                        )}

                        {result.type === "server" && (
                          <>
                            <span>
                              {result.players}/{result.maxPlayers} players
                            </span>
                            <span>{result.location}</span>
                            <span>Uptime: {result.uptime}</span>
                          </>
                        )}

                        {result.type === "community" && (
                          <>
                            {result.visits && (
                              <div className="flex items-center space-x-1">
                                <Clock className="w-4 h-4" />
                                <span>
                                  {result.visits.toLocaleString()} visits
                                </span>
                              </div>
                            )}
                            {result.members && (
                              <span>
                                {result.members.toLocaleString()} members
                              </span>
                            )}
                            {result.lastUpdated && (
                              <span>Updated {result.lastUpdated}</span>
                            )}
                          </>
                        )}
                      </div>
                    </div>

                    <button className="ml-4 px-4 py-2 bg-blue-600 text-white text-sm rounded-md hover:bg-blue-700 transition-colors flex items-center space-x-2">
                      <ExternalLink className="w-4 h-4" />
                      <span>View</span>
                    </button>
                  </div>
                </div>
              ))}
            </div>
          )}
        </div>
      </div>
    </div>
  );
}

export default SearchTab;
