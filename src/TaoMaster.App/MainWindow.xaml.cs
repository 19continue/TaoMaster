using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using Forms = System.Windows.Forms;
using TaoMaster.App.Localization;
using TaoMaster.Core;
using TaoMaster.Core.Discovery;
using TaoMaster.Core.Models;
using TaoMaster.Core.RemoteSources;
using TaoMaster.Core.Services;
using TaoMaster.Core.State;
using WpfButton = System.Windows.Controls.Button;
using WpfCheckBox = System.Windows.Controls.CheckBox;
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
    Settings,
    MavenConfig
}

internal sealed record ConfigurationScopeOption(MavenConfigurationScope Scope, string DisplayName);

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
    private readonly OracleJdkPackageSource _oracleSource;
    private readonly ApacheMavenPackageSource _mavenSource;
    private readonly PackageInstallationService _packageInstallationService;
    private readonly MavenConfigurationService _mavenConfigurationService;
    private readonly JdkDownloadSourceService _jdkDownloadSourceService;
    private readonly ManagedInstallLayoutService _managedInstallLayoutService;

    private ManagerState _state;
    private DoctorReport? _lastDoctorReport;
    private ShellIntegrationStatus? _shellIntegrationStatus;
    private AppLocalizer _localizer;
    private readonly ObservableCollection<string> _activityEntries = [];
    private readonly ObservableCollection<MavenMirrorConfiguration> _configuredMavenMirrors = [];
    private readonly ObservableCollection<JdkDownloadSourceConfiguration> _availableJdkDownloadSources = [];
    private readonly ObservableCollection<MavenDownloadSourceConfiguration> _availableMavenDownloadSources = [];
    private bool _hasLoaded;
    private bool _suppressLanguageSelectionChanged;
    private bool _suppressConfigurationScopeSelectionChanged;
    private bool _suppressDownloadSourceSelectionChanged;
    private bool _suppressMirrorEditorTextChanged;
    private bool _suppressToolchainsEditorTextChanged;
    private bool _mavenMirrorsEditorDirty;
    private bool _toolchainsEditorDirty;
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
        _oracleSource = new OracleJdkPackageSource(_httpClient);
        _mavenSource = new ApacheMavenPackageSource(_httpClient);
        _mavenConfigurationService = new MavenConfigurationService();
        _jdkDownloadSourceService = new JdkDownloadSourceService();
        _managedInstallLayoutService = new ManagedInstallLayoutService();

        var downloadService = new PackageDownloadService(_httpClient);
        var checksumService = new ChecksumService();
        var zipExtractionService = new ZipExtractionService();
        _packageInstallationService = new PackageInstallationService(downloadService, checksumService, zipExtractionService, inspector);

        _state = _stateStore.EnsureInitialized(_layout);
        _localizer = CreateInitialLocalizer(_state);

        InitializeComponent();
        ActivityListBox.ItemsSource = _activityEntries;
        ConfiguredMavenMirrorsListBox.ItemsSource = _configuredMavenMirrors;
        BuiltInMavenMirrorComboBox.ItemsSource = _mavenConfigurationService.GetBuiltInMirrors();
        RemoteJdkDownloadSourceComboBox.ItemsSource = _availableJdkDownloadSources;
        SettingsJdkDownloadSourceComboBox.ItemsSource = _availableJdkDownloadSources;
        RemoteMavenDownloadSourceComboBox.ItemsSource = _availableMavenDownloadSources;
        SettingsMavenDownloadSourceComboBox.ItemsSource = _availableMavenDownloadSources;
        MavenConfigurationScopeComboBox.ItemsSource = BuildConfigurationScopeOptions();
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
                await Task.Run(() => SyncMavenSettingsFromFile(persistState: true));
                RefreshStateBindings();

                await RefreshRemoteVersionsCoreAsync();

                return _localizer.Format("workspaceLoadedStatus", _state.Jdks.Count, _state.Mavens.Count);
            });

        RefreshLocalizedView();
    }

    private static AppLocalizer CreateInitialLocalizer(ManagerState state)
    {
        if (AppLocalizer.TryParseLanguage(state.Settings.PreferredUiLanguage, out var persistedLanguage))
        {
            return new AppLocalizer(persistedLanguage);
        }

        return new AppLocalizer(AppLanguage.SimplifiedChinese);
    }

    private void ApplyActivationWithShellIntegration(ManagerState state)
    {
        _activationService.Apply(state);
        _shellIntegrationStatus = _shellIntegrationService.EnsureEnabled(_layout, state);
    }

    private void InitializeLanguageSelector()
    {
        _suppressLanguageSelectionChanged = true;
        var options = GetLanguageOptions();
        LanguageComboBox.ItemsSource = options;
        LanguageComboBox.SelectedItem = options
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
        SetStatus(_localizer.Format("languageChangedStatus", option.DisplayName));
    }

    private void ApplyLocalization()
    {
        Title = $"{_localizer["windowTitle"]} {ProductInfo.Version}";
        ProductNameTextBlock.Text = _localizer.GetProductDisplayName();
        ProductVersionTextBlock.Text = ProductVersionLabel;
        ApplyLocalizationRecursive(this);
        ApplyLocalizedOverrides();
        ApplyCurrentSection();

        if (_lastDoctorReport is null)
        {
            DoctorOutputTextBox.Text = _localizer["doctorPlaceholder"];
        }
    }

    private void ApplyLocalizedOverrides()
    {
        ManagedJdkInstallRootLabelTextBlock.Text = Localize(
            "Managed JDK Install Directory",
            "受管 JDK 安装目录");
        ManagedMavenInstallRootLabelTextBlock.Text = Localize(
            "Managed Maven Install Directory",
            "受管 Maven 安装目录");
        SaveManagedRootsButton.Content = Localize(
            "Save and Migrate Install Directories",
            "保存并迁移安装目录");
        LoadMavenSettingsButton.Content = Localize(
            "Load Mirrors From Config File",
            "读取配置文件镜像");
        ImportMirrorXmlButton.Content = Localize(
            "Import Mirror XML",
            "导入镜像 XML");
        MavenRepositoryMigrationCheckBox.Content = Localize(
            "Move existing local repository contents when the repository directory changes.",
            "当本地仓库目录变更时，一并迁移已有仓库内容。");
        UpdateSelectedMirrorButton.Content = Localize(
            "Update Selected Mirror",
            "更新选中镜像");
        SaveMavenSettingsButton.Content = Localize(
            "Save Maven Configuration",
            "保存 Maven 配置");
        UpdateSelectedJdkDownloadSourceButton.Content = Localize(
            "Update Selected JDK Source",
            "更新选中的 JDK 下载源");
        AddCustomJdkDownloadSourceButton.Content = Localize(
            "Add Custom JDK Source",
            "添加自定义 JDK 下载源");
        RemoveSelectedJdkDownloadSourceButton.Content = Localize(
            "Remove Selected JDK Override",
            "移除选中的 JDK 覆盖源");
        UpdateSelectedMavenDownloadSourceButton.Content = Localize(
            "Update Selected Maven Source",
            "更新选中的 Maven 下载源");
        AddCustomMavenDownloadSourceButton.Content = Localize(
            "Add Custom Maven Source",
            "添加自定义 Maven 下载源");
        RemoveSelectedMavenDownloadSourceButton.Content = Localize(
            "Remove Selected Maven Override",
            "移除选中的 Maven 覆盖源");
        OpenMavenSettingsFileButton.Content = Localize(
            "Open",
            "打开");
        OpenMavenToolchainsFileButton.Content = Localize(
            "Open",
            "打开");
        RemoteJdkAvailabilityHintTextBlock.Text = Localize(
            "Only versions that can be downloaded by the selected source should be used.",
            "请优先选择当前下载源可直接下载的 JDK 版本。");
        RemoteMavenAvailabilityHintTextBlock.Text = Localize(
            "Unavailable versions stay visible so you can tell which mirror is missing which package.",
            "列表会保留不可下载版本，方便判断当前镜像缺哪些包。");
        DownloadSourcesTitleTextBlock.Text = Localize(
            "Download Sources",
            "下载源");
        DownloadSourcesDescriptionTextBlock.Text = Localize(
            "Manage the active JDK and Maven download mirrors here. Changes are reflected immediately in the remote install page.",
            "在这里管理 JDK 和 Maven 的下载镜像源。切换后会立即刷新远程安装页的可下载状态。");
        RemoteJdkDownloadSourceLabelTextBlock.Text = Localize(
            "JDK Download Source",
            "JDK 下载源");
        RemoteMavenDownloadSourceLabelTextBlock.Text = Localize(
            "Maven Download Source",
            "Maven 下载源");
        SettingsJdkDownloadSourceLabelTextBlock.Text = Localize(
            "JDK Download Source",
            "JDK 下载源");
        JdkDownloadSourceProvidersLabelTextBlock.Text = Localize(
            "Supported Providers",
            "支持的发行方");
        SettingsMavenDownloadSourceLabelTextBlock.Text = Localize(
            "Maven Download Source",
            "Maven 下载源");
        SettingsConfigurationScopeLabelTextBlock.Text = Localize(
            "Configuration Scope",
            "配置作用域");
        SettingsToolchainsFilePathLabelTextBlock.Text = Localize(
            "toolchains.xml Path",
            "toolchains.xml 路径");
        ManagedJdkInstallRootLabelTextBlock.Text = Localize("Managed JDK Install Directory", "受管 JDK 安装目录");
        ManagedMavenInstallRootLabelTextBlock.Text = Localize("Managed Maven Install Directory", "受管 Maven 安装目录");
        SaveManagedRootsButton.Content = Localize("Save and Migrate Install Directories", "保存并迁移安装目录");
        MavenConfigNavButton.Content = Localize("Maven Config", "Maven 配置");
        LoadMavenSettingsButton.Content = Localize("Read Mirrors From Config File", "读取配置文件镜像");
        LoadMavenToolchainsButton.Content = Localize("Read Toolchains From Config File", "读取配置文件中的 toolchains");
        ImportMirrorXmlButton.Content = Localize("Import Mirror XML", "导入镜像 XML");
        MavenRepositoryMigrationCheckBox.Content = Localize(
            "Move existing local repository contents when the repository directory changes.",
            "当本地仓库目录变化时，一并迁移已有仓库内容。");
        SaveMavenSettingsButton.Content = Localize("Save Maven Configuration", "保存 Maven 配置");
        AddBuiltInMirrorButton.Content = Localize("Insert Built-in Mirror", "插入内置镜像");
        OpenMavenSettingsFileButton.Content = Localize("Open", "打开");
        OpenMavenToolchainsFileButton.Content = Localize("Open", "打开");
        SettingsConfigurationScopeLabelTextBlock.Text = Localize("Configuration Scope", "配置作用域");
        SettingsToolchainsFilePathLabelTextBlock.Text = Localize("toolchains.xml Path", "toolchains.xml 路径");
        MavenConfigurationScopeHintTextBlock.Text = Localize(
            "User scope writes to ~/.m2. Global scope writes to the selected Maven installation under conf.",
            "用户级配置写入当前用户的 .m2 目录；全局配置写入当前选定 Maven 安装目录下的 conf。");
        MirrorXmlEditorLabelTextBlock.Text = Localize("Mirror XML", "镜像 XML");
        MavenMirrorsXmlEditorHintTextBlock.Text = Localize(
            "Edit the <mirrors> XML directly. TaoMaster only updates the mirrors node and localRepository value in settings.xml.",
            "直接编辑 <mirrors> XML。TaoMaster 只会更新 settings.xml 中的 mirrors 节点和 localRepository 值。");
        ToolchainsJdkSelectorLabelTextBlock.Text = Localize("Insert Installed JDK", "插入已安装的 JDK");
        InsertSelectedJdkToolchainButton.Content = Localize("Insert Selected JDK", "插入所选 JDK");
        ToolchainsXmlEditorLabelTextBlock.Text = Localize("Toolchains XML", "Toolchains XML");
        ToolchainsXmlEditorHintTextBlock.Text = Localize(
            "Edit the full toolchains.xml here. TaoMaster can insert installed JDK entries and keeps the file formatted when saving.",
            "在这里编辑完整的 toolchains.xml。TaoMaster 可以插入已安装的 JDK 条目，并在保存时整理文件格式。");
        MavenConfigTargetTitleTextBlock.Text = Localize("Current Target", "当前目标");
        MavenConfigTargetDescriptionTextBlock.Text = Localize(
            "Global scope writes to the selected Maven installation. User scope writes to the current user's .m2 directory.",
            "全局配置会写入当前选定的 Maven 安装；用户级配置会写入当前用户的 .m2 目录。");
        MavenConfigCurrentScopeLabelTextBlock.Text = Localize("Scope", "作用域");
        MavenConfigCurrentMavenLabelTextBlock.Text = Localize("Target Maven", "目标 Maven");
        MavenConfigEffectiveSettingsLabelTextBlock.Text = Localize("Effective settings.xml", "生效中的 settings.xml");
        MavenConfigEffectiveToolchainsLabelTextBlock.Text = Localize("Effective toolchains.xml", "生效中的 toolchains.xml");
        MavenConfigEditorGuideTitleTextBlock.Text = Localize("Editing Guide", "编辑说明");
        MavenConfigIntroTextBlock.Text = Localize(
            "Configure settings.xml, toolchains.xml, the local repository path, and mirror XML for your Maven and IDE environment.",
            "为 Maven 与 IDE 开发环境配置 settings.xml、toolchains.xml、本地仓库目录和镜像 XML。");
        MavenConfigEditorGuideTextBlock.Text = Localize(
            "Use 'Read Mirrors From Config File' to load the current mirrors, edit the XML directly, then save. Opening settings.xml or toolchains.xml will normalize the file format before launching Notepad.",
            "先用“读取配置文件镜像”载入当前镜像，再直接编辑 XML 并保存。打开 settings.xml 或 toolchains.xml 前，程序会先整理文件格式。");
        RemoteJdkAvailabilityHintTextBlock.Text = Localize(
            "Only versions confirmed against the official download links are shown as ready.",
            "仅将已确认可从官方链接下载的版本标记为可用。");
        RemoteMavenAvailabilityHintTextBlock.Text = Localize(
            "Versions are checked against Apache's official download site in real time.",
            "版本会实时对照 Apache 官网下载地址进行校验。");
    }

    private string Localize(string english, string chinese) =>
        _localizer.Language == AppLanguage.SimplifiedChinese ? chinese : english;

    private void ApplyLanguage(AppLanguage language, bool persistPreference)
    {
        _localizer = new AppLocalizer(language);
        SelectLanguageOption(language);
        ApplyLocalization();
        RefreshStateBindings(GetSelectedInstallationId(JdkListBox), GetSelectedInstallationId(MavenListBox));

        if (persistPreference)
        {
            PersistLanguagePreference(language);
        }
    }

    private void RefreshLocalizedView()
    {
        ApplyLocalization();
        RefreshStateBindings(GetSelectedInstallationId(JdkListBox), GetSelectedInstallationId(MavenListBox));
    }

    private void SelectLanguageOption(AppLanguage language)
    {
        _suppressLanguageSelectionChanged = true;
        LanguageComboBox.SelectedItem = GetLanguageOptions()
            .First(option => option.Language == language);
        _suppressLanguageSelectionChanged = false;
    }

    private static IReadOnlyList<LanguageOption> GetLanguageOptions() =>
    [
        new LanguageOption(AppLanguage.English, "English"),
        new LanguageOption(AppLanguage.SimplifiedChinese, "简体中文")
    ];

    private IReadOnlyList<ConfigurationScopeOption> BuildConfigurationScopeOptions() =>
    [
        new ConfigurationScopeOption(MavenConfigurationScope.User, Localize("User Configuration", "用户级配置")),
        new ConfigurationScopeOption(MavenConfigurationScope.Global, Localize("Global Configuration", "全局配置"))
    ];

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
        ApplyLocalizationRecursive(root, new HashSet<DependencyObject>());
    }

    private void ApplyLocalizationRecursive(DependencyObject root, ISet<DependencyObject> visited)
    {
        if (!visited.Add(root))
        {
            return;
        }

        ApplyLocalizationToElement(root);

        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            ApplyLocalizationRecursive(child, visited);
        }

        if (root is not Visual && root is not Visual3D)
        {
            return;
        }

        var childCount = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < childCount; index++)
        {
            ApplyLocalizationRecursive(VisualTreeHelper.GetChild(root, index), visited);
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
            case WpfCheckBox checkBox when checkBox.Tag is string key:
                checkBox.Content = _localizer[key];
                break;
            case Run run when run.Tag is string key:
                run.Text = _localizer[key];
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
        ManagedJdkInstallRootTextBox.Text = _state.Settings.ManagedJdkInstallRoot;
        ManagedMavenInstallRootTextBox.Text = _state.Settings.ManagedMavenInstallRoot;
        PathModeTextBlock.Text = BuildPathModeText();
        AppVersionTextBlock.Text = ProductInfo.Version;
        ShellSyncStatusTextBlock.Text = BuildShellSyncStatusText();
        ShellSyncDetailTextBlock.Text = BuildShellSyncDetailText();
        RefreshConfigurationScopeSelector();
        RefreshConfiguredMavenMirrors();
        RefreshMavenConfigurationBindings();
        RefreshJdkDownloadSources();
        RefreshMavenDownloadSources();

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
        ToolchainsJdkComboBox.ItemsSource = _state.Jdks.ToList();

        SelectInstallation(JdkListBox, _state.Jdks, preferredJdkId ?? _state.ActiveSelection.JdkId);
        SelectInstallation(MavenListBox, _state.Mavens, preferredMavenId ?? _state.ActiveSelection.MavenId);
        SelectInstallationInComboBox(DashboardJdkComboBox, _state.Jdks, preferredJdkId ?? _state.ActiveSelection.JdkId);
        SelectInstallationInComboBox(DashboardMavenComboBox, _state.Mavens, preferredMavenId ?? _state.ActiveSelection.MavenId);
        SelectInstallationInComboBox(ToolchainsJdkComboBox, _state.Jdks, preferredJdkId ?? _state.ActiveSelection.JdkId);

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

    private void RefreshConfigurationScopeSelector()
    {
        _suppressConfigurationScopeSelectionChanged = true;
        var options = BuildConfigurationScopeOptions();
        MavenConfigurationScopeComboBox.ItemsSource = options;
        MavenConfigurationScopeComboBox.SelectedItem = options.First(option => option.Scope == _state.Settings.MavenConfigurationScope);
        _suppressConfigurationScopeSelectionChanged = false;
    }

    private void RefreshConfiguredMavenMirrors()
    {
        _configuredMavenMirrors.Clear();
        foreach (var mirror in _state.Settings.MavenMirrors)
        {
            _configuredMavenMirrors.Add(mirror);
        }

        if (ConfiguredMavenMirrorsListBox.SelectedItem is not MavenMirrorConfiguration selectedMirror)
        {
            PopulateMavenMirrorEditor(null);
            return;
        }

        ConfiguredMavenMirrorsListBox.SelectedItem = _configuredMavenMirrors.FirstOrDefault(mirror =>
            mirror.Id.Equals(selectedMirror.Id, StringComparison.OrdinalIgnoreCase));
        PopulateMavenMirrorEditor(ConfiguredMavenMirrorsListBox.SelectedItem as MavenMirrorConfiguration);
    }

    private void RefreshMavenDownloadSources()
    {
        _suppressDownloadSourceSelectionChanged = true;
        _availableMavenDownloadSources.Clear();
        foreach (var source in _mavenSource.GetBuiltInDownloadSources()
                     .Where(source => source.Id.Equals("apache-official", StringComparison.OrdinalIgnoreCase)))
        {
            _availableMavenDownloadSources.Add(source);
        }

        var selected = _availableMavenDownloadSources.FirstOrDefault();
        RemoteMavenDownloadSourceComboBox.SelectedItem = selected;
        SettingsMavenDownloadSourceComboBox.SelectedItem = selected;
        MavenDownloadSourceNameTextBox.Text = selected?.Name ?? string.Empty;
        MavenDownloadSourceUrlTextBox.Text = selected?.BaseUrl ?? string.Empty;
        _suppressDownloadSourceSelectionChanged = false;
    }

    private void RefreshJdkDownloadSources()
    {
        _suppressDownloadSourceSelectionChanged = true;
        _availableJdkDownloadSources.Clear();
        foreach (var source in _jdkDownloadSourceService.GetBuiltInSources()
                     .Where(source => source.Id.Equals("jdk-official", StringComparison.OrdinalIgnoreCase)))
        {
            _availableJdkDownloadSources.Add(source);
        }

        var selected = _availableJdkDownloadSources.FirstOrDefault();
        RemoteJdkDownloadSourceComboBox.SelectedItem = selected;
        SettingsJdkDownloadSourceComboBox.SelectedItem = selected;
        JdkDownloadSourceNameTextBox.Text = selected?.Name ?? string.Empty;
        JdkDownloadSourceUrlPrefixTextBox.Text = selected?.UrlPrefix ?? string.Empty;
        JdkDownloadSourceProvidersTextBox.Text = selected?.SupportedProviders ?? "*";
        _suppressDownloadSourceSelectionChanged = false;
    }

    private ManagedInstallation? ResolveMavenInstallationForConfiguration()
    {
        var selection = _selectionResolver.Resolve(_state);
        return MavenListBox.SelectedItem as ManagedInstallation
               ?? DashboardMavenComboBox.SelectedItem as ManagedInstallation
               ?? selection.Maven
               ?? _state.Mavens.FirstOrDefault();
    }

    private bool TryResolveMavenConfigurationPaths(
        MavenConfigurationScope scope,
        out string settingsFilePath,
        out string toolchainsFilePath)
    {
        try
        {
            var mavenHome = scope == MavenConfigurationScope.Global
                ? ResolveMavenInstallationForConfiguration()?.HomeDirectory
                : null;

            settingsFilePath = _mavenConfigurationService.ResolveSettingsFilePath(scope, mavenHome);
            toolchainsFilePath = _mavenConfigurationService.ResolveToolchainsFilePath(scope, mavenHome);
            return true;
        }
        catch (InvalidOperationException)
        {
            settingsFilePath = string.Empty;
            toolchainsFilePath = string.Empty;
            return false;
        }
    }

    private void RefreshMavenConfigurationBindings(bool forceEditorRefresh = false)
    {
        var scope = _state.Settings.MavenConfigurationScope;
        var isUserScope = scope == MavenConfigurationScope.User;
        var hasEffectivePaths = TryResolveMavenConfigurationPaths(scope, out var effectiveSettingsPath, out var effectiveToolchainsPath);
        var effectiveMaven = ResolveMavenInstallationForConfiguration();

        MavenSettingsFilePathTextBox.Text = isUserScope ? _state.Settings.MavenSettingsFilePath : effectiveSettingsPath;
        MavenToolchainsFilePathTextBox.Text = isUserScope ? _state.Settings.MavenToolchainsFilePath : effectiveToolchainsPath;
        MavenLocalRepositoryPathTextBox.Text = _state.Settings.MavenLocalRepositoryPath;
        MavenSettingsFilePathTextBox.IsReadOnly = !isUserScope;
        MavenToolchainsFilePathTextBox.IsReadOnly = !isUserScope;
        BrowseMavenSettingsFileButton.IsEnabled = isUserScope;
        BrowseMavenToolchainsFileButton.IsEnabled = isUserScope;
        OpenMavenSettingsFileButton.IsEnabled = hasEffectivePaths;
        OpenMavenToolchainsFileButton.IsEnabled = hasEffectivePaths;

        MavenConfigCurrentScopeTextBlock.Text = isUserScope
            ? Localize("User Configuration", "用户级配置")
            : Localize("Global Configuration", "全局配置");
        MavenConfigCurrentMavenTextBlock.Text = effectiveMaven?.DisplayName ?? _localizer["nonePlaceholder"];
        MavenConfigCurrentMavenPathTextBlock.Text = effectiveMaven?.HomeDirectory
                                                    ?? Localize(
                                                        "Select or activate a Maven installation to target global configuration.",
                                                        "请选择或激活一个 Maven 安装，以便定位全局配置。");
        MavenConfigEffectiveSettingsPathTextBlock.Text = hasEffectivePaths
            ? effectiveSettingsPath
            : Localize("Unavailable until a Maven installation is selected.", "在选择 Maven 安装前不可用。");
        MavenConfigEffectiveToolchainsPathTextBlock.Text = hasEffectivePaths
            ? effectiveToolchainsPath
            : Localize("Unavailable until a Maven installation is selected.", "在选择 Maven 安装前不可用。");
        MavenConfigurationScopeHintTextBlock.Text = isUserScope
            ? Localize(
                "User scope writes to the current user's .m2 directory.",
                "用户级配置写入当前用户的 .m2 目录。")
            : effectiveMaven is null
                ? Localize(
                    "Select or activate a Maven installation before using global configuration files.",
                    "使用全局配置文件前，请先选择或激活一个 Maven 安装。")
                : FormatLocalized(
                    "Global scope writes to {0}\\conf.",
                    "全局配置将写入 {0}\\conf。",
                    effectiveMaven.HomeDirectory);

        RefreshMavenMirrorsXmlEditor(forceEditorRefresh);
        RefreshToolchainsXmlEditor(forceEditorRefresh);
    }

    private void RefreshMavenMirrorsXmlEditor(bool force = false)
    {
        if (!force && _mavenMirrorsEditorDirty)
        {
            return;
        }

        SetMavenMirrorsEditorText(_mavenConfigurationService.BuildMirrorsXml(_state.Settings.MavenMirrors));
    }

    private void SetMavenMirrorsEditorText(string xmlContent)
    {
        _suppressMirrorEditorTextChanged = true;
        MavenMirrorsXmlEditorTextBox.Text = xmlContent;
        _suppressMirrorEditorTextChanged = false;
        _mavenMirrorsEditorDirty = false;
    }

    private void RefreshToolchainsXmlEditor(bool force = false)
    {
        if (!force && _toolchainsEditorDirty)
        {
            return;
        }

        var toolchainsFilePath = ResolveConfiguredMavenToolchainsFilePath();
        var xmlContent = string.IsNullOrWhiteSpace(toolchainsFilePath)
            ? _mavenConfigurationService.NormalizeToolchainsXml(string.Empty)
            : _mavenConfigurationService.ReadToolchainsXml(toolchainsFilePath);

        SetToolchainsEditorText(xmlContent);
    }

    private void SetToolchainsEditorText(string xmlContent)
    {
        _suppressToolchainsEditorTextChanged = true;
        ToolchainsXmlEditorTextBox.Text = xmlContent;
        _suppressToolchainsEditorTextChanged = false;
        _toolchainsEditorDirty = false;
    }

    private IReadOnlyList<MavenMirrorConfiguration> ReadMirrorsFromEditor()
    {
        var xmlContent = MavenMirrorsXmlEditorTextBox.Text.Trim();
        return string.IsNullOrWhiteSpace(xmlContent)
            ? Array.Empty<MavenMirrorConfiguration>()
            : _mavenConfigurationService.ImportMirrorsFromXmlContent(xmlContent);
    }

    private string ResolveConfiguredMavenToolchainsFilePath()
    {
        var scope = _state.Settings.MavenConfigurationScope;
        if (scope == MavenConfigurationScope.User)
        {
            return _state.Settings.MavenToolchainsFilePath;
        }

        return TryResolveMavenConfigurationPaths(scope, out _, out var toolchainsFilePath)
            ? toolchainsFilePath
            : string.Empty;
    }

    private MavenJdkToolchainConfiguration BuildJdkToolchainConfiguration(ManagedInstallation installation)
    {
        var version = NormalizeToolchainVersion(installation.Version);
        var vendor = string.IsNullOrWhiteSpace(installation.Vendor)
            ? null
            : installation.Vendor.Trim().ToLowerInvariant();
        var architecture = string.IsNullOrWhiteSpace(installation.Architecture)
            ? null
            : installation.Architecture.Trim().ToLowerInvariant();

        return new MavenJdkToolchainConfiguration(
            JdkHome: installation.HomeDirectory,
            Version: version,
            Vendor: vendor,
            Architecture: architecture);
    }

    private static string NormalizeToolchainVersion(string version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return "17";
        }

        var normalized = version.Trim();
        if (normalized.StartsWith("1.8", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("8.", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("8u", StringComparison.OrdinalIgnoreCase))
        {
            return "8";
        }

        var featureText = normalized.Split(['.', '+', '-'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();

        return int.TryParse(featureText, out var featureVersion) && featureVersion > 0
            ? featureVersion.ToString(CultureInfo.InvariantCulture)
            : normalized;
    }

    private void PopulateMavenMirrorEditor(MavenMirrorConfiguration? mirror)
    {
        if (mirror is null)
        {
            CustomMavenMirrorNameTextBox.Clear();
            CustomMavenMirrorUrlTextBox.Clear();
            CustomMavenMirrorOfTextBox.Text = "*";
            return;
        }

        CustomMavenMirrorNameTextBox.Text = mirror.Name;
        CustomMavenMirrorUrlTextBox.Text = mirror.Url;
        CustomMavenMirrorOfTextBox.Text = string.IsNullOrWhiteSpace(mirror.MirrorOf) ? "*" : mirror.MirrorOf;
    }

    private WorkspaceLayout GetManagedLayout() =>
        _managedInstallLayoutService.Resolve(_layout, _state.Settings);

    private void SyncMavenSettingsFromFile(bool persistState, string? settingsFilePathOverride = null)
    {
        var selectedPath = string.IsNullOrWhiteSpace(settingsFilePathOverride)
            ? ResolveConfiguredMavenSettingsFilePath()
            : settingsFilePathOverride.Trim();

        if (string.IsNullOrWhiteSpace(selectedPath))
        {
            return;
        }

        var snapshot = _mavenConfigurationService.ReadSettingsSnapshot(selectedPath);
        _state = _state with
        {
            Settings = _state.Settings with
            {
                MavenSettingsFilePath = _state.Settings.MavenConfigurationScope == MavenConfigurationScope.User
                    ? snapshot.SettingsFilePath
                    : _state.Settings.MavenSettingsFilePath,
                MavenLocalRepositoryPath = snapshot.LocalRepositoryPath,
                MavenMirrors = snapshot.Mirrors
            }
        };

        if (persistState)
        {
            _stateStore.Save(_layout, _state);
        }
    }

    private string ResolveConfiguredMavenSettingsFilePath()
    {
        var scope = _state.Settings.MavenConfigurationScope;
        if (scope == MavenConfigurationScope.User)
        {
            return _state.Settings.MavenSettingsFilePath;
        }

        return TryResolveMavenConfigurationPaths(scope, out var settingsFilePath, out _)
            ? settingsFilePath
            : string.Empty;
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
        MavenConfigPage.Visibility = _currentSection == AppSection.MavenConfig ? Visibility.Visible : Visibility.Collapsed;

        if (_currentSection == AppSection.MavenConfig)
        {
            PageTitleTextBlock.Text = Localize("Maven Config", "Maven 配置");
            PageDescriptionTextBlock.Text = Localize(
                "Manage settings.xml, toolchains.xml, the local repository, and mirror XML for Maven and IDE builds.",
                "集中管理 Maven 与 IDE 构建所使用的 settings.xml、toolchains.xml、本地仓库和镜像 XML。");
        }
        else if (_currentSection == AppSection.Settings)
        {
            PageTitleTextBlock.Text = _localizer["pageSettingsTitle"];
            PageDescriptionTextBlock.Text = Localize(
                "Workspace paths, managed install roots, language, and environment behavior are grouped here.",
                "这里集中管理工作区路径、受管安装目录、界面语言和环境行为。");
        }
        else
        {
            PageTitleTextBlock.Text = _localizer[GetSectionTitleKey(_currentSection)];
            PageDescriptionTextBlock.Text = _localizer[GetSectionDescriptionKey(_currentSection)];
        }

        ApplyNavigationButtonState(DashboardNavButton, _currentSection == AppSection.Dashboard);
        ApplyNavigationButtonState(VersionsNavButton, _currentSection == AppSection.Versions);
        ApplyNavigationButtonState(ProjectsNavButton, _currentSection == AppSection.Projects);
        ApplyNavigationButtonState(DiagnosticsNavButton, _currentSection == AppSection.Diagnostics);
        ApplyNavigationButtonState(SettingsNavButton, _currentSection == AppSection.Settings);
        ApplyNavigationButtonState(MavenConfigNavButton, _currentSection == AppSection.MavenConfig);
    }

    private static string GetSectionTitleKey(AppSection section) =>
        section switch
        {
            AppSection.Dashboard => "pageDashboardTitle",
            AppSection.Versions => "pageVersionsTitle",
            AppSection.Projects => "pageProjectsTitle",
            AppSection.Diagnostics => "pageDiagnosticsTitle",
            AppSection.Settings => "pageSettingsTitle",
            AppSection.MavenConfig => "pageSettingsTitle",
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
            AppSection.MavenConfig => "pageSettingsDescription",
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
        var currentJdkPackage = RemoteJdkVersionComboBox.SelectedItem as RemotePackageDescriptor;
        var currentMavenPackage = RemoteMavenVersionComboBox.SelectedItem as RemotePackageDescriptor;
        var selectedJdkSource = _jdkDownloadSourceService.GetBuiltInSources()
            .First(source => source.Id.Equals("jdk-official", StringComparison.OrdinalIgnoreCase));

        var temurinTask = _temurinSource.GetLatestPackagesByFeatureAsync("x64", CancellationToken.None);
        var oracleTask = _oracleSource.GetAvailablePackagesAsync(CancellationToken.None);
        var mavenTask = _mavenSource.GetAvailablePackagesAsync("apache-official", null, CancellationToken.None);

        await Task.WhenAll(temurinTask, oracleTask, mavenTask);

        var rawJdkVersions = (await temurinTask)
            .Concat(await oracleTask)
            .OrderByDescending(package => ParseRemoteJdkSortKey(package.Version))
            .ThenBy(package => package.Provider, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var jdkVersions = await BuildRemoteJdkOptionsAsyncV2(rawJdkVersions, selectedJdkSource);
        var mavenVersions = (await mavenTask)
            .Select(WithAvailabilityMessage)
            .OrderByDescending(package => package.IsDownloadAvailable)
            .ThenByDescending(package => package.Version, TaoMaster.Core.Utilities.VersionStringComparer.Instance)
            .ToList();

        RemoteJdkVersionComboBox.ItemsSource = jdkVersions;
        RemoteJdkVersionComboBox.SelectedItem = currentJdkPackage is not null
                                                 && jdkVersions.Any(package =>
                                                     package.Version.Equals(currentJdkPackage.Version, StringComparison.OrdinalIgnoreCase)
                                                     && package.Provider.Equals(currentJdkPackage.Provider, StringComparison.OrdinalIgnoreCase)
                                                     && string.Equals(package.DownloadSourceId, currentJdkPackage.DownloadSourceId, StringComparison.OrdinalIgnoreCase))
            ? jdkVersions.First(package =>
                package.Version.Equals(currentJdkPackage.Version, StringComparison.OrdinalIgnoreCase)
                && package.Provider.Equals(currentJdkPackage.Provider, StringComparison.OrdinalIgnoreCase)
                && string.Equals(package.DownloadSourceId, currentJdkPackage.DownloadSourceId, StringComparison.OrdinalIgnoreCase))
            : jdkVersions.FirstOrDefault();

        RemoteMavenVersionComboBox.ItemsSource = mavenVersions;
        RemoteMavenVersionComboBox.SelectedItem = currentMavenPackage is not null
                                                 && mavenVersions.Any(package =>
                                                     package.Version.Equals(currentMavenPackage.Version, StringComparison.OrdinalIgnoreCase)
                                                     && string.Equals(package.DownloadSourceId, currentMavenPackage.DownloadSourceId, StringComparison.OrdinalIgnoreCase))
            ? mavenVersions.First(package =>
                package.Version.Equals(currentMavenPackage.Version, StringComparison.OrdinalIgnoreCase)
                && string.Equals(package.DownloadSourceId, currentMavenPackage.DownloadSourceId, StringComparison.OrdinalIgnoreCase))
            : mavenVersions.FirstOrDefault();

        UpdateRemoteAvailabilityHints(jdkVersions, mavenVersions);
    }

    private async Task<IReadOnlyList<RemotePackageDescriptor>> BuildRemoteJdkOptionsAsync(
        IReadOnlyList<RemotePackageDescriptor> packages,
        JdkDownloadSourceConfiguration source)
    {
        var tasks = packages.Select(async package =>
        {
            var transformed = _jdkDownloadSourceService.ApplySource(package, source);
            if (!transformed.IsDownloadAvailable)
            {
                return transformed with
                {
                    AvailabilityMessage = Localize("Unsupported by source", "当前下载源不支持")
                };
            }

            var exists = await UrlExistsAsync(transformed.DownloadUrl, CancellationToken.None);
            return transformed with
            {
                IsDownloadAvailable = exists,
                AvailabilityMessage = exists
                    ? Localize("Ready", "可下载")
                    : Localize("Unavailable", "当前源不可下载")
            };
        });

        var resolved = await Task.WhenAll(tasks);
        return resolved
            .OrderByDescending(package => package.IsDownloadAvailable)
            .ThenByDescending(package => ParseRemoteJdkSortKey(package.Version))
            .ThenBy(package => package.Provider, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static int ParseRemoteJdkSortKey(string version)
    {
        var featureText = version.StartsWith("8u", StringComparison.OrdinalIgnoreCase)
            ? "8"
            : version.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? version;

        return int.TryParse(featureText, out var featureVersion) ? featureVersion : 0;
    }

    private async Task<IReadOnlyList<RemotePackageDescriptor>> BuildRemoteJdkOptionsAsyncV2(
        IReadOnlyList<RemotePackageDescriptor> packages,
        JdkDownloadSourceConfiguration source)
    {
        var tasks = packages.Select(async package =>
        {
            var transformed = _jdkDownloadSourceService.ApplySource(package, source);
            if (!transformed.IsDownloadAvailable)
            {
                return WithAvailabilityMessage(transformed);
            }

            var exists = await UrlExistsAsync(transformed.DownloadUrl, CancellationToken.None);
            return WithAvailabilityMessage(transformed with
            {
                IsDownloadAvailable = exists
            });
        });

        var resolved = await Task.WhenAll(tasks);
        return resolved
            .OrderByDescending(package => package.IsDownloadAvailable)
            .ThenByDescending(package => ParseRemoteJdkSortKey(package.Version))
            .ThenBy(package => package.Provider, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private RemotePackageDescriptor WithAvailabilityMessage(RemotePackageDescriptor package)
    {
        var sourceName = package.DownloadSourceName ?? package.Provider;
        if (package.IsDownloadAvailable)
        {
            return package with
            {
                AvailabilityMessage = FormatLocalized(
                    "Ready in {0}",
                    "{0} 可下载",
                    sourceName)
            };
        }

        var unsupported = !string.IsNullOrWhiteSpace(package.AvailabilityMessage)
                          && package.AvailabilityMessage.Contains("support", StringComparison.OrdinalIgnoreCase);
        return package with
        {
            AvailabilityMessage = unsupported
                ? FormatLocalized(
                    "{0} does not support this vendor",
                    "{0} 不支持当前发行方",
                    sourceName)
                : FormatLocalized(
                    "Unavailable in {0}",
                    "{0} 暂不可下载",
                    sourceName)
        };
    }

    private void UpdateRemoteAvailabilityHints(
        IReadOnlyList<RemotePackageDescriptor> jdkVersions,
        IReadOnlyList<RemotePackageDescriptor> mavenVersions)
    {
        RemoteJdkAvailabilityHintTextBlock.Text = BuildAvailabilitySummary(
            jdkVersions,
            Localize("official source", "官方源"),
            "JDK");
        RemoteMavenAvailabilityHintTextBlock.Text = BuildAvailabilitySummary(
            mavenVersions,
            Localize("official source", "官方源"),
            "Maven");
    }

    private string BuildAvailabilitySummary(
        IReadOnlyList<RemotePackageDescriptor> packages,
        string sourceName,
        string label)
    {
        if (packages.Count == 0)
        {
            return FormatLocalized(
                "No remote {0} packages were loaded for {1}.",
                "{1} 当前没有加载到远程 {0} 包。",
                label,
                sourceName);
        }

        var availableCount = packages.Count(package => package.IsDownloadAvailable);
        return availableCount == packages.Count
            ? FormatLocalized(
                "{0} remote {1} packages are ready in {2}.",
                "{2} 当前可下载 {0} 个远程 {1} 包。",
                availableCount,
                label,
                sourceName)
            : FormatLocalized(
                "{0}/{1} remote {2} packages are ready in {3}.",
                "{3} 当前可下载 {0}/{1} 个远程 {2} 包。",
                availableCount,
                packages.Count,
                label,
                sourceName);
    }

    private async Task<bool> UrlExistsAsync(string url, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        try
        {
            using var headRequest = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Head, url);
            using var headResponse = await _httpClient.SendAsync(
                headRequest,
                System.Net.Http.HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (headResponse.IsSuccessStatusCode)
            {
                return true;
            }
        }
        catch
        {
            // Some mirrors reject HEAD. Fall back to GET.
        }

        try
        {
            using var getRequest = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, url);
            using var getResponse = await _httpClient.SendAsync(
                getRequest,
                System.Net.Http.HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            return getResponse.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
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
            System.Windows.MessageBox.Show(
                this,
                failure.Message,
                _localizer["errorTitle"],
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async Task ExecuteBusyWithMessageAsync(string busyMessage, Func<Task<string?>> operation, Action<string>? onSuccess = null)
    {
        SetBusy(true, busyMessage, _localizer["busyDetailPreparing"], null);

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

    private void ShowSuccessDialog(string message)
    {
        System.Windows.MessageBox.Show(
            this,
            message,
            _localizer["successTitle"],
            MessageBoxButton.OK,
            MessageBoxImage.Information);
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
        ShowValidationMessage(message);
    }

    private void ShowValidationMessage(string message)
    {
        SetStatus(message);
        System.Windows.MessageBox.Show(
            this,
            message,
            _localizer["warningTitle"],
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    private string FormatLocalized(string english, string chinese, params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, Localize(english, chinese), args);

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

    private void OnMavenConfigNavClicked(object sender, RoutedEventArgs e)
    {
        _currentSection = AppSection.MavenConfig;
        ApplyCurrentSection();
    }

    private void OnBrowseJdkImportClicked(object sender, RoutedEventArgs e)
    {
        BrowseForFolderByKey(JdkImportPathTextBox, "browseJdkDescription");
    }

    private void OnBrowseMavenImportClicked(object sender, RoutedEventArgs e)
    {
        BrowseForFolderByKey(MavenImportPathTextBox, "browseMavenDescription");
    }

    private void OnBrowseMavenSettingsFileClicked(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "Maven settings.xml|settings.xml|XML files|*.xml",
            FileName = Path.GetFileName(_state.Settings.MavenSettingsFilePath),
            InitialDirectory = Path.GetDirectoryName(MavenSettingsFilePathTextBox.Text) ?? Path.GetDirectoryName(_state.Settings.MavenSettingsFilePath)
        };

        if (dialog.ShowDialog(this) == true)
        {
            MavenSettingsFilePathTextBox.Text = dialog.FileName;
        }
    }

    private void OnBrowseMavenLocalRepositoryClicked(object sender, RoutedEventArgs e)
    {
        BrowseForFolderByKey(MavenLocalRepositoryPathTextBox, "browseMavenLocalRepositoryDescription");
    }

    private void OnBrowseManagedJdkInstallRootClicked(object sender, RoutedEventArgs e)
    {
        BrowseForFolder(
            ManagedJdkInstallRootTextBox,
            Localize("Select a managed JDK install directory", "选择受管 JDK 安装目录"));
    }

    private void OnBrowseManagedMavenInstallRootClicked(object sender, RoutedEventArgs e)
    {
        BrowseForFolder(
            ManagedMavenInstallRootTextBox,
            Localize("Select a managed Maven install directory", "选择受管 Maven 安装目录"));
    }

    private void BrowseForFolderByKey(WpfTextBox textBox, string descriptionKey)
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

    private void BrowseForFolder(WpfTextBox textBox, string description)
    {
        using var dialog = new Forms.FolderBrowserDialog
        {
            Description = description,
            ShowNewFolderButton = false,
            SelectedPath = System.IO.Directory.Exists(textBox.Text) ? textBox.Text : string.Empty
        };

        if (dialog.ShowDialog() == Forms.DialogResult.OK)
        {
            textBox.Text = dialog.SelectedPath;
        }
    }

    #pragma warning disable CS0162
    private async void OnLoadMavenSettingsClicked(object sender, RoutedEventArgs e)
    {
        await ExecuteBusyWithMessageAsync(
            Localize("Loading Maven configuration...", "正在读取 Maven 配置..."),
            async () =>
            {
                var settingsFilePath = MavenSettingsFilePathTextBox.Text.Trim();
                await Task.Run(() => SyncMavenSettingsFromFile(persistState: true, settingsFilePathOverride: settingsFilePath));
                RefreshMavenMirrorsXmlEditor(force: true);
                RefreshStateBindings(GetSelectedInstallationId(JdkListBox), GetSelectedInstallationId(MavenListBox));
                return Localize("Loaded mirrors from the configuration file.", "已从配置文件读取镜像。");
            });
        return;

        await ExecuteBusyWithMessageAsync(
            Localize("Loading Maven settings...", "正在读取 Maven 配置..."),
            async () =>
            {
                await Task.Run(() => SyncMavenSettingsFromFile(
                    persistState: true,
                    settingsFilePathOverride: MavenSettingsFilePathTextBox.Text.Trim()));
                RefreshStateBindings(GetSelectedInstallationId(JdkListBox), GetSelectedInstallationId(MavenListBox));
                return Localize("Loaded mirrors from the config file.", "已读取配置文件中的镜像。");
            });
    }

    private async void OnLoadMavenToolchainsClicked(object sender, RoutedEventArgs e)
    {
        await ExecuteBusyWithMessageAsync(
            Localize("Loading Maven toolchains...", "正在读取 Maven toolchains 配置..."),
            async () =>
            {
                var xmlContent = await Task.Run(() =>
                    _mavenConfigurationService.ReadToolchainsXml(MavenToolchainsFilePathTextBox.Text.Trim()));
                SetToolchainsEditorText(xmlContent);
                return Localize("Loaded toolchains from the configuration file.", "已从配置文件读取 toolchains。");
            });
    }

    private void OnAddBuiltInMirrorClicked(object sender, RoutedEventArgs e)
    {
        if (BuiltInMavenMirrorComboBox.SelectedItem is not MavenMirrorConfiguration selectedMirror)
        {
            ShowValidationWarning("validationSelectBuiltInMirror");
            return;
        }

        var mergedMirrors = ReadMirrorsFromEditor()
            .Where(existing => !existing.Id.Equals(selectedMirror.Id, StringComparison.OrdinalIgnoreCase))
            .Concat([selectedMirror with { IsBuiltIn = true }])
            .OrderBy(existing => existing.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        SetMavenMirrorsEditorText(_mavenConfigurationService.BuildMirrorsXml(mergedMirrors));
        _mavenMirrorsEditorDirty = true;
        SetStatus(_localizer.Format("mavenMirrorAddedStatus", selectedMirror.Name));
        return;

        if (BuiltInMavenMirrorComboBox.SelectedItem is not MavenMirrorConfiguration mirror)
        {
            ShowValidationWarning("validationSelectBuiltInMirror");
            return;
        }

        AddMirrorToState(mirror with { IsBuiltIn = true });
    }

    private void OnInsertSelectedJdkToolchainClicked(object sender, RoutedEventArgs e)
    {
        if (ToolchainsJdkComboBox.SelectedItem is not ManagedInstallation installation)
        {
            ShowValidationMessage(Localize("Select a JDK first.", "请先选择一个 JDK。"));
            return;
        }

        try
        {
            var toolchain = BuildJdkToolchainConfiguration(installation);
            var updatedXml = _mavenConfigurationService.UpsertJdkToolchain(ToolchainsXmlEditorTextBox.Text, toolchain);
            SetToolchainsEditorText(updatedXml);
            _toolchainsEditorDirty = true;
            SetStatus(FormatLocalized(
                "Inserted JDK toolchain: {0}",
                "已插入 JDK toolchain：{0}",
                installation.DisplayName));
        }
        catch (Exception ex)
        {
            ShowValidationMessage(ex.Message);
        }
    }

    private void OnAddCustomMirrorClicked(object sender, RoutedEventArgs e)
    {
        var mirror = BuildMirrorFromEditor();
        if (mirror is null)
        {
            ShowValidationWarning("validationCustomMirror");
            return;
        }

        AddMirrorToState(mirror);
        PopulateMavenMirrorEditor(null);
    }

    private void AddMirrorToState(MavenMirrorConfiguration mirror)
    {
        var mirrors = _state.Settings.MavenMirrors
            .Where(existing => !existing.Id.Equals(mirror.Id, StringComparison.OrdinalIgnoreCase))
            .Concat([mirror])
            .OrderBy(existing => existing.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        _state = _state with
        {
            Settings = _state.Settings with
            {
                MavenMirrors = mirrors
            }
        };

        _stateStore.Save(_layout, _state);
        RefreshConfiguredMavenMirrors();
        SetStatus(_localizer.Format("mavenMirrorAddedStatus", mirror.Name));
    }

    private void OnRemoveSelectedMirrorClicked(object sender, RoutedEventArgs e)
    {
        if (ConfiguredMavenMirrorsListBox.SelectedItem is not MavenMirrorConfiguration mirror)
        {
            ShowValidationWarning("validationSelectConfiguredMirror");
            return;
        }

        _state = _state with
        {
            Settings = _state.Settings with
            {
                MavenMirrors = _state.Settings.MavenMirrors
                    .Where(existing => !existing.Id.Equals(mirror.Id, StringComparison.OrdinalIgnoreCase))
                    .ToList()
            }
        };

        _stateStore.Save(_layout, _state);
        RefreshConfiguredMavenMirrors();
        SetStatus(_localizer.Format("mavenMirrorRemovedStatus", mirror.Name));
    }

    private void OnConfiguredMavenMirrorSelectionChanged(object sender, WpfSelectionChangedEventArgs e)
    {
        PopulateMavenMirrorEditor(ConfiguredMavenMirrorsListBox.SelectedItem as MavenMirrorConfiguration);
    }

    private void OnUpdateSelectedMirrorClicked(object sender, RoutedEventArgs e)
    {
        if (ConfiguredMavenMirrorsListBox.SelectedItem is not MavenMirrorConfiguration selectedMirror)
        {
            ShowValidationWarning("validationSelectConfiguredMirror");
            return;
        }

        var updatedMirror = BuildMirrorFromEditor(selectedMirror.Id, selectedMirror.IsBuiltIn);
        if (updatedMirror is null)
        {
            ShowValidationWarning("validationCustomMirror");
            return;
        }

        var mirrors = _state.Settings.MavenMirrors
            .Select(mirror => mirror.Id.Equals(selectedMirror.Id, StringComparison.OrdinalIgnoreCase) ? updatedMirror : mirror)
            .OrderBy(mirror => mirror.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        _state = _state with
        {
            Settings = _state.Settings with
            {
                MavenMirrors = mirrors
            }
        };

        _stateStore.Save(_layout, _state);
        RefreshConfiguredMavenMirrors();
        ConfiguredMavenMirrorsListBox.SelectedItem = _configuredMavenMirrors.FirstOrDefault(mirror =>
            mirror.Id.Equals(updatedMirror.Id, StringComparison.OrdinalIgnoreCase));
        SetStatus(FormatLocalized(
            "Mirror updated: {0}",
            "已更新镜像：{0}",
            updatedMirror.Name));
    }

    private void OnClearMirrorEditorClicked(object sender, RoutedEventArgs e)
    {
        ConfiguredMavenMirrorsListBox.SelectedItem = null;
        PopulateMavenMirrorEditor(null);
    }

    private async void OnSaveMavenSettingsClicked(object sender, RoutedEventArgs e)
    {
        await ApplyMavenSettingsAsync(MavenRepositoryMigrationCheckBox.IsChecked == true);
    }

    private async void OnRemoteJdkDownloadSourceSelectionChanged(object sender, WpfSelectionChangedEventArgs e)
    {
        if (_suppressDownloadSourceSelectionChanged || RemoteJdkDownloadSourceComboBox.SelectedItem is not JdkDownloadSourceConfiguration source)
        {
            return;
        }

        await PersistPreferredJdkDownloadSourceAsync(source);
    }

    private async void OnSettingsJdkDownloadSourceSelectionChanged(object sender, WpfSelectionChangedEventArgs e)
    {
        if (_suppressDownloadSourceSelectionChanged || SettingsJdkDownloadSourceComboBox.SelectedItem is not JdkDownloadSourceConfiguration source)
        {
            return;
        }

        await PersistPreferredJdkDownloadSourceAsync(source);
    }

    private async void OnRemoteMavenDownloadSourceSelectionChanged(object sender, WpfSelectionChangedEventArgs e)
    {
        if (_suppressDownloadSourceSelectionChanged || RemoteMavenDownloadSourceComboBox.SelectedItem is not MavenDownloadSourceConfiguration source)
        {
            return;
        }

        await PersistPreferredMavenDownloadSourceAsync(source);
    }

    private async void OnSettingsMavenDownloadSourceSelectionChanged(object sender, WpfSelectionChangedEventArgs e)
    {
        if (_suppressDownloadSourceSelectionChanged || SettingsMavenDownloadSourceComboBox.SelectedItem is not MavenDownloadSourceConfiguration source)
        {
            return;
        }

        await PersistPreferredMavenDownloadSourceAsync(source);
    }

    private async Task PersistPreferredJdkDownloadSourceAsync(JdkDownloadSourceConfiguration source)
    {
        _state = _state with
        {
            Settings = _state.Settings with
            {
                PreferredJdkDownloadSourceId = source.Id
            }
        };

        _stateStore.Save(_layout, _state);
        RefreshJdkDownloadSources();
        await RefreshRemoteVersionsAfterSourceChangeAsync(
            Localize("Refreshing JDK download availability...", "正在刷新 JDK 下载可用性..."),
            FormatLocalized("JDK download source set to: {0}", "JDK 下载源已切换为：{0}", source.Name));
    }

    private async Task PersistPreferredMavenDownloadSourceAsync(MavenDownloadSourceConfiguration source)
    {
        _state = _state with
        {
            Settings = _state.Settings with
            {
                PreferredMavenDownloadSourceId = source.Id
            }
        };

        _stateStore.Save(_layout, _state);
        RefreshMavenDownloadSources();
        await RefreshRemoteVersionsAfterSourceChangeAsync(
            Localize("Refreshing Maven download availability...", "正在刷新 Maven 下载可用性..."),
            FormatLocalized("Maven download source set to: {0}", "Maven 下载源已切换为：{0}", source.Name));
    }

    private async Task RefreshRemoteVersionsAfterSourceChangeAsync(string busyMessage, string statusMessage)
    {
        if (!_hasLoaded)
        {
            SetStatus(statusMessage);
            return;
        }

        await ExecuteBusyWithMessageAsync(
            busyMessage,
            async () =>
            {
                await RefreshRemoteVersionsCoreAsync();
                return statusMessage;
            });
    }

    private async void OnUpdateSelectedJdkDownloadSourceClicked(object sender, RoutedEventArgs e)
    {
        if (SettingsJdkDownloadSourceComboBox.SelectedItem is not JdkDownloadSourceConfiguration selectedSource)
        {
            ShowValidationMessage(Localize(
                "Select a JDK download source first.",
                "请先选择一个 JDK 下载源。"));
            return;
        }

        var updatedSource = BuildJdkDownloadSourceFromEditor(selectedSource.Id, selectedSource.IsBuiltIn);
        if (updatedSource is null)
        {
            ShowValidationMessage(Localize(
                "Enter a JDK source name first.",
                "请先填写 JDK 下载源名称。"));
            return;
        }

        var customSources = _state.Settings.CustomJdkDownloadSources
            .Where(existing => !existing.Id.Equals(updatedSource.Id, StringComparison.OrdinalIgnoreCase))
            .Concat([updatedSource])
            .OrderBy(existing => existing.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        _state = _state with
        {
            Settings = _state.Settings with
            {
                CustomJdkDownloadSources = customSources,
                PreferredJdkDownloadSourceId = updatedSource.Id
            }
        };

        _stateStore.Save(_layout, _state);
        RefreshJdkDownloadSources();
        await RefreshRemoteVersionsAfterSourceChangeAsync(
            Localize("Refreshing JDK download availability...", "正在刷新 JDK 下载可用性..."),
            FormatLocalized("JDK download source updated: {0}", "JDK 下载源已更新：{0}", updatedSource.Name));
    }

    private async void OnAddCustomJdkDownloadSourceClicked(object sender, RoutedEventArgs e)
    {
        var source = BuildJdkDownloadSourceFromEditor(requireUrlPrefix: true);
        if (source is null)
        {
            ShowValidationMessage(Localize(
                "Enter a JDK source name and URL prefix first.",
                "请先填写 JDK 下载源名称和 URL 前缀。"));
            return;
        }

        var customSources = _state.Settings.CustomJdkDownloadSources
            .Where(existing => !existing.Id.Equals(source.Id, StringComparison.OrdinalIgnoreCase))
            .Concat([source])
            .OrderBy(existing => existing.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        _state = _state with
        {
            Settings = _state.Settings with
            {
                CustomJdkDownloadSources = customSources,
                PreferredJdkDownloadSourceId = source.Id
            }
        };

        _stateStore.Save(_layout, _state);
        RefreshJdkDownloadSources();
        await RefreshRemoteVersionsAfterSourceChangeAsync(
            Localize("Refreshing JDK download availability...", "正在刷新 JDK 下载可用性..."),
            FormatLocalized("Custom JDK download source added: {0}", "已添加自定义 JDK 下载源：{0}", source.Name));
    }

    private async void OnRemoveSelectedJdkDownloadSourceClicked(object sender, RoutedEventArgs e)
    {
        if (SettingsJdkDownloadSourceComboBox.SelectedItem is not JdkDownloadSourceConfiguration selectedSource)
        {
            ShowValidationMessage(Localize(
                "Select a JDK download source first.",
                "请先选择一个 JDK 下载源。"));
            return;
        }

        var customExists = _state.Settings.CustomJdkDownloadSources.Any(existing =>
            existing.Id.Equals(selectedSource.Id, StringComparison.OrdinalIgnoreCase));
        if (!customExists)
        {
            ShowValidationMessage(Localize(
                "The selected JDK source is using the built-in definition. Nothing can be removed.",
                "当前 JDK 下载源使用的是内置定义，没有可移除的覆盖。"));
            return;
        }

        var remaining = _state.Settings.CustomJdkDownloadSources
            .Where(existing => !existing.Id.Equals(selectedSource.Id, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var preferredId = _jdkDownloadSourceService.BuildAvailableSources(remaining)
            .Any(source => source.Id.Equals(selectedSource.Id, StringComparison.OrdinalIgnoreCase))
            ? selectedSource.Id
            : "jdk-official";

        _state = _state with
        {
            Settings = _state.Settings with
            {
                CustomJdkDownloadSources = remaining,
                PreferredJdkDownloadSourceId = preferredId
            }
        };

        _stateStore.Save(_layout, _state);
        RefreshJdkDownloadSources();
        await RefreshRemoteVersionsAfterSourceChangeAsync(
            Localize("Refreshing JDK download availability...", "正在刷新 JDK 下载可用性..."),
            FormatLocalized("JDK download source restored: {0}", "JDK 下载源已恢复：{0}", selectedSource.Name));
    }

    private async void OnUpdateSelectedMavenDownloadSourceClicked(object sender, RoutedEventArgs e)
    {
        if (SettingsMavenDownloadSourceComboBox.SelectedItem is not MavenDownloadSourceConfiguration selectedSource)
        {
            ShowValidationMessage(Localize(
                "Select a Maven download source first.",
                "请先选择一个 Maven 下载源。"));
            return;
        }

        var updatedSource = BuildMavenDownloadSourceFromEditor(selectedSource.Id, selectedSource.IsBuiltIn);
        if (updatedSource is null)
        {
            ShowValidationMessage(Localize(
                "Enter a Maven source name and base URL first.",
                "请先填写 Maven 下载源名称和基础 URL。"));
            return;
        }

        var customSources = _state.Settings.CustomMavenDownloadSources
            .Where(existing => !existing.Id.Equals(updatedSource.Id, StringComparison.OrdinalIgnoreCase))
            .Concat([updatedSource])
            .OrderBy(existing => existing.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        _state = _state with
        {
            Settings = _state.Settings with
            {
                CustomMavenDownloadSources = customSources,
                PreferredMavenDownloadSourceId = updatedSource.Id
            }
        };

        _stateStore.Save(_layout, _state);
        RefreshMavenDownloadSources();
        await RefreshRemoteVersionsAfterSourceChangeAsync(
            Localize("Refreshing Maven download availability...", "正在刷新 Maven 下载可用性..."),
            FormatLocalized("Maven download source updated: {0}", "Maven 下载源已更新：{0}", updatedSource.Name));
    }

    private async void OnAddCustomMavenDownloadSourceClicked(object sender, RoutedEventArgs e)
    {
        var source = BuildMavenDownloadSourceFromEditor();
        if (source is null)
        {
            ShowValidationMessage(Localize(
                "Enter a Maven source name and base URL first.",
                "请先填写 Maven 下载源名称和基础 URL。"));
            return;
        }

        var customSources = _state.Settings.CustomMavenDownloadSources
            .Where(existing => !existing.Id.Equals(source.Id, StringComparison.OrdinalIgnoreCase))
            .Concat([source])
            .OrderBy(existing => existing.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        _state = _state with
        {
            Settings = _state.Settings with
            {
                CustomMavenDownloadSources = customSources,
                PreferredMavenDownloadSourceId = source.Id
            }
        };

        _stateStore.Save(_layout, _state);
        RefreshMavenDownloadSources();
        await RefreshRemoteVersionsAfterSourceChangeAsync(
            Localize("Refreshing Maven download availability...", "正在刷新 Maven 下载可用性..."),
            FormatLocalized("Custom Maven download source added: {0}", "已添加自定义 Maven 下载源：{0}", source.Name));
    }

    private async void OnRemoveSelectedMavenDownloadSourceClicked(object sender, RoutedEventArgs e)
    {
        if (SettingsMavenDownloadSourceComboBox.SelectedItem is not MavenDownloadSourceConfiguration selectedSource)
        {
            ShowValidationMessage(Localize(
                "Select a Maven download source first.",
                "请先选择一个 Maven 下载源。"));
            return;
        }

        var customExists = _state.Settings.CustomMavenDownloadSources.Any(existing =>
            existing.Id.Equals(selectedSource.Id, StringComparison.OrdinalIgnoreCase));
        if (!customExists)
        {
            ShowValidationMessage(Localize(
                "The selected Maven source is using the built-in definition. Nothing can be removed.",
                "当前 Maven 下载源使用的是内置定义，没有可移除的覆盖。"));
            return;
        }

        var remaining = _state.Settings.CustomMavenDownloadSources
            .Where(existing => !existing.Id.Equals(selectedSource.Id, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var preferredId = _mavenSource.BuildAvailableDownloadSources(remaining)
            .Any(source => source.Id.Equals(selectedSource.Id, StringComparison.OrdinalIgnoreCase))
            ? selectedSource.Id
            : "apache-official";

        _state = _state with
        {
            Settings = _state.Settings with
            {
                CustomMavenDownloadSources = remaining,
                PreferredMavenDownloadSourceId = preferredId
            }
        };

        _stateStore.Save(_layout, _state);
        RefreshMavenDownloadSources();
        await RefreshRemoteVersionsAfterSourceChangeAsync(
            Localize("Refreshing Maven download availability...", "正在刷新 Maven 下载可用性..."),
            FormatLocalized("Maven download source restored: {0}", "Maven 下载源已恢复：{0}", selectedSource.Name));
    }

    private void OnMavenConfigurationScopeSelectionChanged(object sender, WpfSelectionChangedEventArgs e)
    {
        if (_suppressConfigurationScopeSelectionChanged || MavenConfigurationScopeComboBox.SelectedItem is not ConfigurationScopeOption selectedOption)
        {
            return;
        }

        if (selectedOption.Scope == MavenConfigurationScope.Global
            && ResolveMavenInstallationForConfiguration() is null)
        {
            RefreshConfigurationScopeSelector();
            ShowValidationMessage(Localize(
                "Select or activate a Maven installation before using global configuration files.",
                "使用全局配置文件前，请先选择或激活一个 Maven 安装。"));
            return;
        }

        _state = _state with
        {
            Settings = _state.Settings with
            {
                MavenConfigurationScope = selectedOption.Scope
            }
        };

        _stateStore.Save(_layout, _state);
        RefreshMavenConfigurationBindings(forceEditorRefresh: false);
        SetStatus(FormatLocalized(
            "Maven configuration scope set to: {0}",
            "Maven 配置作用域已切换为：{0}",
            selectedOption.DisplayName));
        return;

        if (_suppressConfigurationScopeSelectionChanged || MavenConfigurationScopeComboBox.SelectedItem is not ConfigurationScopeOption option)
        {
            return;
        }

        if (!TryResolveMavenConfigurationPaths(option.Scope, out var settingsFilePath, out var toolchainsFilePath))
        {
            RefreshConfigurationScopeSelector();
            ShowValidationMessage(Localize(
                "Select a Maven installation before using global Maven configuration files.",
                "切换到全局 Maven 配置前，请先选择一个 Maven 安装。"));
            return;
        }

        _state = _state with
        {
            Settings = _state.Settings with
            {
                MavenConfigurationScope = option.Scope,
                MavenSettingsFilePath = settingsFilePath,
                MavenToolchainsFilePath = toolchainsFilePath
            }
        };

        _stateStore.Save(_layout, _state);
        RefreshStateBindings(GetSelectedInstallationId(JdkListBox), GetSelectedInstallationId(MavenListBox));
        SetStatus(FormatLocalized(
            "Maven configuration scope set to: {0}",
            "Maven 配置作用域已切换为：{0}",
            option.DisplayName));
    }

    private async void OnImportMirrorXmlClicked(object sender, RoutedEventArgs e)
    {
        var importDialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "XML files|*.xml|All files|*.*",
            CheckFileExists = true
        };

        if (importDialog.ShowDialog(this) != true)
        {
            return;
        }

        await ExecuteBusyWithMessageAsync(
            Localize("Importing mirror XML...", "正在导入镜像 XML..."),
            async () =>
            {
                var importedMirrors = await Task.Run(() => _mavenConfigurationService.ImportMirrorsFromXmlFile(importDialog.FileName));
                var mergedMirrors = ReadMirrorsFromEditor()
                    .Where(existing => importedMirrors.All(imported => !imported.Id.Equals(existing.Id, StringComparison.OrdinalIgnoreCase)))
                    .Concat(importedMirrors)
                    .OrderBy(existing => existing.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                SetMavenMirrorsEditorText(_mavenConfigurationService.BuildMirrorsXml(mergedMirrors));
                _mavenMirrorsEditorDirty = true;
                return FormatLocalized(
                    "Imported {0} mirror entries from XML.",
                    "已从 XML 导入 {0} 条镜像配置。",
                    importedMirrors.Count);
            });
        return;

        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "XML files|*.xml|All files|*.*",
            CheckFileExists = true
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        await ExecuteBusyWithMessageAsync(
            Localize("Importing mirror XML...", "正在导入镜像 XML..."),
            async () =>
            {
                var importedMirrors = await Task.Run(() => _mavenConfigurationService.ImportMirrorsFromXmlFile(dialog.FileName));
                var mergedMirrors = _state.Settings.MavenMirrors
                    .Where(existing => importedMirrors.All(imported => !imported.Id.Equals(existing.Id, StringComparison.OrdinalIgnoreCase)))
                    .Concat(importedMirrors)
                    .OrderBy(existing => existing.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                _state = _state with
                {
                    Settings = _state.Settings with
                    {
                        MavenMirrors = mergedMirrors
                    }
                };

                await Task.Run(() => _stateStore.Save(_layout, _state));
                RefreshConfiguredMavenMirrors();
                return FormatLocalized(
                    "Imported {0} mirror entries from XML.",
                    "已从 XML 导入 {0} 个镜像条目。",
                    importedMirrors.Count);
            });
    }

    private void OnBrowseMavenToolchainsFileClicked(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "Maven toolchains.xml|toolchains.xml|XML files|*.xml",
            FileName = Path.GetFileName(_state.Settings.MavenToolchainsFilePath),
            InitialDirectory = Path.GetDirectoryName(MavenToolchainsFilePathTextBox.Text)
                               ?? Path.GetDirectoryName(_state.Settings.MavenToolchainsFilePath)
        };

        if (dialog.ShowDialog(this) == true)
        {
            MavenToolchainsFilePathTextBox.Text = dialog.FileName;
        }
    }

    private void OnOpenMavenSettingsFileClicked(object sender, RoutedEventArgs e)
    {
        OpenConfigFileInEditor(
            MavenSettingsFilePathTextBox.Text,
            _mavenConfigurationService.EnsureEditableSettingsFile,
            Localize("settings.xml opened in the editor.", "已在编辑器中打开 settings.xml。"));
    }

    private void OnOpenMavenToolchainsFileClicked(object sender, RoutedEventArgs e)
    {
        OpenConfigFileInEditor(
            MavenToolchainsFilePathTextBox.Text,
            _mavenConfigurationService.EnsureEditableToolchainsFile,
            Localize("toolchains.xml opened in the editor.", "已在编辑器中打开 toolchains.xml。"));
    }

    private void OpenConfigFileInEditor(string filePath, Action<string> ensureFileExists, string successMessage)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            ShowValidationMessage(Localize(
                "Enter a configuration file path first.",
                "请先填写配置文件路径。"));
            return;
        }

        try
        {
            var normalizedPath = Path.GetFullPath(filePath.Trim());
            ensureFileExists(normalizedPath);
            Process.Start(new ProcessStartInfo
            {
                FileName = "notepad.exe",
                Arguments = $"\"{normalizedPath}\"",
                UseShellExecute = true
            });
            SetStatus(successMessage);
        }
        catch (Exception ex)
        {
            ShowValidationMessage(ex.Message);
        }
    }

    private JdkDownloadSourceConfiguration? BuildJdkDownloadSourceFromEditor(
        string? existingId = null,
        bool isBuiltIn = false,
        bool requireUrlPrefix = false)
    {
        var name = JdkDownloadSourceNameTextBox.Text.Trim();
        var urlPrefix = JdkDownloadSourceUrlPrefixTextBox.Text.Trim();
        var providers = JdkDownloadSourceProvidersTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name)
            || (requireUrlPrefix && string.IsNullOrWhiteSpace(urlPrefix)))
        {
            return null;
        }

        return new JdkDownloadSourceConfiguration(
            string.IsNullOrWhiteSpace(existingId) ? BuildMirrorId(name) : existingId,
            name,
            NormalizeUrlPrefix(urlPrefix),
            string.IsNullOrWhiteSpace(providers) ? "*" : providers,
            isBuiltIn);
    }

    private MavenDownloadSourceConfiguration? BuildMavenDownloadSourceFromEditor(
        string? existingId = null,
        bool isBuiltIn = false)
    {
        var name = MavenDownloadSourceNameTextBox.Text.Trim();
        var baseUrl = MavenDownloadSourceUrlTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(baseUrl))
        {
            return null;
        }

        return new MavenDownloadSourceConfiguration(
            string.IsNullOrWhiteSpace(existingId) ? BuildMirrorId(name) : existingId,
            name,
            baseUrl.Trim().TrimEnd('/'),
            isBuiltIn);
    }

    private static string NormalizeUrlPrefix(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = value.Trim();
        return trimmed.EndsWith("/", StringComparison.Ordinal) ? trimmed : $"{trimmed}/";
    }

    private void OnMavenDownloadSourceSelectionChanged(object sender, WpfSelectionChangedEventArgs e)
    {
        if (_suppressDownloadSourceSelectionChanged || MavenDownloadSourceComboBox.SelectedItem is not MavenDownloadSourceConfiguration source)
        {
            return;
        }

        _state = _state with
        {
            Settings = _state.Settings with
            {
                PreferredMavenDownloadSourceId = source.Id
            }
        };

        _stateStore.Save(_layout, _state);
        MavenDownloadSourceBaseUrlTextBlock.Text = source.BaseUrl;
        SetStatus(FormatLocalized(
            "Maven download source set to: {0}",
            "Maven 下载源已切换为：{0}",
            source.Name));
    }

    private void OnAddCustomDownloadSourceClicked(object sender, RoutedEventArgs e)
    {
        var name = CustomMavenDownloadSourceNameTextBox.Text.Trim();
        var baseUrl = CustomMavenDownloadSourceUrlTextBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(baseUrl))
        {
            ShowValidationMessage(Localize(
                "Enter a download source name and base URL first.",
                "请先填写下载源名称和基础地址。"));
            return;
        }

        var source = new MavenDownloadSourceConfiguration(
            Id: BuildMirrorId(name),
            Name: name,
            BaseUrl: baseUrl.Trim().TrimEnd('/'),
            IsBuiltIn: false);
        var customSources = _state.Settings.CustomMavenDownloadSources
            .Where(existing => !existing.Id.Equals(source.Id, StringComparison.OrdinalIgnoreCase))
            .Concat([source])
            .OrderBy(existing => existing.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        _state = _state with
        {
            Settings = _state.Settings with
            {
                CustomMavenDownloadSources = customSources,
                PreferredMavenDownloadSourceId = source.Id
            }
        };

        _stateStore.Save(_layout, _state);
        RefreshMavenDownloadSources();
        CustomMavenDownloadSourceNameTextBox.Clear();
        CustomMavenDownloadSourceUrlTextBox.Clear();
        SetStatus(FormatLocalized(
            "Maven download source set to: {0}",
            "Maven 下载源已切换为：{0}",
            source.Name));
    }

    private void OnRemoveSelectedDownloadSourceClicked(object sender, RoutedEventArgs e)
    {
        if (MavenDownloadSourceComboBox.SelectedItem is not MavenDownloadSourceConfiguration source)
        {
            ShowValidationMessage(Localize(
                "Select a download source first.",
                "请先选择一个下载源。"));
            return;
        }

        if (source.IsBuiltIn)
        {
            ShowValidationMessage(Localize(
                "Built-in download sources cannot be removed.",
                "内置下载源不能移除。"));
            return;
        }

        var remainingSources = _state.Settings.CustomMavenDownloadSources
            .Where(existing => !existing.Id.Equals(source.Id, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var fallbackSource = _mavenSource.BuildAvailableDownloadSources(remainingSources)
            .FirstOrDefault(item => item.Id.Equals("apache-official", StringComparison.OrdinalIgnoreCase))
            ?? _mavenSource.BuildAvailableDownloadSources(remainingSources).First();

        _state = _state with
        {
            Settings = _state.Settings with
            {
                CustomMavenDownloadSources = remainingSources,
                PreferredMavenDownloadSourceId = fallbackSource.Id
            }
        };

        _stateStore.Save(_layout, _state);
        RefreshMavenDownloadSources();
        SetStatus(FormatLocalized(
            "Removed download source: {0}",
            "已移除下载源：{0}",
            source.Name));
    }

    private async void OnSaveManagedInstallRootsClicked(object sender, RoutedEventArgs e)
    {
        var targetJdkRoot = ManagedJdkInstallRootTextBox.Text.Trim();
        var targetMavenRoot = ManagedMavenInstallRootTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(targetJdkRoot) || string.IsNullOrWhiteSpace(targetMavenRoot))
        {
            ShowValidationMessage(Localize(
                "Provide both the managed JDK and Maven install directories.",
                "请同时填写受管 JDK 和 Maven 安装目录。"));
            return;
        }

        await ExecuteBusyWithMessageAsync(
            Localize("Migrating managed install directories...", "正在迁移受管安装目录..."),
            async () =>
            {
                var result = await Task.Run(() => _managedInstallLayoutService.MigrateManagedInstallRoots(
                    _state,
                    _layout,
                    targetJdkRoot,
                    targetMavenRoot));

                _state = result.State;
                await Task.Run(() => _stateStore.Save(_layout, _state));
                await Task.Run(() => ApplyActivationWithShellIntegration(_state));
                InvalidateDoctorOutput();
                RefreshStateBindings(GetSelectedInstallationId(JdkListBox), GetSelectedInstallationId(MavenListBox));

                return FormatLocalized(
                    "Managed install directories updated. Migrated {0} JDK(s) and {1} Maven installation(s).",
                    "受管安装目录已更新，共迁移 {0} 个 JDK、{1} 个 Maven。",
                    result.MigratedJdks,
                    result.MigratedMavens);
            },
            ShowSuccessDialog);
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
                    var snapshot = _discoveryService.Discover(GetManagedLayout());
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
                    _catalogService.ImportInstallation(currentState, ToolchainKind.Jdk, importPath, GetManagedLayout()));

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
                    _catalogService.ImportInstallation(currentState, ToolchainKind.Maven, importPath, GetManagedLayout()));

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
            ShowSuccessDialog);
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
        if (!TryGetSelectedRemoteJdkPackage(out var package))
        {
            ShowValidationWarning("validationSelectRemoteJdkVersion");
            return;
        }

        if (!package.IsDownloadAvailable)
        {
            ShowValidationMessage(package.AvailabilityMessage ?? Localize(
                "The selected JDK download source cannot download this package right now.",
                "当前 JDK 下载源暂时无法下载这个版本。"));
            return;
        }

        var preferredJdkId = GetSelectedInstallationId(JdkListBox);
        var preferredMavenId = GetSelectedInstallationId(MavenListBox);

        await ExecuteBusyAsync(
            "busyInstallingJdk",
            async () =>
            {
                ReportBusyStage("busyInstallingJdk", "busyDetailResolvingPackage", 8);
                var progress = new Progress<PackageInstallProgress>(value => ReportPackageInstallProgress("busyInstallingJdk", value));
                var installation = await _packageInstallationService.InstallAsync(package, GetManagedLayout(), CancellationToken.None, progress);
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
            ShowSuccessDialog);
    }

    private async Task InstallRemoteMavenAsync(bool switchAfterInstall)
    {
        if (!TryGetSelectedRemoteMavenPackage(out var package))
        {
            ShowValidationWarning("validationSelectRemoteMavenVersion");
            return;
        }

        if (!package.IsDownloadAvailable)
        {
            ShowValidationMessage(package.AvailabilityMessage ?? Localize(
                "The selected Maven download source cannot download this package right now.",
                "当前 Maven 下载源暂时无法下载这个版本。"));
            return;
        }

        var preferredJdkId = GetSelectedInstallationId(JdkListBox);
        var preferredMavenId = GetSelectedInstallationId(MavenListBox);

        await ExecuteBusyAsync(
            "busyInstallingMaven",
            async () =>
            {
                ReportBusyStage("busyInstallingMaven", "busyDetailResolvingPackage", 8);
                var progress = new Progress<PackageInstallProgress>(value => ReportPackageInstallProgress("busyInstallingMaven", value));
                var installation = await _packageInstallationService.InstallAsync(package, GetManagedLayout(), CancellationToken.None, progress);
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
            ShowSuccessDialog);
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
                    _catalogService.RemoveInstallation(_state, kind, installation.Id, GetManagedLayout(), deleteFiles));

                await Task.Run(() => _stateStore.Save(_layout, result.State));
                _state = result.State;
                await Task.Run(() => ApplyActivationWithShellIntegration(result.State));

                InvalidateDoctorOutput();
                RefreshStateBindings(preferredJdkId, preferredMavenId);

                return _localizer.Format(GetRemovalStatusKey(kind, deleteFiles), result.Installation.DisplayName);
            });
    }

    private bool TryGetSelectedRemoteJdkPackage(out RemotePackageDescriptor package)
    {
        if (RemoteJdkVersionComboBox.SelectedItem is RemotePackageDescriptor selectedPackage)
        {
            package = selectedPackage;
            return true;
        }

        package = null!;
        return false;
    }

    private bool TryGetSelectedRemoteMavenPackage(out RemotePackageDescriptor package)
    {
        if (RemoteMavenVersionComboBox.SelectedItem is RemotePackageDescriptor selectedPackage)
        {
            package = selectedPackage;
            return true;
        }

        package = null!;
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

    private async Task ApplyMavenSettingsAsync(bool migrateRepository)
    {
        IReadOnlyList<MavenMirrorConfiguration> parsedMirrors;
        string normalizedToolchainsXml;

        try
        {
            parsedMirrors = ReadMirrorsFromEditor();
            normalizedToolchainsXml = _mavenConfigurationService.NormalizeToolchainsXml(ToolchainsXmlEditorTextBox.Text);
        }
        catch (Exception ex)
        {
            ShowValidationMessage(ex.Message);
            return;
        }

        var effectiveScope = (MavenConfigurationScopeComboBox.SelectedItem as ConfigurationScopeOption)?.Scope
                             ?? _state.Settings.MavenConfigurationScope;

        if (!TryResolveMavenConfigurationPaths(effectiveScope, out var effectiveSettingsPath, out var effectiveToolchainsPath))
        {
            ShowValidationMessage(Localize(
                "Select or activate a Maven installation before using global configuration files.",
                "使用全局配置文件前，请先选择或激活一个 Maven 安装。"));
            return;
        }

        var resolvedSettingsFilePath = effectiveScope == MavenConfigurationScope.User
            ? MavenSettingsFilePathTextBox.Text.Trim()
            : effectiveSettingsPath;
        var resolvedToolchainsFilePath = effectiveScope == MavenConfigurationScope.User
            ? MavenToolchainsFilePathTextBox.Text.Trim()
            : effectiveToolchainsPath;
        var resolvedLocalRepositoryPath = MavenLocalRepositoryPathTextBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(resolvedSettingsFilePath)
            || string.IsNullOrWhiteSpace(resolvedToolchainsFilePath)
            || string.IsNullOrWhiteSpace(resolvedLocalRepositoryPath))
        {
            ShowValidationMessage(Localize(
                "Provide the settings.xml path, toolchains.xml path, and the Maven local repository path.",
                "请填写 settings.xml 路径、toolchains.xml 路径和 Maven 本地仓库目录。"));
            return;
        }

        await ExecuteBusyAsync(
            migrateRepository ? "busyMigratingMavenRepository" : "busyApplyingMavenSettings",
            async () =>
            {
                var result = await Task.Run(() => _mavenConfigurationService.ApplySettings(
                    resolvedSettingsFilePath,
                    resolvedLocalRepositoryPath,
                    parsedMirrors,
                    _state.Settings.MavenLocalRepositoryPath,
                    migrateRepository));
                await Task.Run(() => _mavenConfigurationService.ApplyToolchainsXml(resolvedToolchainsFilePath, normalizedToolchainsXml));

                _state = _state with
                {
                    Settings = _state.Settings with
                    {
                        MavenConfigurationScope = effectiveScope,
                        MavenSettingsFilePath = effectiveScope == MavenConfigurationScope.User
                            ? result.SettingsFilePath
                            : _state.Settings.MavenSettingsFilePath,
                        MavenToolchainsFilePath = effectiveScope == MavenConfigurationScope.User
                            ? Path.GetFullPath(resolvedToolchainsFilePath)
                            : _state.Settings.MavenToolchainsFilePath,
                        MavenLocalRepositoryPath = result.LocalRepositoryPath,
                        MavenMirrors = parsedMirrors
                    }
                };

                await Task.Run(() => _stateStore.Save(_layout, _state));
                RefreshMavenMirrorsXmlEditor(force: true);
                SetToolchainsEditorText(normalizedToolchainsXml);
                RefreshStateBindings(GetSelectedInstallationId(JdkListBox), GetSelectedInstallationId(MavenListBox));

                return result.RepositoryMigrated
                    ? _localizer.Format("mavenRepositoryMigratedStatus", result.LocalRepositoryPath)
                    : _localizer.Format("mavenSettingsAppliedStatus", result.SettingsFilePath);
            },
            ShowSuccessDialog);
        return;

        var settingsFilePath = MavenSettingsFilePathTextBox.Text.Trim();
        var toolchainsFilePath = MavenToolchainsFilePathTextBox.Text.Trim();
        var localRepositoryPath = MavenLocalRepositoryPathTextBox.Text.Trim();
        var selectedScope = (MavenConfigurationScopeComboBox.SelectedItem as ConfigurationScopeOption)?.Scope
                            ?? _state.Settings.MavenConfigurationScope;

        if (string.IsNullOrWhiteSpace(settingsFilePath)
            || string.IsNullOrWhiteSpace(toolchainsFilePath)
            || string.IsNullOrWhiteSpace(localRepositoryPath))
        {
            ShowValidationMessage(Localize(
                "Provide the settings.xml path, toolchains.xml path, and the Maven local repository path.",
                "请填写 settings.xml 路径、toolchains.xml 路径和 Maven 本地仓库目录。"));
            return;
        }

        await ExecuteBusyAsync(
            migrateRepository ? "busyMigratingMavenRepository" : "busyApplyingMavenSettings",
            async () =>
            {
                var result = await Task.Run(() => _mavenConfigurationService.ApplySettings(
                    settingsFilePath,
                    localRepositoryPath,
                    _state.Settings.MavenMirrors,
                    _state.Settings.MavenLocalRepositoryPath,
                    migrateRepository));
                await Task.Run(() => _mavenConfigurationService.EnsureEditableToolchainsFile(toolchainsFilePath));

                _state = _state with
                {
                    Settings = _state.Settings with
                    {
                        MavenConfigurationScope = selectedScope,
                        MavenSettingsFilePath = result.SettingsFilePath,
                        MavenToolchainsFilePath = Path.GetFullPath(toolchainsFilePath),
                        MavenLocalRepositoryPath = result.LocalRepositoryPath
                    }
                };

                await Task.Run(() => _stateStore.Save(_layout, _state));
                RefreshStateBindings(GetSelectedInstallationId(JdkListBox), GetSelectedInstallationId(MavenListBox));

                return result.RepositoryMigrated
                    ? _localizer.Format("mavenRepositoryMigratedStatus", result.LocalRepositoryPath)
                    : _localizer.Format("mavenSettingsAppliedStatus", result.SettingsFilePath);
            },
            ShowSuccessDialog);
    }
    #pragma warning restore CS0162

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

    private MavenMirrorConfiguration? BuildMirrorFromEditor(string? existingId = null, bool isBuiltIn = false)
    {
        var name = CustomMavenMirrorNameTextBox.Text.Trim();
        var url = CustomMavenMirrorUrlTextBox.Text.Trim();
        var mirrorOf = CustomMavenMirrorOfTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        return new MavenMirrorConfiguration(
            Id: string.IsNullOrWhiteSpace(existingId) ? BuildMirrorId(name) : existingId,
            Name: name,
            Url: url.Trim().TrimEnd('/'),
            MirrorOf: string.IsNullOrWhiteSpace(mirrorOf) ? "*" : mirrorOf,
            IsBuiltIn: isBuiltIn);
    }

    private static string BuildMirrorId(string name)
    {
        var sanitized = new string(name
            .Trim()
            .ToLowerInvariant()
            .Select(character => char.IsLetterOrDigit(character) ? character : '-')
            .ToArray());

        while (sanitized.Contains("--", StringComparison.Ordinal))
        {
            sanitized = sanitized.Replace("--", "-", StringComparison.Ordinal);
        }

        sanitized = sanitized.Trim('-');
        return string.IsNullOrWhiteSpace(sanitized) ? "custom-mirror" : sanitized;
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

    private void OnMavenMirrorsXmlEditorTextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (_suppressMirrorEditorTextChanged)
        {
            return;
        }

        _mavenMirrorsEditorDirty = true;
    }

    private void OnToolchainsXmlEditorTextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (_suppressToolchainsEditorTextChanged)
        {
            return;
        }

        _toolchainsEditorDirty = true;
    }

    private void OnComboBoxPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not WpfComboBox comboBox || comboBox.IsDropDownOpen)
        {
            return;
        }

        var scrollViewer = FindAncestors<WpfScrollViewer>(comboBox).Skip(1).FirstOrDefault()
                           ?? FindAncestors<WpfScrollViewer>(comboBox).FirstOrDefault();
        if (scrollViewer is null)
        {
            e.Handled = true;
            return;
        }

        var forwardedEvent = new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
        {
            RoutedEvent = UIElement.MouseWheelEvent,
            Source = sender
        };

        e.Handled = true;
        scrollViewer.RaiseEvent(forwardedEvent);
    }

    private void OnEmbeddedListPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not DependencyObject dependencyObject)
        {
            return;
        }

        var scrollViewer = FindAncestors<WpfScrollViewer>(dependencyObject).Skip(1).FirstOrDefault()
                           ?? FindAncestors<WpfScrollViewer>(dependencyObject).FirstOrDefault();
        if (scrollViewer is null)
        {
            return;
        }

        var forwardedEvent = new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
        {
            RoutedEvent = UIElement.MouseWheelEvent,
            Source = sender
        };

        e.Handled = true;
        scrollViewer.RaiseEvent(forwardedEvent);
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

    private static IEnumerable<T> FindAncestors<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T target)
            {
                yield return target;
            }

            current = VisualTreeHelper.GetParent(current);
        }
    }
}
