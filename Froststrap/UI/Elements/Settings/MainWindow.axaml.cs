using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.LogicalTree;
using FluentAvalonia.UI.Controls;
using Froststrap.UI.Elements.Controls;
using Froststrap.UI.Utility;
using Froststrap.UI.ViewModels;
using Froststrap.UI.ViewModels.Settings;
using Froststrap.UI.ViewModels.Settings.Mods;
using LucideAvalonia.Enum;
using System.ComponentModel;
using ThemeManager = Froststrap.Utility.ThemeManager;

namespace Froststrap.UI.Elements.Settings
{
    public partial class MainWindow : Base.AvaloniaWindow
    {
        public static MainWindow? Instance { get; private set; }

        private static Models.Persistable.WindowState State => App.State.Prop.SettingsWindow;
        private readonly MainWindowViewModel? _viewModel;

        private Border? _currentNotification;
        private CancellationTokenSource? _notificationCts;
        private bool _isAnimatingOut = false;

        private readonly Dictionary<string, Control> _cachedPages = [];

        public MainWindow()
        {
            Instance = this;
            InitializeComponent();

            BackgroundQuickList.ItemsSource = Froststrap.Utility.SceneCatalog.All;

            LinuxNavItem.IsVisible = OperatingSystem.IsLinux();
            PrivacySection.IsVisible = OperatingSystem.IsWindows();

            App.Cookies.StateChanged += OnCookieStateChanged;
            ThemeManager.ThemeChanged += UpdateBackdrop;
            ThemeManager.BackdropChanged += UpdateBackdrop;
            Closed += (_, _) =>
            {
                App.Cookies.StateChanged -= OnCookieStateChanged;
                ThemeManager.ThemeChanged -= UpdateBackdrop;
                ThemeManager.BackdropChanged -= UpdateBackdrop;
            };
            UpdateCookieStatus();
            UpdateBackdrop();

            KeyDown += MainWindow_KeyDown;
        }

        public MainWindow(bool showAlreadyRunningWarning) : this()
        {
            _viewModel = new MainWindowViewModel();
            DataContext = _viewModel;

            _viewModel.RequestSaveNoticeEvent += (_, _) => ShowSaveNotification();
            _viewModel.RequestCloseWindowEvent += (_, _) => Close();
            _viewModel.SearchBar.SearchResultSelected += (_, item) => OnSearchResultSelected(item);

            App.Logger.WriteLine("MainWindow", "Initializing settings window");

            GbsNavItem.IsEnabled = _viewModel.GBSEnabled;
            GbsNavItem.Opacity = _viewModel.GBSEnabled ? 1 : 0.5;

            if (showAlreadyRunningWarning)
                ShowAlreadyRunningNotification();

            LoadState();

            _viewModel.PropertyChanged += OnViewModelPropertyChanged;

            this.Closing += MainWindow_Closing;
            this.Closed += MainWindow_Closed;

            UpdatePageView(_viewModel.CurrentPage);
            UpdateSelectedNav(_viewModel.SelectedPage);

            Dispatcher.UIThread.Post(() =>
            {
                AttachTitleBarButtons();
                BuildSearchIndex();
                ApplyLaunchPage();
            }, DispatcherPriority.Loaded);
        }

        private void OnCookieStateChanged(object? sender, CookieState state) => Dispatcher.UIThread.Post(UpdateCookieStatus);

        private void UpdateCookieStatus()
        {
            (string text, string brush) = App.Cookies.State switch
            {
                CookieState.Success => ("Signed in with your Roblox session", "WbSuccessBrush"),
                CookieState.NotAllowed => ("Cookie access is off", "WbFgFaintBrush"),
                CookieState.NotFound => ("No Roblox session found", "WbWarningBrush"),
                CookieState.Invalid => ("Roblox session expired", "WbWarningBrush"),
                CookieState.Failed => ("Couldn't read the Roblox session", "WbDangerBrush"),
                _ => (App.Settings.Prop.AllowCookieAccess ? "Loading Roblox session..." : "Cookie access is off", "WbFgFaintBrush")
            };

            CookieStatusText.Text = text;
            CookieStatusDot.Bind(Avalonia.Controls.Shapes.Shape.FillProperty, new DynamicResourceExtension(brush));

            var user = App.Cookies.State == CookieState.Success ? App.Cookies.User : null;
            SessionNameText.Text = user is null ? "Roblox session" : user.DisplayName;
            if (user is null)
                SessionAvatar.Source = null;
            else
                _ = LoadSessionAvatarAsync(user.Id);
        }

