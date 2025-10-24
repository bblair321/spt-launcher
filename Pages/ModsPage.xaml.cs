using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using SptLauncherWpf.Services;

namespace SptLauncherWpf.Pages
{
    public partial class ModsPage : Page
    {
        private bool _isAuthenticated = false;
        private string _authToken = "";
        private List<ModInfo> _mods = new();
        private ModInfo? _selectedMod = null;
        private int _currentPage = 1;
        private int _perPage = 12;
        private string _searchTerm = "";
        private bool _showFilters = false;

        public ModsPage()
        {
            InitializeComponent();
            CheckAuthentication();
        }

        private void CheckAuthentication()
        {
            _authToken = SettingsService.Instance.AuthToken ?? "";
            _isAuthenticated = !string.IsNullOrEmpty(_authToken);
            
            if (_isAuthenticated)
            {
                AuthPanel.Visibility = Visibility.Collapsed;
                MainContentPanel.Visibility = Visibility.Visible;
                UserInfoText.Text = SettingsService.Instance.UserName ?? "User";
                LoadMods();
            }
            else
            {
                AuthPanel.Visibility = Visibility.Visible;
                MainContentPanel.Visibility = Visibility.Collapsed;
            }
        }

        private async void LoginButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(EmailTextBox.Text) || string.IsNullOrEmpty(PasswordBox.Password))
            {
                MessageBox.Show("Please enter both email and password.", "Invalid Input", 
                              MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                LoginButton.IsEnabled = false;
                LoginButton.Content = "Signing in...";

                // Simulate authentication - replace with actual API call
                await Task.Delay(1000);
                
                // For demo purposes, accept any email/password
                _authToken = "demo_token_" + Guid.NewGuid().ToString();
                _isAuthenticated = true;

                SettingsService.Instance.AuthToken = _authToken;
                SettingsService.Instance.UserName = EmailTextBox.Text.Split('@')[0];
                SettingsService.Instance.SaveSettings();

                AuthPanel.Visibility = Visibility.Collapsed;
                MainContentPanel.Visibility = Visibility.Visible;
                UserInfoText.Text = SettingsService.Instance.UserName;

                await LoadMods();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Login failed: {ex.Message}", "Login Error", 
                              MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                LoginButton.IsEnabled = true;
                LoginButton.Content = "Sign In";
            }
        }

        private void LogoutButton_Click(object sender, RoutedEventArgs e)
        {
            _isAuthenticated = false;
            _authToken = "";
            SettingsService.Instance.AuthToken = "";
            SettingsService.Instance.UserName = "";
            SettingsService.Instance.SaveSettings();

            AuthPanel.Visibility = Visibility.Visible;
            MainContentPanel.Visibility = Visibility.Collapsed;
            _mods.Clear();
            UpdateModsList();
        }

