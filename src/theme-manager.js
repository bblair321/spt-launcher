// Theme Manager for SPT-AKI Launcher
class ThemeManager {
  constructor() {
    this.currentTheme = "default";
    this.customColors = {};
    this.saveTimeout = null;
    this.currentNotificationElement = null;
    this.notificationTimeout = null;
    this.themes = {
      default: {
        name: "Default Green",
        category: "Professional",
        description: "Classic green theme with professional appearance",
        colors: {
          primary: "#556b2f",
          secondary: "#6b8e23",
          accent: "#9acd32",
          background:
            "linear-gradient(135deg, #4a5d23 0%, #6b7a3a 20%, #8b9a4a 40%, #a8b75a 60%, #c4d4a0 80%, #e8f5e8 100%)",
          cardBackground: "rgba(74, 93, 35, 0.9)",
          textPrimary: "#2d5016",
          textSecondary: "#e8f5e8",
          textMuted: "#c3e6cb",
          border: "#556b2f",
          success: "#228b22",
          danger: "#8b4513",
          warning: "#ffd43b",
          info: "#4facfe",
        },
      },
      dark: {
        name: "Dark Theme",
        category: "Professional",
        description: "Modern dark theme for reduced eye strain",
        colors: {
          primary: "#2c3e50",
          secondary: "#34495e",
          accent: "#3498db",
          background:
            "linear-gradient(135deg, #1a1a1a 0%, #2c3e50 20%, #34495e 40%, #5d6d7e 60%, #85929e 80%, #bdc3c7 100%)",
          cardBackground: "rgba(44, 62, 80, 0.9)",
          textPrimary: "#ecf0f1",
          textSecondary: "#bdc3c7",
          textMuted: "#95a5a6",
          border: "#34495e",
          success: "#27ae60",
          danger: "#e74c3c",
          warning: "#f39c12",
          info: "#3498db",
        },
      },
      blue: {
        name: "Blue Ocean",
        category: "Gaming",
        description: "Cool blue theme inspired by ocean depths",
        colors: {
          primary: "#1e3a8a",
          secondary: "#3b82f6",
          accent: "#60a5fa",
          background:
            "linear-gradient(135deg, #1e3a8a 0%, #3b82f6 20%, #60a5fa 40%, #93c5fd 60%, #bfdbfe 80%, #dbeafe 100%)",
          cardBackground: "rgba(30, 58, 138, 0.9)",
          textPrimary: "#1e293b",
          textSecondary: "#f8fafc",
          textMuted: "#cbd5e1",
          border: "#3b82f6",
          success: "#059669",
          danger: "#dc2626",
          warning: "#d97706",
          info: "#0891b2",
        },
      },
      purple: {
        name: "Purple Dream",
        category: "Creative",
        description: "Vibrant purple theme for creative minds",
        colors: {
          primary: "#581c87",
          secondary: "#7c3aed",
          accent: "#a855f7",
          background:
            "linear-gradient(135deg, #581c87 0%, #7c3aed 20%, #a855f7 40%, #c084fc 60%, #d8b4fe 80%, #f3e8ff 100%)",
          cardBackground: "rgba(88, 28, 135, 0.9)",
          textPrimary: "#1e1b4b",
          textSecondary: "#faf5ff",
          textMuted: "#e9d5ff",
          border: "#7c3aed",
          success: "#059669",
          danger: "#dc2626",
          warning: "#d97706",
          info: "#0891b2",
        },
      },
      red: {
        name: "Red Fire",
        category: "Gaming",
        description: "Intense red theme for high-energy gaming",
        colors: {
          primary: "#7f1d1d",
          secondary: "#dc2626",
          accent: "#ef4444",
          background:
            "linear-gradient(135deg, #7f1d1d 0%, #dc2626 20%, #ef4444 40%, #f87171 60%, #fca5a5 80%, #fecaca 100%)",
          cardBackground: "rgba(127, 29, 29, 0.9)",
          textPrimary: "#1f2937",
          textSecondary: "#fef2f2",
          textMuted: "#fecaca",
          border: "#dc2626",
          success: "#059669",
          danger: "#dc2626",
          warning: "#d97706",
          info: "#0891b2",
        },
      },
      orange: {
        name: "Orange Sunset",
        category: "Creative",
        description: "Warm orange theme inspired by sunset",
        colors: {
          primary: "#9a3412",
          secondary: "#ea580c",
          accent: "#f97316",
          background:
            "linear-gradient(135deg, #9a3412 0%, #ea580c 20%, #f97316 40%, #fb923c 60%, #fdba74 80%, #fed7aa 100%)",
          cardBackground: "rgba(154, 52, 18, 0.9)",
          textPrimary: "#1f2937",
          textSecondary: "#fff7ed",
          textMuted: "#fed7aa",
          border: "#ea580c",
          success: "#059669",
          danger: "#dc2626",
          warning: "#d97706",
          info: "#0891b2",
        },
      },
    };

    // Color presets for quick selection
    this.colorPresets = {
      primary: [
        "#556b2f",
        "#1e3a8a",
        "#581c87",
        "#7f1d1d",
        "#9a3412",
        "#059669",
        "#dc2626",
        "#d97706",
      ],
      secondary: [
        "#6b8e23",
        "#3b82f6",
        "#7c3aed",
        "#dc2626",
        "#ea580c",
        "#10b981",
        "#ef4444",
        "#f59e0b",
      ],
      accent: [
        "#9acd32",
        "#60a5fa",
        "#a855f7",
        "#ef4444",
        "#f97316",
        "#34d399",
        "#f87171",
        "#fbbf24",
      ],
      success: [
        "#228b22",
        "#059669",
        "#059669",
        "#059669",
        "#059669",
        "#059669",
        "#059669",
        "#059669",
      ],
      danger: [
        "#8b4513",
        "#dc2626",
        "#dc2626",
        "#dc2626",
        "#dc2626",
        "#dc2626",
        "#dc2626",
        "#dc2626",
      ],
      warning: [
        "#ffd43b",
        "#d97706",
        "#d97706",
        "#d97706",
        "#d97706",
        "#d97706",
        "#d97706",
        "#d97706",
      ],
      background: [
        "#4a5d23",
        "#1a1a1a",
        "#1e3a8a",
        "#581c87",
        "#7f1d1d",
        "#9a3412",
        "#059669",
        "#2c3e50",
      ],
    };

    this.init();
  }

