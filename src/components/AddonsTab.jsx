import React, { useState, useEffect } from "react";
import {
  Puzzle,
  Trash2,
  Edit,
  FolderOpen,
  Save,
  ToggleLeft,
  ToggleRight,
  Package,
  AlertCircle,
} from "lucide-react";

function AddonsTab() {
  const [addons, setAddons] = useState([]);
  const [selectedAddon, setSelectedAddon] = useState(null);
  const [isEditing, setIsEditing] = useState(false);
  const [formData, setFormData] = useState({
    name: "",
    path: "",
    description: "",
    enabled: true,
    version: "",
    author: "",
  });

  useEffect(() => {
    const savedAddons = localStorage.getItem("sptAddons");
    if (savedAddons) {
      setAddons(JSON.parse(savedAddons));
    }
  }, []);

  useEffect(() => {
    localStorage.setItem("sptAddons", JSON.stringify(addons));
  }, [addons]);

  const selectAddonPath = async () => {
    if (window.electronAPI) {
      try {
        const path = await window.electronAPI.selectFolder();
        if (path) {
          setFormData((prev) => ({ ...prev, path }));
        }
      } catch (error) {
        console.error("Failed to select addon path:", error);
      }
    }
  };

  const handleSubmit = (e) => {
    e.preventDefault();

    if (isEditing && selectedAddon) {
      setAddons((prev) =>
        prev.map((addon) =>
          addon.id === selectedAddon.id ? { ...formData, id: addon.id } : addon
        )
      );
      setIsEditing(false);
      setSelectedAddon(null);
    } else {
      const newAddon = {
        ...formData,
        id: Date.now().toString(),
        createdAt: new Date().toISOString(),
      };
      setAddons((prev) => [...prev, newAddon]);
    }

    setFormData({
      name: "",
      path: "",
      description: "",
      enabled: true,
      version: "",
      author: "",
    });
  };

  const editAddon = (addon) => {
    setSelectedAddon(addon);
    setFormData({
      name: addon.name,
      path: addon.path,
      description: addon.description,
      enabled: addon.enabled,
      version: addon.version,
      author: addon.author,
    });
    setIsEditing(true);
  };

  const deleteAddon = (addonId) => {
    setAddons((prev) => prev.filter((addon) => addon.id !== addonId));
    if (selectedAddon?.id === addonId) {
      setSelectedAddon(null);
      setIsEditing(false);
    }
  };

  const toggleAddon = (addonId) => {
    setAddons((prev) =>
      prev.map((addon) =>
        addon.id === addonId ? { ...addon, enabled: !addon.enabled } : addon
      )
    );
  };

  return (
    <div className="space-y-6">
      <div className="text-center">
        <h1 className="text-3xl font-bold text-gray-900 dark:text-gray-100 mb-2">
          Addon Management
        </h1>
        <p className="text-gray-600 dark:text-gray-400">
          Manage your SPT-AKI addons and mods
        </p>
      </div>

      <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
        <div className="bg-white dark:bg-gray-800 p-6 rounded-lg border border-gray-200 dark:border-gray-700 shadow-sm">
          <h2 className="text-xl font-semibold mb-4 flex items-center space-x-2 text-gray-900 dark:text-gray-100">
            <Puzzle className="w-5 h-5" />
            <span>{isEditing ? "Edit Addon" : "Add New Addon"}</span>
          </h2>

          <form onSubmit={handleSubmit} className="space-y-4">
            <div>
              <label className="block text-sm font-medium mb-2 text-gray-900 dark:text-gray-100">
                Addon Name
              </label>
              <input
                type="text"
                value={formData.name}
                onChange={(e) =>
                  setFormData((prev) => ({ ...prev, name: e.target.value }))
                }
                placeholder="My Addon"
                className="w-full px-3 py-2 border border-gray-300 dark:border-gray-600 rounded-md bg-white dark:bg-gray-700 text-gray-900 dark:text-gray-100"
                required
              />
            </div>

            <div>
              <label className="block text-sm font-medium mb-2 text-gray-900 dark:text-gray-100">
                Addon Path
              </label>
              <div className="flex space-x-2">
                <input
                  type="text"
                  value={formData.path}
                  onChange={(e) =>
                    setFormData((prev) => ({ ...prev, path: e.target.value }))
                  }
                  placeholder="Select addon directory..."
                  className="flex-1 px-3 py-2 border border-gray-300 dark:border-gray-600 rounded-md bg-white dark:bg-gray-700 text-gray-900 dark:text-gray-100"
                  required
                />
                <button
                  type="button"
                  onClick={selectAddonPath}
                  className="px-4 py-2 bg-gray-200 dark:bg-gray-600 hover:bg-gray-300 dark:hover:bg-gray-500 text-gray-700 dark:text-gray-200 rounded-md transition-colors"
                >
                  <FolderOpen className="w-4 h-4" />
                </button>
              </div>
            </div>

            <div className="grid grid-cols-2 gap-4">
              <div>
                <label className="block text-sm font-medium mb-2 text-gray-900 dark:text-gray-100">
                  Version
                </label>
                <input
                  type="text"
                  value={formData.version}
                  onChange={(e) =>
                    setFormData((prev) => ({
                      ...prev,
                      version: e.target.value,
                    }))
                  }
                  placeholder="1.0.0"
                  className="w-full px-3 py-2 border border-gray-300 dark:border-gray-600 rounded-md bg-white dark:bg-gray-700 text-gray-900 dark:text-gray-100"
                />
              </div>

              <div>
                <label className="block text-sm font-medium mb-2 text-gray-900 dark:text-gray-100">
                  Author
                </label>
                <input
                  type="text"
                  value={formData.author}
                  onChange={(e) =>
                    setFormData((prev) => ({ ...prev, author: e.target.value }))
                  }
                  placeholder="Author Name"
                  className="w-full px-3 py-2 border border-gray-300 dark:border-gray-600 rounded-md bg-white dark:bg-gray-700 text-gray-900 dark:text-gray-100"
                />
              </div>
            </div>

            <div>
              <label className="block text-sm font-medium mb-2 text-gray-900 dark:text-gray-100">
                Description
              </label>
              <textarea
                value={formData.description}
                onChange={(e) =>
                  setFormData((prev) => ({
                    ...prev,
                    description: e.target.value,
                  }))
                }
                placeholder="Addon description..."
                rows={3}
                className="w-full px-3 py-2 border border-gray-300 dark:border-gray-600 rounded-md bg-white dark:bg-gray-700 text-gray-900 dark:text-gray-100"
              />
            </div>

            <div className="flex items-center space-x-2">
              <input
                type="checkbox"
                id="enabled"
                checked={formData.enabled}
                onChange={(e) =>
                  setFormData((prev) => ({
                    ...prev,
                    enabled: e.target.checked,
                  }))
                }
                className="rounded border-gray-300"
              />
              <label
                htmlFor="enabled"
                className="text-sm font-medium text-gray-900 dark:text-gray-100"
              >
                Enabled by default
              </label>
            </div>

            <div className="flex space-x-2">
              <button
                type="submit"
                className="flex-1 px-4 py-2 bg-blue-600 text-white rounded-md hover:bg-blue-700 transition-colors flex items-center justify-center space-x-2"
              >
                <Save className="w-4 h-4" />
                <span>{isEditing ? "Update Addon" : "Add Addon"}</span>
              </button>

              {isEditing && (
                <button
                  type="button"
                  onClick={() => {
                    setIsEditing(false);
                    setSelectedAddon(null);
                    setFormData({
                      name: "",
                      path: "",
                      description: "",
                      enabled: true,
                      version: "",
                      author: "",
                    });
                  }}
                  className="px-4 py-2 bg-gray-200 dark:bg-gray-600 text-gray-800 dark:text-gray-200 rounded-md hover:bg-gray-300 dark:hover:bg-gray-500 transition-colors"
                >
                  Cancel
                </button>
              )}
            </div>
          </form>
        </div>

        <div className="bg-white dark:bg-gray-800 p-6 rounded-lg border border-gray-200 dark:border-gray-700 shadow-sm">
          <h2 className="text-xl font-semibold mb-4 flex items-center space-x-2 text-gray-900 dark:text-gray-100">
            <Package className="w-5 h-5" />
            <span>Installed Addons</span>
          </h2>

          {addons.length === 0 ? (
            <div className="text-center py-8 text-gray-500 dark:text-gray-400">
              <Puzzle className="w-12 h-12 mx-auto mb-4 opacity-50" />
              <p>No addons installed yet</p>
              <p className="text-sm">Add your first addon using the form</p>
            </div>
          ) : (
            <div className="space-y-3">
              {addons.map((addon) => (
                <div
                  key={addon.id}
                  className={`p-4 border border-gray-200 dark:border-gray-700 rounded-lg transition-colors ${
                    addon.enabled
                      ? "bg-blue-50 dark:bg-blue-900/20"
                      : "bg-gray-50 dark:bg-gray-700/50"
                  }`}
                >
                  <div className="flex items-center justify-between mb-2">
                    <div className="flex items-center space-x-2">
                      <h3 className="font-semibold">{addon.name}</h3>
                      <button
                        onClick={() => toggleAddon(addon.id)}
                        className="p-1 hover:bg-gray-200 dark:hover:bg-gray-600 rounded transition-colors"
                        title={addon.enabled ? "Disable Addon" : "Enable Addon"}
                      >
                        {addon.enabled ? (
                          <ToggleRight className="w-4 h-4 text-green-500" />
                        ) : (
                          <ToggleLeft className="w-4 h-4 text-gray-500" />
                        )}
                      </button>
                    </div>

                    <div className="flex items-center space-x-1">
                      <button
                        onClick={() => editAddon(addon)}
                        className="p-1 hover:bg-gray-200 dark:hover:bg-gray-600 rounded transition-colors"
                        title="Edit Addon"
                      >
                        <Edit className="w-4 h-4 text-blue-500" />
                      </button>
                      <button
                        onClick={() => deleteAddon(addon.id)}
                        className="p-1 hover:bg-gray-200 dark:hover:bg-gray-600 rounded transition-colors"
                        title="Delete Addon"
                      >
                        <Trash2 className="w-4 h-4 text-red-500" />
                      </button>
                    </div>
                  </div>

                  <div className="text-sm text-gray-600 dark:text-gray-400 space-y-1">
                    <p>
                      <strong>Path:</strong> {addon.path}
                    </p>
                    {addon.version && (
                      <p>
                        <strong>Version:</strong> {addon.version}
                      </p>
                    )}
                    {addon.author && (
                      <p>
                        <strong>Author:</strong> {addon.author}
                      </p>
                    )}
                    {addon.description && (
                      <p>
                        <strong>Description:</strong> {addon.description}
                      </p>
                    )}
                    <p>
                      <strong>Status:</strong>{" "}
                      {addon.enabled ? "Enabled" : "Disabled"}
                    </p>
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

export default AddonsTab;