        private async Task LoadMods()
        {
            try
            {
                // Simulate loading mods - replace with actual API call
                await Task.Delay(500);
                
                _mods = GenerateSampleMods();
                UpdateModsList();
                UpdateResultsText();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to load mods: {ex.Message}", "Load Error", 
                              MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private List<ModInfo> GenerateSampleMods()
        {
            var mods = new List<ModInfo>();
            var modNames = new[]
            {
                "Realism Mod", "Server Value Modifier", "Lua's Spawn Rework", 
                "Amands Graphics", "Questing Bots", "SAIN", "Waypoints",
                "Advanced AI", "Looting Bots", "Custom Scavs", "Hideout Architect",
                "Fleamarket Trader", "Insurance Overhaul", "Dynamic Loot"
            };

            var descriptions = new[]
            {
                "Complete overhaul of the game mechanics for a more realistic experience",
                "Modify server values to customize your gameplay experience",
                "Advanced spawn system with intelligent bot placement",
                "Enhanced graphics and visual effects for better immersion",
                "AI bots that complete quests and interact with the world",
                "Smart AI system for more challenging and realistic combat",
                "Advanced waypoint system for better navigation",
                "Improved AI behavior and decision making",
                "AI bots that loot and interact with the environment",
                "Customizable scav behavior and spawning",
                "Complete hideout customization system",
                "Enhanced flea market with new trading mechanics",
                "Overhauled insurance system with new features",
                "Dynamic loot spawning based on player behavior"
            };

            for (int i = 0; i < 20; i++)
            {
                var mod = new ModInfo
                {
                    Id = i.ToString(),
                    Name = modNames[i % modNames.Length] + (i > modNames.Length - 1 ? $" {i / modNames.Length + 1}" : ""),
                    Description = descriptions[i % descriptions.Length],
                    Downloads = new Random().Next(100, 10000),
                    Rating = Math.Round(new Random().NextDouble() * 5, 1),
                    CreatedAt = DateTime.Now.AddDays(-new Random().Next(1, 365)),
                    Author = "Mod Author " + (i % 5 + 1),
                    Version = "1." + new Random().Next(0, 9) + "." + new Random().Next(0, 9)
                };
                mods.Add(mod);
            }

            return mods;
        }

        private void UpdateModsList()
        {
            // Check if control is initialized before accessing it
            if (ModsListPanel == null)
                return;
                
            ModsListPanel.Children.Clear();

            if (_mods.Count == 0)
            {
                var emptyPanel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
                emptyPanel.Children.Add(new Ellipse { Width = 64, Height = 64, Fill = new SolidColorBrush(Color.FromRgb(156, 163, 175)), Margin = new Thickness(0, 0, 0, 16) });
                emptyPanel.Children.Add(new TextBlock { Text = "No mods found", FontSize = 18, FontWeight = FontWeights.Medium, Foreground = new SolidColorBrush(Color.FromRgb(17, 24, 39)), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 8) });
                emptyPanel.Children.Add(new TextBlock { Text = "Try adjusting your search or filter criteria", FontSize = 14, Foreground = new SolidColorBrush(Color.FromRgb(107, 114, 128)), HorizontalAlignment = HorizontalAlignment.Center });
                
                ModsListPanel.Children.Add(emptyPanel);
                return;
            }

            foreach (var mod in _mods)
            {
                var modCard = CreateModCard(mod);
                ModsListPanel.Children.Add(modCard);
            }
        }

        private Border CreateModCard(ModInfo mod)
        {
            var card = new Border
            {
                Background = new SolidColorBrush(Colors.White),
                BorderBrush = new SolidColorBrush(Color.FromRgb(229, 231, 235)),
                BorderThickness = new Thickness(1, 1, 1, 1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(16, 16, 16, 16),
                Margin = new Thickness(0, 0, 0, 12),
                Cursor = Cursors.Hand
            };

            var panel = new StackPanel();

            // Header with name
            var nameText = new TextBlock
            {
                Text = mod.Name,
                FontWeight = FontWeights.Medium,
                FontSize = 18,
                Foreground = new SolidColorBrush(Color.FromRgb(17, 24, 39)),
                Margin = new Thickness(0, 0, 0, 8)
            };
            panel.Children.Add(nameText);

            // Description
            var descText = new TextBlock
            {
                Text = mod.Description,
                FontSize = 14,
                Foreground = new SolidColorBrush(Color.FromRgb(107, 114, 128)),
                TextWrapping = TextWrapping.Wrap,
                MaxHeight = 40,
                Margin = new Thickness(0, 0, 0, 12)
            };
            panel.Children.Add(descText);

            // Stats
            var statsPanel = new StackPanel { Orientation = Orientation.Horizontal };
            
            var downloadsText = new TextBlock
            {
                Text = $"⬇ {mod.Downloads}",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(107, 114, 128)),
                Margin = new Thickness(0, 0, 16, 0)
            };
            statsPanel.Children.Add(downloadsText);

            var ratingText = new TextBlock
            {
                Text = $"⭐ {mod.Rating}",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(107, 114, 128)),
                Margin = new Thickness(0, 0, 16, 0)
            };
            statsPanel.Children.Add(ratingText);

            var dateText = new TextBlock
            {
                Text = $"📅 {mod.CreatedAt:MMM dd, yyyy}",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(107, 114, 128))
            };
            statsPanel.Children.Add(dateText);

            panel.Children.Add(statsPanel);

            card.Child = panel;
            card.MouseLeftButtonDown += (s, e) => SelectMod(mod);