  init() {
    this.loadTheme();
    this.applyTheme();
    this.setupThemeUI();
  }

  loadTheme() {
    try {
      const savedTheme = localStorage.getItem("spt-theme");
      if (savedTheme) {
        this.currentTheme = savedTheme;
      }

      const savedCustomColors = localStorage.getItem("spt-custom-colors");
      if (savedCustomColors) {
        this.customColors = JSON.parse(savedCustomColors);
      }

      // Load imported themes
      const savedImportedThemes = localStorage.getItem("spt-imported-themes");
      if (savedImportedThemes) {
        const importedThemes = JSON.parse(savedImportedThemes);
        Object.assign(this.themes, importedThemes);
      }
    } catch (error) {
      console.error("Failed to load theme settings:", error);
    }
  }

  saveTheme() {
    // Debounce save operations
    if (this.saveTimeout) {
      clearTimeout(this.saveTimeout);
    }

    this.saveTimeout = setTimeout(() => {
      try {
        localStorage.setItem("spt-theme", this.currentTheme);
        localStorage.setItem(
          "spt-custom-colors",
          JSON.stringify(this.customColors)
        );

        // Save imported themes
        const importedThemes = {};
        Object.entries(this.themes).forEach(([key, theme]) => {
          if (key.startsWith("imported_")) {
            importedThemes[key] = theme;
          }
        });
        localStorage.setItem(
          "spt-imported-themes",
          JSON.stringify(importedThemes)
        );
      } catch (error) {
        console.error("Failed to save theme settings:", error);
      }
    }, 300);
  }

  applyTheme() {
    const theme = this.themes[this.currentTheme];
    if (!theme) return;

    const colors = { ...theme.colors, ...this.customColors };

    // Handle background color - if custom background is set, generate a gradient
    if (this.customColors.background) {
      const bgColor = this.customColors.background;
      // Generate a gradient based on the custom background color
      const lighterColor = this.lightenColor(bgColor, 0.3);
      const darkerColor = this.darkenColor(bgColor, 0.2);
      colors.background = `linear-gradient(135deg, ${darkerColor} 0%, ${bgColor} 20%, ${lighterColor} 40%, ${this.lightenColor(
        bgColor,
        0.5
      )} 60%, ${this.lightenColor(bgColor, 0.7)} 80%, ${this.lightenColor(
        bgColor,
        0.9
      )} 100%)`;
    }

    // Apply CSS custom properties
    const root = document.documentElement;
    Object.entries(colors).forEach(([key, value]) => {
      root.style.setProperty(`--color-${key}`, value);
    });

    // Set button-specific variables
    root.style.setProperty(
      "--btn-background",
      `linear-gradient(45deg, ${colors.primary}, ${colors.secondary})`
    );
    root.style.setProperty("--btn-color", colors.textSecondary);

    // Apply specific theme classes
    document.body.className = `theme-${this.currentTheme}`;

    this.saveTheme();
  }

