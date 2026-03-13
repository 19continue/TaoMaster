using System.Collections.ObjectModel;
using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Forms = System.Windows.Forms;
using TaoMaster.App.Localization;
using TaoMaster.Core;
using TaoMaster.Core.Discovery;
using TaoMaster.Core.Models;
using TaoMaster.Core.RemoteSources;
using TaoMaster.Core.Services;
using TaoMaster.Core.State;
using WpfButton = System.Windows.Controls.Button;
using WpfComboBox = System.Windows.Controls.ComboBox;
using WpfListBox = System.Windows.Controls.ListBox;
using WpfSelectionChangedEventArgs = System.Windows.Controls.SelectionChangedEventArgs;
using WpfScrollViewer = System.Windows.Controls.ScrollViewer;
using WpfTextBlock = System.Windows.Controls.TextBlock;
using WpfTextBox = System.Windows.Controls.TextBox;
using MediaColor = System.Windows.Media.Color;
using Path = System.IO.Path;

namespace TaoMaster.App;

internal enum AppSection
{
    Dashboard,
    Versions,
    Projects,
    Diagnostics,
    Settings
}

[SupportedOSPlatform("windows")]
public partial class MainWindow : Window
{
    private const string ProductVersionLabel = "v" + ProductInfo.Version;

    private readonly WorkspaceLayout _layout;
    private readonly ManagerStateStore _stateStore;
    private readonly LocalInstallationDiscoveryService _discoveryService;
    private readonly InstallationCatalogService _catalogService;
    private readonly ToolchainSelectionResolver _selectionResolver;
    private readonly WindowsUserEnvironmentService _environmentService;
    private readonly SelectionActivationService _activationService;
    private readonly WindowsShellIntegrationService _shellIntegrationService;
    private readonly DoctorService _doctorService;
    private readonly System.Net.Http.HttpClient _httpClient;
    private readonly TemurinJdkPackageSource _temurinSource;
    private readonly ApacheMavenPackageSource _mavenSource;
    private readonly PackageInstallationService _packageInstallationService;

    private ManagerState _state;
    private DoctorReport? _lastDoctorReport;
    private ShellIntegrationStatus? _shellIntegrationStatus;
    private AppLocalizer _localizer;
    private readonly ObservableCollection<string> _activityEntries = [];
    private bool _hasLoaded;
    private bool _suppressLanguageSelectionChanged;
    private AppSection _currentSection = AppSection.Dashboard;

    public MainWindow()
    {
        var workspaceInitializer = new WorkspaceInitializer();
        _layout = WorkspaceLayout.CreateDefault();
        _stateStore = new ManagerStateStore(workspaceInitializer);

        var inspector = new InstallationInspector();
        _discoveryService = new LocalInstallationDiscoveryService(inspector);
        _catalogService = new InstallationCatalogService(inspector);
        _selectionResolver = new ToolchainSelectionResolver();
        _environmentService = new WindowsUserEnvironmentService();
        _activationService = new SelectionActivationService(_selectionResolver, _environmentService);
        _shellIntegrationService = new WindowsShellIntegrationService(_activationService);
        _doctorService = new DoctorService(_selectionResolver, _environmentService);
        _httpClient = new System.Net.Http.HttpClient();
        _temurinSource = new TemurinJdkPackageSource(_httpClient);
        _mavenSource = new ApacheMavenPackageSource(_httpClient);

        var downloadService = new PackageDownloadService(_httpClient);
        var checksumService = new ChecksumService();
        var zipExtractionService = new ZipExtractionService();
        _packageInstallationService = new PackageInstallationService(downloadService, checksumService, zipExtractionService, inspector);

        _state = _stateStore.EnsureInitialized(_layout);
        _localizer = CreateInitialLocalizer(_state);

        InitializeComponent();
        ActivityListBox.ItemsSource = _activityEntries;
        InitializeLanguageSelector();
        ApplyCurrentSection();
        ApplyLocalization();
        RefreshStateBindings();
        SetStatus(_localizer["readyStatus"]);
    }

    protected override void OnClosed(EventArgs e)
    {
        _httpClient.Dispose();
        base.OnClosed(e);
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_hasLoaded)
        {
            return;
        }

        _hasLoaded = true;