        private async Task LoadSessionAvatarAsync(long userId)
        {
            try
            {
                var urls = await Thumbnails.GetThumbnailUrlsAsync(
                    [new ThumbnailRequest { TargetId = (ulong)userId, Type = ThumbnailType.AvatarHeadShot, Size = "150x150", IsCircular = true }],
                    CancellationToken.None);

                if (urls.FirstOrDefault() is not { Length: > 0 } url)
                    return;

                var bitmap = await Froststrap.UI.Converters.UrlToBitmapConverter.GetBitmapFromCacheOrDownloadAsync(url);
                if (bitmap is not null && App.Cookies.User?.Id == userId)
                    SessionAvatar.Source = bitmap;
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine("MainWindow::LoadSessionAvatar", ex.Message);
            }
        }

        private void UpdateBackdrop()
        {
            var settings = App.Settings?.Prop;
            if (settings is null)
                return;

            Backdrop.ApplyFromSettings();
            if (SpectreLogo.CreateBrandIcon() is { } icon)
                Icon = icon;
        }

        private void MainWindow_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.K && e.KeyModifiers.HasFlag(KeyModifiers.Control))
            {
                TitleSearchBar.FocusSearch();
                e.Handled = true;
            }
        }

        private void ApplyLaunchPage()
        {
            string? page = App.LaunchSettings?.PageFlag.Data;
            if (string.IsNullOrWhiteSpace(page))
                return;

            string tag = page.Trim().ToLowerInvariant() switch
            {
                "games" => "quickplay",
                "library" or "presets" => "mods",
                "mymods" => "custommods",
                "settings" => "integrations",
                var other => other
            };

            if (tag != _viewModel?.SelectedPage)
                GetNavigationAction(tag)?.Invoke();
        }

        #region Navigation

        private void Nav_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: string tag } || _viewModel is null)
                return;

            if (tag != "about" && _viewModel.SelectedPage == tag)
                return;

            if (tag != "about")
                SaveCurrentPage();

            _viewModel.NavigateTo(tag);
        }

        private static string NavTagFor(string page) => page switch
        {
            "fastflageditor" => "fastflags",
            "globalsettingseditor" => "globalsettings",
            _ => page
        };

        private void UpdateSelectedNav(string selectedPage)
        {
            string tag = NavTagFor(selectedPage);

            foreach (var button in NavPanel.GetLogicalDescendants().OfType<NavButton>())
                button.Classes.Set("selected", button.Tag as string == tag);
        }

        private void QuickBackground_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: string id })
                return;

            App.Settings.Prop.BackgroundId = id;
            ThemeManager.ApplyFromSettings();
        }

        private void OpenAppearance_Click(object? sender, RoutedEventArgs e)
            => _viewModel?.NavigateToAppearanceCommand.Execute(null);

        #endregion

        private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (_viewModel == null) return;

            if (e.PropertyName == nameof(MainWindowViewModel.CurrentPage))
            {
                UpdatePageView(_viewModel.CurrentPage);
            }
            else if (e.PropertyName == nameof(MainWindowViewModel.SelectedPage))
            {
                UpdateSelectedNav(_viewModel.SelectedPage);
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
            var command = _viewModel?.GetNavigationCommand(pageTag);
            return command is null ? null : () => command.Execute(null);
        }

        private readonly Dictionary<string, (string Title, LucideIconNames Icon)> _pageInfo = new()
        {
            ["integrations"] = (Strings.Menu_Integrations_Title, LucideIconNames.Plug),
            ["behaviour"] = (Strings.Menu_Behaviour_Title, LucideIconNames.Rocket),
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

        #region Notifications

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

            if (_currentNotification != null && notificationPanel.Children.Contains(_currentNotification))
            {
                _isAnimatingOut = true;
                var oldNotification = _currentNotification;

                oldNotification.Opacity = 0;
                oldNotification.RenderTransform = new TranslateTransform(0, 24);

                Task.Run(async () =>
                {
                    await Task.Delay(200);
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

            (string toneKey, LucideIconNames defaultIcon) = type switch
            {
                FAInfoBarSeverity.Success => ("WbSuccessBrush", LucideIconNames.CircleCheck),
                FAInfoBarSeverity.Warning => ("WbWarningBrush", LucideIconNames.TriangleAlert),
                FAInfoBarSeverity.Error => ("WbDangerBrush", LucideIconNames.CircleX),
                _ => ("WbAccentBrush", LucideIconNames.Info)
            };

            var contentGrid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
                ColumnSpacing = 12
            };

            var icon = new LucideAvalonia.Lucide
            {
                Icon = customIcon ?? defaultIcon,
                Width = 16,
                Height = 16,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
            };
            icon.Bind(LucideAvalonia.Lucide.StrokeBrushProperty, new DynamicResourceExtension(toneKey));
            Grid.SetColumn(icon, 0);
            contentGrid.Children.Add(icon);

            var textPanel = new StackPanel { VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, Spacing = 1 };

            var titleText = new TextBlock { Text = title, FontWeight = FontWeight.SemiBold, FontSize = 13 };
            titleText.Bind(TextBlock.ForegroundProperty, new DynamicResourceExtension("WbFgBrightBrush"));
            var subtitleText = new TextBlock { Text = subtitle, FontSize = 12, TextWrapping = TextWrapping.Wrap };
            subtitleText.Bind(TextBlock.ForegroundProperty, new DynamicResourceExtension("WbFgMutedBrush"));

            textPanel.Children.Add(titleText);
            textPanel.Children.Add(subtitleText);
            Grid.SetColumn(textPanel, 1);
            contentGrid.Children.Add(textPanel);

            var closeButton = new Button
            {
                Classes = { "wb-icon" },
                Content = new LucideAvalonia.Lucide { Icon = LucideIconNames.X },
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
            };
            Grid.SetColumn(closeButton, 2);
            contentGrid.Children.Add(closeButton);

            var notification = new Border
            {
                BorderThickness = new Thickness(1),
                Padding = new Thickness(14, 10, 8, 10),
                MinWidth = 340,
                MaxWidth = 520,
                CornerRadius = new CornerRadius(6),
                Opacity = 0,
                RenderTransform = new TranslateTransform(0, 24),
                Child = contentGrid,
                BoxShadow = new BoxShadows(new BoxShadow { Blur = 18, OffsetY = 6, Color = Color.Parse("#40000000") }),
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center
            };

            notification.Bind(Border.BackgroundProperty, new DynamicResourceExtension("WbOverlayBrush"));
            notification.Bind(Border.BorderBrushProperty, new DynamicResourceExtension("WbLineStrongBrush"));

            notification.Transitions =
            [
                new TransformOperationsTransition { Property = Border.RenderTransformProperty, Duration = TimeSpan.FromMilliseconds(180), Easing = new CubicEaseOut() },
                new DoubleTransition { Property = Border.OpacityProperty, Duration = TimeSpan.FromMilliseconds(150) }
            ];

            async void Dismiss()
            {
                if (_notificationCts?.Token.IsCancellationRequested ?? false) return;
                if (!notificationPanel.Children.Contains(notification)) return;
                notification.Opacity = 0;
                notification.RenderTransform = new TranslateTransform(0, 24);
                await Task.Delay(200);
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
                if (e.Source is Button) return;
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

        #endregion

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

        #region Search

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

            if (!OperatingSystem.IsLinux())
                pages.RemoveAll(p => p.PageTag == "linuxsettings");

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

        #endregion

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
            Instance = null;
            App.Logger.WriteLine("MainWindow", "Settings window closed");
        }

        #endregion
    }
}