  setTheme(themeName) {
    if (this.themes[themeName]) {
      this.currentTheme = themeName;
      // Reset custom colors when selecting a pre-built theme
      this.customColors = {};
      this.applyTheme();
      this.updateThemeUI();
      this.showNotification(
        `Applied ${this.themes[themeName].name} theme`,
        "success"
      );
    }
  }

  setCustomColor(colorKey, colorValue) {
    this.customColors[colorKey] = colorValue;
    this.applyTheme();
    this.showNotification(`Updated ${colorKey} color`, "info");
  }

  resetCustomColors() {
    if (this.customColors && Object.keys(this.customColors).length > 0) {
      if (
        confirm(
          "Are you sure you want to reset all custom colors? This action cannot be undone."
        )
      ) {
        this.customColors = {};
        this.applyTheme();
        this.updateColorPickers();
        this.showNotification("Custom colors reset", "success");
      }
    }
  }

  getAvailableThemes() {
    return Object.keys(this.themes).map((key) => ({
      key,
      name: this.themes[key].name,
      category: this.themes[key].category,
      description: this.themes[key].description,
    }));
  }

  getThemesByCategory() {
    const themes = this.getAvailableThemes();
    const categories = {};

    themes.forEach((theme) => {
      if (!categories[theme.category]) {
        categories[theme.category] = [];
      }
      categories[theme.category].push(theme);
    });

    return categories;
  }

  getCurrentTheme() {
    return {
      name: this.currentTheme,
      displayName: this.themes[this.currentTheme]?.name || "Custom",
      colors: {
        ...this.themes[this.currentTheme]?.colors,
        ...this.customColors,
      },
    };
  }

