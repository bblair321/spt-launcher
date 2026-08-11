using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SptLauncherWpf.Services;
using Brush = System.Windows.Media.Brush;

namespace SptLauncherWpf.Pages
{
    public partial class ModsPage : Page
    {
        private readonly List<ForgeModSummary> _mods = new();
        private ForgeModSummary? _selectedMod;
        private List<ForgeModVersion> _selectedVersions = new();
        private ForgeModVersion? _selectedVersion;
        private ForgeFileTree? _selectedFileTree;
        private ModPathClassification? _selectedClassification;

        private int _currentPage = 1;
        private int _lastPage = 1;
        private int _total = 0;
        private string _sptRoot = "";
        private string? _sptVersion;
        private CancellationTokenSource? _loadCts;
        private bool _busy;
        private bool _showInstalled;
        private List<InstalledModInfo> _installedMods = new();
        private List<ForgeDependencyNode> _selectedDependencies = new();

        public ModsPage()
        {
            InitializeComponent();
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            RefreshSptContext();
            ApplyModeUi();
            RefreshInstalledModsCache();
            if (_mods.Count == 0)
            {
                _ = LoadModsAsync(resetPage: true);
            }
        }

        private void BrowseModeButton_Click(object sender, RoutedEventArgs e)
        {
            _showInstalled = false;
            ApplyModeUi();
        }

        private void InstalledModeButton_Click(object sender, RoutedEventArgs e)
        {
            _showInstalled = true;
            ApplyModeUi();
            RefreshInstalledMods();
        }

        private void RefreshInstalledButton_Click(object sender, RoutedEventArgs e) =>
            RefreshInstalledMods();

        private void ApplyModeUi()
        {
            BrowseSearchPanel.Visibility = _showInstalled ? Visibility.Collapsed : Visibility.Visible;
            BrowseContentPanel.Visibility = _showInstalled ? Visibility.Collapsed : Visibility.Visible;
            BrowsePaginationPanel.Visibility = _showInstalled ? Visibility.Collapsed : Visibility.Visible;
            InstalledContentPanel.Visibility = _showInstalled ? Visibility.Visible : Visibility.Collapsed;
            RefreshInstalledButton.Visibility = _showInstalled ? Visibility.Visible : Visibility.Collapsed;

            BrowseModeButton.Background = _showInstalled
                ? (Brush)new BrushConverter().ConvertFrom("#6B7280")!
                : (Brush)FindResource("PrimaryColor");
            InstalledModeButton.Background = _showInstalled
                ? (Brush)FindResource("PrimaryColor")
                : (Brush)new BrushConverter().ConvertFrom("#6B7280")!;

            HeaderSubtitleText.Text = _showInstalled
                ? "Enable, disable, open, or remove mods already in your SPT folder."
                : "Browse sp-mod.com and install into your SPT folder. Server mods go to user/mods; client mods go to BepInEx.";
        }

        private void RefreshInstalledModsCache()
        {
            RefreshSptContext();
            if (string.IsNullOrWhiteSpace(_sptRoot))
            {
                _installedMods = new List<InstalledModInfo>();
                return;
            }

            try
            {
                _installedMods = InstalledModsService.ScanInstalledMods(_sptRoot);
            }
            catch
            {
                _installedMods = new List<InstalledModInfo>();
            }
        }

        private bool IsForgeModInstalled(ForgeModSummary mod) =>
            _installedMods.Any(m => InstalledModsService.IsInstalledMatch(m, mod));

        private void RefreshSptContext()
        {
            var launcherPath = SettingsService.Instance.LauncherPath;
            if (SptInstallPathHelper.TryResolveFromLauncherPath(
                    launcherPath, out var root, out var version, out var error))
            {
                _sptRoot = root;
                _sptVersion = version;
                SptContextText.Text = string.IsNullOrWhiteSpace(version)
                    ? $"Install root: {_sptRoot}"
                    : $"SPT {version} · {_sptRoot}";
            }
            else
            {
                _sptRoot = "";
                _sptVersion = null;
                SptContextText.Text = error;
            }
        }

        private async void SearchButton_Click(object sender, RoutedEventArgs e) =>
            await LoadModsAsync(resetPage: true);