        await ExecuteBusyAsync(
            "busyLoadingWorkspace",
            async () =>
            {
                _shellIntegrationStatus = await Task.Run(() => _shellIntegrationService.EnsureEnabled(_layout, _state));
                RefreshStateBindings();

                await RefreshRemoteVersionsCoreAsync();

                return _localizer.Format("workspaceLoadedStatus", _state.Jdks.Count, _state.Mavens.Count);
            });
    }

    private static AppLocalizer CreateInitialLocalizer(ManagerState state)
    {
        if (AppLocalizer.TryParseLanguage(state.Settings.PreferredUiLanguage, out var persistedLanguage))
        {
            return new AppLocalizer(persistedLanguage);
        }

        return new AppLocalizer(AppLanguage.English);
    }

    private void ApplyActivationWithShellIntegration(ManagerState state)
    {
        _activationService.Apply(state);
        _shellIntegrationStatus = _shellIntegrationService.EnsureEnabled(_layout, state);
    }

    private void InitializeLanguageSelector()
    {
        _suppressLanguageSelectionChanged = true;
        LanguageComboBox.ItemsSource = AppLocalizer.SupportedLanguages;
        LanguageComboBox.SelectedItem = AppLocalizer.SupportedLanguages
            .First(option => option.Language == _localizer.Language);
        _suppressLanguageSelectionChanged = false;
    }

    private void OnLanguageSelectionChanged(object sender, WpfSelectionChangedEventArgs e)
    {
        if (_suppressLanguageSelectionChanged || LanguageComboBox.SelectedItem is not LanguageOption option)
        {
            return;
        }

        ApplyLanguage(option.Language, persistPreference: true);
        RefreshStateBindings(GetSelectedInstallationId(JdkListBox), GetSelectedInstallationId(MavenListBox));
        SetStatus(_localizer.Format("languageChangedStatus", option.DisplayName));
    }

    private void ApplyLocalization()
    {
        Title = $"{_localizer["windowTitle"]} {ProductInfo.Version}";
        ProductNameTextBlock.Text = _localizer.GetProductDisplayName();
        ProductVersionTextBlock.Text = ProductVersionLabel;
        ApplyLocalizationRecursive(this);
        ApplyCurrentSection();

        if (_lastDoctorReport is null)
        {
            DoctorOutputTextBox.Text = _localizer["doctorPlaceholder"];
        }

        if (SuccessNoticeBorder.Visibility == Visibility.Visible)
        {
            SuccessNoticeTitleTextBlock.Text = _localizer["successNoticeTitle"];
        }
    }

    private void ApplyLanguage(AppLanguage language, bool persistPreference)
    {
        _localizer = new AppLocalizer(language);
        SelectLanguageOption(language);
        ApplyLocalization();

        if (persistPreference)
        {
            PersistLanguagePreference(language);
        }
    }

    private void SelectLanguageOption(AppLanguage language)
    {
        _suppressLanguageSelectionChanged = true;
        LanguageComboBox.SelectedItem = AppLocalizer.SupportedLanguages
            .First(option => option.Language == language);
        _suppressLanguageSelectionChanged = false;
    }

    private void PersistLanguagePreference(AppLanguage language)
    {
        _state = _state with
        {
            Settings = _state.Settings with
            {
                PreferredUiLanguage = language.ToString()
            }
        };

        _stateStore.Save(_layout, _state);
    }

    private void ApplyLocalizationRecursive(DependencyObject root)
    {
        ApplyLocalizationToElement(root);

        var childCount = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < childCount; index++)
        {
            ApplyLocalizationRecursive(VisualTreeHelper.GetChild(root, index));
        }
    }

    private void ApplyLocalizationToElement(DependencyObject element)
    {
        switch (element)
        {
            case WpfTextBlock textBlock when textBlock.Tag is string key:
                textBlock.Text = _localizer[key];
                break;
            case WpfButton button when button.Tag is string key:
                button.Content = _localizer[key];
                break;
        }
    }

    private void RefreshStateBindings(string? preferredJdkId = null, string? preferredMavenId = null)
    {
        _shellIntegrationStatus = _shellIntegrationService.GetStatus(_layout);

        SidebarWorkspaceTextBlock.Text = _layout.RootDirectory;
        SidebarScopeTextBlock.Text = _localizer["scopeGlobal"];
        WorkspaceRootTextBlock.Text = _layout.RootDirectory;
        StateFileTextBlock.Text = _layout.StateFile;
        InstallRootTextBlock.Text = _state.Settings.InstallRoot;
        PathModeTextBlock.Text = BuildPathModeText();
        AppVersionTextBlock.Text = ProductInfo.Version;
        ShellSyncStatusTextBlock.Text = BuildShellSyncStatusText();
        ShellSyncDetailTextBlock.Text = BuildShellSyncDetailText();

        var selection = _selectionResolver.Resolve(_state);
        DashboardCurrentJdkTextBlock.Text = selection.Jdk?.DisplayName ?? _localizer["nonePlaceholder"];
        DashboardCurrentJdkPathTextBlock.Text = selection.Jdk?.HomeDirectory ?? _localizer["dashboardNoJdkDetail"];
        DashboardCurrentMavenTextBlock.Text = selection.Maven?.DisplayName ?? _localizer["nonePlaceholder"];
        DashboardCurrentMavenPathTextBlock.Text = selection.Maven?.HomeDirectory ?? _localizer["dashboardNoMavenDetail"];
        DashboardScopeTextBlock.Text = _localizer["scopeGlobal"];
        DashboardScopeDetailTextBlock.Text = _localizer["dashboardScopeDetail"];
        ProjectsScopeTextBlock.Text = _localizer["projectsScopeValue"];
        ProjectsScopeDetailTextBlock.Text = _localizer["projectsScopeDetail"];
        ProjectsRulesTextBlock.Text = _localizer["projectsRulesValue"];
        HeaderScopeBadgeTextBlock.Text = _localizer["scopeGlobal"];

        JdkListBox.ItemsSource = _state.Jdks.ToList();
        MavenListBox.ItemsSource = _state.Mavens.ToList();
        DashboardJdkComboBox.ItemsSource = _state.Jdks.ToList();
        DashboardMavenComboBox.ItemsSource = _state.Mavens.ToList();

        SelectInstallation(JdkListBox, _state.Jdks, preferredJdkId ?? _state.ActiveSelection.JdkId);
        SelectInstallation(MavenListBox, _state.Mavens, preferredMavenId ?? _state.ActiveSelection.MavenId);
        SelectInstallationInComboBox(DashboardJdkComboBox, _state.Jdks, preferredJdkId ?? _state.ActiveSelection.JdkId);
        SelectInstallationInComboBox(DashboardMavenComboBox, _state.Mavens, preferredMavenId ?? _state.ActiveSelection.MavenId);

        PowerShellScriptTextBox.Text = BuildShellPreviewText("powershell");
        EnvironmentSnapshotTextBox.Text = BuildEnvironmentSnapshot(selection);
        RefreshEnvironmentHealth(selection);

        if (_lastDoctorReport is not null)
        {
            DoctorOutputTextBox.Text = BuildDoctorOutput(_lastDoctorReport);
        }
        else
        {
            DoctorOutputTextBox.Text = _localizer["doctorPlaceholder"];
        }
    }

    private static void SelectInstallation(
        WpfListBox listBox,
        IEnumerable<ManagedInstallation> installations,
        string? installationId)
    {
        if (string.IsNullOrWhiteSpace(installationId))
        {
            listBox.SelectedItem = null;
            return;
        }

        listBox.SelectedItem = installations.FirstOrDefault(
            installation => installation.Id.Equals(installationId, StringComparison.OrdinalIgnoreCase));
    }

    private static void SelectInstallationInComboBox(
        WpfComboBox comboBox,
        IEnumerable<ManagedInstallation> installations,
        string? installationId)
    {
        var resolved = string.IsNullOrWhiteSpace(installationId)
            ? installations.FirstOrDefault()
            : installations.FirstOrDefault(installation => installation.Id.Equals(installationId, StringComparison.OrdinalIgnoreCase));

        comboBox.SelectedItem = resolved;
    }

    private void ApplyCurrentSection()
    {
        DashboardPage.Visibility = _currentSection == AppSection.Dashboard ? Visibility.Visible : Visibility.Collapsed;
        VersionsPage.Visibility = _currentSection == AppSection.Versions ? Visibility.Visible : Visibility.Collapsed;
        ProjectsPage.Visibility = _currentSection == AppSection.Projects ? Visibility.Visible : Visibility.Collapsed;
        DiagnosticsPage.Visibility = _currentSection == AppSection.Diagnostics ? Visibility.Visible : Visibility.Collapsed;
        SettingsPage.Visibility = _currentSection == AppSection.Settings ? Visibility.Visible : Visibility.Collapsed;

        PageTitleTextBlock.Text = _localizer[GetSectionTitleKey(_currentSection)];
        PageDescriptionTextBlock.Text = _localizer[GetSectionDescriptionKey(_currentSection)];

        ApplyNavigationButtonState(DashboardNavButton, _currentSection == AppSection.Dashboard);
        ApplyNavigationButtonState(VersionsNavButton, _currentSection == AppSection.Versions);
        ApplyNavigationButtonState(ProjectsNavButton, _currentSection == AppSection.Projects);
        ApplyNavigationButtonState(DiagnosticsNavButton, _currentSection == AppSection.Diagnostics);
        ApplyNavigationButtonState(SettingsNavButton, _currentSection == AppSection.Settings);
    }

    private static string GetSectionTitleKey(AppSection section) =>
        section switch
        {
            AppSection.Dashboard => "pageDashboardTitle",
            AppSection.Versions => "pageVersionsTitle",
            AppSection.Projects => "pageProjectsTitle",
            AppSection.Diagnostics => "pageDiagnosticsTitle",
            AppSection.Settings => "pageSettingsTitle",
            _ => "pageDashboardTitle"
        };

    private static string GetSectionDescriptionKey(AppSection section) =>
        section switch
        {
            AppSection.Dashboard => "pageDashboardDescription",
            AppSection.Versions => "pageVersionsDescription",
            AppSection.Projects => "pageProjectsDescription",
            AppSection.Diagnostics => "pageDiagnosticsDescription",
            AppSection.Settings => "pageSettingsDescription",
            _ => "pageDashboardDescription"
        };

    private void ApplyNavigationButtonState(WpfButton button, bool isActive)
    {
        button.Background = isActive
            ? new SolidColorBrush(MediaColor.FromRgb(30, 138, 102))
            : System.Windows.Media.Brushes.Transparent;
        button.BorderBrush = isActive
            ? new SolidColorBrush(MediaColor.FromRgb(137, 208, 183))
            : System.Windows.Media.Brushes.Transparent;
        button.Foreground = isActive
            ? System.Windows.Media.Brushes.White
            : new SolidColorBrush(MediaColor.FromRgb(199, 213, 206));
    }

    private string BuildPathModeText()
    {
        if (_shellIntegrationStatus?.IsEnabled == true)
        {
            return _localizer["settingsPathModeManagedShell"];
        }

        return _localizer["settingsPathModeManagedOnly"];
    }

    private string BuildShellSyncStatusText()
    {
        if (_shellIntegrationStatus is null)
        {
            return _localizer["shellSyncStatusPending"];
        }

        return _shellIntegrationStatus.IsEnabled
            ? _localizer["shellSyncStatusEnabled"]
            : _localizer["shellSyncStatusPartial"];
    }

    private string BuildShellSyncDetailText()
    {
        if (_shellIntegrationStatus is null)
        {
            return _localizer["shellSyncDetailPending"];
        }

        return _localizer.Format(
            _shellIntegrationStatus.IsEnabled ? "shellSyncDetailEnabled" : "shellSyncDetailPartial",
            _shellIntegrationStatus.PowerShellEnabledProfileCount,
            _shellIntegrationStatus.PowerShellProfileCount);
    }

    private void RefreshEnvironmentHealth(ActiveToolchainSelection selection)
    {
        var pathHealthOk = selection.Jdk is not null || selection.Maven is not null;
        var userPath = _environmentService.GetUserVariable(EnvironmentVariableNames.Path);
        if (selection.Jdk is not null)
        {
            pathHealthOk &= userPath?.Contains("%JAVA_HOME%\\bin", StringComparison.OrdinalIgnoreCase) == true;
        }

        if (selection.Maven is not null)
        {
            pathHealthOk &= userPath?.Contains("%MAVEN_HOME%\\bin", StringComparison.OrdinalIgnoreCase) == true;
        }

        var doctorPasses = _lastDoctorReport?.Checks.Count(check => check.Status == DoctorCheckStatus.Pass) ?? 0;
        var doctorWarns = _lastDoctorReport?.Checks.Count(check => check.Status == DoctorCheckStatus.Warn) ?? 0;
        var doctorFails = _lastDoctorReport?.Checks.Count(check => check.Status == DoctorCheckStatus.Fail) ?? 0;

        ApplyHealthState(
            JavaHealthBadgeBorder,
            JavaHealthBadgeTextBlock,
            selection.Jdk is not null,
            selection.Jdk is not null ? "healthReady" : "healthMissing",
            JavaHealthHintTextBlock,
            selection.Jdk?.DisplayName ?? _localizer["healthSelectJdkHint"]);

        ApplyHealthState(
            MavenHealthBadgeBorder,
            MavenHealthBadgeTextBlock,
            selection.Maven is not null,
            selection.Maven is not null ? "healthReady" : "healthMissing",
            MavenHealthHintTextBlock,
            selection.Maven?.DisplayName ?? _localizer["healthSelectMavenHint"]);

        ApplyHealthState(
            PathHealthBadgeBorder,
            PathHealthBadgeTextBlock,
            pathHealthOk,
            pathHealthOk ? "healthReady" : "healthAttention",
            PathHealthHintTextBlock,
            pathHealthOk
                ? _shellIntegrationStatus?.IsEnabled == true
                    ? _localizer["healthPathShellSyncHint"]
                    : _localizer["healthPathReadyHint"]
                : _localizer["healthPathCheckHint"]);

        if (_lastDoctorReport is null)
        {
            ApplyHealthState(
                DoctorHealthBadgeBorder,
                DoctorHealthBadgeTextBlock,
                false,
                "healthPending",
                DoctorHealthHintTextBlock,
                _localizer["healthDoctorPendingHint"]);

            ApplyHeaderStatus("healthPending", _localizer["healthDoctorPendingHint"]);
            ApplyDashboardEnvironment("healthPending", _localizer["healthDoctorPendingHint"]);
            DashboardInsightTextBlock.Text = _localizer["dashboardInsightPending"];
            return;
        }

        var doctorOk = doctorFails == 0 && doctorWarns == 0;
        var doctorKey = doctorFails > 0
            ? "healthError"
            : doctorWarns > 0
                ? "healthAttention"
                : "healthReady";
        var doctorHint = doctorFails > 0
            ? _localizer.Format("healthDoctorFailureHint", doctorFails)
            : doctorWarns > 0
                ? _localizer.Format("healthDoctorWarningHint", doctorWarns)
                : _localizer.Format("healthDoctorReadyHint", doctorPasses);

        ApplyHealthState(
            DoctorHealthBadgeBorder,
            DoctorHealthBadgeTextBlock,
            doctorOk,
            doctorKey,
            DoctorHealthHintTextBlock,
            doctorHint);

        ApplyHeaderStatus(doctorKey, doctorHint);
        ApplyDashboardEnvironment(doctorKey, doctorHint);
        DashboardInsightTextBlock.Text = BuildDashboardInsight();
    }

    private void ApplyDashboardEnvironment(string statusKey, string detail)
    {
        DashboardEnvironmentTextBlock.Text = _localizer[statusKey];
        DashboardEnvironmentDetailTextBlock.Text = detail;
        ApplyBadgeTone(DashboardEnvironmentBadgeBorder, DashboardEnvironmentTextBlock, statusKey);
    }

    private void ApplyHeaderStatus(string statusKey, string detail)
    {
        HeaderEnvironmentBadgeTextBlock.Text = _localizer[statusKey];
        HeaderEnvironmentHintTextBlock.Text = detail;
        ApplyBadgeTone(HeaderEnvironmentBadgeBorder, HeaderEnvironmentBadgeTextBlock, statusKey);
    }

    private void ApplyHealthState(
        System.Windows.Controls.Border border,
        WpfTextBlock textBlock,
        bool success,
        string statusKey,
        WpfTextBlock hintTextBlock,
        string hint)
    {
        textBlock.Text = _localizer[statusKey];
        hintTextBlock.Text = hint;
        ApplyBadgeTone(border, textBlock, success ? "healthReady" : statusKey);
    }

    private static (MediaColor Background, MediaColor Foreground) GetBadgeColors(string statusKey) =>
        statusKey switch
        {
            "healthReady" => (MediaColor.FromRgb(216, 239, 230), MediaColor.FromRgb(30, 138, 102)),
            "healthAttention" => (MediaColor.FromRgb(246, 235, 214), MediaColor.FromRgb(208, 138, 47)),
            "healthError" => (MediaColor.FromRgb(245, 226, 221), MediaColor.FromRgb(196, 81, 64)),
            _ => (MediaColor.FromRgb(230, 238, 247), MediaColor.FromRgb(44, 108, 166))
        };

    private static void ApplyBadgeTone(System.Windows.Controls.Border border, WpfTextBlock textBlock, string statusKey)
    {
        var (background, foreground) = GetBadgeColors(statusKey);
        border.Background = new SolidColorBrush(background);
        textBlock.Foreground = new SolidColorBrush(foreground);
    }

    private string BuildEnvironmentSnapshot(ActiveToolchainSelection selection)
    {
        var userJavaHome = _environmentService.GetUserVariable(EnvironmentVariableNames.JavaHome) ?? _localizer["nonePlaceholder"];
        var userMavenHome = _environmentService.GetUserVariable(EnvironmentVariableNames.MavenHome) ?? _localizer["nonePlaceholder"];
        var userM2Home = _environmentService.GetUserVariable(EnvironmentVariableNames.M2Home) ?? _localizer["nonePlaceholder"];
        var userPath = _environmentService.GetUserVariable(EnvironmentVariableNames.Path) ?? _localizer["nonePlaceholder"];
        var shellSyncStatus = _shellIntegrationStatus is null
            ? _localizer["shellSyncStatusPending"]
            : _shellIntegrationStatus.IsEnabled
                ? _localizer["shellSyncStatusEnabled"]
                : _localizer["shellSyncStatusPartial"];

        var lines = new List<string>
        {
            $"{_localizer["snapshotScopeLabel"]}: {_localizer["scopeGlobal"]}",
            $"{_localizer["snapshotSelectedJdkLabel"]}: {selection.Jdk?.Id ?? _localizer["nonePlaceholder"]}",
            $"{_localizer["snapshotSelectedMavenLabel"]}: {selection.Maven?.Id ?? _localizer["nonePlaceholder"]}",
            $"{_localizer["snapshotJavaHomeLabel"]}: {userJavaHome}",
            $"{_localizer["snapshotMavenHomeLabel"]}: {userMavenHome}",
            $"{_localizer["snapshotM2HomeLabel"]}: {userM2Home}",
            $"{_localizer["snapshotShellSyncLabel"]}: {shellSyncStatus}"
        };

        lines.AddRange(BuildPathDetailLines(_localizer["snapshotPathLabel"], userPath));
        return string.Join(Environment.NewLine, lines);
    }

    private string BuildDashboardInsight()
    {
        if (_lastDoctorReport is null)
        {
            return _localizer["dashboardInsightPending"];
        }

        var firstFailure = _lastDoctorReport.Checks.FirstOrDefault(check => check.Status == DoctorCheckStatus.Fail);
        if (firstFailure is not null)
        {
            return $"{firstFailure.Code}: {_localizer.GetDoctorMessage(firstFailure.Code, firstFailure.Status)}";
        }

        var firstWarning = _lastDoctorReport.Checks.FirstOrDefault(check => check.Status == DoctorCheckStatus.Warn);
        if (firstWarning is not null)
        {
            return $"{firstWarning.Code}: {_localizer.GetDoctorMessage(firstWarning.Code, firstWarning.Status)}";
        }

        return _localizer["dashboardInsightHealthy"];
    }

    private void ReportPackageInstallProgress(string operationKey, PackageInstallProgress progress)
    {
        switch (progress.Stage)
        {
            case PackageInstallStage.Downloading:
                var progressValue = progress.TotalBytes is > 0
                    ? 18d + (double)progress.BytesReceived / progress.TotalBytes.Value * 52d
                    : (double?)null;
                var receivedText = FormatByteSize(progress.BytesReceived);
                if (progress.TotalBytes is > 0)
                {
                    ReportBusyStage(
                        operationKey,
                        "busyDetailDownloadingKnown",
                        progressValue,
                        receivedText,
                        FormatByteSize(progress.TotalBytes.Value));
                }
                else
                {
                    ReportBusyStage(operationKey, "busyDetailDownloadingUnknown", progressValue, receivedText);
                }

                break;
            case PackageInstallStage.Verifying:
                ReportBusyStage(operationKey, "busyDetailVerifyingPackage", 78);
                break;
            case PackageInstallStage.Extracting:
                ReportBusyStage(operationKey, "busyDetailExtractingPackage", 88);
                break;
            case PackageInstallStage.Completed:
                ReportBusyStage(operationKey, "busyDetailFinishingInstall", 96);
                break;
        }
    }

    private static string FormatByteSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        double value = bytes;
        var unitIndex = 0;

        while (value >= 1024 && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        return $"{value:0.##} {units[unitIndex]}";
    }

    private async Task RefreshRemoteVersionsCoreAsync()
    {
        var currentJdkVersion = RemoteJdkVersionComboBox.SelectedItem as int?;
        var currentMavenVersion = RemoteMavenVersionComboBox.SelectedItem as string;

        var jdkTask = _temurinSource.GetAvailableFeatureReleasesAsync(CancellationToken.None);
        var mavenTask = _mavenSource.GetCurrentVersionsAsync(CancellationToken.None);

        await Task.WhenAll(jdkTask, mavenTask);

        var jdkVersions = (await jdkTask).ToList();
        var mavenVersions = (await mavenTask).ToList();

        RemoteJdkVersionComboBox.ItemsSource = jdkVersions;
        RemoteJdkVersionComboBox.SelectedItem = currentJdkVersion.HasValue && jdkVersions.Contains(currentJdkVersion.Value)
            ? currentJdkVersion.Value
            : jdkVersions.FirstOrDefault();

        RemoteMavenVersionComboBox.ItemsSource = mavenVersions;
        RemoteMavenVersionComboBox.SelectedItem = !string.IsNullOrWhiteSpace(currentMavenVersion)
                                                 && mavenVersions.Contains(currentMavenVersion, StringComparer.OrdinalIgnoreCase)
            ? mavenVersions.First(version => version.Equals(currentMavenVersion, StringComparison.OrdinalIgnoreCase))
            : mavenVersions.FirstOrDefault();
    }

    private string BuildShellPreviewText(string shellKind)
    {
        try
        {
            var script = _activationService.BuildShellScript(_state, shellKind);
            return string.IsNullOrWhiteSpace(script) ? _localizer["shellPreviewEmpty"] : script;
        }
        catch (Exception ex)
        {
            return _localizer.Format("shellPreviewUnavailable", ex.Message);
        }
    }

    private void ReportBusyStage(string messageKey, string? detailKey = null, double? progress = null, params object?[] detailArgs)
    {
        var message = _localizer[messageKey];
        var detail = string.IsNullOrWhiteSpace(detailKey)
            ? null
            : detailArgs.Length == 0
                ? _localizer[detailKey]
                : _localizer.Format(detailKey, detailArgs);

        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => SetBusy(true, message, detail, progress));
            return;
        }

        SetBusy(true, message, detail, progress);
    }

    private async Task ExecuteBusyAsync(string busyKey, Func<Task<string?>> operation, Action<string>? onSuccess = null)
    {
        SetBusy(true, _localizer[busyKey], _localizer["busyDetailPreparing"], null);

        Exception? failure = null;
        string? statusText = null;

        try
        {
            statusText = await operation();
        }
        catch (Exception ex)
        {
            failure = ex;
            statusText = _localizer.Format("statusOperationFailed", ex.Message);
        }
        finally
        {
            SetBusy(false, null, null, null);
        }

        if (!string.IsNullOrWhiteSpace(statusText))
        {
            SetStatus(statusText);
            if (failure is null)
            {
                onSuccess?.Invoke(statusText);
            }
        }

        if (failure is not null)
        {
            HideSuccessNotice();
            System.Windows.MessageBox.Show(
                this,
                failure.Message,
                _localizer["errorTitle"],
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void SetBusy(bool isBusy, string? message, string? detail, double? progress)
    {
        BusyOverlay.Visibility = isBusy ? Visibility.Visible : Visibility.Collapsed;
        BusyTextBlock.Text = message ?? string.Empty;
        BusyDetailTextBlock.Text = detail ?? string.Empty;
        BusyProgressBar.IsIndeterminate = progress is null;
        BusyProgressBar.Value = progress is null ? 12 : Math.Clamp(progress.Value, 0, 100);
    }

    private void SetStatus(string message)
    {
        StatusTextBlock.Text = message;
        if (!string.IsNullOrWhiteSpace(message))
        {
            PushActivity(message);
        }
    }

    private void ShowSuccessNotice(string message)
    {
        SuccessNoticeTitleTextBlock.Text = _localizer["successNoticeTitle"];
        SuccessNoticeMessageTextBlock.Text = message;
        SuccessNoticeBorder.Visibility = Visibility.Visible;
    }

    private void HideSuccessNotice()
    {
        SuccessNoticeMessageTextBlock.Text = string.Empty;
        SuccessNoticeBorder.Visibility = Visibility.Collapsed;
    }

    private void PushActivity(string message)
    {
        var entry = $"{DateTime.Now:HH:mm}  {message}";
        if (_activityEntries.Count > 0
            && _activityEntries[0].EndsWith(message, StringComparison.Ordinal))
        {
            return;
        }

        _activityEntries.Insert(0, entry);
        while (_activityEntries.Count > 8)
        {
            _activityEntries.RemoveAt(_activityEntries.Count - 1);
        }
    }

    private void InvalidateDoctorOutput()
    {
        _lastDoctorReport = null;
        DoctorOutputTextBox.Text = _localizer["doctorPlaceholder"];
    }

    private void ShowValidationWarning(string messageKey)
    {
        var message = _localizer[messageKey];
        HideSuccessNotice();
        SetStatus(message);
        System.Windows.MessageBox.Show(
            this,
            message,
            _localizer["warningTitle"],
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    private void OnDashboardNavClicked(object sender, RoutedEventArgs e)
    {
        _currentSection = AppSection.Dashboard;
        ApplyCurrentSection();
    }

    private void OnVersionsNavClicked(object sender, RoutedEventArgs e)
    {
        _currentSection = AppSection.Versions;
        ApplyCurrentSection();
    }

    private void OnProjectsNavClicked(object sender, RoutedEventArgs e)
    {
        _currentSection = AppSection.Projects;
        ApplyCurrentSection();
    }

    private void OnDiagnosticsNavClicked(object sender, RoutedEventArgs e)
    {
        _currentSection = AppSection.Diagnostics;
        ApplyCurrentSection();
    }

    private void OnSettingsNavClicked(object sender, RoutedEventArgs e)
    {
        _currentSection = AppSection.Settings;
        ApplyCurrentSection();
    }

    private void OnBrowseJdkImportClicked(object sender, RoutedEventArgs e)
    {
        BrowseForFolder(JdkImportPathTextBox, "browseJdkDescription");
    }

    private void OnBrowseMavenImportClicked(object sender, RoutedEventArgs e)
    {
        BrowseForFolder(MavenImportPathTextBox, "browseMavenDescription");
    }

    private void BrowseForFolder(WpfTextBox textBox, string descriptionKey)
    {
        using var dialog = new Forms.FolderBrowserDialog
        {
            Description = _localizer[descriptionKey],
            ShowNewFolderButton = false,
            SelectedPath = System.IO.Directory.Exists(textBox.Text) ? textBox.Text : string.Empty
        };

        if (dialog.ShowDialog() == Forms.DialogResult.OK)
        {
            textBox.Text = dialog.SelectedPath;
        }
    }

    private async void OnSyncClicked(object sender, RoutedEventArgs e)
    {
        var preferredJdkId = GetSelectedInstallationId(JdkListBox);
        var preferredMavenId = GetSelectedInstallationId(MavenListBox);

        await ExecuteBusyAsync(
            "busySyncingLocalInstallations",
            async () =>
            {
                var currentState = _state;
                _state = await Task.Run(() =>
                {
                    var snapshot = _discoveryService.Discover(_layout);
                    var updatedState = _catalogService.MergeDiscovered(currentState, snapshot);
                    _stateStore.Save(_layout, updatedState);
                    return updatedState;
                });

                InvalidateDoctorOutput();
                RefreshStateBindings(preferredJdkId, preferredMavenId);
                return _localizer.Format("syncCompletedStatus", _state.Jdks.Count, _state.Mavens.Count);
            });
    }

    private async void OnImportJdkClicked(object sender, RoutedEventArgs e)
    {
        var importPath = JdkImportPathTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(importPath))
        {
            ShowValidationWarning("validationChooseJdkFolder");
            return;
        }

        await ExecuteBusyAsync(
            "busyImportingJdk",
            async () =>
            {
                var currentState = _state;
                var result = await Task.Run(() =>
                    _catalogService.ImportInstallation(currentState, ToolchainKind.Jdk, importPath, _layout));

                _state = result.State;
                await Task.Run(() => _stateStore.Save(_layout, _state));

                InvalidateDoctorOutput();
                RefreshStateBindings(result.Installation.Id, GetSelectedInstallationId(MavenListBox));
                return _localizer.Format("jdkImportedStatus", result.Installation.DisplayName);
            });
    }

    private async void OnImportMavenClicked(object sender, RoutedEventArgs e)
    {
        var importPath = MavenImportPathTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(importPath))
        {
            ShowValidationWarning("validationChooseMavenFolder");
            return;
        }

        await ExecuteBusyAsync(
            "busyImportingMaven",
            async () =>
            {
                var currentState = _state;
                var result = await Task.Run(() =>
                    _catalogService.ImportInstallation(currentState, ToolchainKind.Maven, importPath, _layout));

                _state = result.State;
                await Task.Run(() => _stateStore.Save(_layout, _state));

                InvalidateDoctorOutput();
                RefreshStateBindings(GetSelectedInstallationId(JdkListBox), result.Installation.Id);
                return _localizer.Format("mavenImportedStatus", result.Installation.DisplayName);
            });
    }

    private async void OnDashboardUseSelectedJdkClicked(object sender, RoutedEventArgs e)
    {
        if (DashboardJdkComboBox.SelectedItem is not ManagedInstallation installation)
        {
            ShowValidationWarning("validationSelectJdk");
            return;
        }

        await SwitchInstallationAsync(ToolchainKind.Jdk, installation);
    }

    private async void OnDashboardUseSelectedMavenClicked(object sender, RoutedEventArgs e)
    {
        if (DashboardMavenComboBox.SelectedItem is not ManagedInstallation installation)
        {
            ShowValidationWarning("validationSelectMaven");
            return;
        }

        await SwitchInstallationAsync(ToolchainKind.Maven, installation);
    }

    private async void OnUseSelectedJdkClicked(object sender, RoutedEventArgs e)
    {
        if (JdkListBox.SelectedItem is not ManagedInstallation installation)
        {
            ShowValidationWarning("validationSelectJdk");
            return;
        }

        await SwitchInstallationAsync(ToolchainKind.Jdk, installation);
    }

    private async void OnUseSelectedMavenClicked(object sender, RoutedEventArgs e)
    {
        if (MavenListBox.SelectedItem is not ManagedInstallation installation)
        {
            ShowValidationWarning("validationSelectMaven");
            return;
        }

        await SwitchInstallationAsync(ToolchainKind.Maven, installation);
    }

    private async Task SwitchInstallationAsync(ToolchainKind kind, ManagedInstallation installation)
    {
        var operationKey = kind == ToolchainKind.Jdk ? "busySwitchingJdk" : "busySwitchingMaven";
        await ExecuteBusyAsync(
            operationKey,
            async () =>
            {
                ReportBusyStage(operationKey, "busyDetailPreparingSwitch", 14);
                var updatedState = kind switch
                {
                    ToolchainKind.Jdk => _state with
                    {
                        ActiveSelection = _state.ActiveSelection with { JdkId = installation.Id }
                    },
                    ToolchainKind.Maven => _state with
                    {
                        ActiveSelection = _state.ActiveSelection with { MavenId = installation.Id }
                    },
                    _ => _state
                };

                ReportBusyStage(operationKey, "busyDetailApplyingEnvironment", 46);
                await Task.Run(() => ApplyActivationWithShellIntegration(updatedState));
                ReportBusyStage(operationKey, "busyDetailSyncingShells", 72);
                await Task.Run(() => _stateStore.Save(_layout, updatedState));
                ReportBusyStage(operationKey, "busyDetailSavingState", 88);

                _state = updatedState;
                InvalidateDoctorOutput();
                ReportBusyStage(operationKey, "busyDetailRefreshingUi", 96);
                RefreshStateBindings(
                    kind == ToolchainKind.Jdk ? installation.Id : GetSelectedInstallationId(JdkListBox),
                    kind == ToolchainKind.Maven ? installation.Id : GetSelectedInstallationId(MavenListBox));

                return _localizer.Format(
                    kind == ToolchainKind.Jdk ? "jdkSwitchedStatus" : "mavenSwitchedStatus",
                    installation.DisplayName);
            },
            ShowSuccessNotice);
    }

    private async void OnRemoveSelectedJdkClicked(object sender, RoutedEventArgs e)
    {
        await RemoveSelectedInstallationAsync(ToolchainKind.Jdk, deleteFiles: false);
    }

    private async void OnUninstallSelectedJdkClicked(object sender, RoutedEventArgs e)
    {
        await RemoveSelectedInstallationAsync(ToolchainKind.Jdk, deleteFiles: true);
    }

    private async void OnRemoveSelectedMavenClicked(object sender, RoutedEventArgs e)
    {
        await RemoveSelectedInstallationAsync(ToolchainKind.Maven, deleteFiles: false);
    }

    private async void OnUninstallSelectedMavenClicked(object sender, RoutedEventArgs e)
    {
        await RemoveSelectedInstallationAsync(ToolchainKind.Maven, deleteFiles: true);
    }

    private async void OnInstallJdkClicked(object sender, RoutedEventArgs e)
    {
        await InstallRemoteJdkAsync(switchAfterInstall: false);
    }

    private async void OnInstallAndUseJdkClicked(object sender, RoutedEventArgs e)
    {
        await InstallRemoteJdkAsync(switchAfterInstall: true);
    }

    private async void OnInstallMavenClicked(object sender, RoutedEventArgs e)
    {
        await InstallRemoteMavenAsync(switchAfterInstall: false);
    }

    private async void OnInstallAndUseMavenClicked(object sender, RoutedEventArgs e)
    {
        await InstallRemoteMavenAsync(switchAfterInstall: true);
    }

    private async Task InstallRemoteJdkAsync(bool switchAfterInstall)
    {
        if (!TryGetSelectedRemoteJdkFeature(out var featureVersion))
        {
            ShowValidationWarning("validationSelectRemoteJdkVersion");
            return;
        }

        var preferredJdkId = GetSelectedInstallationId(JdkListBox);
        var preferredMavenId = GetSelectedInstallationId(MavenListBox);

        await ExecuteBusyAsync(
            "busyInstallingJdk",
            async () =>
            {
                ReportBusyStage("busyInstallingJdk", "busyDetailResolvingPackage", 8);
                var package = await _temurinSource.ResolveLatestAsync(featureVersion, "x64", CancellationToken.None);
                var progress = new Progress<PackageInstallProgress>(value => ReportPackageInstallProgress("busyInstallingJdk", value));
                var installation = await _packageInstallationService.InstallAsync(package, _layout, CancellationToken.None, progress);
                ReportBusyStage("busyInstallingJdk", "busyDetailRegisteringInstallation", 97);
                var updatedState = _catalogService.RegisterInstallation(_state, installation);

                if (switchAfterInstall)
                {
                    updatedState = updatedState with
                    {
                        ActiveSelection = updatedState.ActiveSelection with { JdkId = installation.Id }
                    };

                    ReportBusyStage("busyInstallingJdk", "busyDetailActivatingSelection", 98);
                    await Task.Run(() => ApplyActivationWithShellIntegration(updatedState));
                }

                ReportBusyStage("busyInstallingJdk", "busyDetailSavingState", 99);
                await Task.Run(() => _stateStore.Save(_layout, updatedState));

                _state = updatedState;
                InvalidateDoctorOutput();
                ReportBusyStage("busyInstallingJdk", "busyDetailRefreshingUi", 100);
                RefreshStateBindings(
                    switchAfterInstall ? installation.Id : preferredJdkId,
                    preferredMavenId);

                var statusKey = switchAfterInstall ? "jdkInstalledAndActivatedStatus" : "jdkInstalledStatus";
                return _localizer.Format(statusKey, installation.DisplayName);
            },
            ShowSuccessNotice);
    }

    private async Task InstallRemoteMavenAsync(bool switchAfterInstall)
    {
        if (!TryGetSelectedRemoteMavenVersion(out var version))
        {
            ShowValidationWarning("validationSelectRemoteMavenVersion");
            return;
        }

        var preferredJdkId = GetSelectedInstallationId(JdkListBox);
        var preferredMavenId = GetSelectedInstallationId(MavenListBox);

        await ExecuteBusyAsync(
            "busyInstallingMaven",
            async () =>
            {
                ReportBusyStage("busyInstallingMaven", "busyDetailResolvingPackage", 8);
                var package = await _mavenSource.ResolveAsync(version, CancellationToken.None);
                var progress = new Progress<PackageInstallProgress>(value => ReportPackageInstallProgress("busyInstallingMaven", value));
                var installation = await _packageInstallationService.InstallAsync(package, _layout, CancellationToken.None, progress);
                ReportBusyStage("busyInstallingMaven", "busyDetailRegisteringInstallation", 97);
                var updatedState = _catalogService.RegisterInstallation(_state, installation);

                if (switchAfterInstall)
                {
                    updatedState = updatedState with
                    {
                        ActiveSelection = updatedState.ActiveSelection with { MavenId = installation.Id }
                    };

                    ReportBusyStage("busyInstallingMaven", "busyDetailActivatingSelection", 98);
                    await Task.Run(() => ApplyActivationWithShellIntegration(updatedState));
                }

                ReportBusyStage("busyInstallingMaven", "busyDetailSavingState", 99);
                await Task.Run(() => _stateStore.Save(_layout, updatedState));

                _state = updatedState;
                InvalidateDoctorOutput();
                ReportBusyStage("busyInstallingMaven", "busyDetailRefreshingUi", 100);
                RefreshStateBindings(
                    preferredJdkId,
                    switchAfterInstall ? installation.Id : preferredMavenId);

                var statusKey = switchAfterInstall ? "mavenInstalledAndActivatedStatus" : "mavenInstalledStatus";
                return _localizer.Format(statusKey, installation.DisplayName);
            },
            ShowSuccessNotice);
    }

    private async Task RemoveSelectedInstallationAsync(ToolchainKind kind, bool deleteFiles)
    {
        var listBox = kind == ToolchainKind.Jdk ? JdkListBox : MavenListBox;
        if (listBox.SelectedItem is not ManagedInstallation installation)
        {
            ShowValidationWarning(kind == ToolchainKind.Jdk ? "validationSelectJdk" : "validationSelectMaven");
            return;
        }

        if (!ConfirmRemoval(installation, deleteFiles))
        {
            return;
        }

        var preferredJdkId = kind == ToolchainKind.Jdk ? null : GetSelectedInstallationId(JdkListBox);
        var preferredMavenId = kind == ToolchainKind.Maven ? null : GetSelectedInstallationId(MavenListBox);

        await ExecuteBusyAsync(
            GetRemovalBusyKey(kind, deleteFiles),
            async () =>
            {
                var result = await Task.Run(() =>
                    _catalogService.RemoveInstallation(_state, kind, installation.Id, _layout, deleteFiles));

                await Task.Run(() => _stateStore.Save(_layout, result.State));
                _state = result.State;
                await Task.Run(() => ApplyActivationWithShellIntegration(result.State));

                InvalidateDoctorOutput();
                RefreshStateBindings(preferredJdkId, preferredMavenId);

                return _localizer.Format(GetRemovalStatusKey(kind, deleteFiles), result.Installation.DisplayName);
            });
    }

    private bool TryGetSelectedRemoteJdkFeature(out int featureVersion)
    {
        if (RemoteJdkVersionComboBox.SelectedItem is int selectedFeature)
        {
            featureVersion = selectedFeature;
            return true;
        }

        featureVersion = default;
        return false;
    }

    private bool TryGetSelectedRemoteMavenVersion(out string version)
    {
        if (RemoteMavenVersionComboBox.SelectedItem is string selectedVersion
            && !string.IsNullOrWhiteSpace(selectedVersion))
        {
            version = selectedVersion;
            return true;
        }

        version = string.Empty;
        return false;
    }

    private async void OnRunDoctorClicked(object sender, RoutedEventArgs e)
    {
        _currentSection = AppSection.Diagnostics;
        ApplyCurrentSection();

        await ExecuteBusyAsync(
            "busyRunningDoctor",
            async () =>
            {
                _lastDoctorReport = await Task.Run(() => _doctorService.Run(_state));
                DoctorOutputTextBox.Text = BuildDoctorOutput(_lastDoctorReport);
                RefreshEnvironmentHealth(_selectionResolver.Resolve(_state));

                return _localizer.Format(
                    "doctorCompletedStatus",
                    _lastDoctorReport.Checks.Count(check => check.Status == DoctorCheckStatus.Pass),
                    _lastDoctorReport.Checks.Count(check => check.Status == DoctorCheckStatus.Warn),
                    _lastDoctorReport.Checks.Count(check => check.Status == DoctorCheckStatus.Fail));
            });
    }

    private async void OnRepairUserPathClicked(object sender, RoutedEventArgs e)
    {
        await ExecuteBusyAsync(
            "busyRepairingUserPath",
            async () =>
            {
                var selection = _selectionResolver.Resolve(_state);
                var result = await Task.Run(() => _environmentService.RepairUserPathForManagedToolchains(
                    _environmentService.GetUserVariable(EnvironmentVariableNames.Path),
                    includeJavaEntry: selection.Jdk is not null,
                    includeMavenEntry: selection.Maven is not null));

                await Task.Run(() =>
                {
                    _environmentService.SetUserVariable(EnvironmentVariableNames.Path, result.UpdatedPath);
                    ApplyActivationWithShellIntegration(_state);
                });

                _lastDoctorReport = await Task.Run(() => _doctorService.Run(_state));
                RefreshStateBindings(GetSelectedInstallationId(JdkListBox), GetSelectedInstallationId(MavenListBox));

                return result.Changed
                    ? _localizer.Format("userPathRepairedStatus", result.RemovedSegments.Count)
                    : _localizer["userPathAlreadyCleanStatus"];
            });
    }

    private async void OnCopyMachinePathScriptClicked(object sender, RoutedEventArgs e)
    {
        await ExecuteBusyAsync(
            "busyPreparingMachinePathScript",
            async () =>
            {
                var selection = _selectionResolver.Resolve(_state);
                var plan = await Task.Run(() => _environmentService.BuildMachinePathRepairPlan(
                    selection.Jdk?.HomeDirectory,
                    selection.Maven?.HomeDirectory));

                if (!plan.Changed)
                {
                    return _localizer["machinePathAlreadyCleanStatus"];
                }

                System.Windows.Clipboard.SetText(plan.PowerShellScript);
                return _localizer["machinePathScriptCopiedStatus"];
            });
    }

    private async void OnRefreshRemoteClicked(object sender, RoutedEventArgs e)
    {
        await ExecuteBusyAsync(
            "busyRefreshingRemoteVersions",
            async () =>
            {
                await RefreshRemoteVersionsCoreAsync();
                return _localizer["remoteVersionsRefreshedStatus"];
            });
    }

    private void OnCopyPowerShellScriptClicked(object sender, RoutedEventArgs e)
    {
        CopyShellScript("powershell", "powerShellScriptCopiedStatus");
    }

    private void OnCopyCmdScriptClicked(object sender, RoutedEventArgs e)
    {
        CopyShellScript("cmd", "cmdScriptCopiedStatus");
    }

    private void CopyShellScript(string shellKind, string successStatusKey)
    {
        try
        {
            var script = _activationService.BuildShellScript(_state, shellKind);
            if (string.IsNullOrWhiteSpace(script))
            {
                ShowValidationWarning("shellScriptUnavailableWarning");
                return;
            }

            System.Windows.Clipboard.SetText(script);
            SetStatus(_localizer[successStatusKey]);
        }
        catch (Exception ex)
        {
            HideSuccessNotice();
            SetStatus(_localizer.Format("statusOperationFailed", ex.Message));
            System.Windows.MessageBox.Show(
                this,
                ex.Message,
                _localizer["errorTitle"],
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private bool ConfirmRemoval(ManagedInstallation installation, bool deleteFiles)
    {
        var titleKey = deleteFiles ? "uninstallConfirmTitle" : "removeConfirmTitle";
        var messageKey = deleteFiles ? "uninstallConfirmMessage" : "removeConfirmMessage";
        var message = deleteFiles
            ? _localizer.Format(messageKey, installation.DisplayName, Environment.NewLine, installation.HomeDirectory)
            : _localizer.Format(messageKey, installation.DisplayName);

        return System.Windows.MessageBox.Show(
            this,
            message,
            _localizer[titleKey],
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning) == MessageBoxResult.Yes;
    }

    private static string GetRemovalBusyKey(ToolchainKind kind, bool deleteFiles) =>
        (kind, deleteFiles) switch
        {
            (ToolchainKind.Jdk, false) => "busyRemovingJdk",
            (ToolchainKind.Jdk, true) => "busyUninstallingJdk",
            (ToolchainKind.Maven, false) => "busyRemovingMaven",
            (ToolchainKind.Maven, true) => "busyUninstallingMaven",
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };

    private static string GetRemovalStatusKey(ToolchainKind kind, bool deleteFiles) =>
        (kind, deleteFiles) switch
        {
            (ToolchainKind.Jdk, false) => "jdkRemovedStatus",
            (ToolchainKind.Jdk, true) => "jdkUninstalledStatus",
            (ToolchainKind.Maven, false) => "mavenRemovedStatus",
            (ToolchainKind.Maven, true) => "mavenUninstalledStatus",
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };

    private string BuildDoctorOutput(DoctorReport report)
    {
        var selection = _selectionResolver.Resolve(_state);
        var userJavaHome = _environmentService.GetUserVariable(EnvironmentVariableNames.JavaHome);
        var userMavenHome = _environmentService.GetUserVariable(EnvironmentVariableNames.MavenHome);
        var userM2Home = _environmentService.GetUserVariable(EnvironmentVariableNames.M2Home);
        var userPath = _environmentService.GetUserVariable(EnvironmentVariableNames.Path);
        var expectedUserPath = _environmentService.BuildManagedUserPath(
            userPath,
            includeJavaEntry: selection.Jdk is not null,
            includeMavenEntry: selection.Maven is not null);
        var variableMap = _environmentService.BuildVariableMap(
            selection.Jdk?.HomeDirectory ?? userJavaHome,
            selection.Maven?.HomeDirectory ?? userMavenHome,
            selection.Maven?.HomeDirectory ?? userM2Home);
        var javaCandidates = _environmentService.FindExecutableCandidates("java.exe", expectedUserPath, variableMap);
        var mavenCandidates = _environmentService.FindExecutableCandidates("mvn.cmd", expectedUserPath, variableMap);

        var lines = new List<string>
        {
            _localizer["doctorHeader"],
            string.Empty
        };

        foreach (var check in report.Checks)
        {
            lines.Add($"[{check.Status.ToString().ToUpperInvariant()}] {check.Code} - {_localizer.GetDoctorMessage(check.Code, check.Status)}");

            foreach (var detailLine in BuildDoctorDetailLines(
                         check,
                         selection,
                         userJavaHome,
                         userMavenHome,
                         userM2Home,
                         userPath,
                         expectedUserPath,
                         javaCandidates,
                         mavenCandidates))
            {
                lines.Add($"    {detailLine}");
            }
        }

        lines.Add(string.Empty);
        lines.Add(_localizer.Format(
            "doctorSummary",
            report.Checks.Count(check => check.Status == DoctorCheckStatus.Pass),
            report.Checks.Count(check => check.Status == DoctorCheckStatus.Warn),
            report.Checks.Count(check => check.Status == DoctorCheckStatus.Fail)));

        return string.Join(Environment.NewLine, lines);
    }

    private IEnumerable<string> BuildDoctorDetailLines(
        DoctorCheck check,
        ActiveToolchainSelection selection,
        string? userJavaHome,
        string? userMavenHome,
        string? userM2Home,
        string? userPath,
        string expectedUserPath,
        IReadOnlyList<ResolvedExecutableCandidate> javaCandidates,
        IReadOnlyList<ResolvedExecutableCandidate> mavenCandidates)
    {
        return check.Code switch
        {
            "selected-jdk" => BuildSelectionDetails(_state.ActiveSelection.JdkId, selection.Jdk),
            "selected-maven" => BuildSelectionDetails(_state.ActiveSelection.MavenId, selection.Maven),
            "java-home" => BuildExpectedActualDetails(selection.Jdk?.HomeDirectory, userJavaHome),
            "maven-home" => BuildExpectedActualDetails(selection.Maven?.HomeDirectory, userMavenHome),
            "m2-home" => BuildExpectedActualDetails(selection.Maven?.HomeDirectory, userM2Home),
            "user-path" => check.Status == DoctorCheckStatus.Pass
                ? BuildActualDetail(userPath)
                : BuildExpectedActualDetails(expectedUserPath, userPath),
            "java-resolve" => BuildExecutableResolutionDetails(
                selection.Jdk is null ? null : Path.Combine(selection.Jdk.HomeDirectory, "bin", "java.exe"),
                javaCandidates),
            "maven-resolve" => BuildExecutableResolutionDetails(
                selection.Maven is null ? null : Path.Combine(selection.Maven.HomeDirectory, "bin", "mvn.cmd"),
                mavenCandidates),
            "maven-probe" => BuildOutputDetails(check.Detail),
            _ => BuildOutputDetails(check.Detail)
        };
    }

    private IEnumerable<string> BuildSelectionDetails(string? selectedId, ManagedInstallation? installation)
    {
        if (installation is not null)
        {
            return
            [
                $"{_localizer["detailSelectionLabel"]}: {installation.DisplayName}",
                $"{_localizer["detailIdLabel"]}: {installation.Id}"
            ];
        }

        return string.IsNullOrWhiteSpace(selectedId)
            ? Array.Empty<string>()
            :
            [
                $"{_localizer["detailIdLabel"]}: {selectedId}"
            ];
    }

    private IEnumerable<string> BuildExpectedActualDetails(string? expected, string? actual)
    {
        var lines = new List<string>();

        if (!string.IsNullOrWhiteSpace(expected))
        {
            lines.AddRange(BuildDetailLines(_localizer["detailExpectedLabel"], expected));
        }

        if (!string.IsNullOrWhiteSpace(actual))
        {
            lines.AddRange(BuildDetailLines(_localizer["detailActualLabel"], actual));
        }

        return lines;
    }

    private IEnumerable<string> BuildExecutableResolutionDetails(
        string? expected,
        IReadOnlyList<ResolvedExecutableCandidate> candidates)
    {
        var lines = new List<string>();

        if (!string.IsNullOrWhiteSpace(expected))
        {
            lines.AddRange(BuildDetailLines(_localizer["detailExpectedLabel"], expected));
        }

        var winningCandidate = candidates.FirstOrDefault();
        if (winningCandidate is null)
        {
            lines.Add($"{_localizer["detailActualLabel"]}: (none)");
            return lines;
        }

        lines.AddRange(BuildDetailLines(_localizer["detailActualLabel"], winningCandidate.CandidatePath));
        lines.Add($"{_localizer["detailScopeLabel"]}: {_localizer[winningCandidate.Scope == EnvironmentPathScope.Machine ? "pathScopeMachine" : "pathScopeUser"]}");
        lines.AddRange(BuildDetailLines(_localizer["detailPathEntryLabel"], winningCandidate.OriginalPathSegment));

        var expectedMatched = !string.IsNullOrWhiteSpace(expected)
                              && string.Equals(winningCandidate.CandidatePath, expected, StringComparison.OrdinalIgnoreCase);
        var recommendation = expectedMatched
            ? _localizer["doctorNoActionNeeded"]
            : winningCandidate.Scope == EnvironmentPathScope.Machine && _shellIntegrationStatus?.IsEnabled == true
                ? _localizer["doctorShellSyncRecommendation"]
                : winningCandidate.Scope == EnvironmentPathScope.Machine
                ? _localizer["doctorRepairMachinePathRecommendation"]
                : _localizer["doctorRepairUserPathRecommendation"];

        lines.Add($"{_localizer["detailRecommendationLabel"]}: {recommendation}");
        return lines;
    }

    private IEnumerable<string> BuildActualDetail(string? actual)
    {
        return string.IsNullOrWhiteSpace(actual)
            ? Array.Empty<string>()
            : BuildDetailLines(_localizer["detailActualLabel"], actual);
    }

    private IEnumerable<string> BuildOutputDetails(string? output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return Array.Empty<string>();
        }

        var lines = output
            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToList();

        if (lines.Count == 0)
        {
            return Array.Empty<string>();
        }

        if (lines.Count == 1)
        {
            return
            [
                $"{_localizer["detailOutputLabel"]}: {lines[0]}"
            ];
        }

        var formattedLines = new List<string>
        {
            $"{_localizer["detailOutputLabel"]}:"
        };
        formattedLines.AddRange(lines);
        return formattedLines;
    }

    private static string? GetSelectedInstallationId(WpfListBox listBox) =>
        listBox.SelectedItem is ManagedInstallation installation
            ? installation.Id
            : null;

    private IEnumerable<string> BuildDetailLines(string label, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Array.Empty<string>();
        }

        return value.Contains(';', StringComparison.Ordinal)
            ? BuildPathDetailLines(label, value)
            : [$"{label}: {value}"];
    }

    private IEnumerable<string> BuildPathDetailLines(string label, string value)
    {
        var segments = value
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        if (segments.Count <= 1)
        {
            return [$"{label}: {value}"];
        }

        var lines = new List<string> { $"{label}:" };
        lines.AddRange(segments.Select(segment => $"  - {segment}"));
        return lines;
    }

    private void OnDismissSuccessNoticeClicked(object sender, RoutedEventArgs e)
    {
        HideSuccessNotice();
    }

    private void OnEmbeddedListPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not DependencyObject dependencyObject)
        {
            return;
        }

        var scrollViewer = FindAncestor<WpfScrollViewer>(dependencyObject);
        if (scrollViewer is null)
        {
            return;
        }

        scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset - (e.Delta / 3d));
        e.Handled = true;
    }

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T target)
            {
                return target;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }
}
