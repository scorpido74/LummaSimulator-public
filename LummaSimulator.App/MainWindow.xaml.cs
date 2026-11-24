using System.ComponentModel;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.IO;
using System.Windows.Threading;
using LummaSimulator.Configuration;
using LummaSimulator.App.Services;
using LummaSimulator.App.ViewModels;

namespace LummaSimulator.App;

public partial class MainWindow : Window
{
    public static readonly DependencyProperty ResellerLogoProperty = DependencyProperty.Register(nameof(ResellerLogo), typeof(ImageSource), typeof(MainWindow));
    public ImageSource? ResellerLogo { get => (ImageSource?)GetValue(ResellerLogoProperty); private set => SetValue(ResellerLogoProperty, value); }
    public static readonly DependencyProperty OrganizationNameProperty = DependencyProperty.Register(nameof(OrganizationName), typeof(string), typeof(MainWindow), new PropertyMetadata("Infinigate"));
    public string OrganizationName { get => (string)GetValue(OrganizationNameProperty); private set => SetValue(OrganizationNameProperty, value); }
    private void OnDutch(object sender, RoutedEventArgs e) => DesktopLanguage.Current.Select("nl-NL");
    private void OnEnglish(object sender, RoutedEventArgs e) => DesktopLanguage.Current.Select("en-GB");
    private void OnMonitorSave(object sender, RoutedEventArgs e)
    {
        try { if (MonitorConsent.IsChecked == true) _viewModel.SessionMonitor.Integration?.Save(MonitorSecret.Password); }
        finally { MonitorSecret.Clear(); MonitorConsent.IsChecked = false; }
    }
    private void OnMonitorGrantSave(object sender,RoutedEventArgs e)
    {
        try { _viewModel.SessionMonitor.Integration?.SetGrant(_viewModel.SessionMonitor.PortalDemoRunId,MonitorGrantSecret.Password); }
        catch(ArgumentException){MessageBox.Show(DesktopLanguage.Current["IntegrationGrantInvalid"],"LummaSimulator",MessageBoxButton.OK,MessageBoxImage.Information);}
        finally{MonitorGrantSecret.Clear();}
    }
    private void OnMonitorReplace(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SessionMonitor.Integration is not { CanEdit: true } integration) return;
        integration.ReplaceCommand.Execute(null); MonitorSecret.Clear(); MonitorSecret.Focus();
    }
    private void LoadBranding()
    {
        var settings = DesktopSettings.Load();
        OrganizationName = !string.IsNullOrWhiteSpace(settings.OrganizationName) && settings.OrganizationName.Length <= 80
            ? settings.OrganizationName : "Infinigate";
        var path = BrandingAssets.ResolveLogo(Path.Combine(AppContext.BaseDirectory, "Branding"), settings.LogoFile);
        if (path is not null)
        {
            try {
                using var input = File.OpenRead(path);
                var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad;
                image.StreamSource = input; image.EndInit(); image.Freeze(); ResellerLogo = image;
            } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or FileFormatException or ArgumentException or InvalidOperationException or System.Runtime.InteropServices.COMException) { }
        }
        else if (string.IsNullOrEmpty(settings.LogoFile) && OrganizationName == "Infinigate")
            ResellerLogo = TryFindResource("InfinigateLogo") as ImageSource;
    }
    public static readonly DependencyProperty PhaseColumnsProperty = DependencyProperty.Register(nameof(PhaseColumns), typeof(int), typeof(MainWindow), new PropertyMetadata(3));
    public static readonly DependencyProperty OverviewColumnsProperty = DependencyProperty.Register(nameof(OverviewColumns), typeof(int), typeof(MainWindow), new PropertyMetadata(2));
    public int PhaseColumns { get => (int)GetValue(PhaseColumnsProperty); private set => SetValue(PhaseColumnsProperty, value); }
    public static readonly DependencyProperty PhaseCardWidthProperty = DependencyProperty.Register(nameof(PhaseCardWidth), typeof(double), typeof(MainWindow), new PropertyMetadata(260.0));
    public double PhaseCardWidth { get => (double)GetValue(PhaseCardWidthProperty); private set => SetValue(PhaseCardWidthProperty, value); }
    public int OverviewColumns { get => (int)GetValue(OverviewColumnsProperty); private set => SetValue(OverviewColumnsProperty, value); }

    private bool _assessmentOpen;
    private void OnToggleAssessment(object sender, RoutedEventArgs e)
    {
        _assessmentOpen = !_assessmentOpen;
        UpdateResponsiveLayout();
    }
    private void UpdateResponsiveLayout()
    {
        var width = Math.Max(0, ActualWidth - 64);

        PhaseColumns = width >= 1150 ? 3 : width >= 760 ? 2 : 1;
        OverviewColumns = width >= 760 ? 2 : 1;
        PhaseCardWidth = Math.Max(180, (width - 32) / PhaseColumns);
        ScenarioTabs.Height = double.NaN;
        WorkspaceGrid.ColumnDefinitions[1].Width = new GridLength(0);
        AssessmentPanel.Visibility = _assessmentOpen && ScenarioTabs.SelectedIndex == 0 ? Visibility.Visible : Visibility.Collapsed;
        AttackLayout.ColumnDefinitions[1].Width = new GridLength(0);
        AttackLayout.RowDefinitions[1].Height = new GridLength(0);
        System.Windows.Controls.Grid.SetColumn(AssessmentPanel, 0);
        System.Windows.Controls.Grid.SetRow(AssessmentPanel, 4);
        System.Windows.Controls.Grid.SetRowSpan(AssessmentPanel, 1);
        var monitorNarrow = width < 1000;
        MonitorLayout.ColumnDefinitions[0].Width = monitorNarrow ? new GridLength(1, GridUnitType.Star) : new GridLength(340);
        MonitorLayout.ColumnDefinitions[1].Width = monitorNarrow ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        MonitorLayout.RowDefinitions[1].Height = monitorNarrow ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        System.Windows.Controls.Grid.SetColumn(MonitorResults, monitorNarrow ? 0 : 1);
        System.Windows.Controls.Grid.SetRow(MonitorResults, monitorNarrow ? 1 : 0);
    }
    private readonly MainViewModel _viewModel;
    private bool _shutdownRequested, _shutdownSucceeded, _allowClose, _closed, _closePending, _closeQueued;
    private int _closingDepth;
    private Task? _shutdown;
    private readonly Func<Task> _shutdownAction;
    private HelperReceiver? _helperReceiver;
    public static readonly DependencyProperty ShutdownErrorProperty = DependencyProperty.Register(nameof(ShutdownError), typeof(string), typeof(MainWindow), new PropertyMetadata(""));
    public string ShutdownError { get => (string)GetValue(ShutdownErrorProperty); private set => SetValue(ShutdownErrorProperty, value); }
    internal Task? ShutdownCompletion => _shutdown;
    private string _eventCodeRun = "";
    private void OnEventCodeChanged(object sender, RoutedEventArgs e)
    { if (DataContext is MainViewModel vm) vm.EventEdition.Code = EventPairingCode.Password; }
    private void OnEventChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(EventEditionViewModel.RunId) && _eventCodeRun != _viewModel.EventEdition.RunId)
        { _eventCodeRun = _viewModel.EventEdition.RunId; EventPairingCode.Clear(); }
    }

    public MainWindow() : this(new MainViewModel(new SimulationService(), new JsonLogService())) { }

    public MainWindow(MainViewModel viewModel) : this(viewModel, viewModel.ShutdownAsync) { }

    internal MainWindow(MainViewModel viewModel, Func<Task> shutdownAction)
    {
        InitializeComponent();
        Title = "Lumma Attack Simulator · v" + typeof(MainWindow).Assembly
            .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .Cast<System.Reflection.AssemblyInformationalVersionAttribute>().Single().InformationalVersion;
        Width = Math.Min(Width, SystemParameters.WorkArea.Width);
        Height = Math.Min(Height, SystemParameters.WorkArea.Height);
        LoadBranding();
        _viewModel = viewModel;
        _shutdownAction = async () => { if (_helperReceiver is not null) await _helperReceiver.DisposeAsync(); await shutdownAction(); };
        DataContext = _viewModel;
        _viewModel.EventEdition.PropertyChanged += OnEventChanged;
        _viewModel.Entries.CollectionChanged += OnLogChanged;
        Loaded += OnLoaded;
        SizeChanged += OnSizeChanged;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        try {
            if (HelperReceiverSettings.Load().Enabled) {
                _helperReceiver = new HelperReceiver(_viewModel, Dispatcher); _helperReceiver.Start();
                HelperReceiverNotice.Text = "Helperontvanger beschikbaar via HTTPS 443.";
            }
        }
        catch (Exception error) when (error is not OutOfMemoryException) {
            HelperReceiverNotice.Text = "Helperontvanger niet beschikbaar. Controleer de afzonderlijke beheerinrichting voor HTTPS 443.";
        }
        Width = Math.Min(Width, SystemParameters.WorkArea.Width);
        Height = Math.Min(Height, SystemParameters.WorkArea.Height);
        UpdateResponsiveLayout();
    }
    private void OnSizeChanged(object sender, SizeChangedEventArgs e) => UpdateResponsiveLayout();
    private void OnScenarioTabChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (IsInitialized && ReferenceEquals(e.Source, ScenarioTabs)) UpdateResponsiveLayout();
    }

    private void OnLogChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (ActivityLog.Items.Count > 0)
            ActivityLog.ScrollIntoView(ActivityLog.Items[ActivityLog.Items.Count - 1]);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        _closingDepth++;
        try
        {
            if (!_allowClose)
            {
                e.Cancel = true;
                if (!_shutdownRequested)
                {
                    // Publish the guard before any callback can re-enter this window.
                    _shutdownRequested = true;
                    IsEnabled = false;
                    _shutdown = ShutdownAndCloseAsync();
                }
            }
            base.OnClosing(e);
            if (!_allowClose) e.Cancel = true;
        }
        finally
        {
            _closingDepth--;
            if (_closingDepth == 0 && _closePending) QueueFinalClose();
        }
    }

    private async Task ShutdownAndCloseAsync()
    {
        try
        {
            // Unconditional dispatcher hop, including when shutdown completes synchronously.
            await System.Windows.Threading.Dispatcher.Yield(DispatcherPriority.Background);
            await _shutdownAction();
            _shutdownSucceeded = true;
            _closePending = true;
            QueueFinalClose();
        }
        catch (Exception)
        {
            // Do not close with unconfirmed cleanup or expose raw exception/configuration data.
            ShutdownError = DesktopLanguage.Current["ShutdownFailed"];
        }
    }

    private void QueueFinalClose()
    {
        if (_closingDepth != 0 || _closeQueued || _closed || !_shutdownSucceeded || Dispatcher.HasShutdownStarted) return;
        _closeQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            _closeQueued = false;
            // Other Closing handlers may pump a nested dispatcher frame. Wait for the override's finally.
            if (_closingDepth != 0 || _closed) return;
            _closePending = false;
            _allowClose = true;
            try { Close(); }
            catch (Exception)
            {
                _allowClose = false;
                ShutdownError = DesktopLanguage.Current["ShutdownFailed"];
            }
        }));
    }

    protected override void OnClosed(EventArgs e)
    {
        _closed = true;
        MonitorSecret.Clear();
        MonitorGrantSecret.Clear();
        _viewModel.EventEdition.PropertyChanged -= OnEventChanged;
        _viewModel.Entries.CollectionChanged -= OnLogChanged;
        EventPairingCode.PasswordChanged -= OnEventCodeChanged;
        Loaded -= OnLoaded;
        SizeChanged -= OnSizeChanged;
        base.OnClosed(e);
    }
}