            return card;
        }

        private void SelectMod(ModInfo mod)
        {
            _selectedMod = mod;
            UpdateModDetails();
        }

        private void UpdateModDetails()
        {
            ModDetailsPanel.Children.Clear();

            if (_selectedMod == null)
            {
                var emptyPanel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 32, 0, 16) };
                emptyPanel.Children.Add(new Ellipse { Width = 48, Height = 48, Fill = new SolidColorBrush(Color.FromRgb(156, 163, 175)), Margin = new Thickness(0, 0, 0, 8) });
                emptyPanel.Children.Add(new TextBlock { Text = "Select a mod to view details", FontSize = 14, Foreground = new SolidColorBrush(Color.FromRgb(107, 114, 128)), HorizontalAlignment = HorizontalAlignment.Center });
                ModDetailsPanel.Children.Add(emptyPanel);
                return;
            }

            var detailsPanel = new StackPanel();

            // Mod name
            var nameText = new TextBlock
            {
                Text = _selectedMod.Name,
                FontWeight = FontWeights.SemiBold,
                FontSize = 18,
                Foreground = new SolidColorBrush(Color.FromRgb(17, 24, 39)),
                Margin = new Thickness(0, 0, 0, 8)
            };
            detailsPanel.Children.Add(nameText);

            // Description
            var descText = new TextBlock
            {
                Text = _selectedMod.Description,
                FontSize = 14,
                Foreground = new SolidColorBrush(Color.FromRgb(107, 114, 128)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 16)
            };
            detailsPanel.Children.Add(descText);

            // Stats
            var statsPanel = new StackPanel { Orientation = Orientation.Horizontal };
            
            var downloadsText = new TextBlock
            {
                Text = $"⬇ {_selectedMod.Downloads} downloads",
                FontSize = 14,
                Foreground = new SolidColorBrush(Color.FromRgb(107, 114, 128))
            };
            statsPanel.Children.Add(downloadsText);

            var ratingText = new TextBlock
            {
                Text = $"⭐ {_selectedMod.Rating} rating",
                FontSize = 14,
                Foreground = new SolidColorBrush(Color.FromRgb(107, 114, 128))
            };
            statsPanel.Children.Add(ratingText);

            var dateText = new TextBlock
            {
                Text = $"📅 {_selectedMod.CreatedAt:MMM dd, yyyy}",
                FontSize = 14,
                Foreground = new SolidColorBrush(Color.FromRgb(107, 114, 128))
            };
            statsPanel.Children.Add(dateText);

            var authorText = new TextBlock
            {
                Text = $"👤 {_selectedMod.Author}",
                FontSize = 14,
                Foreground = new SolidColorBrush(Color.FromRgb(107, 114, 128))
            };
            statsPanel.Children.Add(authorText);

            var versionText = new TextBlock
            {
                Text = $"🔢 Version {_selectedMod.Version}",
                FontSize = 14,
                Foreground = new SolidColorBrush(Color.FromRgb(107, 114, 128))
            };
            statsPanel.Children.Add(versionText);

            detailsPanel.Children.Add(statsPanel);

            // Download button
            var downloadButton = new Button
            {
                Content = "Download",
                Background = new SolidColorBrush(Color.FromRgb(37, 99, 235)),
                Foreground = new SolidColorBrush(Colors.White),
                BorderThickness = new Thickness(0, 0, 0, 0),
                Padding = new Thickness(16, 8, 16, 8),
                FontSize = 14,
                Margin = new Thickness(0, 16, 0, 0),
                Cursor = Cursors.Hand
            };
            downloadButton.Click += (s, e) => DownloadMod(_selectedMod);
            detailsPanel.Children.Add(downloadButton);

            ModDetailsPanel.Children.Add(detailsPanel);
        }

        private void DownloadMod(ModInfo mod)
        {
            MessageBox.Show($"Downloading {mod.Name}...\n\nThis is a demo - actual download functionality would be implemented here.", 
                          "Download", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _searchTerm = SearchTextBox.Text;
            // Implement search filtering
            FilterMods();
        }

        private void SearchButton_Click(object sender, RoutedEventArgs e)
        {
            _searchTerm = SearchTextBox.Text;
            FilterMods();
        }

        private void FilterMods()
        {
            var filteredMods = _mods.Where(mod => 
                string.IsNullOrEmpty(_searchTerm) || 
                mod.Name.Contains(_searchTerm, StringComparison.OrdinalIgnoreCase) ||
                mod.Description.Contains(_searchTerm, StringComparison.OrdinalIgnoreCase)
            ).ToList();

            _mods = filteredMods;
            UpdateModsList();
            UpdateResultsText();
        }

        private void UpdateResultsText()
        {
            // Check if control is initialized before accessing it
            if (ResultsText == null)
                return;
                
            ResultsText.Text = $"Showing {_mods.Count} of {_mods.Count} mods";
        }

        private void FiltersButton_Click(object sender, RoutedEventArgs e)
        {
            // Check if control is initialized before accessing it
            if (FiltersPanel == null)
                return;
                
            _showFilters = !_showFilters;
            FiltersPanel.Visibility = _showFilters ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ResetFiltersButton_Click(object sender, RoutedEventArgs e)
        {
            // Check if controls are initialized before accessing them
            if (SearchTextBox == null || PerPageComboBox == null || FiltersPanel == null)
                return;
                
            SearchTextBox.Text = "";
            _searchTerm = "";
            PerPageComboBox.SelectedIndex = 1; // 12 per page
            _perPage = 12;
            _currentPage = 1;
            FiltersPanel.Visibility = Visibility.Collapsed;
            _showFilters = false;
            
            LoadMods();
        }

        private void PrevPageButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentPage > 1)
            {
                _currentPage--;
                LoadMods();
            }
        }

        private void NextPageButton_Click(object sender, RoutedEventArgs e)
        {
            _currentPage++;
            LoadMods();
        }
    }

    public class ModInfo
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public int Downloads { get; set; } = 0;
        public double Rating { get; set; } = 0.0;
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public string Author { get; set; } = "";
        public string Version { get; set; } = "1.0.0";
    }
}
