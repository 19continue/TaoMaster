using System.Runtime.Versioning;
using System.Windows;
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
using WpfListBox = System.Windows.Controls.ListBox;
using WpfSelectionChangedEventArgs = System.Windows.Controls.SelectionChangedEventArgs;
using WpfTextBlock = System.Windows.Controls.TextBlock;
using WpfTextBox = System.Windows.Controls.TextBox;
using Path = System.IO.Path;

namespace TaoMaster.App;

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
    private readonly DoctorService _doctorService;
    private readonly System.Net.Http.HttpClient _httpClient;
    private readonly TemurinJdkPackageSource _temurinSource;
    private readonly ApacheMavenPackageSource _mavenSource;
    private readonly PackageInstallationService _packageInstallationService;

    private ManagerState _state;
    private DoctorReport? _lastDoctorReport;
    private AppLocalizer _localizer;
    private bool _hasLoaded;
    private bool _suppressLanguageSelectionChanged;

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
        _doctorService = new DoctorService(_selectionResolver, _environmentService);
        _httpClient = new System.Net.Http.HttpClient();
        _temurinSource = new TemurinJdkPackageSource(_httpClient);
        _mavenSource = new ApacheMavenPackageSource(_httpClient);

        var downloadService = new PackageDownloadService(_httpClient);
        var checksumService = new ChecksumService();
        var zipExtractionService = new ZipExtractionService();
        _packageInstallationService = new PackageInstallationService(downloadService, checksumService, zipExtractionService, inspector);

        _state = ManagerState.CreateDefault(_layout);
        _localizer = new AppLocalizer(AppLocalizer.DetectDefaultLanguage(System.Globalization.CultureInfo.CurrentUICulture));

        InitializeComponent();
        InitializeLanguageSelector();
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
                _state = await Task.Run(() => _stateStore.EnsureInitialized(_layout));
                ApplyPersistedLanguagePreference();
                RefreshStateBindings();

                await RefreshRemoteVersionsCoreAsync();

                return _localizer.Format("workspaceLoadedStatus", _state.Jdks.Count, _state.Mavens.Count);
            });
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

        if (_lastDoctorReport is null)
        {
            DoctorOutputTextBox.Text = _localizer["doctorPlaceholder"];
        }
    }

    private void ApplyPersistedLanguagePreference()
    {
        if (!AppLocalizer.TryParseLanguage(_state.Settings.PreferredUiLanguage, out var persistedLanguage))
        {
            return;
        }

        ApplyLanguage(persistedLanguage, persistPreference: false);
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
        WorkspaceRootTextBlock.Text = _layout.RootDirectory;
        StateFileTextBlock.Text = _layout.StateFile;

        var selection = _selectionResolver.Resolve(_state);

        ActiveJdkTextBlock.Text = selection.Jdk?.DisplayName ?? _localizer["nonePlaceholder"];
        JavaHomeTextBlock.Text = selection.Jdk?.HomeDirectory ?? _localizer["nonePlaceholder"];
        ActiveMavenTextBlock.Text = selection.Maven?.DisplayName ?? _localizer["nonePlaceholder"];
        MavenHomeTextBlock.Text = selection.Maven?.HomeDirectory ?? _localizer["nonePlaceholder"];

        JdkListBox.ItemsSource = _state.Jdks.ToList();
        MavenListBox.ItemsSource = _state.Mavens.ToList();

        SelectInstallation(JdkListBox, _state.Jdks, preferredJdkId ?? _state.ActiveSelection.JdkId);
        SelectInstallation(MavenListBox, _state.Mavens, preferredMavenId ?? _state.ActiveSelection.MavenId);

        PowerShellScriptTextBox.Text = BuildShellPreviewText("powershell");

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

    private async Task ExecuteBusyAsync(string busyKey, Func<Task<string?>> operation)
    {
        SetBusy(true, _localizer[busyKey]);

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
            SetBusy(false, null);
        }

        if (!string.IsNullOrWhiteSpace(statusText))
        {
            SetStatus(statusText);
        }

        if (failure is not null)
        {
            System.Windows.MessageBox.Show(
                this,
                failure.Message,
                _localizer["errorTitle"],
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void SetBusy(bool isBusy, string? message)
    {
        BusyOverlay.Visibility = isBusy ? Visibility.Visible : Visibility.Collapsed;
        BusyTextBlock.Text = message ?? string.Empty;
    }

    private void SetStatus(string message)
    {
        StatusTextBlock.Text = message;
    }

    private void InvalidateDoctorOutput()
    {
        _lastDoctorReport = null;
        DoctorOutputTextBox.Text = _localizer["doctorPlaceholder"];
    }

    private void ShowValidationWarning(string messageKey)
    {
        var message = _localizer[messageKey];
        SetStatus(message);
        System.Windows.MessageBox.Show(
            this,
            message,
            _localizer["warningTitle"],
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
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

    private async void OnUseSelectedJdkClicked(object sender, RoutedEventArgs e)
    {
        if (JdkListBox.SelectedItem is not ManagedInstallation installation)
        {
            ShowValidationWarning("validationSelectJdk");
            return;
        }

        await ExecuteBusyAsync(
            "busySwitchingJdk",
            async () =>
            {
                var updatedState = _state with
                {
                    ActiveSelection = _state.ActiveSelection with { JdkId = installation.Id }
                };

                await Task.Run(() => _activationService.Apply(updatedState));
                await Task.Run(() => _stateStore.Save(_layout, updatedState));

                _state = updatedState;
                InvalidateDoctorOutput();
                RefreshStateBindings(installation.Id, GetSelectedInstallationId(MavenListBox));
                return _localizer.Format("jdkSwitchedStatus", installation.DisplayName);
            });
    }

    private async void OnUseSelectedMavenClicked(object sender, RoutedEventArgs e)
    {
        if (MavenListBox.SelectedItem is not ManagedInstallation installation)
        {
            ShowValidationWarning("validationSelectMaven");
            return;
        }

        await ExecuteBusyAsync(
            "busySwitchingMaven",
            async () =>
            {
                var updatedState = _state with
                {
                    ActiveSelection = _state.ActiveSelection with { MavenId = installation.Id }
                };

                await Task.Run(() => _activationService.Apply(updatedState));
                await Task.Run(() => _stateStore.Save(_layout, updatedState));

                _state = updatedState;
                InvalidateDoctorOutput();
                RefreshStateBindings(GetSelectedInstallationId(JdkListBox), installation.Id);
                return _localizer.Format("mavenSwitchedStatus", installation.DisplayName);
            });
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

        var preferredMavenId = GetSelectedInstallationId(MavenListBox);

        await ExecuteBusyAsync(
            "busyInstallingJdk",
            async () =>
            {
                var package = await _temurinSource.ResolveLatestAsync(featureVersion, "x64", CancellationToken.None);
                var installation = await _packageInstallationService.InstallAsync(package, _layout, CancellationToken.None);
                var updatedState = _catalogService.RegisterInstallation(_state, installation);

                if (switchAfterInstall)
                {
                    updatedState = updatedState with
                    {
                        ActiveSelection = updatedState.ActiveSelection with { JdkId = installation.Id }
                    };

                    await Task.Run(() => _activationService.Apply(updatedState));
                }

                await Task.Run(() => _stateStore.Save(_layout, updatedState));

                _state = updatedState;
                InvalidateDoctorOutput();
                RefreshStateBindings(installation.Id, preferredMavenId);

                var statusKey = switchAfterInstall ? "jdkInstalledAndActivatedStatus" : "jdkInstalledStatus";
                return _localizer.Format(statusKey, installation.DisplayName);
            });
    }

    private async Task InstallRemoteMavenAsync(bool switchAfterInstall)
    {
        if (!TryGetSelectedRemoteMavenVersion(out var version))
        {
            ShowValidationWarning("validationSelectRemoteMavenVersion");
            return;
        }

        var preferredJdkId = GetSelectedInstallationId(JdkListBox);

        await ExecuteBusyAsync(
            "busyInstallingMaven",
            async () =>
            {
                var package = await _mavenSource.ResolveAsync(version, CancellationToken.None);
                var installation = await _packageInstallationService.InstallAsync(package, _layout, CancellationToken.None);
                var updatedState = _catalogService.RegisterInstallation(_state, installation);

                if (switchAfterInstall)
                {
                    updatedState = updatedState with
                    {
                        ActiveSelection = updatedState.ActiveSelection with { MavenId = installation.Id }
                    };

                    await Task.Run(() => _activationService.Apply(updatedState));
                }

                await Task.Run(() => _stateStore.Save(_layout, updatedState));

                _state = updatedState;
                InvalidateDoctorOutput();
                RefreshStateBindings(preferredJdkId, installation.Id);

                var statusKey = switchAfterInstall ? "mavenInstalledAndActivatedStatus" : "mavenInstalledStatus";
                return _localizer.Format(statusKey, installation.DisplayName);
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
        await ExecuteBusyAsync(
            "busyRunningDoctor",
            async () =>
            {
                _lastDoctorReport = await Task.Run(() => _doctorService.Run(_state));
                DoctorOutputTextBox.Text = BuildDoctorOutput(_lastDoctorReport);

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
                    _activationService.Apply(_state);
                });

                _lastDoctorReport = await Task.Run(() => _doctorService.Run(_state));
                RefreshStateBindings(GetSelectedInstallationId(JdkListBox), GetSelectedInstallationId(MavenListBox));

                return result.Changed
                    ? _localizer.Format("userPathRepairedStatus", result.RemovedSegments.Count)
                    : _localizer["userPathAlreadyCleanStatus"];
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
            SetStatus(_localizer.Format("statusOperationFailed", ex.Message));
            System.Windows.MessageBox.Show(
                this,
                ex.Message,
                _localizer["errorTitle"],
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

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
            lines.Add($"{_localizer["detailExpectedLabel"]}: {expected}");
        }

        if (!string.IsNullOrWhiteSpace(actual))
        {
            lines.Add($"{_localizer["detailActualLabel"]}: {actual}");
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
            lines.Add($"{_localizer["detailExpectedLabel"]}: {expected}");
        }

        var winningCandidate = candidates.FirstOrDefault();
        if (winningCandidate is null)
        {
            lines.Add($"{_localizer["detailActualLabel"]}: (none)");
            return lines;
        }

        lines.Add($"{_localizer["detailActualLabel"]}: {winningCandidate.CandidatePath}");
        lines.Add($"{_localizer["detailScopeLabel"]}: {_localizer[winningCandidate.Scope == EnvironmentPathScope.Machine ? "pathScopeMachine" : "pathScopeUser"]}");
        lines.Add($"{_localizer["detailPathEntryLabel"]}: {winningCandidate.OriginalPathSegment}");

        var recommendation = !string.IsNullOrWhiteSpace(expected)
                             && string.Equals(winningCandidate.CandidatePath, expected, StringComparison.OrdinalIgnoreCase)
            ? _localizer["doctorNoActionNeeded"]
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
            :
            [
                $"{_localizer["detailActualLabel"]}: {actual}"
            ];
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
}
