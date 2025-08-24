import React, { useState } from "react";
import { Wrench, Terminal, FileText, Database } from "lucide-react";

function DevToolsTab() {
  return (
    <div className="space-y-6">
      <div className="text-center">
        <h1 className="text-3xl font-bold text-gray-900 dark:text-gray-100 mb-2">
          Developer Tools
        </h1>
        <p className="text-gray-600 dark:text-gray-400">
          Advanced tools for developers and power users
        </p>
      </div>

      <div className="grid grid-cols-1 md:grid-cols-2 gap-6">
        <div className="bg-white dark:bg-gray-800 p-6 rounded-lg border border-gray-200 dark:border-gray-700 shadow-sm">
          <h2 className="text-xl font-semibold mb-4 flex items-center space-x-2 text-gray-900 dark:text-gray-100">
            <Terminal className="w-5 h-5" />
            <span>Process Monitor</span>
          </h2>
          <p className="text-gray-600 dark:text-gray-400 mb-4">
            Monitor running SPT processes and system resources
          </p>
          <button className="px-4 py-2 bg-blue-600 text-white rounded-md hover:bg-blue-700 transition-colors">
            Open Process Monitor
          </button>
        </div>

        <div className="bg-white dark:bg-gray-800 p-6 rounded-lg border border-gray-200 dark:border-gray-700 shadow-sm">
          <h2 className="text-xl font-semibold mb-4 flex items-center space-x-2 text-gray-900 dark:text-gray-100">
            <FileText className="w-5 h-5" />
            <span>Log Viewer</span>
          </h2>
          <p className="text-gray-600 dark:text-gray-400 mb-4">
            View and analyze SPT server and client logs
          </p>
          <button className="px-4 py-2 bg-blue-600 text-white rounded-md hover:bg-blue-700 transition-colors">
            Open Log Viewer
          </button>
        </div>

        <div className="bg-white dark:bg-gray-800 p-6 rounded-lg border border-gray-200 dark:border-gray-700 shadow-sm">
          <h2 className="text-xl font-semibold mb-4 flex items-center space-x-2 text-gray-900 dark:text-gray-100">
            <Database className="w-5 h-5" />
            <span>Database Tools</span>
          </h2>
          <p className="text-gray-600 dark:text-gray-400 mb-4">
            Manage SPT database and profile data
          </p>
          <button className="px-4 py-2 bg-blue-600 text-white rounded-md hover:bg-blue-700 transition-colors">
            Open Database Tools
          </button>
        </div>

        <div className="bg-white dark:bg-gray-800 p-6 rounded-lg border border-gray-200 dark:border-gray-700 shadow-sm">
          <h2 className="text-xl font-semibold mb-4 flex items-center space-x-2 text-gray-900 dark:text-gray-100">
            <Wrench className="w-5 h-5" />
            <span>Configuration Editor</span>
          </h2>
          <p className="text-gray-600 dark:text-gray-400 mb-4">
            Edit SPT configuration files directly
          </p>
          <button className="px-4 py-2 bg-blue-600 text-white rounded-md hover:bg-blue-700 transition-colors">
            Open Config Editor
          </button>
        </div>
      </div>
    </div>
  );
}

export default DevToolsTab;