  exportTheme() {
    const themeData = {
      name: this.themes[this.currentTheme]?.name || "Custom Theme",
      customColors: this.customColors,
      timestamp: new Date().toISOString(),
    };

    // Prompt user for filename
    const defaultName = `${
      this.themes[this.currentTheme]?.name || "Custom"
    }-Theme`;
    const fileName = prompt("Enter a name for your theme file:", defaultName);

    if (fileName === null) {
      // User cancelled
      return;
    }

    if (fileName.trim() === "") {
      this.showNotification("Please enter a valid filename", "error");
      return;
    }

    // Clean the filename (remove invalid characters)
    const cleanFileName = fileName.replace(/[<>:"/\\|?*]/g, "_").trim();

    const blob = new Blob([JSON.stringify(themeData, null, 2)], {
      type: "application/json",
    });
    const url = URL.createObjectURL(blob);
    const a = document.createElement("a");
    a.href = url;
    a.download = `${cleanFileName}.json`;
    a.click();
    URL.revokeObjectURL(url);

    this.showNotification(
      `Theme exported as "${cleanFileName}.json"`,
      "success"
    );
  }

  importTheme(file) {
    const reader = new FileReader();
    reader.onload = (e) => {
      try {
        const themeData = JSON.parse(e.target.result);
        if (themeData.customColors) {
          // Add the imported theme to the themes list
          const themeKey = `imported_${Date.now()}`;

          // Use the filename as the theme name if available, otherwise use the theme data name
          const fileName = file.name.replace(".json", "");
          const themeName = fileName || themeData.name || "Imported Theme";

          // Create a new theme entry with the imported colors as the base
          this.themes[themeKey] = {
            name: themeName,
            category: "Imported",
            description: `Imported theme: ${themeName}`,
            colors: {
              // Start with default colors and override with imported custom colors
              ...this.themes.default.colors,
              ...themeData.customColors,
            },
          };

          // Set this as the current theme and clear custom colors
          this.currentTheme = themeKey;
          this.customColors = {}; // Clear custom colors since they're now part of the theme

          // Apply the theme and update UI
          this.applyTheme();

          // Force recreate UI to include new theme (after theme is added to themes object)
          this.createThemeUI();

          // Update the UI immediately after recreation
          this.updateThemeUI();

          // Force update the theme selector to ensure it's set correctly
          this.forceUpdateThemeSelector();

          // Save the imported theme immediately
          this.saveTheme();

          // Show single notification with the correct theme name
          this.showNotification(`Imported theme: ${themeName}`, "success");
        }
      } catch (error) {
        this.showNotification("Failed to import theme", "error");
        console.error("Theme import error:", error);
      }
    };
    reader.readAsText(file);
  }

  setupThemeUI() {
    // Create theme selector if it doesn't exist
    if (!document.getElementById("theme-selector")) {
      this.createThemeUI();
    }
    this.updateThemeUI();
  }

  createThemeUI() {
    // Add theme section to settings tab
    const settingsTab = document.getElementById("settings");
    if (settingsTab) {
      // Remove existing theme section if it exists
      const existingThemeSection =
        settingsTab.querySelector(".settings-section");
      if (existingThemeSection) {
        existingThemeSection.remove();
      }

      const themeSection = document.createElement("div");
      themeSection.className = "settings-section";

      const categories = this.getThemesByCategory();
      const themeOptions = Object.entries(categories)
        .map(([category, themes]) => {
          const optgroup = `<optgroup label="${category}">`;
          const options = themes
            .map(
              (theme) =>
                `<option value="${theme.key}" title="${theme.description}">${theme.name}</option>`
            )
            .join("");
          return optgroup + options + "</optgroup>";
        })
        .join("");

      themeSection.innerHTML = `
        <h3>Theme Settings</h3>
        <div class="settings-grid">
          <div class="form-group">
            <label for="theme-selector">Theme:</label>
            <select id="theme-selector" class="form-control">
              ${themeOptions}
            </select>
            <small class="theme-description" id="theme-description"></small>
          </div>
        </div>
        <div class="theme-customization">
          <h4>Custom Colors</h4>
          <div class="color-grid">
            <div class="color-item">
              <label>Primary Color:</label>
              <div class="color-picker-container">
                <input type="color" id="custom-primary" class="color-picker">
                <div class="color-presets" id="primary-presets"></div>
              </div>
            </div>
            <div class="color-item">
              <label>Secondary Color:</label>
              <div class="color-picker-container">
                <input type="color" id="custom-secondary" class="color-picker">
                <div class="color-presets" id="secondary-presets"></div>
              </div>
            </div>
            <div class="color-item">
              <label>Accent Color:</label>
              <div class="color-picker-container">
                <input type="color" id="custom-accent" class="color-picker">
                <div class="color-presets" id="accent-presets"></div>
              </div>
            </div>
            <div class="color-item">
              <label>Success Color:</label>
              <div class="color-picker-container">
                <input type="color" id="custom-success" class="color-picker">
                <div class="color-presets" id="success-presets"></div>
              </div>
            </div>
            <div class="color-item">
              <label>Danger Color:</label>
              <div class="color-picker-container">
                <input type="color" id="custom-danger" class="color-picker">
                <div class="color-presets" id="danger-presets"></div>
              </div>
            </div>
            <div class="color-item">
              <label>Warning Color:</label>
              <div class="color-picker-container">
                <input type="color" id="custom-warning" class="color-picker">
                <div class="color-presets" id="warning-presets"></div>
              </div>
            </div>
            <div class="color-item">
              <label>Text Color:</label>
              <input type="color" id="custom-textPrimary" class="color-picker">
            </div>
            <div class="color-item">
              <label>Text Secondary:</label>
              <input type="color" id="custom-textSecondary" class="color-picker">
            </div>
            <div class="color-item">
              <label>Background Color:</label>
              <div class="color-picker-container">
                <input type="color" id="custom-background" class="color-picker">
                <div class="color-presets" id="background-presets"></div>
              </div>
              <small>Note: Background uses gradient, this sets the primary color</small>
            </div>
          </div>
          <div class="control-buttons">
            <button class="btn btn-secondary" id="reset-colors">Reset Custom Colors</button>
            <button class="btn btn-primary" id="preview-theme">Preview Theme</button>
            <button class="btn btn-info" id="export-theme">Export Theme</button>
            <button class="btn btn-info" id="import-theme">Import Theme</button>
            <input type="file" id="theme-file-input" accept=".json" style="display: none;">
          </div>
        </div>
      `;

      settingsTab.appendChild(themeSection);

      // Add event listeners
      this.setupThemeEventListeners();
      this.setupColorPresets();
    }
  }

  setupColorPresets() {
    Object.entries(this.colorPresets).forEach(([colorKey, presets]) => {
      const container = document.getElementById(`${colorKey}-presets`);
      if (container) {
        container.innerHTML = presets
          .map(
            (color) =>
              `<div class="color-preset" style="background: ${color}" data-color="${color}"></div>`
          )
          .join("");

        container.addEventListener("click", (e) => {
          if (e.target.classList.contains("color-preset")) {
            const color = e.target.dataset.color;
            document.getElementById(`custom-${colorKey}`).value = color;
            this.setCustomColor(colorKey, color);
          }
        });
      }
    });
  }

  setupThemeEventListeners() {
    const themeSelector = document.getElementById("theme-selector");
    if (themeSelector) {
      themeSelector.addEventListener("change", (e) => {
        this.setTheme(e.target.value);
        this.updateThemeDescription();
      });
    }

    // Color picker event listeners
    const colorPickers = document.querySelectorAll(".color-picker");
    colorPickers.forEach((picker) => {
      picker.addEventListener("change", (e) => {
        const colorKey = e.target.id.replace("custom-", "");
        this.setCustomColor(colorKey, e.target.value);
      });
    });

    // Reset colors button
    const resetBtn = document.getElementById("reset-colors");
    if (resetBtn) {
      resetBtn.addEventListener("click", () => {
        this.resetCustomColors();
      });
    }

    // Preview theme button
    const previewBtn = document.getElementById("preview-theme");
    if (previewBtn) {
      previewBtn.addEventListener("click", () => {
        this.showThemePreview();
      });
    }

    // Export theme button
    const exportBtn = document.getElementById("export-theme");
    if (exportBtn) {
      exportBtn.addEventListener("click", () => {
        this.exportTheme();
      });
    }

    // Import theme button
    const importBtn = document.getElementById("import-theme");
    const fileInput = document.getElementById("theme-file-input");
    if (importBtn && fileInput) {
      importBtn.addEventListener("click", () => {
        fileInput.click();
      });

      fileInput.addEventListener("change", (e) => {
        if (e.target.files.length > 0) {
          this.importTheme(e.target.files[0]);
          e.target.value = ""; // Reset file input
        }
      });
    }
  }

  updateThemeUI() {
    const themeSelector = document.getElementById("theme-selector");
    if (themeSelector) {
      themeSelector.value = this.currentTheme;
    }

    this.updateColorPickers();
    this.updateThemeDescription();
  }

  // Method to force update theme selector
  forceUpdateThemeSelector() {
    const themeSelector = document.getElementById("theme-selector");
    if (themeSelector) {
      // Force the selector to update by triggering a change event
      themeSelector.value = this.currentTheme;
      themeSelector.dispatchEvent(new Event("change"));
    }
  }

  updateThemeDescription() {
    const description = document.getElementById("theme-description");
    if (description) {
      const theme = this.themes[this.currentTheme];
      if (theme) {
        description.textContent = theme.description;
      }
    }
  }

  updateColorPickers() {
    const currentColors = this.getCurrentTheme().colors;

    const colorMappings = {
      "custom-primary": "primary",
      "custom-secondary": "secondary",
      "custom-accent": "accent",
      "custom-success": "success",
      "custom-danger": "danger",
      "custom-warning": "warning",
      "custom-textPrimary": "textPrimary",
      "custom-textSecondary": "textSecondary",
      "custom-background": "background",
    };

    Object.entries(colorMappings).forEach(([pickerId, colorKey]) => {
      const picker = document.getElementById(pickerId);
      if (picker && currentColors[colorKey]) {
        picker.value = currentColors[colorKey];
      }
    });
  }

  showThemePreview() {
    // Create a preview modal
    const modal = document.createElement("div");
    modal.className = "theme-preview-modal";
    modal.innerHTML = `
      <div class="theme-preview-content">
        <h3>Theme Preview</h3>
        <div class="preview-card">
          <h4>Sample Interface</h4>
          <p>This is how your interface will look with the current theme.</p>
          <div class="preview-buttons">
            <button class="btn btn-primary">Primary Button</button>
            <button class="btn btn-secondary">Secondary Button</button>
            <button class="btn btn-success">Success Button</button>
            <button class="btn btn-danger">Danger Button</button>
          </div>
          <div class="preview-status">
            <div class="status running">Running Status</div>
            <div class="status stopped">Stopped Status</div>
          </div>
          <div class="preview-logs">
            <div class="log-line success">Success Log Message</div>
            <div class="log-line error">Error Log Message</div>
            <div class="log-line warning">Warning Log Message</div>
          </div>
        </div>
        <div class="preview-controls">
          <button class="btn btn-primary" id="apply-theme">Apply Theme</button>
          <button class="btn btn-secondary" id="close-preview">Close</button>
        </div>
      </div>
    `;

    document.body.appendChild(modal);

    // Add event listeners
    modal.querySelector("#close-preview").addEventListener("click", () => {
      modal.remove();
    });

    modal.querySelector("#apply-theme").addEventListener("click", () => {
      this.saveTheme();
      modal.remove();
      this.showNotification("Theme applied successfully!", "success");
    });
  }

  showNotification(message, type = "info") {
    // Clear any existing notification timeout
    if (this.notificationTimeout) {
      clearTimeout(this.notificationTimeout);
      this.notificationTimeout = null;
    }

    // Remove any existing notifications first
    const existingNotifications = document.querySelectorAll(".notification");
    existingNotifications.forEach((notification) => {
      notification.remove();
    });

    // Create new notification
    const notification = document.createElement("div");
    notification.className = `notification notification-${type}`;
    notification.textContent = message;

    document.body.appendChild(notification);

    // Store reference to current notification and set timeout
    this.currentNotificationElement = notification;
    this.notificationTimeout = setTimeout(() => {
      if (
        this.currentNotificationElement &&
        this.currentNotificationElement.parentNode
      ) {
        this.currentNotificationElement.remove();
        this.currentNotificationElement = null;
      }
      this.notificationTimeout = null;
    }, 3000);
  }

  // Helper methods for color manipulation
  lightenColor(color, amount) {
    const num = parseInt(color.replace("#", ""), 16);
    const amt = Math.round(2.55 * amount * 100);
    const R = (num >> 16) + amt;
    const G = ((num >> 8) & 0x00ff) + amt;
    const B = (num & 0x0000ff) + amt;
    return (
      "#" +
      (
        0x1000000 +
        (R < 255 ? (R < 1 ? 0 : R) : 255) * 0x10000 +
        (G < 255 ? (G < 1 ? 0 : G) : 255) * 0x100 +
        (B < 255 ? (B < 1 ? 0 : B) : 255)
      )
        .toString(16)
        .slice(1)
    );
  }

  darkenColor(color, amount) {
    const num = parseInt(color.replace("#", ""), 16);
    const amt = Math.round(2.55 * amount * 100);
    const R = (num >> 16) - amt;
    const G = ((num >> 8) & 0x00ff) - amt;
    const B = (num & 0x0000ff) - amt;
    return (
      "#" +
      (
        0x1000000 +
        (R > 255 ? 255 : R < 0 ? 0 : R) * 0x10000 +
        (G > 255 ? 255 : G < 0 ? 0 : G) * 0x100 +
        (B > 255 ? 255 : B < 0 ? 0 : B)
      )
        .toString(16)
        .slice(1)
    );
  }

  // Method to remove imported themes
  removeImportedTheme(themeKey) {
    if (this.themes[themeKey] && themeKey.startsWith("imported_")) {
      delete this.themes[themeKey];

      // If the current theme was removed, switch to default
      if (this.currentTheme === themeKey) {
        this.currentTheme = "default";
        this.customColors = {};
      }

      this.applyTheme();
      this.updateThemeUI();
      this.setupThemeUI();
      this.saveTheme();

      this.showNotification("Imported theme removed", "success");
    }
  }

  // Method to get imported themes
  getImportedThemes() {
    const importedThemes = {};
    Object.entries(this.themes).forEach(([key, theme]) => {
      if (key.startsWith("imported_")) {
        importedThemes[key] = theme;
      }
    });
    return importedThemes;
  }
}

// Initialize theme manager when DOM is loaded
document.addEventListener("DOMContentLoaded", () => {
  window.themeManager = new ThemeManager();
});
