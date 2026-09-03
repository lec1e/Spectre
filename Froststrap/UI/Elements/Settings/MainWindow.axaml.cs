using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FluentAvalonia.UI.Controls;
using Froststrap.UI.Elements.Controls;
using Froststrap.UI.Utility;
using Froststrap.UI.ViewModels;
using Froststrap.UI.ViewModels.Settings;
using Froststrap.UI.ViewModels.Settings.Mods;
using LucideAvalonia;
using LucideAvalonia.Enum;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace Froststrap.UI.Elements.Settings
{
    public partial class MainWindow : Base.AvaloniaWindow
    {
        public static MainWindow? Instance { get; private set; }

        private static Models.Persistable.WindowState State => App.State.Prop.SettingsWindow;
        private readonly MainWindowViewModel? _viewModel;
        private readonly ObservableCollection<RailNavItem> _railItems = [];

        private Border? _currentNotification;
        private CancellationTokenSource? _notificationCts;
        private bool _isAnimatingOut = false;

        private readonly Dictionary<string, Control> _cachedPages = [];

        public MainWindow()
        {
            Instance = this;
            InitializeComponent();

            // Frosted glass chrome (Acrylic/Mica when the OS supports it).
            TransparencyLevelHint =
            [
                WindowTransparencyLevel.AcrylicBlur,
                WindowTransparencyLevel.Mica,
                WindowTransparencyLevel.Blur,
                WindowTransparencyLevel.None
            ];
            Background = Brushes.Transparent;

            AddHandler(PointerMovedEvent, OnWindowPointerMoved, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
            AddHandler(PointerExitedEvent, OnWindowPointerExited, RoutingStrategies.Bubble, handledEventsToo: true);
            global::Froststrap.Utility.ThemeManager.ThemeChanged += OnThemeChanged;
            Closed += (_, _) => global::Froststrap.Utility.ThemeManager.ThemeChanged -= OnThemeChanged;

            Dispatcher.UIThread.Post(() =>
            {
                ShellGlass?.SyncTintFromTheme();
                ShellGlass?.ApplyFromSettings();
                AbyssBackground?.SyncFromTheme();
                AbyssBackground?.SyncFromSettings();
                SyncPointerEffects();
            }, DispatcherPriority.Loaded);
        }

        public void SyncPointerEffects()
        {
            bool on = App.Settings?.Prop.EnableLiquidCursor ?? true;
            if (LiquidCursor is null)
                return;

            LiquidCursor.IsVisible = on;
            LiquidCursor.IsEffectEnabled = on;
            LiquidCursor.SyncColorsFromTheme();
        }

        private void OnThemeChanged()
        {
            Dispatcher.UIThread.Post(() =>
            {
                ShellGlass?.SyncTintFromTheme();
                ShellGlass?.ApplyFromSettings();
                AbyssBackground?.SyncFromTheme();
                AbyssBackground?.SyncFromSettings();
                SyncPointerEffects();
            });
        }

        private void OnWindowPointerMoved(object? sender, Avalonia.Input.PointerEventArgs e)
        {
            AbyssBackground?.SetPointer(e.GetPosition(AbyssBackground), true);
            LiquidCursor?.SetPointer(e.GetPosition(LiquidCursor), true);
        }

        private void OnWindowPointerExited(object? sender, Avalonia.Input.PointerEventArgs e)
        {
            AbyssBackground?.SetPointer(new Point(-40, -40), false);
            LiquidCursor?.SetPointer(new Point(-40, -40), false);
        }

        public MainWindow(bool showAlreadyRunningWarning) : this()
        {
            _viewModel = new MainWindowViewModel();
            DataContext = _viewModel;

            _viewModel.RequestSaveNoticeEvent += (_, _) => ShowSaveNotification();
            _viewModel.RequestCloseWindowEvent += (_, _) => Close();
            _viewModel.SearchBar.SearchResultSelected += (_, item) => OnSearchResultSelected(item);

            App.Logger.WriteLine("MainWindow", "Initializing settings window");

            if (showAlreadyRunningWarning)
                ShowAlreadyRunningNotification();

            BuildRailItems();
            ApplyGbsRailState();

            LoadState();

            App.RemoteData.Subscribe((_, _) => Dispatcher.UIThread.Post(() =>
            {
                // Remote alert banners from third-party config hosts are disabled.
                if (AlertBar is not null)
                    AlertBar.IsVisible = false;
            }));

            _viewModel.PropertyChanged += OnViewModelPropertyChanged;

            this.Closing += MainWindow_Closing;
            this.Closed += MainWindow_Closed;

            UpdatePageView(_viewModel.CurrentPage);

            Dispatcher.UIThread.Post(() =>
            {
                UpdateSelectedRailItem(_viewModel.SelectedPage);
                AttachTitleBarButtons();
                BuildSearchIndex();
                ApplyLaunchPage();
            }, DispatcherPriority.Loaded);
        }

        private void ApplyLaunchPage()
        {
            string? page = App.LaunchSettings?.PageFlag.Data;
            if (string.IsNullOrWhiteSpace(page))
                return;

            string tag = page.Trim().ToLowerInvariant() switch
            {
                "games" or "quickplay" => "quickplay",
                "library" or "mods" or "presets" => "mods",
                "custommods" or "mymods" => "custommods",
                "settings" or "tools" => "tools",
                _ => "home"
            };

            GetNavigationAction(tag)?.Invoke();
        }

        private void BuildRailItems()
        {
            _railItems.Clear();

            void Add(string tag, string title, LucideIconNames icon, bool visible = true)
                => _railItems.Add(new RailNavItem { Tag = tag, Title = title, Icon = icon, IsVisible = visible });

            void Sep() => _railItems.Add(new RailNavItem { IsSeparator = true, Tag = $"sep-{_railItems.Count}" });

            // Exact mockup nav — everything else lives under Settings (tools hub)
            Add("home", "Home", LucideIconNames.House);
            Add("quickplay", "Games", LucideIconNames.Gamepad2);
            Add("mods", "Library", LucideIconNames.Library);
            Add("custommods", "Mods", LucideIconNames.Puzzle);
            Add("tools", "Settings", LucideIconNames.Settings);

            if (RailItemsControl is not null)
                RailItemsControl.ItemsSource = _railItems;
        }

        private void ApplyGbsRailState()
        {
            // GBS lives under Settings tools hub now — nothing to toggle on the rail.
        }

        private void TopDownload_Click(object? sender, RoutedEventArgs e)
            => _viewModel?.NavigateToChannelsCommand.Execute(null);

        private void TopNews_Click(object? sender, RoutedEventArgs e)
            => _viewModel?.NavigateToNewsCommand.Execute(null);

        private void RailItem_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: string tag })
                return;

            SaveCurrentPage();
            GetNavigationAction(tag)?.Invoke();
        }

        private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (_viewModel == null) return;

            if (e.PropertyName == nameof(MainWindowViewModel.CurrentPage))
            {
                UpdatePageView(_viewModel.CurrentPage);
            }
            else if (e.PropertyName == nameof(MainWindowViewModel.SelectedPage))
            {
                UpdateSelectedRailItem(_viewModel.SelectedPage);
            }
        }

        private SearchBarItem? _pendingSearchScrollItem;

        private void OnSearchResultSelected(SearchBarItem item)
        {
            _pendingSearchScrollItem = item;

            if (_viewModel?.SelectedPage != item.PageTag)
            {
                SaveCurrentPage();

                // Navigation will trigger UpdatePageView, which will scroll to the item
                var action = GetNavigationAction(item.PageTag ?? "");
                action?.Invoke();
            }
            else
            {
                ScrollToSearchItem(item);
            }
        }

        private Action? GetNavigationAction(string pageTag)
        {
            return pageTag switch
            {
                "home" => () => _viewModel?.NavigateToHomeCommand.Execute(null),
                "tools" => () => _viewModel?.NavigateToToolsHubCommand.Execute(null),
                "custommods" => () => _viewModel?.NavigateToMyModsCommand.Execute(null),
                "integrations" => () => _viewModel?.NavigateToIntegrationsCommand.Execute(null),
                "behaviour" => () => _viewModel?.NavigateToBehaviourCommand.Execute(null),
                "linuxsettings" => () => _viewModel?.NavigateToLinuxSettingsCommand.Execute(null),
                "mods" => () => _viewModel?.NavigateToPresetModsCommand.Execute(null),
                "fastflags" => () => _viewModel?.NavigateToFastFlagsCommand.Execute(null),
                "appearance" => () => _viewModel?.NavigateToAppearanceCommand.Execute(null),
                "regionselector" => () => _viewModel?.NavigateToRegionSelectorCommand.Execute(null),
                "globalsettings" => () => _viewModel?.NavigateToGlobalSettingsCommand.Execute(null),
                "shortcuts" => () => _viewModel?.NavigateToShortcutsCommand.Execute(null),
                "quickplay" => () => _viewModel?.NavigateToQuickPlayCommand.Execute(null),
                "channels" => () => _viewModel?.NavigateToChannelsCommand.Execute(null),
                "versions" => () => _viewModel?.NavigateToVersionsManagerCommand.Execute(null),
                "banasync" => () => _viewModel?.NavigateToBanAsyncCommand.Execute(null),
                "hwidspoofer" => () => _viewModel?.NavigateToHwidSpooferCommand.Execute(null),
                "multiinstance" => () => _viewModel?.NavigateToMultiInstanceCommand.Execute(null),
                "vipserver" => () => _viewModel?.NavigateToVipServerCommand.Execute(null),
                "news" => () => _viewModel?.NavigateToNewsCommand.Execute(null),
                _ => null
            };
        }

        private readonly Dictionary<string, (string Title, LucideIconNames Icon)> _pageInfo = new()
        {
            ["home"] = ("Home", LucideIconNames.House),
            ["integrations"] = (Strings.Menu_Integrations_Title, LucideIconNames.Plus),
            ["behaviour"] = (Strings.Menu_Behaviour_Title, LucideIconNames.Play),
            ["linuxsettings"] = (Strings.Menu_LinuxSettings_Title, LucideIconNames.Cpu),
            ["mods"] = (Strings.Menu_PresetMods_Title, LucideIconNames.BookOpen),
            ["fastflags"] = (Strings.Menu_FastFlags_Title, LucideIconNames.Flag),
            ["appearance"] = (Strings.Menu_Appearance_Title, LucideIconNames.Palette),
            ["regionselector"] = ("Server Browser", LucideIconNames.Globe),
            ["globalsettings"] = (Strings.Menu_GlobalSettings_Title, LucideIconNames.PenLine),
            ["shortcuts"] = (Strings.Common_Shortcuts, LucideIconNames.Link2),
            ["quickplay"] = (Strings.Menu_QuickPlay_Title, LucideIconNames.Gamepad2),
            ["channels"] = (Strings.Common_Deployment, LucideIconNames.HardDriveUpload),
            ["versions"] = ("Versions Manager", LucideIconNames.Layers),
            ["banasync"] = ("BanAsync", LucideIconNames.Shield),
            ["hwidspoofer"] = ("HWID Spoofer", LucideIconNames.FingerprintPattern),
            ["multiinstance"] = ("Multi Instance", LucideIconNames.Copy),
            ["vipserver"] = ("VIP Server", LucideIconNames.Crown),
            ["news"] = ("News", LucideIconNames.Newspaper),
        };

        private void UpdatePageView(object? viewModel)
        {
            SaveCurrentPage();

            var pageControl = this.FindControl<TransitioningContentControl>("PageContentControl");
            if (pageControl == null || viewModel == null) return;

            bool shouldReinitialize = viewModel is AppearanceViewModel || viewModel is ModsViewModel;

            string pageTag = _viewModel?.SelectedPage ?? "";
            Control? view = null;

            if (!shouldReinitialize && !string.IsNullOrEmpty(pageTag) && _cachedPages.TryGetValue(pageTag, out var cachedView))
            {
                view = cachedView;
            }
            else
            {
                view = ResolveViewForViewModel(viewModel);

                if (view != null && !shouldReinitialize && !string.IsNullOrEmpty(pageTag))
                {
                    _cachedPages[pageTag] = view;
                }
            }

            if (view != null)
            {
                view.DataContext = viewModel;
                pageControl.Content = view;

                Dispatcher.UIThread.Post(() =>
                {
                    if (!string.IsNullOrEmpty(pageTag) && _pageInfo.TryGetValue(pageTag, out var info))
                    {
                        IndexPage(view, pageTag, info.Title, info.Icon);
                    }

                    if (_pendingSearchScrollItem != null)
                    {
                        Dispatcher.UIThread.Post(() =>
                        {
                            ScrollToSearchItem(_pendingSearchScrollItem);
                            _pendingSearchScrollItem = null;
                        }, DispatcherPriority.Render);
                    }
                }, DispatcherPriority.Background);
            }
        }

        private void UpdateSelectedRailItem(string selectedPage)
        {
            string mapped = selectedPage switch
            {
                "home" => "home",
                "quickplay" => "quickplay",
                "mods" => "mods",
                "custommods" => "custommods",
                "tools" or "integrations" or "behaviour" or "linuxsettings"
                    or "fastflags" or "appearance" or "regionselector" or "globalsettings"
                    or "shortcuts" or "channels" or "versions" or "banasync" or "hwidspoofer"
                    or "multiinstance" or "vipserver" or "news" => "tools",
                _ => selectedPage
            };

            foreach (var item in _railItems)
            {
                if (item.IsSeparator)
                    continue;
                item.IsSelected = item.Tag == mapped;
            }
        }

        private static Control? ResolveViewForViewModel(object viewModel)
        {
            var viewModelName = viewModel.GetType().Name;
            var viewName = viewModelName.Replace("ViewModel", "");

            var viewTypeNames = new[]
            {
                $"Froststrap.UI.Elements.Settings.Pages.GlobalSettings.{viewName}",
                $"Froststrap.UI.Elements.Settings.Pages.FastFlags.{viewName}",
                $"Froststrap.UI.Elements.Settings.Pages.Mods.{viewName}Page",
                $"Froststrap.UI.Elements.Settings.Pages.{viewName}Page",
                $"Froststrap.UI.Elements.Settings.Pages.{viewName}",
                $"Froststrap.UI.Elements.Settings.{viewName}Page",
                $"Froststrap.UI.Elements.Settings.{viewName}"
            };

            foreach (var viewTypeName in viewTypeNames)
            {
                var viewType = Type.GetType(viewTypeName) ??
                               System.Reflection.Assembly.GetExecutingAssembly().GetType(viewTypeName);

                if (viewType != null && typeof(Control).IsAssignableFrom(viewType))
                {
                    try
                    {
                        return Activator.CreateInstance(viewType) as Control;
                    }
                    catch (Exception ex)
                    {
                        App.Logger.WriteLine("MainWindow", $"Failed to create view {viewTypeName}: {ex.Message}");
                    }
                }
            }

            return null;
        }

        public void LoadState()
        {
            var screen = Screens.Primary?.Bounds;
            if (screen != null)
            {
                if (State.Left > screen.Value.Width) State.Left = 0;
                if (State.Top > screen.Value.Height) State.Top = 0;
            }

            if (State.Width > 0) this.Width = State.Width;
            if (State.Height > 0) this.Height = State.Height;

            if (State.Left > 0 && State.Top > 0)
            {
                this.WindowStartupLocation = WindowStartupLocation.Manual;
                this.Position = new PixelPoint((int)State.Left, (int)State.Top);
            }
        }

        private void ShowSaveNotification()
        {
            ShowNotification(
                Strings.Menu_SettingsSaved_Title,
                Strings.Menu_SettingsSaved_Message,
                FAInfoBarSeverity.Success,
                3000);
        }

        private async void ShowAlreadyRunningNotification()
        {
            await Task.Delay(500);
            ShowNotification(
                Strings.Menu_AlreadyRunning_Title,
                Strings.Menu_AlreadyRunning_Caption,
                FAInfoBarSeverity.Warning,
                5000);
        }

        public static void ShowGlobalNotification(string title, string subtitle, FAInfoBarSeverity type, int timeout = 3000, LucideIconNames? icon = null)
        {
            Dispatcher.UIThread.Post(() => Instance?.ShowNotification(title, subtitle, type, timeout, icon));
        }

        public void ShowNotification(string title, string subtitle, FAInfoBarSeverity type, int timeout, LucideIconNames? customIcon = null)
        {
            var notificationPanel = this.FindControl<Panel>("NotificationPanel");
            if (notificationPanel == null) return;

            if (_isAnimatingOut)
            {
                Task.Run(async () =>
                {
                    while (_isAnimatingOut)
                    {
                        await Task.Delay(50);
                    }
                    Dispatcher.UIThread.Post(() => ShowNotification(title, subtitle, type, timeout, customIcon));
                });
                return;
            }

            _notificationCts?.Cancel();
            _notificationCts?.Dispose();
            _notificationCts = new CancellationTokenSource();
            var token = _notificationCts.Token;

            if (_currentNotification != null && notificationPanel.Children.Contains(_currentNotification))
            {
                _isAnimatingOut = true;
                var oldNotification = _currentNotification;

                oldNotification.Opacity = 0;
                oldNotification.RenderTransform = new TranslateTransform(0, 40);

                Task.Run(async () =>
                {
                    await Task.Delay(350);
                    Dispatcher.UIThread.Post(() =>
                    {
                        if (notificationPanel.Children.Contains(oldNotification))
                        {
                            notificationPanel.Children.Remove(oldNotification);
                        }
                        _isAnimatingOut = false;
                        _currentNotification = null;

                        ShowNotificationInternal(title, subtitle, type, timeout, customIcon);
                    });
                });
                return;
            }

            ShowNotificationInternal(title, subtitle, type, timeout, customIcon);
        }

        private void ShowNotificationInternal(string title, string subtitle, FAInfoBarSeverity type, int timeout, LucideIconNames? customIcon = null)
        {
            var notificationPanel = this.FindControl<Panel>("NotificationPanel");
            if (notificationPanel == null) return;

            var accentColor = type == FAInfoBarSeverity.Success ? "#E11D48" : "#9F1239";

            var contentGrid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
                Margin = new Thickness(0)
            };

            var icon = new Image
            {
                Width = 36,
                Height = 36,
                Source = new Bitmap(AssetLoader.Open(new Uri("avares://Spectre/SpectreMark.png"))),
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                Margin = new Thickness(16, 0, 12, 0)
            };
            Grid.SetColumn(icon, 0);
            contentGrid.Children.Add(icon);

            var textPanel = new StackPanel { VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, Spacing = 2 };

            var titleText = new TextBlock { Text = title, FontWeight = FontWeight.SemiBold, FontSize = 14, Foreground = Brushes.White };
            var subtitleText = new TextBlock { Text = subtitle, FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.White };

            textPanel.Children.Add(titleText);
            textPanel.Children.Add(subtitleText);
            Grid.SetColumn(textPanel, 1);
            contentGrid.Children.Add(textPanel);

            var closeButton = new IconButton
            {
                Icon = LucideIconNames.X,
                IconSize = 16,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(8, 4, 8, 4),
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                Width = 32,
                Height = 32,
                Margin = new Thickness(0, 0, 12, 0)
            };

            closeButton.Bind(IconButton.ForegroundProperty, new DynamicResourceExtension("TextFillColorSecondaryBrush"));

            Grid.SetColumn(closeButton, 2);
            contentGrid.Children.Add(closeButton);

            var notification = new Border
            {
                BorderBrush = new SolidColorBrush(Color.Parse(accentColor)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(0, 12, 0, 12),
                Margin = new Thickness(200, 0, 200, 40),
                MinWidth = 350,
                Height = 80,
                CornerRadius = new CornerRadius(6),
                Opacity = 0,
                RenderTransform = new TranslateTransform(0, 40),
                Child = contentGrid,
                BoxShadow = new BoxShadows(new BoxShadow { Blur = 10, OffsetY = 4, Color = Color.Parse("#40000000") }),
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch
            };

            notification.Bind(Border.BackgroundProperty, new DynamicResourceExtension("NotificationBackgroundColor"));

            notification.Transitions =
            [
                new TransformOperationsTransition { Property = Border.RenderTransformProperty, Duration = TimeSpan.FromMilliseconds(350), Easing = new QuarticEaseOut() },
                new DoubleTransition { Property = Border.OpacityProperty, Duration = TimeSpan.FromMilliseconds(250) }
            ];

            async void Dismiss()
            {
                if (_notificationCts?.Token.IsCancellationRequested ?? false) return;
                if (!notificationPanel.Children.Contains(notification)) return;
                notification.Opacity = 0;
                notification.RenderTransform = new TranslateTransform(0, 40);
                await Task.Delay(350);
                if (notificationPanel.Children.Contains(notification))
                {
                    notificationPanel.Children.Remove(notification);
                }
                if (_currentNotification == notification)
                {
                    _currentNotification = null;
                }
            }

            closeButton.Click += (s, e) =>
            {
                e.Handled = true;
                Dismiss();
            };

            notification.PointerPressed += (s, e) =>
            {
                if (e.Source is IconButton) return;
                Dismiss();
            };

            _currentNotification = notification;
            notificationPanel.Children.Add(notification);

            Dispatcher.UIThread.InvokeAsync(async () =>
            {
                if (_notificationCts?.Token.IsCancellationRequested ?? false) return;
                await Task.Delay(50);
                if (_notificationCts?.Token.IsCancellationRequested ?? false) return;
                notification.Opacity = 1;
                notification.RenderTransform = new TranslateTransform(0, 0);

                await Task.Delay(timeout);
                if (!(_notificationCts?.Token.IsCancellationRequested ?? false))
                {
                    Dismiss();
                }
            });
        }

        public void ShowLoading(string message = "Loading...")
        {
            var loadingOverlay = this.FindControl<Grid>("LoadingOverlay");
            var loadingText = this.FindControl<TextBlock>("LoadingOverlayText");

            if (loadingOverlay != null && loadingText != null)
            {
                loadingText.Text = message;
                loadingOverlay.IsVisible = true;
            }
        }

        public void HideLoading()
        {
            var loadingOverlay = this.FindControl<Grid>("LoadingOverlay");
            loadingOverlay?.IsVisible = false;
        }

        private void AttachTitleBarButtons()
        {
            var minimizeButton = this.FindControl<IconButton>("PART_MinimizeButton");
            var maximizeButton = this.FindControl<IconButton>("PART_MaximizeButton");
            var closeButton = this.FindControl<IconButton>("PART_CloseButton");

            minimizeButton?.Click += (s, e) =>
            {
                this.WindowState = Avalonia.Controls.WindowState.Minimized;
            };

            maximizeButton?.Click += (s, e) =>
            {
                this.WindowState = this.WindowState == Avalonia.Controls.WindowState.Maximized
                    ? Avalonia.Controls.WindowState.Normal
                    : Avalonia.Controls.WindowState.Maximized;
            };

            closeButton?.Click += (s, e) =>
            {
                this.Close();
            };
        }

        private SearchIndexBuilder? _searchIndexBuilder;

        private void BuildSearchIndex()
        {
            if (_viewModel == null) return;

            _searchIndexBuilder = new SearchIndexBuilder();

            var pages = new List<(string PageTag, string PageTitle, object PageViewModel)>
            {
                ("integrations", Strings.Menu_Integrations_Title, new IntegrationsViewModel()),
                ("behaviour", Strings.Menu_Behaviour_Title, new BehaviourViewModel()),
                ("linuxsettings", Strings.Menu_LinuxSettings_Title, new LinuxSettingsViewModel()),
                ("mods", Strings.Menu_PresetMods_Title, new ModsPresetsViewModel()),
                ("fastflags", Strings.Menu_FastFlags_Title, new FastFlagsViewModel()),
                ("appearance", Strings.Menu_Appearance_Title, new AppearanceViewModel()),
                ("regionselector", "Server Browser", new RegionSelectorViewModel()),
                ("globalsettings", Strings.Menu_GlobalSettings_Title, new GlobalSettingsViewModel()),
                ("shortcuts", Strings.Common_Shortcuts, new ShortcutsViewModel()),
                ("quickplay", Strings.Menu_QuickPlay_Title, new QuickPlayViewModel()),
                ("channels", Strings.Common_Deployment, new ChannelViewModel()),
            };

            if (!_viewModel.GBSEnabled)
                pages.RemoveAll(p => p.PageTag == "globalsettings");

            var searchIndex = _searchIndexBuilder.BuildIndex(pages);

            var navigationActions = new Dictionary<string, Action>
            {
                { "integrations", () => _viewModel.NavigateToIntegrationsCommand.Execute(null) },
                { "behaviour", () => _viewModel.NavigateToBehaviourCommand.Execute(null) },
                { "linuxsettings", () => _viewModel.NavigateToLinuxSettingsCommand.Execute(null) },
                { "mods", () => _viewModel.NavigateToPresetModsCommand.Execute(null) },
                { "fastflags", () => _viewModel.NavigateToFastFlagsCommand.Execute(null) },
                { "appearance", () => _viewModel.NavigateToAppearanceCommand.Execute(null) },
                { "regionselector", () => _viewModel.NavigateToRegionSelectorCommand.Execute(null) },
                { "globalsettings", () => _viewModel.NavigateToGlobalSettingsCommand.Execute(null) },
                { "shortcuts", () => _viewModel.NavigateToShortcutsCommand.Execute(null) },
                { "quickplay", () => _viewModel.NavigateToQuickPlayCommand.Execute(null) },
                { "channels", () => _viewModel.NavigateToChannelsCommand.Execute(null) },
            };

            foreach (var item in searchIndex)
            {
                if (item.PageTag != null && navigationActions.TryGetValue(item.PageTag, out var action))
                {
                    item.NavigateAction = action;
                }
            }

            _viewModel.SearchBar.SetSearchIndex([]);

            PreIndexPages(pages);
        }

        private async void PreIndexPages(List<(string PageTag, string PageTitle, object PageViewModel)> pages)
        {
            var stagingArea = this.FindControl<Border>("OffscreenIndexingCanvas");
            if (stagingArea == null)
            {
                App.Logger.WriteLine("MainWindow::PreIndexPages", "OffscreenIndexingCanvas not found, skipping pre-index");
                return;
            }

            stagingArea.IsVisible = true;

            foreach (var (pageTag, pageTitle, pageViewModel) in pages)
            {
                try
                {
                    var view = ResolveViewForViewModel(pageViewModel);
                    if (view == null) continue;

                    view.DataContext = pageViewModel;
                    stagingArea.Child = view;

                    await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
                    await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);

                    var (_, icon) = _pageInfo[pageTag];
                    IndexPage(view, pageTag, pageTitle, icon);

                    stagingArea.Child = null;

                    // Small yield between pages to keep the UI responsive
                    await Task.Delay(30);
                }
                catch (Exception ex)
                {
                    App.Logger.WriteLine("MainWindow::PreIndexPages", $"Error pre-indexing page {pageTag}: {ex.Message}");
                }
            }

            stagingArea.IsVisible = false;
        }

        private void IndexPage(Control pageView, string pageTag, string pageTitle, LucideIconNames pageIcon)
        {
            if (_viewModel == null || _searchIndexBuilder == null) return;

            try
            {
                var addedItems = _searchIndexBuilder.ScanRenderedPageForElements(pageView, pageTag);

                if (addedItems.Count > 0)
                {
                    foreach (var item in addedItems)
                    {
                        item.PageName = pageTitle;
                        item.IconSymbol = pageIcon;
                    }

                    var hiddenControlHeaders = pageView.GetVisualDescendants()
                        .Where(c => !c.IsVisible)
                        .Select(c => {
                            if (c is OptionControl oc) return oc.Header?.ToString();
                            if (c is CardExpander ce) return ce.Header?.ToString();
                            if (c is CardAction ca) return ca.Header?.ToString();
                            if (c is TextBlock tb) return tb.Text;
                            return null;
                        })
                        .Where(name => !string.IsNullOrEmpty(name))
                        .ToHashSet();

                    var filteredItems = addedItems
                        .Where(item => !hiddenControlHeaders.Contains(item.DisplayName))
                        .ToList();

                    if (filteredItems.Count > 0)
                    {
                        var currentIndex = _viewModel.SearchBar.GetSearchIndex();
                        currentIndex.AddRange(filteredItems);
                    }
                }
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine("MainWindow::IndexPage",
                    $"Error scanning page {pageTag}: {ex.Message}");
            }
        }

        private void ScrollToSearchItem(SearchBarItem item)
        {
            try
            {
                var pageControl = this.FindControl<TransitioningContentControl>("PageContentControl");
                if (pageControl?.Content is not Control pageView) return;

                if (!string.IsNullOrWhiteSpace(item.ParentSectionName))
                {
                    var parentExpander = pageView.GetVisualDescendants()
                        .OfType<CardExpander>()
                        .FirstOrDefault(ce => (ce.Header as string) == item.ParentSectionName);

                    parentExpander?.IsExpanded = true;
                }

                Control? targetControl = null;

                switch (item.Category)
                {
                    case "Section":
                        targetControl = pageView.GetVisualDescendants()
                            .OfType<CardExpander>()
                            .FirstOrDefault(ce => (ce.Header as string) == item.DisplayName);
                        break;

                    case "Setting":
                        targetControl = pageView.GetVisualDescendants()
                            .OfType<OptionControl>()
                            .FirstOrDefault(oc => oc.Header == item.DisplayName);
                        break;

                    case "Action":
                        targetControl = pageView.GetVisualDescendants()
                            .OfType<CardAction>()
                            .FirstOrDefault(ca => (ca.Content as string) == item.DisplayName);
                        break;

                    case "Label":
                        targetControl = pageView.GetVisualDescendants()
                            .OfType<TextBlock>()
                            .FirstOrDefault(tb => tb.Text == item.DisplayName);
                        break;
                }

                targetControl?.BringIntoView();
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine("MainWindow::ScrollToSearchItem",
                    $"Error scrolling to item: {ex.Message}");
            }
        }

        private void SaveCurrentPage()
        {
            if (_viewModel?.CurrentPage != null)
            {
                App.State.Prop.LastPage = _viewModel.CurrentPage.GetType().FullName;
                App.State.SaveSetting("LastPage");
            }
        }

        #region Event Handlers

        private async void MainWindow_Closing(object? sender, CancelEventArgs e)
        {
            if (MainWindowViewModel.HasUnsavedChanges)
            {
                e.Cancel = true;

                var result = await Frontend.ShowMessageBox(
                    Strings.Menu_UnsavedChangesPrompt,
                    MessageBoxImage.Warning,
                    MessageBoxButton.YesNoCancel
                );

                if (result == MessageBoxResult.Yes)
                    _viewModel?.SaveSettings();
                else if (result == MessageBoxResult.Cancel)
                    return;

                this.Closing -= MainWindow_Closing;
                this.Close();
                return;
            }

            State.Width = this.Width;
            State.Height = this.Height;
            State.Left = this.Position.X;
            State.Top = this.Position.Y;

            SaveCurrentPage();
        }

        private void MainWindow_Closed(object? sender, EventArgs e)
        {
            if (App.LaunchSettings.TestModeFlag.Active)
                LaunchHandler.LaunchRoblox(LaunchMode.Player);
            else
                App.SoftTerminate();

            App.Logger.WriteLine("MainWindow", "Settings window closed");
        }

        #endregion
    }
}