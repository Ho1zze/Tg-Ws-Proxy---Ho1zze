using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using Color = System.Windows.Media.Color;
using FontFamily = System.Windows.Media.FontFamily;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using TextBox = System.Windows.Controls.TextBox;
using Clipboard = System.Windows.Clipboard;
using Image = System.Windows.Controls.Image;

namespace TgProxyHoizze.UI {
    public class MainWindow : Window {
        private readonly ProxySettings _settings;
        private ProxyServer _server;
        private NotifyIcon _notifyIcon;
        private DispatcherTimer _uptimeTimer;
        private bool _isExplicitExit = false;
        private bool _trayNoticeShown = false;

        // UI Controls - Bottom Floating Tabs
        private Button _tabBtnMain;
        private Button _tabBtnSettings;
        private Button _tabBtnLogs;
        private UIElement _viewMain;
        private UIElement _viewSettings;
        private UIElement _viewLogs;

        // UI Controls - Main View
        private Border _statusBadge;
        private TextBlock _statusBadgeDot;
        private TextBlock _statusBadgeText;
        private Button _btnToggleProxy;
        private TextBlock _txtServerAddress;
        private TextBlock _txtServerSecret;
        private TextBlock _txtTargetDc;
        private TextBlock _statActiveConn;
        private TextBlock _statTotalConn;
        private TextBlock _statRx;
        private TextBlock _statTx;
        private TextBlock _statUptime;
        private Button _btnCopySecret;

        // UI Controls - Settings View
        private TextBox _tbPort;
        private TextBox _tbDcIp;
        private TextBox _tbSecret;
        private CheckBox _cbAutoStart;
        private CheckBox _cbCfProxy;
        private TextBlock _settingsNotification;

        // UI Controls - Logs View
        private TextBox _tbLogs;
        private CheckBox _cbAutoScroll;
        private readonly StringBuilder _logBuffer = new StringBuilder();

        public MainWindow(ProxySettings settings, int initialTab = 0) {
            _settings = settings;
            InitializeWindow();
            BuildUI();
            InitializeTray();

            if (initialTab > 0) {
                SwitchTab(initialTab);
            }

            _server = new ProxyServer(_settings);
            _server.LogMessage += OnServerLog;
            _server.StatusChanged += OnServerStatusChanged;

            _uptimeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _uptimeTimer.Tick += (s, e) => {
                UpdateUptime();
                UpdateMetricsDisplay();
            };
            _uptimeTimer.Start();

            // Auto-start proxy on launch if enabled
            if (_settings.AutoStartProxy) {
                try {
                    _server.Start();
                } catch (Exception ex) {
                    OnServerLog("ERROR", "Ошибка автозапуска: " + ex.Message);
                }
            }
        }

        private void InitializeWindow() {
            Title = "TG PROXY Ho1zze";
            Width = 430;
            Height = 800;
            MinWidth = 410;
            MinHeight = 700;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = System.Windows.Media.Brushes.Transparent;
            Foreground = ModernTheme.TextPrimary;
            FontFamily = new FontFamily("Segoe UI, -apple-system, BlinkMacSystemFont, Tahoma, sans-serif");
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;

            try {
                if (File.Exists("app.ico")) {
                    Icon = new System.Windows.Media.Imaging.BitmapImage(new Uri(Path.GetFullPath("app.ico")));
                } else {
                    var logoSrc = ModernTheme.GetLogoSource();
                    if (logoSrc != null) {
                        Icon = logoSrc;
                    }
                }
            } catch { }

            // Minimize to tray on close
            Closing += (s, e) => {
                if (!_isExplicitExit) {
                    e.Cancel = true;
                    Hide();
                    if (!_trayNoticeShown && _notifyIcon != null) {
                        _trayNoticeShown = true;
                        try {
                            _notifyIcon.ShowBalloonTip(2000, "TG PROXY Ho1zze", "Программа свёрнута в системный трей и продолжает работать.", ToolTipIcon.Info);
                        } catch { }
                    }
                }
            };
        }

