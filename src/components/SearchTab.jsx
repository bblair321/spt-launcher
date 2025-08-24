import React, { useState } from "react";
import { Search } from "lucide-react";

function SearchTab() {
  const [searchQuery, setSearchQuery] = useState("");
  const [searchResults, setSearchResults] = useState([]);

  const handleSearch = (e) => {
    e.preventDefault();
    // Implement search functionality
    console.log("Searching for:", searchQuery);
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
        <form onSubmit={handleSearch} className="mb-8">
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
              className="px-6 py-3 bg-blue-600 text-white rounded-lg hover:bg-blue-700 transition-colors flex items-center space-x-2"
            >
              <Search className="w-5 h-5" />
              <span>Search</span>
            </button>
          </div>
        </form>

        <div className="bg-white dark:bg-gray-800 p-6 rounded-lg border border-gray-200 dark:border-gray-700 shadow-sm">
          <h2 className="text-xl font-semibold mb-4 flex items-center space-x-2 text-gray-900 dark:text-gray-100">
            <Search className="w-5 h-5" />
            <span>Search Results</span>
          </h2>

          {searchResults.length === 0 ? (
            <div className="text-center py-12 text-gray-500 dark:text-gray-400">
              <Search className="w-16 h-16 mx-auto mb-4 opacity-50" />
              <p className="text-lg">No search results yet</p>
              <p>Enter a search term above to find SPT content</p>
            </div>
          ) : (
            <div className="space-y-4">
              {/* Search results would go here */}
            </div>
          )}
        </div>
      </div>
    </div>
  );
}

export default SearchTab;