        private async void SearchTextBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                await LoadModsAsync(resetPage: true);
            }
        }

        private async void FilterBySptCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (IsLoaded)
            {
                await LoadModsAsync(resetPage: true);
            }
        }

        private async void PrevPageButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentPage > 1)
            {
                _currentPage--;
                await LoadModsAsync(resetPage: false);
            }
        }

        private async void NextPageButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentPage < _lastPage)
            {
                _currentPage++;
                await LoadModsAsync(resetPage: false);
            }
        }

        private async Task LoadModsAsync(bool resetPage)
        {
            if (_busy)
            {
                return;
            }

            RefreshSptContext();
            if (resetPage)
            {
                _currentPage = 1;
            }

            _loadCts?.Cancel();
            _loadCts = new CancellationTokenSource();
            var token = _loadCts.Token;

            try
            {
                SetBusy(true, "Loading mods…");
                var query = SearchTextBox?.Text?.Trim();
                if (string.Equals(query, "Search mods…", StringComparison.OrdinalIgnoreCase))
                {
                    query = null;
                }

                var filterVersion = FilterBySptCheckBox?.IsChecked == true ? _sptVersion : null;
                var page = await ForgeApiService.Instance.SearchModsAsync(
                    query: query,
                    sptVersion: filterVersion,
                    page: _currentPage,
                    perPage: 12,
                    cancellationToken: token);

                token.ThrowIfCancellationRequested();

                RefreshInstalledModsCache();
                _mods.Clear();
                _mods.AddRange(page.Mods);
                _currentPage = page.CurrentPage;
                _lastPage = page.LastPage;
                _total = page.Total;

                RenderModsList();
                UpdatePagination();
                StatusText.Text = _mods.Count == 0
                    ? "No mods matched."
                    : $"Loaded {_mods.Count} mods.";
            }
            catch (OperationCanceledException)
            {
                // superseded search
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Failed to load mods: {ex.Message}";
                System.Windows.MessageBox.Show(
                    $"Could not reach the mods site (sp-mod.com).\n\n{ex.Message}",
                    "Mods error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                SetBusy(false);
            }
        }

        private void RenderModsList()
        {
            ModsListPanel.Children.Clear();
            ResultsText.Text = _total > 0
                ? $"Showing {_mods.Count} on page {_currentPage} · {_total:N0} total"
                : "No results";

            if (_mods.Count == 0)
            {
                ModsListPanel.Children.Add(new TextBlock
                {
                    Text = "No mods found. Try another search or turn off the SPT version filter.",
                    Foreground = (Brush)FindResource("TextSecondaryColor"),
                    FontSize = 13,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(8)
                });
                return;
            }

            foreach (var mod in _mods)
            {
                ModsListPanel.Children.Add(CreateModRow(mod));
            }
        }

        private Border CreateModRow(ForgeModSummary mod)
        {
            var selected = _selectedMod?.Id == mod.Id;
            var card = new Border
            {
                Background = selected
                    ? (Brush)FindResource("HoverColor")
                    : (Brush)FindResource("CardBackgroundColor"),
                BorderBrush = (Brush)FindResource("BorderColor"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12),
                Margin = new Thickness(0, 0, 0, 8),
                Cursor = System.Windows.Input.Cursors.Hand
            };

            var panel = new StackPanel();
            var titleRow = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
            titleRow.Children.Add(new TextBlock
            {
                Text = mod.Name,
                FontWeight = FontWeights.SemiBold,
                FontSize = 14,
                Foreground = (Brush)FindResource("TextPrimaryColor"),
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center
            });

            if (IsForgeModInstalled(mod))
            {
                titleRow.Children.Add(new Border
                {
                    Background = (Brush)FindResource("StatusSuccessColor"),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(8, 2, 8, 2),
                    Margin = new Thickness(8, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = new TextBlock
                    {
                        Text = "Installed",
                        FontSize = 10,
                        FontWeight = FontWeights.SemiBold,
                        Foreground = System.Windows.Media.Brushes.White
                    }
                });
            }

            panel.Children.Add(titleRow);

            var meta = new TextBlock
            {
                Text =
                    $"{mod.Owner?.Name ?? "Unknown"} · {FormatCount(mod.Downloads)} downloads" +
                    (mod.FikaCompatibility ? " · Fika" : "") +
                    (string.IsNullOrWhiteSpace(mod.Category?.Title ?? mod.Category?.Slug)
                        ? ""
                        : $" · {mod.Category?.Title ?? mod.Category?.Slug}"),
                FontSize = 11,
                Foreground = (Brush)FindResource("TextSecondaryColor"),
                Margin = new Thickness(0, 4, 0, 0)
            };
            panel.Children.Add(meta);

            if (!string.IsNullOrWhiteSpace(mod.Teaser))
            {
                panel.Children.Add(new TextBlock
                {
                    Text = mod.Teaser,
                    FontSize = 12,
                    Foreground = (Brush)FindResource("TextSecondaryColor"),
                    TextWrapping = TextWrapping.Wrap,
                    MaxHeight = 36,
                    Margin = new Thickness(0, 6, 0, 0)
                });
            }

            card.Child = panel;
            card.MouseLeftButtonUp += async (_, _) => await SelectModAsync(mod);
            return card;
        }

        private async Task SelectModAsync(ForgeModSummary summary)
        {
            if (_busy)
            {
                return;
            }

            try
            {
                SetBusy(true, $"Loading {summary.Name}…");
                RefreshSptContext();

                var detail = await ForgeApiService.Instance.GetModAsync(summary.Id);
                var versions = await ForgeApiService.Instance.GetModVersionsAsync(
                    summary.Id,
                    FilterBySptCheckBox?.IsChecked == true ? _sptVersion : null);

                if (versions.Count == 0)
                {
                    versions = detail.Versions ?? new List<ForgeModVersion>();
                }

                _selectedMod = detail;
                _selectedVersions = versions;
                _selectedVersion = versions.FirstOrDefault();
                _selectedFileTree = null;
                _selectedClassification = null;
                _selectedDependencies = new List<ForgeDependencyNode>();

                if (_selectedVersion != null)
                {
                    await LoadClassificationForSelectedVersionAsync();
                    await LoadDependenciesForSelectedVersionAsync();
                }

                RenderModsList();
                RenderDetails();
                StatusText.Text = $"Selected {detail.Name}";
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Failed to load mod: {ex.Message}";
            }
            finally
            {
                SetBusy(false);
            }
        }

        private async Task LoadClassificationForSelectedVersionAsync()
        {
            _selectedFileTree = null;
            _selectedClassification = null;
            if (_selectedMod == null || _selectedVersion == null)
            {
                return;
            }

            try
            {
                _selectedFileTree = await ForgeApiService.Instance.GetFileTreeAsync(
                    _selectedMod.Id, _selectedVersion.Id);
            }
            catch
            {
                _selectedFileTree = null;
            }

            var hasRuntime = SptInstallPathHelper.InstallHasSptRuntime(_sptRoot);
            if (_selectedFileTree?.Files is { Count: > 0 })
            {
                _selectedClassification = ModPathClassifier.Classify(_selectedFileTree.Files, hasRuntime);
            }
        }

        private async Task LoadDependenciesForSelectedVersionAsync()
        {
            _selectedDependencies = new List<ForgeDependencyNode>();
            if (_selectedMod == null || _selectedVersion == null || string.IsNullOrWhiteSpace(_sptVersion))
            {
                return;
            }

            try
            {
                var deps = await ForgeApiService.Instance.GetDependenciesAsync(
                    _selectedMod.Id,
                    _selectedVersion.Version,
                    _sptVersion);
                _selectedDependencies = deps.ToList();
            }
            catch
            {
                _selectedDependencies = new List<ForgeDependencyNode>();
            }
        }

        private void RenderDetails()
        {
            ModDetailsPanel.Children.Clear();
            if (_selectedMod == null)
            {
                ModDetailsPanel.Children.Add(new TextBlock
                {
                    Text = "Select a mod",
                    FontSize = 14,
                    Foreground = (Brush)FindResource("TextSecondaryColor"),
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    Margin = new Thickness(0, 40, 0, 0)
                });
                return;
            }

            ModDetailsPanel.Children.Add(new TextBlock
            {
                Text = _selectedMod.Name,
                FontSize = 18,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)FindResource("TextPrimaryColor"),
                TextWrapping = TextWrapping.Wrap
            });

            ModDetailsPanel.Children.Add(new TextBlock
            {
                Text =
                    $"By {_selectedMod.Owner?.Name ?? "Unknown"} · {FormatCount(_selectedMod.Downloads)} downloads",
                FontSize = 12,
                Foreground = (Brush)FindResource("TextSecondaryColor"),
                Margin = new Thickness(0, 6, 0, 0)
            });

            if (!string.IsNullOrWhiteSpace(_selectedMod.Teaser))
            {
                ModDetailsPanel.Children.Add(new TextBlock
                {
                    Text = _selectedMod.Teaser,
                    FontSize = 13,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = (Brush)FindResource("TextSecondaryColor"),
                    Margin = new Thickness(0, 12, 0, 0)
                });
            }

            var isTools = ModInstallService.IsToolsCategory(_selectedMod);
            if (isTools)
            {
                ModDetailsPanel.Children.Add(new TextBlock
                {
                    Text = "This is a Forge Tool — open it on the website instead of auto-installing.",
                    FontSize = 12,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = (Brush)FindResource("StatusWarningColor"),
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 12, 0, 0)
                });
            }

            ModDetailsPanel.Children.Add(new TextBlock
            {
                Text = "Version",
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)FindResource("TextPrimaryColor"),
                Margin = new Thickness(0, 16, 0, 6)
            });

            if (_selectedVersions.Count == 0)
            {
                ModDetailsPanel.Children.Add(new TextBlock
                {
                    Text = "No versions available for this filter.",
                    FontSize = 12,
                    Foreground = (Brush)FindResource("TextSecondaryColor")
                });
            }
            else
            {
                var versionList = new StackPanel { Margin = new Thickness(0, 0, 0, 4) };
                foreach (var version in _selectedVersions.Take(12))
                {
                    versionList.Children.Add(CreateVersionRow(version));
                }

                if (_selectedVersions.Count > 12)
                {
                    versionList.Children.Add(new TextBlock
                    {
                        Text = $"+ {_selectedVersions.Count - 12} older versions on Forge",
                        FontSize = 11,
                        Foreground = (Brush)FindResource("TextSecondaryColor"),
                        Margin = new Thickness(0, 6, 0, 0)
                    });
                }

                ModDetailsPanel.Children.Add(versionList);
            }

            if (_selectedVersion != null)
            {
                ModDetailsPanel.Children.Add(new TextBlock
                {
                    Text =
                        $"Selected {_selectedVersion.Version} · Fika: {_selectedVersion.FikaCompatibilityText}" +
                        (string.IsNullOrWhiteSpace(_selectedVersion.SptVersionConstraint)
                            ? ""
                            : $" · SPT {_selectedVersion.SptVersionConstraint}"),
                    FontSize = 12,
                    Foreground = (Brush)FindResource("TextSecondaryColor"),
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 8, 0, 0)
                });
            }

            if (_selectedClassification != null)
            {
                ModDetailsPanel.Children.Add(new TextBlock
                {
                    Text = _selectedClassification.Summary,
                    FontSize = 12,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = (Brush)FindResource(
                        _selectedClassification.CanAutoInstall ? "StatusSuccessColor" : "StatusWarningColor"),
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 12, 0, 0)
                });
            }

            if (_selectedDependencies.Count > 0)
            {
                var depNames = FlattenDependencyNames(_selectedDependencies).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                ModDetailsPanel.Children.Add(new TextBlock
                {
                    Text = depNames.Count == 0
                        ? "This version lists dependencies on Forge."
                        : $"Also needs: {string.Join(", ", depNames.Take(8))}" +
                          (depNames.Count > 8 ? $" (+{depNames.Count - 8} more)" : ""),
                    FontSize = 12,
                    Foreground = (Brush)FindResource("StatusWarningColor"),
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 10, 0, 0)
                });
            }

            if (_selectedMod != null && IsForgeModInstalled(_selectedMod))
            {
                ModDetailsPanel.Children.Add(new TextBlock
                {
                    Text = "Already installed in your SPT folder (reinstall will overwrite matching files).",
                    FontSize = 12,
                    Foreground = (Brush)FindResource("StatusInfoColor"),
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 10, 0, 0)
                });
            }

            var actions = new StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                Margin = new Thickness(0, 16, 0, 0)
            };

            // File-tree preview is best-effort. Forge sometimes 403s it even when download works
            // (LootNET 1.1.0). Install can still classify from the archive after download.
            var layoutUnsupported = _selectedClassification is { CanAutoInstall: false };
            var canInstall = !isTools
                             && _selectedVersion != null
                             && !string.IsNullOrWhiteSpace(_selectedVersion.Link)
                             && !string.IsNullOrWhiteSpace(_sptRoot)
                             && !layoutUnsupported;

            var installButton = new System.Windows.Controls.Button
            {
                Content = "Install",
                Style = (Style)FindResource("ModernButtonStyle"),
                Padding = new Thickness(16, 8, 16, 8),
                FontSize = 13,
                IsEnabled = canInstall,
                Margin = new Thickness(0, 0, 8, 0)
            };
            installButton.Click += async (_, _) => await InstallSelectedAsync();
            actions.Children.Add(installButton);

            var openButton = new System.Windows.Controls.Button
            {
                Content = "Open on website",
                Style = (Style)FindResource("ModernButtonStyle"),
                Background = (Brush)new BrushConverter().ConvertFrom("#6B7280")!,
                Padding = new Thickness(16, 8, 16, 8),
                FontSize = 13
            };
            openButton.Click += (_, _) => OpenSelectedOnForge();
            actions.Children.Add(openButton);

            ModDetailsPanel.Children.Add(actions);

            if (string.IsNullOrWhiteSpace(_sptRoot))
            {
                ModDetailsPanel.Children.Add(new TextBlock
                {
                    Text = "Set your SPT.Launcher.exe path on the Launcher tab before installing.",
                    FontSize = 12,
                    Foreground = (Brush)FindResource("StatusWarningColor"),
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 12, 0, 0)
                });
            }
            else if (layoutUnsupported)
            {
                ModDetailsPanel.Children.Add(new TextBlock
                {
                    Text = "Auto-install isn’t available for this package layout. Use Open on website.",
                    FontSize = 12,
                    Foreground = (Brush)FindResource("TextSecondaryColor"),
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 12, 0, 0)
                });
            }
            else if (_selectedClassification == null && canInstall)
            {
                ModDetailsPanel.Children.Add(new TextBlock
                {
                    Text = "No file preview for this version. Install will inspect the archive after download.",
                    FontSize = 12,
                    Foreground = (Brush)FindResource("TextSecondaryColor"),
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 12, 0, 0)
                });
            }
        }

        private async Task InstallSelectedAsync()
        {
            if (_selectedMod == null || _selectedVersion == null || _busy)
            {
                return;
            }

            RefreshSptContext();
            if (string.IsNullOrWhiteSpace(_sptRoot))
            {
                System.Windows.MessageBox.Show(
                    "Set your SPT.Launcher.exe path on the Launcher tab first.",
                    "SPT path needed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            var confirmMessage =
                $"Install {_selectedMod.Name} {_selectedVersion.Version} into:\n\n{_sptRoot}\n\n" +
                $"{_selectedClassification?.Summary ?? "Unknown layout"}";

            var depNames = FlattenDependencyNames(_selectedDependencies)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (depNames.Count > 0)
            {
                confirmMessage +=
                    "\n\nForge lists these dependencies (not auto-installed):\n" +
                    string.Join("\n", depNames.Take(10).Select(n => "• " + n));
            }

            confirmMessage += "\n\nContinue?";

            var confirm = System.Windows.MessageBox.Show(
                confirmMessage,
                "Install mod",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirm != MessageBoxResult.Yes)
            {
                return;
            }

            try
            {
                SetBusy(true, "Installing…");
                var progress = new Progress<ModInstallProgress>(p => StatusText.Text = p.Message);
                var report = await ModInstallService.Instance.InstallAsync(
                    _selectedMod,
                    _selectedVersion,
                    _sptRoot,
                    _selectedFileTree?.Files,
                    progress);

                StatusText.Text = report.Message;
                System.Windows.MessageBox.Show(
                    report.Message +
                    (report.ServerTargets.Count > 0
                        ? $"\n\nServer:\n{string.Join("\n", report.ServerTargets)}"
                        : "") +
                    (report.ClientTargets.Count > 0
                        ? $"\n\nClient:\n{string.Join("\n", report.ClientTargets)}"
                        : ""),
                    report.Success ? "Install complete" : "Install blocked",
                    MessageBoxButton.OK,
                    report.Success ? MessageBoxImage.Information : MessageBoxImage.Warning);

                if (report.Success)
                {
                    RefreshInstalledModsCache();
                    RenderModsList();
                    if (_showInstalled)
                    {
                        _ = RefreshInstalledModsAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                if (ModInstallService.IsFileLockException(ex))
                {
                    var detail = ModInstallService.BuildPublicFileLockMessage(_sptRoot, ex);
                    StatusText.Text = "Install blocked — a file is locked.";
                    System.Windows.MessageBox.Show(
                        detail,
                        "Install blocked",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
                else
                {
                    StatusText.Text = $"Install failed: {ex.Message}";
                    System.Windows.MessageBox.Show(
                        $"Install failed.\n\n{ex.Message}",
                        "Install error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            }
            finally
            {
                SetBusy(false);
            }
        }

        private void OpenSelectedOnForge()
        {
            var url = _selectedMod?.DetailUrl;
            if (string.IsNullOrWhiteSpace(url) && _selectedMod != null)
            {
                url = $"{ForgeApiService.WebsiteBaseUrl}/mod/{_selectedMod.Id}/{_selectedMod.Slug}";
            }

            if (string.IsNullOrWhiteSpace(url))
            {
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    $"Could not open browser.\n\n{ex.Message}",
                    "Open failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private Border CreateVersionRow(ForgeModVersion version)
        {
            var selected = _selectedVersion?.Id == version.Id;
            var label = version.Version;
            if (!string.IsNullOrWhiteSpace(version.SptVersionConstraint))
            {
                label += $"  ·  SPT {version.SptVersionConstraint}";
            }

            var row = new Border
            {
                Background = selected
                    ? (Brush)FindResource("HoverColor")
                    : (Brush)FindResource("SurfaceColor"),
                BorderBrush = (Brush)FindResource("BorderColor"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(10, 8, 10, 8),
                Margin = new Thickness(0, 0, 0, 6),
                Cursor = System.Windows.Input.Cursors.Hand,
                Tag = version
            };

            row.Child = new TextBlock
            {
                Text = label,
                FontSize = 13,
                FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal,
                Foreground = (Brush)FindResource("TextPrimaryColor"),
                TextWrapping = TextWrapping.Wrap
            };

            row.MouseLeftButtonUp += async (_, _) =>
            {
                if (_busy || _selectedVersion?.Id == version.Id)
                {
                    return;
                }

                _selectedVersion = version;
                try
                {
                    SetBusy(true, $"Checking {version.Version} layout…");
                    await LoadClassificationForSelectedVersionAsync();
                    await LoadDependenciesForSelectedVersionAsync();
                    RenderDetails();
                }
                finally
                {
                    SetBusy(false);
                }
            };

            return row;
        }

        private void RefreshInstalledMods() => _ = RefreshInstalledModsAsync();

        private async Task RefreshInstalledModsAsync()
        {
            RefreshSptContext();
            InstalledListPanel.Children.Clear();

            if (string.IsNullOrWhiteSpace(_sptRoot))
            {
                _installedMods = new List<InstalledModInfo>();
                InstalledSummaryText.Text = "Set your SPT.Launcher.exe path on the Launcher tab to manage installed mods.";
                StatusText.Text = "SPT path needed for installed mods.";
                return;
            }

            try
            {
                SetBusy(true, "Scanning installed mods…");
                _installedMods = InstalledModsService.ScanInstalledMods(_sptRoot);

                if (!string.IsNullOrWhiteSpace(_sptVersion))
                {
                    StatusText.Text = "Checking for mod updates…";
                    await ApplyUpdateChecksAsync(_installedMods, _sptVersion);
                }

                var serverCount = _installedMods.Count(m => m.Kind == InstalledModKind.Server);
                var clientCount = _installedMods.Count(m => m.Kind == InstalledModKind.Client);
                var disabledCount = _installedMods.Count(m => !m.IsEnabled);
                var updateCount = _installedMods.Count(m => m.AvailableUpdate != null);

                InstalledSummaryText.Text =
                    $"{_installedMods.Count} installed · {serverCount} server · {clientCount} client" +
                    (disabledCount > 0 ? $" · {disabledCount} disabled" : "") +
                    (updateCount > 0 ? $" · {updateCount} updates" : "");

                InstalledListPanel.Children.Clear();
                if (_installedMods.Count == 0)
                {
                    InstalledListPanel.Children.Add(new TextBlock
                    {
                        Text = "No mods found under user/mods or BepInEx/plugins.",
                        FontSize = 13,
                        Foreground = (Brush)FindResource("TextSecondaryColor"),
                        Margin = new Thickness(8),
                        TextWrapping = TextWrapping.Wrap
                    });
                }
                else
                {
                    string? lastSection = null;
                    foreach (var mod in _installedMods)
                    {
                        var section = mod.Kind == InstalledModKind.Server ? "Server (user/mods)" : "Client (BepInEx/plugins)";
                        if (!string.Equals(section, lastSection, StringComparison.Ordinal))
                        {
                            InstalledListPanel.Children.Add(new TextBlock
                            {
                                Text = section,
                                FontSize = 12,
                                FontWeight = FontWeights.SemiBold,
                                Foreground = (Brush)FindResource("TextSecondaryColor"),
                                Margin = new Thickness(4, lastSection == null ? 0 : 12, 4, 6)
                            });
                            lastSection = section;
                        }

                        InstalledListPanel.Children.Add(CreateInstalledRow(mod));
                    }
                }

                StatusText.Text = updateCount > 0
                    ? $"Loaded {_installedMods.Count} installed mods · {updateCount} update(s) available."
                    : $"Loaded {_installedMods.Count} installed mods.";
            }
            catch (Exception ex)
            {
                InstalledSummaryText.Text = "Failed to scan installed mods.";
                StatusText.Text = $"Installed scan failed: {ex.Message}";
            }
            finally
            {
                SetBusy(false);
            }
        }

        private async Task ApplyUpdateChecksAsync(List<InstalledModInfo> mods, string sptVersion)
        {
            var pairs = InstalledModsService.BuildUpdateQueryPairs(mods).ToList();
            if (pairs.Count == 0)
            {
                return;
            }

            try
            {
                // Forge accepts batches; keep requests reasonably sized.
                for (var i = 0; i < pairs.Count; i += 40)
                {
                    var batch = pairs.Skip(i).Take(40);
                    var result = await ForgeApiService.Instance.CheckUpdatesAsync(batch, sptVersion);
                    foreach (var update in result.Updates)
                    {
                        if (update.CurrentVersion == null || update.RecommendedVersion == null)
                        {
                            continue;
                        }

                        var match = mods.FirstOrDefault(m =>
                            (m.ForgeModId is int id && id == update.CurrentVersion.ModId) ||
                            (!string.IsNullOrWhiteSpace(m.ForgeGuid) &&
                             string.Equals(m.ForgeGuid, update.CurrentVersion.Guid, StringComparison.OrdinalIgnoreCase)));
                        if (match != null)
                        {
                            match.AvailableUpdate = update.RecommendedVersion;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Update check failed: {ex.Message}";
            }
        }

        private Border CreateInstalledRow(InstalledModInfo mod)
        {
            var row = new Border
            {
                Background = (Brush)FindResource("SurfaceColor"),
                BorderBrush = (Brush)FindResource("BorderColor"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12),
                Margin = new Thickness(0, 0, 0, 8)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var info = new StackPanel();
            info.Children.Add(new TextBlock
            {
                Text = mod.DisplayName,
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)FindResource("TextPrimaryColor"),
                TextWrapping = TextWrapping.Wrap
            });

            var meta =
                (mod.IsEnabled ? "Enabled" : "Disabled") +
                (string.IsNullOrWhiteSpace(mod.VersionHint) ? "" : $" · v{mod.VersionHint}") +
                (mod.AvailableUpdate != null ? $" · update {mod.AvailableUpdate.Version}" : "") +
                $" · {mod.Path}";

            info.Children.Add(new TextBlock
            {
                Text = meta,
                FontSize = 11,
                Foreground = mod.AvailableUpdate != null
                    ? (Brush)FindResource("StatusInfoColor")
                    : mod.IsEnabled
                        ? (Brush)FindResource("TextSecondaryColor")
                        : (Brush)FindResource("StatusWarningColor"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 0)
            });

            Grid.SetColumn(info, 0);
            grid.Children.Add(info);

            var actions = new StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12, 0, 0, 0)
            };

            if (mod.AvailableUpdate != null)
            {
                var updateBtn = CreateInstalledActionButton(
                    $"Update {mod.AvailableUpdate.Version}",
                    () => _ = UpdateInstalledModAsync(mod));
                updateBtn.Background = (Brush)FindResource("PrimaryColor");
                actions.Children.Add(updateBtn);
            }

            var toggle = CreateInstalledActionButton(
                mod.IsEnabled ? "Disable" : "Enable",
                () => ToggleInstalledMod(mod));
            var open = CreateInstalledActionButton("Open", () => OpenInstalledMod(mod));
            var remove = CreateInstalledActionButton("Remove", () => RemoveInstalledMod(mod));

            actions.Children.Add(toggle);
            actions.Children.Add(open);
            actions.Children.Add(remove);

            Grid.SetColumn(actions, 1);
            grid.Children.Add(actions);

            row.Child = grid;
            return row;
        }

        private System.Windows.Controls.Button CreateInstalledActionButton(string label, Action action)
        {
            var button = new System.Windows.Controls.Button
            {
                Content = label,
                Style = (Style)FindResource("ModernButtonStyle"),
                Background = (Brush)new BrushConverter().ConvertFrom("#6B7280")!,
                Padding = new Thickness(12, 6, 12, 6),
                FontSize = 12,
                Margin = new Thickness(0, 0, 6, 0),
                IsEnabled = !_busy
            };
            button.Click += (_, _) => action();
            return button;
        }

        private void ToggleInstalledMod(InstalledModInfo mod)
        {
            try
            {
                var updated = InstalledModsService.SetEnabled(mod, !mod.IsEnabled);
                StatusText.Text = updated.IsEnabled
                    ? $"Enabled {updated.DisplayName}."
                    : $"Disabled {updated.DisplayName}.";
                RefreshInstalledMods();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    $"Could not change \"{mod.DisplayName}\".\n\n{ex.Message}",
                    "Enable/disable failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void OpenInstalledMod(InstalledModInfo mod)
        {
            try
            {
                InstalledModsService.OpenInExplorer(mod);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    $"Could not open \"{mod.DisplayName}\".\n\n{ex.Message}",
                    "Open failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void RemoveInstalledMod(InstalledModInfo mod)
        {
            var confirm = System.Windows.MessageBox.Show(
                $"Remove \"{mod.DisplayName}\" from disk?\n\n{mod.Path}\n\nThis cannot be undone.",
                "Remove mod",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes)
            {
                return;
            }

            try
            {
                InstalledModsService.Uninstall(mod);
                StatusText.Text = $"Removed {mod.DisplayName}.";
                RefreshInstalledMods();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    $"Could not remove \"{mod.DisplayName}\".\n\n{ex.Message}",
                    "Remove failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private async Task UpdateInstalledModAsync(InstalledModInfo mod)
        {
            if (_busy || mod.AvailableUpdate == null || mod.ForgeModId is not int forgeId)
            {
                System.Windows.MessageBox.Show(
                    "This mod doesn't have site metadata for one-click update. Reinstall it once from Browse Mods.",
                    "Update unavailable",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            RefreshSptContext();
            if (string.IsNullOrWhiteSpace(_sptRoot))
            {
                return;
            }

            var recommended = mod.AvailableUpdate;
            var confirm = System.Windows.MessageBox.Show(
                $"Update {mod.DisplayName} from {mod.VersionHint ?? "?"} to {recommended.Version}?",
                "Update mod",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes)
            {
                return;
            }

            try
            {
                SetBusy(true, $"Updating {mod.DisplayName}…");
                var detail = await ForgeApiService.Instance.GetModAsync(forgeId);
                var version = recommended.ToModVersion();
                if (string.IsNullOrWhiteSpace(version.Link))
                {
                    var versions = await ForgeApiService.Instance.GetModVersionsAsync(forgeId, _sptVersion);
                    version = versions.FirstOrDefault(v => v.Id == recommended.Id) ?? version;
                }

                ForgeFileTree? tree = null;
                try
                {
                    tree = await ForgeApiService.Instance.GetFileTreeAsync(forgeId, version.Id);
                }
                catch
                {
                    // optional
                }

                var progress = new Progress<ModInstallProgress>(p => StatusText.Text = p.Message);
                var report = await ModInstallService.Instance.InstallAsync(
                    detail,
                    version,
                    _sptRoot,
                    tree?.Files,
                    progress);

                StatusText.Text = report.Message;
                System.Windows.MessageBox.Show(
                    report.Message,
                    report.Success ? "Update complete" : "Update blocked",
                    MessageBoxButton.OK,
                    report.Success ? MessageBoxImage.Information : MessageBoxImage.Warning);

                if (report.Success)
                {
                    await RefreshInstalledModsAsync();
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    $"Update failed.\n\n{ex.Message}",
                    "Update error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                SetBusy(false);
            }
        }

        private static IEnumerable<string> FlattenDependencyNames(IEnumerable<ForgeDependencyNode> nodes)
        {
            foreach (var node in nodes)
            {
                if (!string.IsNullOrWhiteSpace(node.Name))
                {
                    yield return node.Name!;
                }

                foreach (var nested in FlattenDependencyNames(node.Dependencies))
                {
                    yield return nested;
                }
            }
        }

        private void UpdatePagination()
        {
            PageInfo.Text = $"Page {_currentPage} of {_lastPage}";
            PrevPageButton.IsEnabled = !_busy && _currentPage > 1;
            NextPageButton.IsEnabled = !_busy && _currentPage < _lastPage;
        }

        private void SetBusy(bool busy, string? status = null)
        {
            _busy = busy;
            SearchButton.IsEnabled = !busy;
            FilterBySptCheckBox.IsEnabled = !busy;
            UpdatePagination();
            if (!string.IsNullOrWhiteSpace(status))
            {
                StatusText.Text = status;
            }
        }

        private static string FormatCount(long value)
        {
            if (value >= 1_000_000)
            {
                return $"{value / 1_000_000d:0.#}M";
            }

            if (value >= 1_000)
            {
                return $"{value / 1_000d:0.#}K";
            }

            return value.ToString("N0");
        }
    }
}