        private void BuildUI() {
            // Root window container with sleek iOS/widget dark style
            var rootBorder = new Border {
                Background = ModernTheme.BgDark,
                BorderBrush = ModernTheme.CardBorder,
                BorderThickness = new Thickness(1.5),
                CornerRadius = new CornerRadius(26)
            };

            var rootGrid = new Grid();
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(64) }); // Top Header Bar
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Content Area
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(86) }); // Floating Bottom Tab Bar & Watermark
            rootBorder.Child = rootGrid;
            Content = rootBorder;

            // 1. Top Header Bar
            var headerGrid = new Grid { Background = System.Windows.Media.Brushes.Transparent };
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Allow dragging window from header
            headerGrid.MouseDown += (s, e) => {
                if (e.LeftButton == MouseButtonState.Pressed) {
                    DragMove();
                }
            };

            // Left: Logo + Company name
            var brandStack = new StackPanel {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                Margin = new Thickness(18, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };

            var logoSrc = ModernTheme.GetLogoSource();
            if (logoSrc != null) {
                var logoImg = new Image {
                    Source = logoSrc,
                    Height = 34,
                    Stretch = Stretch.Uniform,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 10, 0)
                };
                RenderOptions.SetBitmapScalingMode(logoImg, BitmapScalingMode.HighQuality);
                brandStack.Children.Add(logoImg);
            }

            var titlesStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var titleTxt = new TextBlock {
                Text = "TG PROXY",
                FontWeight = FontWeights.Bold,
                FontSize = 14,
                Foreground = ModernTheme.TextPrimary
            };
            var subTitleTxt = new TextBlock {
                Text = "by Ho1zze",
                FontWeight = FontWeights.SemiBold,
                FontSize = 10,
                Foreground = ModernTheme.TextSecondary,
                Margin = new Thickness(0, 1, 0, 0)
            };
            titlesStack.Children.Add(titleTxt);
            titlesStack.Children.Add(subTitleTxt);
            brandStack.Children.Add(titlesStack);

            Grid.SetColumn(brandStack, 0);
            headerGrid.Children.Add(brandStack);

            // Right: Circular Window Control Buttons (Minimize, Close)
            var winBtns = new StackPanel {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 16, 0)
            };

            var btnMin = ModernTheme.CreateIconButton("—", 30, 15);
            btnMin.Margin = new Thickness(0, 0, 8, 0);
            btnMin.Click += (s, e) => { WindowState = WindowState.Minimized; };

            var btnClose = ModernTheme.CreateIconButton("✕", 30, 15);
            btnClose.Click += (s, e) => {
                Hide();
                if (!_trayNoticeShown && _notifyIcon != null) {
                    _trayNoticeShown = true;
                    try {
                        _notifyIcon.ShowBalloonTip(2000, "TG PROXY Ho1zze", "Программа свёрнута в системный трей.", ToolTipIcon.Info);
                    } catch { }
                }
            };

            winBtns.Children.Add(btnMin);
            winBtns.Children.Add(btnClose);
            Grid.SetColumn(winBtns, 1);
            headerGrid.Children.Add(winBtns);

            Grid.SetRow(headerGrid, 0);
            rootGrid.Children.Add(headerGrid);

            // 2. Content Area
            var contentArea = new Grid { Margin = new Thickness(14, 0, 14, 0) };
            _viewMain = BuildMainView();
            _viewSettings = BuildSettingsView();
            _viewLogs = BuildLogsView();

            contentArea.Children.Add(_viewMain);
            contentArea.Children.Add(_viewSettings);
            contentArea.Children.Add(_viewLogs);

            _viewMain.Visibility = Visibility.Visible;
            _viewSettings.Visibility = Visibility.Collapsed;
            _viewLogs.Visibility = Visibility.Collapsed;

            Grid.SetRow(contentArea, 1);
            rootGrid.Children.Add(contentArea);

            // 3. Floating Bottom Navigation Bar (like Photo 2) & Watermark
            var bottomArea = new StackPanel {
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(14, 0, 14, 8)
            };

            var dockContainer = new Border {
                Background = ModernTheme.DockBg,
                BorderBrush = ModernTheme.DockBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(24),
                Padding = new Thickness(4),
                Margin = new Thickness(0, 2, 0, 6),
                Height = 52
            };

            var dockGrid = new Grid();
            dockGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            dockGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            dockGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            _tabBtnMain = CreateDockTabButton("⚡ Главная", true);
            _tabBtnSettings = CreateDockTabButton("⚙️ Настройки", false);
            _tabBtnLogs = CreateDockTabButton("📜 Логи", false);

            _tabBtnMain.Click += (s, e) => SwitchTab(0);
            _tabBtnSettings.Click += (s, e) => SwitchTab(1);
            _tabBtnLogs.Click += (s, e) => SwitchTab(2);

            Grid.SetColumn(_tabBtnMain, 0);
            Grid.SetColumn(_tabBtnSettings, 1);
            Grid.SetColumn(_tabBtnLogs, 2);

            dockGrid.Children.Add(_tabBtnMain);
            dockGrid.Children.Add(_tabBtnSettings);
            dockGrid.Children.Add(_tabBtnLogs);
            dockContainer.Child = dockGrid;

            // Watermark: Created by Hoizze
            var watermark = new TextBlock {
                Text = "Created by Hoizze",
                FontSize = 10,
                FontWeight = FontWeights.Medium,
                Foreground = ModernTheme.TextMuted,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 2),
                Opacity = 0.55
            };

            bottomArea.Children.Add(dockContainer);
            bottomArea.Children.Add(watermark);

            Grid.SetRow(bottomArea, 2);
            rootGrid.Children.Add(bottomArea);
        }

        private Button CreateDockTabButton(string text, bool isActive) {
            var btn = new Button {
                Content = text,
                Height = 44,
                FontSize = 12.5,
                FontWeight = isActive ? FontWeights.SemiBold : FontWeights.Normal,
                Background = isActive ? ModernTheme.CardBgHover : System.Windows.Media.Brushes.Transparent,
                Foreground = isActive ? ModernTheme.TextPrimary : ModernTheme.TextMuted,
                BorderThickness = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand
            };

            var template = new ControlTemplate(typeof(Button));
            var borderFactory = new FrameworkElementFactory(typeof(Border));
            borderFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(20));
            borderFactory.SetValue(Border.BackgroundProperty, btn.Background);
            if (isActive) {
                borderFactory.SetValue(Border.BorderBrushProperty, ModernTheme.CardBorderHighlight);
                borderFactory.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            }

            var cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            cp.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            borderFactory.AppendChild(cp);
            template.VisualTree = borderFactory;
            btn.Template = template;

            return btn;
        }

        private void SwitchTab(int tabIndex) {
            _viewMain.Visibility = tabIndex == 0 ? Visibility.Visible : Visibility.Collapsed;
            _viewSettings.Visibility = tabIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
            _viewLogs.Visibility = tabIndex == 2 ? Visibility.Visible : Visibility.Collapsed;

            UpdateDockTabStyle(_tabBtnMain, tabIndex == 0, "⚡ Главная");
            UpdateDockTabStyle(_tabBtnSettings, tabIndex == 1, "⚙️ Настройки");
            UpdateDockTabStyle(_tabBtnLogs, tabIndex == 2, "📜 Логи");
        }

        private void UpdateDockTabStyle(Button btn, bool isActive, string text) {
            btn.Content = text;
            btn.FontWeight = isActive ? FontWeights.SemiBold : FontWeights.Normal;
            btn.Foreground = isActive ? ModernTheme.TextPrimary : ModernTheme.TextMuted;

            var template = new ControlTemplate(typeof(Button));
            var borderFactory = new FrameworkElementFactory(typeof(Border));
            borderFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(20));
            borderFactory.SetValue(Border.BackgroundProperty, isActive ? ModernTheme.CardBgHover : System.Windows.Media.Brushes.Transparent);
            if (isActive) {
                borderFactory.SetValue(Border.BorderBrushProperty, ModernTheme.CardBorderHighlight);
                borderFactory.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            } else {
                borderFactory.SetValue(Border.BorderThicknessProperty, new Thickness(0));
            }

            var cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            cp.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            borderFactory.AppendChild(cp);
            template.VisualTree = borderFactory;
            btn.Template = template;
        }

        #region Main View (Bento Cards like Photo 2)
        private UIElement BuildMainView() {
            var scrollStack = new StackPanel();

            // 1. Hero Status Card (Top Bento Card)
            var heroStack = new StackPanel();

            // Top row in Hero Card: Status Badge & DC Tag
            var heroHeaderGrid = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            heroHeaderGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            heroHeaderGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _statusBadge = new Border {
                CornerRadius = new CornerRadius(14),
                Background = ModernTheme.CardBgSecondary,
                BorderBrush = ModernTheme.CardBorder,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(10, 5, 12, 5),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center
            };
            var badgeStack = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
            _statusBadgeDot = new TextBlock { Text = "○", Foreground = ModernTheme.TextMuted, FontSize = 12, Margin = new Thickness(0, -1, 6, 0) };
            _statusBadgeText = new TextBlock { Text = "ПРОКСИ ОСТАНОВЛЕН", Foreground = ModernTheme.TextMuted, FontSize = 10.5, FontWeight = FontWeights.Bold };
            badgeStack.Children.Add(_statusBadgeDot);
            badgeStack.Children.Add(_statusBadgeText);
            _statusBadge.Child = badgeStack;
            Grid.SetColumn(_statusBadge, 0);
            heroHeaderGrid.Children.Add(_statusBadge);

            var dcTag = new Border {
                CornerRadius = new CornerRadius(12),
                Background = ModernTheme.CardBgSecondary,
                BorderBrush = ModernTheme.CardBorderSubtle,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(10, 4, 10, 4),
                VerticalAlignment = VerticalAlignment.Center
            };
            var dcTagText = new TextBlock { Text = "DC 4 • WebSocket", FontSize = 10, FontWeight = FontWeights.SemiBold, Foreground = ModernTheme.TextSecondary };
            dcTag.Child = dcTagText;
            Grid.SetColumn(dcTag, 1);
            heroHeaderGrid.Children.Add(dcTag);

            heroStack.Children.Add(heroHeaderGrid);

            // Title & Subtitle inside Hero Card
            var heroTitle = new TextBlock {
                Text = "TG PROXY Ho1zze",
                FontSize = 21,
                FontWeight = FontWeights.Bold,
                Foreground = ModernTheme.TextPrimary,
                Margin = new Thickness(0, 0, 0, 2)
            };
            var heroSubtitle = new TextBlock {
                Text = "Защищенный туннель MTProto к серверам Telegram",
                FontSize = 11.5,
                Foreground = ModernTheme.TextSecondary,
                Margin = new Thickness(0, 0, 0, 12)
            };
            heroStack.Children.Add(heroTitle);
            heroStack.Children.Add(heroSubtitle);

            // Big Power Button
            _btnToggleProxy = ModernTheme.CreateButton("▶  Запустить прокси", ModernTheme.CardBgHover, ModernTheme.TextPrimary, 40, 13, true, 12, ModernTheme.CardBorderHighlight);
            _btnToggleProxy.Click += (s, e) => {
                if (_server.IsRunning) {
                    _server.Stop();
                } else {
                    try {
                        _server.Start();
                    } catch (Exception ex) {
                        System.Windows.MessageBox.Show("Ошибка запуска прокси: " + ex.Message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            };
            heroStack.Children.Add(_btnToggleProxy);

            var heroCard = ModernTheme.CreateCard(heroStack, 14, 20);
            heroCard.Margin = new Thickness(0, 0, 0, 8);
            scrollStack.Children.Add(heroCard);

            // 2. Bento Grid 2 Columns: Quick Connect Cards (like top 2 cards in Photo 2!)
            var connectGrid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            connectGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            connectGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            connectGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // Telegram Connect Card
            var tgStack = new StackPanel();
            var tgHeader = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            var tgBadge = new TextBlock { Text = "MTProto", FontSize = 9.5, FontWeight = FontWeights.Bold, Foreground = ModernTheme.TextMuted };
            var tgIcon = new TextBlock { Text = "✈", FontSize = 12, Foreground = ModernTheme.TextSecondary, HorizontalAlignment = HorizontalAlignment.Right };
            tgHeader.Children.Add(tgBadge);
            tgHeader.Children.Add(tgIcon);
            tgStack.Children.Add(tgHeader);

            var tgTitle = new TextBlock { Text = "Telegram", FontSize = 15, FontWeight = FontWeights.Bold, Foreground = ModernTheme.TextPrimary };
            var tgSub = new TextBlock { Text = "Прямой коннект", FontSize = 10.5, Foreground = ModernTheme.TextSecondary, Margin = new Thickness(0, 1, 0, 10) };
            tgStack.Children.Add(tgTitle);
            tgStack.Children.Add(tgSub);

            var btnConnectMtproto = ModernTheme.CreateButton("🚀  Подключить", ModernTheme.CardBgSecondary, ModernTheme.TextPrimary, 32, 11, true, 10, ModernTheme.CardBorder);
            btnConnectMtproto.Click += (s, e) => {
                int port = _server != null && _server.IsRunning ? _server.ActivePort : _settings.Port;
                string sec = _settings.Secret;
                string secretParam = sec.StartsWith("dd") ? sec : ("dd" + sec);
                string link = string.Format("tg://proxy?server=127.0.0.1&port={0}&secret={1}", port, secretParam);
                try {
                    Process.Start(new ProcessStartInfo(link) { UseShellExecute = true });
                } catch {
                    Clipboard.SetText(link);
                    System.Windows.MessageBox.Show("Ссылка скопирована в буфер обмена:\n" + link, "Telegram Proxy Link", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            };
            tgStack.Children.Add(btnConnectMtproto);

            var tgCard = ModernTheme.CreateCard(tgStack, 12, 16);
            tgCard.Margin = new Thickness(0);
            Grid.SetColumn(tgCard, 0);
            connectGrid.Children.Add(tgCard);

            // SOCKS5 Connect Card
            var socksStack = new StackPanel();
            var socksHeader = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            var socksBadge = new TextBlock { Text = "SOCKS5", FontSize = 9.5, FontWeight = FontWeights.Bold, Foreground = ModernTheme.TextMuted };
            var socksIcon = new TextBlock { Text = "🛡️", FontSize = 11, Foreground = ModernTheme.TextSecondary, HorizontalAlignment = HorizontalAlignment.Right };
            socksHeader.Children.Add(socksBadge);
            socksHeader.Children.Add(socksIcon);
            socksStack.Children.Add(socksHeader);

            var socksTitle = new TextBlock { Text = "SOCKS5", FontSize = 15, FontWeight = FontWeights.Bold, Foreground = ModernTheme.TextPrimary };
            var socksSub = new TextBlock { Text = "Локальный порт", FontSize = 10.5, Foreground = ModernTheme.TextSecondary, Margin = new Thickness(0, 1, 0, 10) };
            socksStack.Children.Add(socksTitle);
            socksStack.Children.Add(socksSub);

            var btnConnectSocks = ModernTheme.CreateButton("🛡️  Подключить", ModernTheme.CardBgSecondary, ModernTheme.TextPrimary, 32, 11, true, 10, ModernTheme.CardBorder);
            btnConnectSocks.Click += (s, e) => {
                int port = _server != null && _server.IsRunning ? _server.ActivePort : _settings.Port;
                string link = string.Format("tg://socks?server=127.0.0.1&port={0}", port);
                try {
                    Process.Start(new ProcessStartInfo(link) { UseShellExecute = true });
                } catch {
                    Clipboard.SetText(link);
                    System.Windows.MessageBox.Show("Ссылка скопирована в буфер обмена:\n" + link, "SOCKS5 Proxy Link", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            };
            socksStack.Children.Add(btnConnectSocks);

            var socksCard = ModernTheme.CreateCard(socksStack, 12, 16);
            socksCard.Margin = new Thickness(0);
            Grid.SetColumn(socksCard, 2);
            connectGrid.Children.Add(socksCard);

            scrollStack.Children.Add(connectGrid);

            // 3. Bento Grid: Metrics 2x2 (like "Volume lifted" / "200 lbs" in Photo 2)
            var metricsGrid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            metricsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            metricsGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(6) });
            metricsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            metricsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            metricsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6) });
            metricsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var tileActive = CreateMetricTile("АКТИВНЫХ", "0", "соединений", out _statActiveConn);
            var tileTotal = CreateMetricTile("ВСЕГО СЕССИЙ", "0", "подключений", out _statTotalConn);
            var tileRx = CreateMetricTile("ВХОДЯЩИЙ RX", "0 KB", "загружено", out _statRx);
            var tileTx = CreateMetricTile("ИСХОДЯЩИЙ TX", "0 KB", "отправлено", out _statTx);

            Grid.SetRow(tileActive, 0);
            Grid.SetColumn(tileActive, 0);

            Grid.SetRow(tileTotal, 0);
            Grid.SetColumn(tileTotal, 2);

            Grid.SetRow(tileRx, 2);
            Grid.SetColumn(tileRx, 0);

            Grid.SetRow(tileTx, 2);
            Grid.SetColumn(tileTx, 2);

            metricsGrid.Children.Add(tileActive);
            metricsGrid.Children.Add(tileTotal);
            metricsGrid.Children.Add(tileRx);
            metricsGrid.Children.Add(tileTx);

            scrollStack.Children.Add(metricsGrid);

            // 4. Uptime Card
            var uptimeStack = new Grid();
            uptimeStack.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            uptimeStack.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var uptimeLeft = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var uptimeDot = new TextBlock { Text = "●", Foreground = ModernTheme.Green, FontSize = 10, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
            var lblUptime = new TextBlock { Text = "Время работы сервера:", FontSize = 11, Foreground = ModernTheme.TextSecondary, VerticalAlignment = VerticalAlignment.Center };
            uptimeLeft.Children.Add(uptimeDot);
            uptimeLeft.Children.Add(lblUptime);
            Grid.SetColumn(uptimeLeft, 0);
            uptimeStack.Children.Add(uptimeLeft);

            _statUptime = new TextBlock {
                Text = "00:00:00",
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                Foreground = ModernTheme.TextPrimary,
                FontFamily = new FontFamily("Consolas, Courier New, monospace"),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(_statUptime, 1);
            uptimeStack.Children.Add(_statUptime);

            var uptimeCard = ModernTheme.CreateCard(uptimeStack, 10, 14, ModernTheme.CardBgSecondary);
            uptimeCard.Margin = new Thickness(0, 0, 0, 8);
            scrollStack.Children.Add(uptimeCard);

            // 5. Connection Parameters Card
            var detailsStack = new StackPanel();
            var detailsHeader = new TextBlock {
                Text = "ПАРАМЕТРЫ СЕРВЕРА",
                FontSize = 10.5,
                FontWeight = FontWeights.Bold,
                Foreground = ModernTheme.TextMuted,
                Margin = new Thickness(0, 0, 0, 8)
            };
            detailsStack.Children.Add(detailsHeader);

            var rowAddr = CreateDetailRow("Адрес сервера:", "127.0.0.1:" + _settings.Port, out _txtServerAddress);
            detailsStack.Children.Add(rowAddr);

            var rowDc = CreateDetailRow("Дата-центр:", "DC 4 -> " + _settings.GetParsedIp(4) + ":443", out _txtTargetDc);
            detailsStack.Children.Add(rowDc);

            // Secret Row with copy button
            var secretRow = new Grid { Margin = new Thickness(0, 3, 0, 0) };
            secretRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(105) });
            secretRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            secretRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var lblSecret = new TextBlock { Text = "MTProto Secret:", Foreground = ModernTheme.TextSecondary, FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
            _txtServerSecret = new TextBlock {
                Text = _settings.Secret,
                Foreground = ModernTheme.TextPrimary,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 11,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0)
            };

            _btnCopySecret = ModernTheme.CreateButton("📋  Копия", ModernTheme.CardBgSecondary, ModernTheme.TextPrimary, 26, 10.5, false, 8, ModernTheme.CardBorder);
            _btnCopySecret.Padding = new Thickness(8, 0, 8, 0);
            _btnCopySecret.Click += (s, e) => {
                Clipboard.SetText(_settings.Secret);
                _btnCopySecret.Content = "✓ Ок!";
                var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
                t.Tick += (st, et) => {
                    _btnCopySecret.Content = "📋  Копия";
                    t.Stop();
                };
                t.Start();
            };

            Grid.SetColumn(lblSecret, 0);
            Grid.SetColumn(_txtServerSecret, 1);
            Grid.SetColumn(_btnCopySecret, 2);
            secretRow.Children.Add(lblSecret);
            secretRow.Children.Add(_txtServerSecret);
            secretRow.Children.Add(_btnCopySecret);
            detailsStack.Children.Add(secretRow);

            var detailsCard = ModernTheme.CreateCard(detailsStack, 12, 16);
            detailsCard.Margin = new Thickness(0, 0, 0, 6);
            scrollStack.Children.Add(detailsCard);

            return ModernTheme.WrapInScrollViewer(scrollStack);
        }

        private Border CreateMetricTile(string label, string initVal, string sub, out TextBlock valBlock) {
            var stack = new StackPanel();
            var lbl = new TextBlock { Text = label, FontSize = 9.5, Foreground = ModernTheme.TextMuted, FontWeight = FontWeights.Bold };
            valBlock = new TextBlock {
                Text = initVal,
                FontSize = 18,
                FontWeight = FontWeights.Bold,
                Foreground = ModernTheme.TextPrimary,
                Margin = new Thickness(0, 3, 0, 1)
            };
            var subText = new TextBlock { Text = sub, FontSize = 9.5, Foreground = ModernTheme.TextMuted };

            stack.Children.Add(lbl);
            stack.Children.Add(valBlock);
            stack.Children.Add(subText);

            var b = new Border {
                Background = ModernTheme.CardBgSecondary,
                BorderBrush = ModernTheme.CardBorderSubtle,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(12, 8, 12, 8),
                Child = stack
            };
            return b;
        }

        private Grid CreateDetailRow(string label, string val, out TextBlock valBlock) {
            var g = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(105) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var lbl = new TextBlock { Text = label, Foreground = ModernTheme.TextSecondary, FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
            valBlock = new TextBlock { Text = val, Foreground = ModernTheme.TextPrimary, FontSize = 11, VerticalAlignment = VerticalAlignment.Center };

            Grid.SetColumn(lbl, 0);
            Grid.SetColumn(valBlock, 1);
            g.Children.Add(lbl);
            g.Children.Add(valBlock);
            return g;
        }
        #endregion

        #region Settings View (Bento Cards like Photo 2)
        private UIElement BuildSettingsView() {
            var scrollStack = new StackPanel();

            // 1. Connection Card
            var connStack = new StackPanel();
            var connHeader = new TextBlock {
                Text = "СЕТЕВЫЕ НАСТРОЙКИ",
                FontSize = 10.5,
                FontWeight = FontWeights.Bold,
                Foreground = ModernTheme.TextMuted,
                Margin = new Thickness(0, 0, 0, 10)
            };
            connStack.Children.Add(connHeader);

            // Port
            var lblPort = new TextBlock { Text = "Локальный порт (Port):", Foreground = ModernTheme.TextPrimary, FontSize = 12, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) };
            _tbPort = ModernTheme.CreateTextBox(_settings.Port.ToString(), 38, 12);
            var hintPort = new TextBlock { Text = "Порт подключения Telegram Desktop (по умолчанию: 1443)", Foreground = ModernTheme.TextMuted, FontSize = 10.5, Margin = new Thickness(0, 3, 0, 10) };
            connStack.Children.Add(lblPort);
            connStack.Children.Add(_tbPort);
            connStack.Children.Add(hintPort);

            // DC IP
            var lblDc = new TextBlock { Text = "DC IP (Дата-центр Telegram):", Foreground = ModernTheme.TextPrimary, FontSize = 12, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) };
            _tbDcIp = ModernTheme.CreateTextBox(_settings.DcIp, 38, 12);
            var hintDc = new TextBlock { Text = "DC 4 (4:149.154.167.220) обеспечивает быструю загрузку фото и видео.", Foreground = ModernTheme.TextMuted, FontSize = 10.5, Margin = new Thickness(0, 3, 0, 4) };
            connStack.Children.Add(lblDc);
            connStack.Children.Add(_tbDcIp);
            connStack.Children.Add(hintDc);

            var connCard = ModernTheme.CreateCard(connStack, 14, 18);
            connCard.Margin = new Thickness(0, 0, 0, 8);
            scrollStack.Children.Add(connCard);

            // 2. Secret Card
            var secretStack = new StackPanel();
            var secretHeader = new TextBlock {
                Text = "СЕКРЕТНЫЙ КЛЮЧ",
                FontSize = 10.5,
                FontWeight = FontWeights.Bold,
                Foreground = ModernTheme.TextMuted,
                Margin = new Thickness(0, 0, 0, 10)
            };
            secretStack.Children.Add(secretHeader);

            var lblSecret = new TextBlock { Text = "MTProto Secret:", Foreground = ModernTheme.TextPrimary, FontSize = 12, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) };
            var secretGrid = new Grid();
            secretGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            secretGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _tbSecret = ModernTheme.CreateTextBox(_settings.Secret, 38, 12);
            var btnGenSecret = ModernTheme.CreateButton("🎲  Сгенерировать", ModernTheme.CardBgSecondary, ModernTheme.TextPrimary, 38, 11, true, 12, ModernTheme.CardBorder);
            btnGenSecret.Margin = new Thickness(8, 0, 0, 0);
            btnGenSecret.Click += (s, e) => {
                _tbSecret.Text = SettingsManager.GenerateRandomSecret();
            };

            Grid.SetColumn(_tbSecret, 0);
            Grid.SetColumn(btnGenSecret, 1);
            secretGrid.Children.Add(_tbSecret);
            secretGrid.Children.Add(btnGenSecret);

            var hintSecret = new TextBlock { Text = "32 hex-символа для криптографической защиты MTProto соединения.", Foreground = ModernTheme.TextMuted, FontSize = 10.5, Margin = new Thickness(0, 3, 0, 0) };
            secretStack.Children.Add(lblSecret);
            secretStack.Children.Add(secretGrid);
            secretStack.Children.Add(hintSecret);

            var secretCard = ModernTheme.CreateCard(secretStack, 14, 18);
            secretCard.Margin = new Thickness(0, 0, 0, 8);
            scrollStack.Children.Add(secretCard);

            // 3. Options Card
            var optStack = new StackPanel();
            var optHeader = new TextBlock {
                Text = "ПАРАМЕТРЫ ЗАПУСКА",
                FontSize = 10.5,
                FontWeight = FontWeights.Bold,
                Foreground = ModernTheme.TextMuted,
                Margin = new Thickness(0, 0, 0, 10)
            };
            optStack.Children.Add(optHeader);

            _cbAutoStart = new CheckBox {
                Content = "Запускать при включении Windows (Автозапуск)",
                IsChecked = _settings.AutoStart,
                Foreground = ModernTheme.TextPrimary,
                FontSize = 12,
                Margin = new Thickness(0, 0, 0, 8),
                Cursor = System.Windows.Input.Cursors.Hand
            };
            optStack.Children.Add(_cbAutoStart);

            _cbCfProxy = new CheckBox {
                Content = "Использовать Cloudflare Proxy fallback",
                IsChecked = _settings.CfProxy,
                Foreground = ModernTheme.TextPrimary,
                FontSize = 12,
                Margin = new Thickness(0, 0, 0, 2),
                Cursor = System.Windows.Input.Cursors.Hand
            };
            optStack.Children.Add(_cbCfProxy);

            var optCard = ModernTheme.CreateCard(optStack, 14, 18);
            optCard.Margin = new Thickness(0, 0, 0, 8);
            scrollStack.Children.Add(optCard);

            // 4. Action Buttons & Notification
            var actStack = new StackPanel();

            _settingsNotification = new TextBlock {
                Text = "",
                FontSize = 12,
                Foreground = ModernTheme.Green,
                Margin = new Thickness(0, 0, 0, 6),
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            actStack.Children.Add(_settingsNotification);

            var btnSave = ModernTheme.CreateButton("💾  Сохранить настройки", ModernTheme.CardBgHover, ModernTheme.TextPrimary, 42, 12.5, true, 12, ModernTheme.CardBorderHighlight);
            btnSave.Click += (s, e) => SaveSettingsFromUI();
            actStack.Children.Add(btnSave);

            var btnReset = ModernTheme.CreateButton("🔄  Сбросить настройки по умолчанию", ModernTheme.CardBgSecondary, ModernTheme.TextSecondary, 36, 11, false, 10, ModernTheme.CardBorder);
            btnReset.Margin = new Thickness(0, 6, 0, 0);
            btnReset.Click += (s, e) => ResetSettingsToDefaults();
            actStack.Children.Add(btnReset);

            var actCard = ModernTheme.CreateCard(actStack, 12, 18, ModernTheme.CardBg);
            actCard.Margin = new Thickness(0, 0, 0, 6);
            scrollStack.Children.Add(actCard);

            return ModernTheme.WrapInScrollViewer(scrollStack);
        }

        private void SaveSettingsFromUI() {
            int port;
            if (!int.TryParse(_tbPort.Text.Trim(), out port) || port <= 0 || port > 65535) {
                System.Windows.MessageBox.Show("Пожалуйста, введите корректный номер порта (1 - 65535)", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string dcIp = _tbDcIp.Text.Trim();
            if (string.IsNullOrEmpty(dcIp)) {
                dcIp = "4:149.154.167.220";
            }

            string secret = _tbSecret.Text.Trim().ToLowerInvariant();
            if (secret.Length < 32) {
                System.Windows.MessageBox.Show("Секрет MTProto должен содержать не менее 32 hex-символов.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _settings.Port = port;
            _settings.DcIp = dcIp;
            _settings.Secret = secret;
            _settings.AutoStart = _cbAutoStart.IsChecked == true;
            _settings.CfProxy = _cbCfProxy.IsChecked == true;

            SettingsManager.Save(_settings);

            // Update main view labels
            _txtServerAddress.Text = "127.0.0.1:" + _settings.Port;
            _txtServerSecret.Text = _settings.Secret;
            _txtTargetDc.Text = "DC 4 -> " + _settings.GetParsedIp(4) + ":443";

            ShowNotification("✓ Настройки успешно сохранены!");

            if (_server.IsRunning) {
                _server.Stop();
                try {
                    _server.Start();
                } catch { }
            }
        }

        private void ResetSettingsToDefaults() {
            var res = System.Windows.MessageBox.Show(
                "Сбросить все настройки к значениям по умолчанию?\n\nПорт: 1443\nDC IP: 4:149.154.167.220\nАвтозапуск: выключен",
                "Сброс настроек",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question
            );

            if (res == MessageBoxResult.Yes) {
                SettingsManager.ResetToDefaults(_settings);

                _tbPort.Text = _settings.Port.ToString();
                _tbDcIp.Text = _settings.DcIp;
                _tbSecret.Text = _settings.Secret;
                _cbAutoStart.IsChecked = _settings.AutoStart;
                _cbCfProxy.IsChecked = _settings.CfProxy;

                _txtServerAddress.Text = "127.0.0.1:" + _settings.Port;
                _txtServerSecret.Text = _settings.Secret;
                _txtTargetDc.Text = "DC 4 -> " + _settings.GetParsedIp(4) + ":443";

                ShowNotification("✓ Настройки сброшены по умолчанию!");

                if (_server.IsRunning) {
                    _server.Stop();
                    try { _server.Start(); } catch { }
                }
            }
        }

        private void ShowNotification(string msg) {
            _settingsNotification.Text = msg;
            var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            t.Tick += (s, e) => {
                _settingsNotification.Text = "";
                t.Stop();
            };
            t.Start();
        }
        #endregion

        #region Logs View (Bento Card like Photo 2)
        private UIElement BuildLogsView() {
            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Header
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Log Box
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Controls

            var headerText = new TextBlock {
                Text = "ЖУРНАЛ СОБЫТИЙ ПРОКСИ",
                FontSize = 10.5,
                FontWeight = FontWeights.Bold,
                Foreground = ModernTheme.TextMuted,
                Margin = new Thickness(2, 0, 0, 8)
            };
            Grid.SetRow(headerText, 0);
            grid.Children.Add(headerText);

            _tbLogs = new TextBox {
                Background = ModernTheme.CardBgSecondary,
                Foreground = new SolidColorBrush(Color.FromRgb(220, 225, 235)),
                BorderBrush = ModernTheme.CardBorder,
                BorderThickness = new Thickness(1),
                FontFamily = new FontFamily("Consolas, Courier New, monospace"),
                FontSize = 11,
                IsReadOnly = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Padding = new Thickness(12)
            };

            // Custom rounded border container for TextBox
            var logBorder = new Border {
                CornerRadius = new CornerRadius(16),
                BorderBrush = ModernTheme.CardBorder,
                BorderThickness = new Thickness(1),
                Background = ModernTheme.CardBgSecondary,
                Child = _tbLogs
            };
            Grid.SetRow(logBorder, 1);
            grid.Children.Add(logBorder);

            // Controls below log box
            var controlsRow = new Grid { Margin = new Thickness(0, 10, 0, 6) };
            controlsRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            controlsRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _cbAutoScroll = new CheckBox {
                Content = "Автопрокрутка",
                IsChecked = true,
                Foreground = ModernTheme.TextSecondary,
                FontSize = 11.5,
                VerticalAlignment = VerticalAlignment.Center,
                Cursor = System.Windows.Input.Cursors.Hand
            };
            Grid.SetColumn(_cbAutoScroll, 0);
            controlsRow.Children.Add(_cbAutoScroll);

            var btnsStack = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
            var btnCopyLogs = ModernTheme.CreateButton("📋  Копировать", ModernTheme.CardBgSecondary, ModernTheme.TextPrimary, 32, 11, false, 10, ModernTheme.CardBorder);
            btnCopyLogs.Click += (s, e) => {
                Clipboard.SetText(_tbLogs.Text);
                btnCopyLogs.Content = "✓ Скопировано!";
                var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
                t.Tick += (st, et) => {
                    btnCopyLogs.Content = "📋  Копировать";
                    t.Stop();
                };
                t.Start();
            };

            var btnClearLogs = ModernTheme.CreateButton("🗑  Очистить", ModernTheme.CardBgSecondary, ModernTheme.TextSecondary, 32, 11, false, 10, ModernTheme.CardBorder);
            btnClearLogs.Margin = new Thickness(8, 0, 0, 0);
            btnClearLogs.Click += (s, e) => {
                _logBuffer.Length = 0;
                _tbLogs.Text = "";
            };

            btnsStack.Children.Add(btnCopyLogs);
            btnsStack.Children.Add(btnClearLogs);
            Grid.SetColumn(btnsStack, 1);
            controlsRow.Children.Add(btnsStack);

            Grid.SetRow(controlsRow, 2);
            grid.Children.Add(controlsRow);

            return grid;
        }

        private void OnServerLog(string level, string msg) {
            try {
                Dispatcher.BeginInvoke(DispatcherPriority.Background, (Action)(() => {
                    if (_tbLogs == null) return;
                    string time = DateTime.Now.ToString("HH:mm:ss");
                    string line = string.Format("[{0}] [{1}] {2}\r\n", time, level, msg);

                    if (_tbLogs.Text.Length > 80000) {
                        _tbLogs.Text = _tbLogs.Text.Substring(25000);
                    }
                    _tbLogs.AppendText(line);

                    if (_cbAutoScroll != null && _cbAutoScroll.IsChecked == true) {
                        _tbLogs.CaretIndex = _tbLogs.Text.Length;
                        _tbLogs.ScrollToEnd();
                    }
                }));
            } catch { }
        }
        #endregion

        #region Server Events & Metrics
        private void OnServerStatusChanged(bool isRunning) {
            try {
                Dispatcher.BeginInvoke(DispatcherPriority.Normal, (Action)(() => {
                    _txtServerAddress.Text = "127.0.0.1:" + _settings.Port;
                    if (_tbPort != null) _tbPort.Text = _settings.Port.ToString();

                    if (isRunning) {
                        _statusBadge.Background = ModernTheme.GreenBg;
                        _statusBadge.BorderBrush = ModernTheme.GreenBorder;
                        _statusBadgeDot.Text = "●";
                        _statusBadgeDot.Foreground = ModernTheme.Green;
                        _statusBadgeText.Text = "ПРОКСИ АКТИВЕН";
                        _statusBadgeText.Foreground = ModernTheme.Green;

                        _btnToggleProxy.Content = "⏹  Остановить прокси";
                        _btnToggleProxy.Background = ModernTheme.RedBg;
                        _btnToggleProxy.BorderBrush = ModernTheme.RedBorder;
                        _btnToggleProxy.Foreground = ModernTheme.Red;
                    } else {
                        _statusBadge.Background = ModernTheme.CardBgSecondary;
                        _statusBadge.BorderBrush = ModernTheme.CardBorder;
                        _statusBadgeDot.Text = "○";
                        _statusBadgeDot.Foreground = ModernTheme.TextMuted;
                        _statusBadgeText.Text = "ПРОКСИ ОСТАНОВЛЕН";
                        _statusBadgeText.Foreground = ModernTheme.TextMuted;

                        _btnToggleProxy.Content = "▶  Запустить прокси";
                        _btnToggleProxy.Background = ModernTheme.CardBgHover;
                        _btnToggleProxy.BorderBrush = ModernTheme.CardBorderHighlight;
                        _btnToggleProxy.Foreground = ModernTheme.TextPrimary;
                    }
                }));
            } catch { }
        }

        private void UpdateMetricsDisplay() {
            if (_server != null) {
                _statActiveConn.Text = _server.ActiveConnections.ToString();
                _statTotalConn.Text = _server.TotalConnections.ToString();
                _statRx.Text = FormatBytes(_server.BytesReceived);
                _statTx.Text = FormatBytes(_server.BytesSent);
            }
        }

        private void UpdateUptime() {
            if (_server != null && _server.IsRunning) {
                TimeSpan elapsed = DateTime.Now - _server.StartTime;
                _statUptime.Text = string.Format("{0:D2}:{1:D2}:{2:D2}", (int)elapsed.TotalHours, elapsed.Minutes, elapsed.Seconds);
            } else {
                _statUptime.Text = "00:00:00";
            }
        }

        private string FormatBytes(long bytes) {
            if (bytes < 1024) return bytes + " B";
            if (bytes < 1024 * 1024) return (bytes / 1024.0).ToString("F1") + " KB";
            if (bytes < 1024 * 1024 * 1024) return (bytes / (1024.0 * 1024.0)).ToString("F2") + " MB";
            return (bytes / (1024.0 * 1024.0 * 1024.0)).ToString("F2") + " GB";
        }
        #endregion

        #region System Tray
        private void InitializeTray() {
            _notifyIcon = new NotifyIcon();
            _notifyIcon.Text = "TG PROXY Ho1zze";
            try {
                if (File.Exists("app.ico")) {
                    _notifyIcon.Icon = new System.Drawing.Icon("app.ico");
                } else {
                    _notifyIcon.Icon = System.Drawing.Icon.ExtractAssociatedIcon(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? SystemIcons.Application;
                }
            } catch {
                _notifyIcon.Icon = SystemIcons.Application;
            }
            _notifyIcon.Visible = true;

            var menu = new ContextMenuStrip();
            var itemOpen = new ToolStripMenuItem("Открыть TG PROXY Ho1zze");
            itemOpen.Click += (s, e) => {
                Show();
                WindowState = WindowState.Normal;
                Activate();
            };

            var itemToggle = new ToolStripMenuItem("Запустить / Остановить");
            itemToggle.Click += (s, e) => {
                if (_server.IsRunning) _server.Stop();
                else {
                    try { _server.Start(); } catch { }
                }
            };

            var itemExit = new ToolStripMenuItem("Выход");
            itemExit.Click += (s, e) => {
                _isExplicitExit = true;
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
                _server.Stop();
                System.Windows.Application.Current.Shutdown();
            };

            menu.Items.Add(itemOpen);
            menu.Items.Add(itemToggle);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(itemExit);

            _notifyIcon.ContextMenuStrip = menu;
            _notifyIcon.DoubleClick += (s, e) => {
                Show();
                WindowState = WindowState.Normal;
                Activate();
            };
            _notifyIcon.MouseClick += (s, e) => {
                if (e.Button == System.Windows.Forms.MouseButtons.Left) {
                    if (Visibility == Visibility.Visible && WindowState != WindowState.Minimized) {
                        Hide();
                    } else {
                        Show();
                        WindowState = WindowState.Normal;
                        Activate();
                    }
                }
            };
        }
        #endregion
    }
}
