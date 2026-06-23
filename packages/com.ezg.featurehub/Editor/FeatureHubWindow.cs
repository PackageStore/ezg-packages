// EZG Feature Hub — cửa sổ Editor (UI Toolkit) cài package vào project.
//  Menu: Ezg > Feature Hub.
//  Tab 1: .unitypackage (asset-catalog.json)  — tải về, import, xóa temp, ghi record local.
//  Tab 2: UPM (unity-template.json)           — ghi vào Packages/manifest.json + scopedRegistries.
//  Giao diện: card expandable + icon động Lottie khắp nơi (rlottie render trong editor; icon
//  ở trạng thái Idle, phát micro-animation khi hover -> sinh động mà nhẹ CPU).
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Ezg.FeatureHub.Editor
{
    public class FeatureHubWindow : EditorWindow
    {
        #region Constants

        private const string PREF_IMPORT_MODE = "Ezg.FeatureHub.ImportMode";
        private const string PREF_TAB = "Ezg.FeatureHub.Tab";
        private const string PREF_FEATURE_PROJECT = "Ezg.FeatureHub.FeatureProject";

        // Palette.
        private static readonly Color C_BG = new Color32(30, 33, 41, 255);
        private static readonly Color C_HEADER = new Color32(38, 42, 53, 255);
        private static readonly Color C_HEADER2 = new Color32(52, 58, 74, 255);
        private static readonly Color C_ACCENT = new Color32(86, 148, 255, 255);
        private static readonly Color C_SUCCESS = new Color32(76, 196, 124, 255);
        private static readonly Color C_WARN = new Color32(232, 168, 56, 255);
        private static readonly Color C_MUTED = new Color32(140, 147, 161, 255);
        private static readonly Color C_TEXT = new Color32(224, 227, 233, 255);
        private static readonly Color C_CARD = new Color(1f, 1f, 1f, 0.035f);
        private static readonly Color C_CARD_HOVER = new Color(1f, 1f, 1f, 0.075f);
        private static readonly Color C_CARD_OPEN = new Color(0.34f, 0.55f, 1f, 0.10f);
        private static readonly Color C_BORDER = new Color(0f, 0f, 0f, 0.22f);
        private static readonly Color C_PILL_OFF = new Color(1f, 1f, 1f, 0.07f);

        #endregion

        #region Fields

        private AssetCatalog _catalog;
        private UnityTemplate _template;
        private Dictionary<string, string> _projectDeps = new Dictionary<string, string>();

        // Mọi package đã RESOLVE trong project (name -> version) lấy từ PackageManager.Client.List —
        // gồm cả dependency gián tiếp/embedded/built-in mà manifest.json không khai báo trực tiếp.
        // Nạp async sau khi mở; dùng để biết UPM nào "đã có sẵn" dù không nằm trong _projectDeps.
        private Dictionary<string, string> _resolvedPackages = new Dictionary<string, string>();

        // Tab "Features": index các dự án feature + catalog của project đang chọn (lazy-load, cache theo id).
        private FeaturesIndex _featuresIndex;
        private string _selectedProjectId;
        private AssetCatalog _featureCatalog;
        private bool _featuresLoading;
        private readonly Dictionary<string, AssetCatalog> _featureCatalogCache = new Dictionary<string, AssetCatalog>();

        private int _tab;
        private ImportMode _importMode = ImportMode.Ask;
        private string _search = string.Empty;
        private bool _busy;
        private bool _loading;
        private bool _registryChecked;

        private VisualElement _content;
        private ScrollView _listContainer;
        private Label _statusLabel;
        private VisualElement _statusIconBox;

        private VisualElement _tabUnity;
        private VisualElement _tabUpm;
        private VisualElement _tabFeatures;
        private LottieElement _tabUnityIcon;
        private LottieElement _tabUpmIcon;
        private LottieElement _tabFeaturesIcon;
        private Label _tabUnityLabel;
        private Label _tabUpmLabel;
        private Label _tabFeaturesLabel;

        #endregion

        #region Initialize

        [MenuItem("Ezg/Feature Hub %#u")] // %#u = Ctrl/Cmd + Shift + U
        public static void Open()
        {
            var window = GetWindow<FeatureHubWindow>();
            window.titleContent = new GUIContent("Feature Hub");
            window.minSize = new Vector2(640, 480);
            window.Show();
        }

        private void CreateGUI()
        {
            // Mặc định Silent: chế độ Dialog dựng PackageImportTreeView của Unity — với một số
            // .unitypackage (repack), tree-view này ném NullReferenceException khiến import treo
            // và KHÔNG ghi file nào. Silent bỏ qua dialog nên cài ổn định.
            _importMode = (ImportMode)EditorPrefs.GetInt(PREF_IMPORT_MODE, (int)ImportMode.Silent);
            _tab = EditorPrefs.GetInt(PREF_TAB, 0);
            _selectedProjectId = EditorPrefs.GetString(PREF_FEATURE_PROJECT, null);

            var root = rootVisualElement;
            root.style.flexDirection = FlexDirection.Column;
            root.style.backgroundColor = C_BG;

            root.Add(BuildHeaderBand());

            _content = new VisualElement { style = { flexGrow = 1, flexShrink = 1 } };
            root.Add(_content);

            _content.Add(BuildToolbar());

            _listContainer = new ScrollView { style = { flexGrow = 1, paddingTop = 4, paddingBottom = 8 } };
            _content.Add(_listContainer);

            root.Add(BuildFooter());

            Reload();
        }

        #endregion

        #region UI — Header & Toolbar & Footer

        private VisualElement BuildHeaderBand()
        {
            var band = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row, alignItems = Align.Center, flexShrink = 0,
                    paddingLeft = 14, paddingRight = 14, paddingTop = 12, paddingBottom = 12,
                    backgroundColor = C_HEADER,
                    borderBottomWidth = 2, borderBottomColor = C_HEADER2,
                },
            };

            string brandJson = LottieLibrary.GetJson(LottieLibrary.BRAND);
            if (brandJson != null)
                band.Add(new LottieElement(brandJson, 34) { style = { marginRight = 12 } });

            var titleCol = new VisualElement { style = { flexGrow = 1 } };
            titleCol.Add(new Label("Feature Hub")
            {
                style = { unityFontStyleAndWeight = FontStyle.Bold, fontSize = 17, color = C_TEXT },
            });
            titleCol.Add(new Label("Cài package & asset cho project")
            {
                style = { fontSize = 11, color = C_MUTED, marginTop = 2 },
            });
            band.Add(titleCol);

            return band;
        }

        private VisualElement BuildToolbar()
        {
            var bar = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row, alignItems = Align.Center, flexShrink = 0,
                    flexWrap = Wrap.Wrap,
                    paddingLeft = 12, paddingRight = 12, paddingTop = 8, paddingBottom = 8,
                    borderBottomWidth = 1, borderBottomColor = C_BORDER,
                },
            };

            _tabUnity = BuildTabPill("Unity Packages", LottieLibrary.ARCHIVE, 0, out _tabUnityIcon, out _tabUnityLabel);
            _tabUpm = BuildTabPill("UPM Packages", LottieLibrary.SETTINGS, 1, out _tabUpmIcon, out _tabUpmLabel);
            _tabFeatures = BuildTabPill("Features", LottieLibrary.STAR, 2, out _tabFeaturesIcon, out _tabFeaturesLabel);
            bar.Add(_tabUnity);
            bar.Add(_tabUpm);
            bar.Add(_tabFeatures);

            bar.Add(new VisualElement { style = { flexGrow = 1, minWidth = 8 } }); // spacer

            var search = new TextField { value = _search };
            search.style.width = 160;
            search.style.backgroundColor = C_PILL_OFF;
            Round(search, 6);
            search.RegisterValueChangedCallback(evt =>
            {
                _search = evt.newValue ?? string.Empty;
                RebuildList();
            });
            bar.Add(LabeledField("🔍", search));

            var modeOptions = new List<string> { "Hỏi mỗi lần", "Silent", "Dialog" };
            var modePopup = new PopupField<string>(modeOptions, (int)_importMode)
            {
                tooltip = "Cách import .unitypackage: hỏi mỗi lần / tự động (khuyên dùng) / hiện hộp thoại Unity.\n" +
                          "Lưu ý: chế độ Dialog có thể treo (NullReferenceException) với một số gói repack — dùng Silent nếu gặp lỗi.",
            };
            modePopup.RegisterValueChangedCallback(evt =>
            {
                _importMode = (ImportMode)modeOptions.IndexOf(evt.newValue);
                EditorPrefs.SetInt(PREF_IMPORT_MODE, (int)_importMode);
            });
            bar.Add(LabeledField("Import", modePopup));

            bar.Add(StyledButton("↻", C_PILL_OFF, C_TEXT, Reload, 30));

            UpdateTabButtons();
            return bar;
        }

        private VisualElement BuildFooter()
        {
            var footer = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row, alignItems = Align.Center, flexShrink = 0,
                    paddingLeft = 12, paddingRight = 12, paddingTop = 6, paddingBottom = 6,
                    backgroundColor = C_HEADER, borderTopWidth = 1, borderTopColor = C_HEADER2,
                },
            };

            _statusIconBox = new VisualElement
            {
                style = { width = 18, height = 18, marginRight = 8, flexShrink = 0 },
            };
            footer.Add(_statusIconBox);

            _statusLabel = new Label
            {
                style = { color = C_MUTED, fontSize = 11, flexGrow = 1, whiteSpace = WhiteSpace.Normal },
            };
            footer.Add(_statusLabel);

            return footer;
        }

        private VisualElement BuildTabPill(string text, string iconKey, int index, out LottieElement icon, out Label label)
        {
            var pill = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row, alignItems = Align.Center,
                    marginRight = 6, paddingLeft = 12, paddingRight = 14, paddingTop = 4, paddingBottom = 4,
                },
            };
            Round(pill, 14);

            icon = IconIdle(iconKey, 18);
            if (icon != null)
            {
                icon.style.marginRight = 6;
                pill.Add(icon);
            }

            label = new Label(text) { style = { fontSize = 12 } };
            pill.Add(label);

            var captured = icon;
            pill.RegisterCallback<ClickEvent>(_ => SetTab(index));
            pill.RegisterCallback<MouseEnterEvent>(_ => captured?.Play());
            return pill;
        }

        private void SetTab(int tab)
        {
            _tab = tab;
            EditorPrefs.SetInt(PREF_TAB, tab);
            UpdateTabButtons();
            RebuildList();
        }

        private void UpdateTabButtons()
        {
            ApplyPillStyle(_tabUnity, _tabUnityLabel, _tabUnityIcon, _tab == 0);
            ApplyPillStyle(_tabUpm, _tabUpmLabel, _tabUpmIcon, _tab == 1);
            ApplyPillStyle(_tabFeatures, _tabFeaturesLabel, _tabFeaturesIcon, _tab == 2);
        }

        private static void ApplyPillStyle(VisualElement pill, Label label, LottieElement icon, bool active)
        {
            if (pill == null)
                return;
            pill.style.backgroundColor = active ? C_ACCENT : C_PILL_OFF;
            if (label != null)
            {
                label.style.color = active ? Color.white : C_MUTED;
                label.style.unityFontStyleAndWeight = active ? FontStyle.Bold : FontStyle.Normal;
            }
            if (active)
                icon?.Play();
        }

        #endregion

        #region Data

        private void Reload()
        {
            _loading = true;
            _catalog = null;
            _template = null;
            _projectDeps = FeatureHubService.LoadProjectDependencies();
            LoadResolvedPackagesAsync();
            SetStatus("Đang tải dữ liệu từ server...");
            ShowSpinner();
            RebuildList();

            FeatureHubService.LoadCatalog((catalog, error) =>
            {
                _catalog = catalog ?? new AssetCatalog();
                if (error != null)
                    Debug.LogWarning($"[FeatureHub] {error}");
                CheckLoaded();
            });

            FeatureHubService.LoadTemplate((template, error) =>
            {
                _template = template ?? new UnityTemplate();
                if (error != null)
                    Debug.LogWarning($"[FeatureHub] {error}");
                CheckLoaded();
            });

            // Index feature tải độc lập: KHÔNG gate vào CheckLoaded để index chậm/lỗi không chặn 2 tab kia.
            FeatureHubService.LoadFeaturesIndex((index, error) =>
            {
                _featuresIndex = index ?? new FeaturesIndex();
                if (error != null)
                    Debug.LogWarning($"[FeatureHub] {error}");
                if (_tab == 2)
                    RebuildList();
            });
        }

        private void CheckLoaded()
        {
            if (_catalog == null || _template == null)
                return;

            _loading = false;
            HideStatusIcon();
            int upmCount = _template.dependencies?
                .Count(kv => !kv.Key.StartsWith(FeatureHubConstants.UNITY_MODULE_PREFIX)) ?? 0;
            SetStatus($"Sẵn sàng — {_catalog.assets.Count} unitypackage · {upmCount} UPM package.");
            RebuildList();

            MaybePromptScopedRegistry();
        }

        /// <summary>
        /// Khi mở Feature Hub: validate project đã có Scoped Registry chưa. Nếu thiếu thì show popup
        /// confirm, đồng ý thì đăng ký vào Packages/manifest.json rồi resolve. Chỉ tự hỏi 1 lần / lần mở.
        /// </summary>
        private void MaybePromptScopedRegistry()
        {
            if (_registryChecked)
                return;
            _registryChecked = true;

            if (FeatureHubService.ValidateScopedRegistries(_template, out var missing))
                return;

            string detail = string.Join("\n", missing.Select(r =>
                $"• {r.name} ({r.url})\n   scopes: {string.Join(", ", r.scopes)}"));

            bool agree = EditorUtility.DisplayDialog(
                "Thiếu Scoped Registry",
                "Project chưa khai báo đủ Scoped Registry để tải UPM package của EZG.\n\n" +
                "Cần đăng ký:\n" + detail + "\n\nĐăng ký ngay vào Packages/manifest.json?",
                "Đăng ký", "Để sau");

            if (!agree)
            {
                SetStatus("Bỏ qua đăng ký Scoped Registry — một số UPM package có thể không tải được.");
                return;
            }

            SetBusy(true);
            SetStatus("Đang đăng ký Scoped Registry...");
            FeatureHubService.EnsureScopedRegistries(_template, resolveNow: true, (ok, error) =>
            {
                SetBusy(false);
                if (ok)
                {
                    SetStatus("Đã đăng ký Scoped Registry vào manifest.");
                    FlashCheck();
                    RefreshState();
                }
                else
                {
                    Debug.LogWarning($"[FeatureHub] {error}");
                    SetStatus($"Đăng ký Scoped Registry lỗi: {error}");
                }
            });
        }

        #endregion

        #region UI — List

        private void RebuildList()
        {
            if (_listContainer == null)
                return;

            _listContainer.Clear();

            if (_loading)
            {
                _listContainer.Add(LoadingCard());
                return;
            }

            switch (_tab)
            {
                case 0: BuildUnityTab(); break;
                case 1: BuildUpmTab(); break;
                default: BuildFeaturesTab(); break;
            }

            StaggerAppear();
        }

        /// <summary>Fade-in lần lượt từng dòng cho mượt.</summary>
        private void StaggerAppear()
        {
            int i = 0;
            foreach (var child in _listContainer.Children())
            {
                var c = child;
                c.style.opacity = 0f;
                int delay = Mathf.Min(i * 16, 280);
                c.schedule.Execute(() =>
                        c.experimental.animation.Start(0f, 1f, 150, (e, v) => e.style.opacity = v))
                    .StartingIn(delay);
                i++;
            }
        }

        private void BuildUnityTab()
        {
            if (_catalog == null || _catalog.assets.Count == 0)
            {
                _listContainer.Add(InfoCard("Không có .unitypackage nào trong catalog."));
                return;
            }

            _listContainer.Add(SectionActionBar("Cài tất cả gói mặc định", InstallAllDefaults));

            var filtered = _catalog.assets.Where(MatchSearchAsset).ToList();
            if (filtered.Count == 0)
            {
                _listContainer.Add(InfoCard("Không khớp từ khóa tìm kiếm."));
                return;
            }

            foreach (var group in filtered
                         .GroupBy(a => string.IsNullOrEmpty(a.category) ? "Khác" : a.category)
                         .OrderBy(g => g.Key))
            {
                _listContainer.Add(CategoryHeader(group.Key, group.Count()));
                foreach (var asset in group.OrderBy(a => a.name))
                    _listContainer.Add(BuildAssetCard(asset));
            }
        }

        private void BuildUpmTab()
        {
            if (_template == null || _template.dependencies == null || _template.dependencies.Count == 0)
            {
                _listContainer.Add(InfoCard("Không có UPM dependency nào trong template."));
                return;
            }

            _listContainer.Add(SectionActionBar("Cập nhật tất cả còn thiếu", InstallAllUpm));

            var deps = _template.dependencies
                .Where(kv => !kv.Key.StartsWith(FeatureHubConstants.UNITY_MODULE_PREFIX))
                .Where(kv => MatchSearchText(kv.Key) || MatchSearchText(kv.Value))
                .OrderBy(kv => kv.Key)
                .ToList();

            if (deps.Count == 0)
            {
                _listContainer.Add(InfoCard("Không khớp từ khóa tìm kiếm."));
                return;
            }

            _listContainer.Add(CategoryHeader("Dependencies", deps.Count));
            foreach (var kv in deps)
                _listContainer.Add(BuildUpmCard(kv.Key, kv.Value));
        }

        private void BuildFeaturesTab()
        {
            // Index chưa về (tải độc lập với gate "ready") -> hiện loading.
            if (_featuresIndex == null)
            {
                _listContainer.Add(LoadingCard());
                return;
            }

            if (_featuresIndex.projects == null || _featuresIndex.projects.Count == 0)
            {
                _listContainer.Add(InfoCard("Chưa có dự án feature nào."));
                return;
            }

            // Chốt project đang chọn: nếu chưa chọn / id không còn trong index -> mặc định project đầu.
            FeatureProject selected = _featuresIndex.projects
                .FirstOrDefault(p => p != null && p.id == _selectedProjectId);
            if (selected == null)
            {
                selected = _featuresIndex.projects[0];
                _selectedProjectId = selected.id;
                EditorPrefs.SetString(PREF_FEATURE_PROJECT, _selectedProjectId);
            }

            _listContainer.Add(BuildProjectSelectorRow(selected));

            // Catalog chưa sẵn sàng và chưa đang tải -> kick off load (sẽ tự RebuildList khi xong).
            // KHÔNG add LoadingCard ở đây: LoadSelectedProjectCatalog gọi RebuildList ngay (set
            // _featuresLoading) nên vẽ lại sẽ rơi vào nhánh loading bên dưới — tránh add trùng.
            if (_featureCatalog == null && !_featuresLoading)
            {
                LoadSelectedProjectCatalog(selected);
                return;
            }

            // Đang tải catalog của project -> loading.
            if (_featuresLoading || _featureCatalog == null)
            {
                _listContainer.Add(LoadingCard());
                return;
            }

            if (_featureCatalog.assets == null || _featureCatalog.assets.Count == 0)
            {
                _listContainer.Add(InfoCard($"Dự án {ProjectDisplayName(selected)} chưa có feature nào."));
                return;
            }

            _listContainer.Add(SectionActionBar("Cài tất cả feature còn thiếu", InstallAllFeatures));

            var filtered = _featureCatalog.assets.Where(MatchSearchAsset).ToList();
            if (filtered.Count == 0)
            {
                _listContainer.Add(InfoCard("Không khớp từ khóa tìm kiếm."));
                return;
            }

            foreach (var group in filtered
                         .GroupBy(a => string.IsNullOrEmpty(a.category) ? "Khác" : a.category)
                         .OrderBy(g => g.Key))
            {
                _listContainer.Add(CategoryHeader(group.Key, group.Count()));
                foreach (var asset in group.OrderBy(a => a.name))
                    _listContainer.Add(BuildAssetCard(asset));
            }
        }

        /// <summary>Header row: popup chọn dự án + số feature của dự án đang chọn.</summary>
        private VisualElement BuildProjectSelectorRow(FeatureProject selected)
        {
            var row = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row, alignItems = Align.Center,
                    paddingLeft = 2, paddingRight = 10, paddingTop = 4, paddingBottom = 4,
                },
            };

            var names = _featuresIndex.projects.Select(ProjectDisplayName).ToList();
            int selectedIndex = Mathf.Max(0, _featuresIndex.projects.IndexOf(selected));
            var popup = new PopupField<string>(names, selectedIndex);
            popup.RegisterValueChangedCallback(evt =>
            {
                int idx = names.IndexOf(evt.newValue);
                if (idx < 0 || idx >= _featuresIndex.projects.Count)
                    return;
                var project = _featuresIndex.projects[idx];
                if (project == null || project.id == _selectedProjectId)
                    return;

                _selectedProjectId = project.id;
                EditorPrefs.SetString(PREF_FEATURE_PROJECT, _selectedProjectId);
                _featureCatalog = null;
                LoadSelectedProjectCatalog(project);
            });
            row.Add(LabeledField("Dự án", popup));

            if (selected.featureCount > 0)
                row.Add(new Label($"· {selected.featureCount} feature")
                {
                    style = { marginLeft = 8, color = C_MUTED, fontSize = 11 },
                });

            return row;
        }

        /// <summary>Tên hiển thị của project: ưu tiên name, fallback id.</summary>
        private static string ProjectDisplayName(FeatureProject project)
        {
            if (project == null)
                return "—";
            return string.IsNullOrEmpty(project.name) ? project.id : project.name;
        }

        /// <summary>Lấy catalog của project đang chọn: ưu tiên cache, không có thì tải lazy.</summary>
        private void LoadSelectedProjectCatalog(FeatureProject project)
        {
            if (project == null)
                return;

            if (_featureCatalogCache.TryGetValue(project.id, out var cached))
            {
                _featureCatalog = cached;
                _featuresLoading = false;
                RebuildList();
                return;
            }

            _featuresLoading = true;
            SetStatus($"Đang tải feature của {ProjectDisplayName(project)}...");
            ShowSpinner();
            RebuildList();

            FeatureHubService.LoadFeatureCatalog(project.catalogUrl, (catalog, error) =>
            {
                _featuresLoading = false;
                HideStatusIcon();
                if (error != null)
                {
                    Debug.LogWarning($"[FeatureHub] {error}");
                    SetStatus($"Lỗi tải feature {ProjectDisplayName(project)}: {error}");
                    _featureCatalog = new AssetCatalog();
                }
                else
                {
                    _featureCatalog = catalog ?? new AssetCatalog();
                    _featureCatalogCache[project.id] = _featureCatalog;
                    SetStatus($"{ProjectDisplayName(project)} — {_featureCatalog.assets.Count} feature.");
                }

                // Chỉ vẽ lại nếu user vẫn đang ở đúng project này.
                if (_selectedProjectId == project.id)
                    RebuildList();
            });
        }

        #endregion

        #region UI — Expandable cards

        private VisualElement BuildAssetCard(CatalogAsset asset)
        {
            var status = FeatureHubInstallRecord.GetStatus(asset);
            Color statusColor = UnityStatusColor(status);

            var detail = DetailContainer();
            detail.Add(DetailRow("Trạng thái", UnityStatusText(status)));
            detail.Add(DetailRow("Danh mục", string.IsNullOrEmpty(asset.category) ? "—" : asset.category));
            detail.Add(DetailRow("File", asset.fileName));
            detail.Add(DetailRow("Mặc định", asset.installedByDefault ? "Có (bootstrap)" : "Không"));
            if (!string.IsNullOrEmpty(asset.sha256))
                detail.Add(DetailRow("SHA-256", Short(asset.sha256, 24)));
            var record = FeatureHubInstallRecord.Get(asset.name);
            if (record != null)
                detail.Add(DetailRow("Đã cài lúc", FormatTime(record.installedAtUtc)));
            detail.Add(UrlRow(asset.url));

            Color btnColor = status == UnityPackageStatus.Installed ? C_PILL_OFF : C_ACCENT;
            Color btnText = status == UnityPackageStatus.Installed ? C_TEXT : Color.white;
            var action = StyledButton(UnityActionText(status), btnColor, btnText,
                () => RunUnityInstall(asset), 88);

            return ExpandableCard(
                asset.name,
                asset.category + (asset.installedByDefault ? "  ·  mặc định" : string.Empty),
                UnityStatusIcon(status), statusColor,
                StatusPill(UnityStatusText(status), statusColor, status == UnityPackageStatus.Installed),
                action, detail);
        }

        private VisualElement BuildUpmCard(string id, string value)
        {
            UpmStatus status = ResolveUpmStatus(id, value);
            _resolvedPackages.TryGetValue(id, out string resolvedVer);
            _projectDeps.TryGetValue(id, out string manifestVal);
            string current = !string.IsNullOrEmpty(resolvedVer) ? resolvedVer : manifestVal;
            Color statusColor = UpmStatusColor(status);

            var detail = DetailContainer();
            detail.Add(DetailRow("Trạng thái", UpmStatusText(status)));
            detail.Add(DetailRow("Loại", UpmSourceType(value)));
            detail.Add(DetailRow("Target", value));
            detail.Add(DetailRow("Hiện tại", string.IsNullOrEmpty(current) ? "— (chưa có trong project)" : current));

            Color btnColor = status == UpmStatus.Installed ? C_PILL_OFF : C_ACCENT;
            Color btnText = status == UpmStatus.Installed ? C_TEXT : Color.white;
            var action = StyledButton(UpmActionText(status), btnColor, btnText,
                () => RunUpmInstall(id, value), 88);

            return ExpandableCard(
                id, UpmSourceType(value),
                UpmStatusIcon(status), statusColor,
                StatusPill(UpmStatusText(status), statusColor, status == UpmStatus.Installed),
                action, detail);
        }

        /// <summary>Card có header bấm để expand + panel detail. Icon trái phát animation khi hover.</summary>
        private VisualElement ExpandableCard(
            string title, string subtitle, string iconKey, Color accentColor,
            VisualElement statusPill, Button action, VisualElement detail)
        {
            var card = new VisualElement
            {
                style =
                {
                    marginLeft = 10, marginRight = 10, marginTop = 3, marginBottom = 3,
                    backgroundColor = C_CARD, overflow = Overflow.Hidden,
                    borderLeftWidth = 0, borderLeftColor = C_ACCENT,
                },
            };
            Round(card, 8);

            var header = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row, alignItems = Align.Center,
                    paddingLeft = 10, paddingRight = 10, paddingTop = 8, paddingBottom = 8,
                },
            };

            var clickArea = new VisualElement
            {
                style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, flexGrow = 1, flexShrink = 1 },
            };
            var chevron = new Label("▸")
            {
                style = { color = C_MUTED, fontSize = 11, width = 14, flexShrink = 0, unityTextAlign = TextAnchor.MiddleCenter },
            };
            clickArea.Add(chevron);

            LottieElement rowIcon = IconIdle(iconKey, 24);
            if (rowIcon != null)
            {
                rowIcon.style.marginRight = 8;
                clickArea.Add(rowIcon);
            }
            else
            {
                clickArea.Add(StatusDot(accentColor));
            }

            var textCol = new VisualElement { style = { flexGrow = 1, flexShrink = 1 } };
            textCol.Add(new Label(title)
            {
                style = { unityFontStyleAndWeight = FontStyle.Bold, color = C_TEXT, fontSize = 12 },
            });
            textCol.Add(SubLabel(subtitle));
            clickArea.Add(textCol);

            header.Add(clickArea);
            header.Add(statusPill);
            header.Add(action);
            card.Add(header);

            detail.style.display = DisplayStyle.None;
            card.Add(detail);

            bool expanded = false;
            clickArea.RegisterCallback<ClickEvent>(_ =>
            {
                expanded = !expanded;
                detail.style.display = expanded ? DisplayStyle.Flex : DisplayStyle.None;
                chevron.text = expanded ? "▾" : "▸";
                card.style.backgroundColor = expanded ? C_CARD_OPEN : C_CARD;
                card.style.borderLeftWidth = expanded ? 2 : 0;
                if (expanded)
                {
                    detail.style.opacity = 0f;
                    detail.experimental.animation.Start(0f, 1f, 160, (e, v) => e.style.opacity = v);
                }
            });

            card.RegisterCallback<MouseEnterEvent>(_ =>
            {
                rowIcon?.Play();
                if (!expanded) card.style.backgroundColor = C_CARD_HOVER;
            });
            card.RegisterCallback<MouseLeaveEvent>(_ =>
            {
                if (!expanded) card.style.backgroundColor = C_CARD;
            });

            return card;
        }

        #endregion

        #region Actions — Single install

        private void RunUnityInstall(CatalogAsset asset)
        {
            if (_busy)
                return;

            bool? interactive = ResolveInteractive(asset.name);
            if (interactive == null)
                return; // user hủy

            SetBusy(true);
            FeatureHubService.InstallUnityPackage(
                asset,
                interactive.Value,
                p => SetStatus($"Đang tải {asset.name}... {p:P0}"),
                (ok, error) =>
                {
                    SetBusy(false);
                    if (!ok && error != null)
                        Debug.LogWarning($"[FeatureHub] {asset.name}: {error}");
                    SetStatus(ok ? $"Đã cài {asset.name}" : $"Lỗi {asset.name}: {error}");
                    if (ok)
                        FlashCheck();
                    RefreshState();
                });
        }

        private void RunUpmInstall(string id, string value)
        {
            if (_busy)
                return;

            SetBusy(true);
            FeatureHubService.InstallUpm(
                id, value, _template, resolveNow: true,
                p => SetStatus($"Đang xử lý {id}... {p:P0}"),
                (ok, error) =>
                {
                    SetBusy(false);
                    if (!ok && error != null)
                        Debug.LogWarning($"[FeatureHub] {id}: {error}");
                    SetStatus(ok ? $"{id} đã ghi vào manifest" : $"Lỗi {id}: {error}");
                    if (ok)
                        FlashCheck();
                    RefreshState();
                });
        }

        #endregion

        #region Actions — Batch install

        private void InstallAllDefaults()
        {
            if (_busy || _catalog == null)
                return;

            var queue = _catalog.assets
                .Where(a => a.installedByDefault &&
                            FeatureHubInstallRecord.GetStatus(a) != UnityPackageStatus.Installed)
                .ToList();

            if (queue.Count == 0)
            {
                SetStatus("Tất cả gói mặc định đã được cài.");
                return;
            }

            if (!EditorUtility.DisplayDialog(
                    "Cài tất cả gói mặc định",
                    $"Sẽ tải & import {queue.Count} gói .unitypackage (chế độ silent). Tiếp tục?",
                    "Cài", "Hủy"))
                return;

            SetBusy(true);
            RunUnityQueue(queue, 0);
        }

        private void RunUnityQueue(List<CatalogAsset> queue, int index)
        {
            if (index >= queue.Count)
            {
                SetBusy(false);
                SetStatus($"Hoàn tất {queue.Count} gói.");
                FlashConfetti();
                RefreshState();
                return;
            }

            var asset = queue[index];
            FeatureHubService.InstallUnityPackage(
                asset,
                interactive: false,
                p => SetStatus($"[{index + 1}/{queue.Count}] Đang tải {asset.name}... {p:P0}"),
                (ok, error) =>
                {
                    if (!ok && error != null)
                        Debug.LogWarning($"[FeatureHub] {asset.name}: {error}");
                    RunUnityQueue(queue, index + 1);
                });
        }

        private void InstallAllFeatures()
        {
            if (_busy || _featureCatalog == null)
                return;

            string projectName = ProjectDisplayName(
                _featuresIndex?.projects?.FirstOrDefault(p => p != null && p.id == _selectedProjectId));

            var queue = _featureCatalog.assets
                .Where(a => FeatureHubInstallRecord.GetStatus(a) != UnityPackageStatus.Installed)
                .ToList();

            if (queue.Count == 0)
            {
                SetStatus($"Tất cả feature của {projectName} đã được cài.");
                return;
            }

            if (!EditorUtility.DisplayDialog(
                    "Cài tất cả feature còn thiếu",
                    $"Sẽ tải & import {queue.Count} feature của {projectName} (chế độ silent). Tiếp tục?",
                    "Cài", "Hủy"))
                return;

            SetBusy(true);
            RunUnityQueue(queue, 0);
        }

        private void InstallAllUpm()
        {
            if (_busy || _template?.dependencies == null)
                return;

            // Chỉ nâng cấp package ĐÃ cài lên bản mới hơn — bỏ qua package chưa cài, không bao giờ hạ bản.
            var queue = _template.dependencies
                .Where(kv => !kv.Key.StartsWith(FeatureHubConstants.UNITY_MODULE_PREFIX))
                .Where(kv => ResolveUpmStatus(kv.Key, kv.Value) == UpmStatus.UpdateAvailable)
                .ToList();

            if (queue.Count == 0)
            {
                SetStatus("Tất cả UPM package đã ở bản mới nhất.");
                return;
            }

            if (!EditorUtility.DisplayDialog(
                    "Cập nhật UPM",
                    $"Sẽ cập nhật {queue.Count} package lên bản mới hơn rồi resolve. Tiếp tục?",
                    "Cập nhật", "Hủy"))
                return;

            SetBusy(true);
            RunUpmQueue(queue, 0);
        }

        private void RunUpmQueue(List<KeyValuePair<string, string>> queue, int index)
        {
            if (index >= queue.Count)
            {
                FeatureHubService.ResolveNow();
                SetBusy(false);
                SetStatus($"Đã ghi {queue.Count} dependency, đang resolve...");
                FlashConfetti();
                RefreshState();
                return;
            }

            var kv = queue[index];
            FeatureHubService.InstallUpm(
                kv.Key, kv.Value, _template, resolveNow: false,
                p => SetStatus($"[{index + 1}/{queue.Count}] {kv.Key}... {p:P0}"),
                (ok, error) =>
                {
                    if (!ok && error != null)
                        Debug.LogWarning($"[FeatureHub] {kv.Key}: {error}");
                    RunUpmQueue(queue, index + 1);
                });
        }

        #endregion

        #region Helpers — Behaviour

        private bool? ResolveInteractive(string assetName)
        {
            switch (_importMode)
            {
                case ImportMode.Silent:
                    return false;
                case ImportMode.Dialog:
                    return true;
                default:
                    int choice = EditorUtility.DisplayDialogComplex(
                        "Cách import",
                        $"Import '{assetName}' bằng cách nào?",
                        "Silent (tự động)", "Hủy", "Dialog (chọn file)");
                    if (choice == 1)
                        return null;
                    return choice == 2; // 0 -> false (silent), 2 -> true (dialog)
            }
        }

        private void SetBusy(bool busy)
        {
            _busy = busy;
            _content?.SetEnabled(!busy);
            if (busy)
                ShowSpinner();
            else
                HideStatusIcon();
        }

        private void RefreshState()
        {
            _projectDeps = FeatureHubService.LoadProjectDependencies();
            LoadResolvedPackagesAsync();
            RebuildList();
        }

        /// <summary>Nạp danh sách package đã resolve (async) rồi vẽ lại để cập nhật trạng thái UPM.</summary>
        private void LoadResolvedPackagesAsync()
        {
            FeatureHubService.LoadResolvedPackages(map =>
            {
                _resolvedPackages = map ?? new Dictionary<string, string>();
                RebuildList();
            });
        }

        /// <summary>
        /// UPM dependency coi như "đủ" (không cần cài) khi đã resolve trong project. Match theo id;
        /// chỉ báo cần-cập-nhật khi cả template lẫn bản resolve đều là version cụ thể và lệch nhau.
        /// </summary>
        private bool IsUpmSatisfied(string id, string value)
        {
            return ResolveUpmStatus(id, value) == UpmStatus.Installed;
        }

        /// <summary>
        /// Tính trạng thái UPM: ưu tiên bản đã resolve (Client.List) — bắt được cả package "đã có sẵn"
        /// không nằm trực tiếp trong manifest; fallback về manifest dependency nếu chưa nạp xong resolve.
        /// </summary>
        private UpmStatus ResolveUpmStatus(string id, string value)
        {
            bool resolved = _resolvedPackages.TryGetValue(id, out string resolvedVer);
            bool inManifest = _projectDeps.TryGetValue(id, out string manifestVal);

            if (!resolved && !inManifest)
                return UpmStatus.NotInstalled;

            // Có mặt trong project. Chỉ đánh "Có bản mới" khi cả target lẫn bản hiện tại là version cụ thể
            // VÀ target mới hơn hẳn — tránh báo nhầm cho dep dạng "file:"/git/range, và KHÔNG bao giờ đề
            // xuất hạ bản (current >= target -> Installed).
            string current = resolved ? resolvedVer : manifestVal;
            if (IsConcreteVersion(value) && IsConcreteVersion(current) &&
                CompareSemver(value, current) > 0)
                return UpmStatus.UpdateAvailable;

            return UpmStatus.Installed;
        }

        /// <summary>
        /// So sánh 2 chuỗi semver cụ thể: trả về &lt;0 nếu a cũ hơn b, 0 nếu bằng, &gt;0 nếu a mới hơn b.
        /// So phần lõi số ("1.2.3") theo từng thành phần (thiếu = 0). Bỏ qua suffix sau '-'/'+' khi so lõi;
        /// nếu lõi bằng nhau thì bản CÓ pre-release ('-') xếp THẤP hơn bản không có (chuẩn semver). Gặp
        /// thành phần không phải số (malformed) -> fallback so sánh chuỗi OrdinalIgnoreCase, bằng thì trả 0.
        /// </summary>
        private static int CompareSemver(string a, string b)
        {
            // Không phải version cụ thể -> không so được, coi như bằng (không đề xuất update).
            if (!IsConcreteVersion(a) || !IsConcreteVersion(b))
                return 0;

            string coreA = SemverCore(a, out bool preA);
            string coreB = SemverCore(b, out bool preB);

            string[] partsA = coreA.Split('.');
            string[] partsB = coreB.Split('.');
            int len = Mathf.Max(partsA.Length, partsB.Length);
            for (int i = 0; i < len; i++)
            {
                if (!TryParseComponent(partsA, i, out int na) || !TryParseComponent(partsB, i, out int nb))
                    return 0; // malformed component -> ambiguous, không đề xuất update
                if (na != nb)
                    return na < nb ? -1 : 1;
            }

            // Lõi bằng nhau: bản có pre-release xếp thấp hơn bản không có.
            if (preA == preB)
                return 0;
            return preA ? -1 : 1;
        }

        /// <summary>Tách phần lõi số của semver (bỏ suffix sau '-' hoặc '+'); báo có pre-release ('-') không.</summary>
        private static string SemverCore(string value, out bool hasPreRelease)
        {
            hasPreRelease = false;
            if (string.IsNullOrEmpty(value))
                return string.Empty;
            int dash = value.IndexOf('-');
            int plus = value.IndexOf('+');
            int cut = -1;
            if (dash >= 0)
            {
                cut = dash;
                hasPreRelease = true;
            }
            if (plus >= 0 && (cut < 0 || plus < cut))
            {
                cut = plus;
                hasPreRelease = dash >= 0 && dash < plus; // '+' build metadata, '-' trước nó mới là pre-release
            }
            return cut < 0 ? value : value.Substring(0, cut);
        }

        private static bool TryParseComponent(string[] parts, int index, out int number)
        {
            if (index >= parts.Length)
            {
                number = 0; // thành phần thiếu = 0
                return true;
            }
            return int.TryParse(parts[index], out number);
        }

        /// <summary>Version cụ thể = chuỗi semver thuần (không phải "file:", git url, "*", range...).</summary>
        private static bool IsConcreteVersion(string value)
        {
            if (string.IsNullOrEmpty(value))
                return false;
            char c = value[0];
            if (c < '0' || c > '9')
                return false;
            return value.IndexOf("://", StringComparison.Ordinal) < 0;
        }

        private void SetStatus(string message)
        {
            if (_statusLabel != null)
                _statusLabel.text = message;
        }

        private bool MatchSearchAsset(CatalogAsset asset)
        {
            return MatchSearchText(asset.name) || MatchSearchText(asset.category);
        }

        private bool MatchSearchText(string text)
        {
            if (string.IsNullOrEmpty(_search))
                return true;
            return !string.IsNullOrEmpty(text) &&
                   text.IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        #endregion

        #region Helpers — Lottie status icons (footer)

        private void ShowSpinner()
        {
            string json = LottieLibrary.GetJson(LottieLibrary.LOADING);
            SetStatusIcon(json != null ? new LottieElement(json, 16) : null);
        }

        private void FlashCheck()
        {
            string json = LottieLibrary.GetJson(LottieLibrary.CHECK);
            if (json == null)
                return;
            var check = new LottieElement(json, 18, LottiePlay.Once);
            SetStatusIcon(check);
            rootVisualElement.schedule.Execute(HideStatusIcon).StartingIn(1600);
        }

        private void FlashConfetti()
        {
            string json = LottieLibrary.GetJson(LottieLibrary.CONFETTI);
            if (json == null)
                return;

            var overlay = new VisualElement
            {
                pickingMode = PickingMode.Ignore,
                style =
                {
                    position = Position.Absolute, left = 0, right = 0, top = 0, bottom = 0,
                    alignItems = Align.Center, justifyContent = Justify.Center,
                },
            };
            var confetti = new LottieElement(json, 240, LottiePlay.Once);
            overlay.Add(confetti);
            rootVisualElement.Add(overlay);
            rootVisualElement.schedule.Execute(() => rootVisualElement.Remove(overlay)).StartingIn(2200);
        }

        private void SetStatusIcon(VisualElement element)
        {
            if (_statusIconBox == null)
                return;
            _statusIconBox.Clear();
            if (element != null)
                _statusIconBox.Add(element);
        }

        private void HideStatusIcon()
        {
            _statusIconBox?.Clear();
        }

        #endregion

        #region Helpers — Widgets

        private LottieElement IconIdle(string key, int size)
        {
            string json = LottieLibrary.GetJson(key);
            return json != null ? new LottieElement(json, size, LottiePlay.Idle) : null;
        }

        private VisualElement SectionActionBar(string buttonText, Action onClick)
        {
            var bar = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row, justifyContent = Justify.FlexEnd, alignItems = Align.Center,
                    paddingLeft = 10, paddingRight = 10, paddingTop = 4, paddingBottom = 4,
                },
            };
            var icon = IconIdle(LottieLibrary.DOWNLOAD, 18);
            if (icon != null)
            {
                icon.style.marginRight = 6;
                bar.Add(icon);
                bar.RegisterCallback<MouseEnterEvent>(_ => icon.Play());
            }
            bar.Add(StyledButton(buttonText, C_SUCCESS, Color.white, () => onClick?.Invoke(), 0));
            return bar;
        }

        private VisualElement LoadingCard()
        {
            var box = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Column, alignItems = Align.Center, justifyContent = Justify.Center,
                    paddingTop = 50, paddingBottom = 50,
                },
            };
            string json = LottieLibrary.GetJson(LottieLibrary.LOADING);
            if (json != null)
                box.Add(new LottieElement(json, 64) { style = { marginBottom = 8 } });
            box.Add(new Label("Đang tải...") { style = { color = C_MUTED } });
            return box;
        }

        private VisualElement InfoCard(string text)
        {
            return new Label(text)
            {
                style = { color = C_MUTED, paddingLeft = 14, paddingTop = 16, paddingBottom = 16 },
            };
        }

        private VisualElement DetailContainer()
        {
            return new VisualElement
            {
                style =
                {
                    paddingLeft = 34, paddingRight = 12, paddingTop = 2, paddingBottom = 10,
                    borderTopWidth = 1, borderTopColor = C_BORDER,
                },
            };
        }

        private VisualElement DetailRow(string label, string value)
        {
            var row = new VisualElement
            {
                style = { flexDirection = FlexDirection.Row, alignItems = Align.FlexStart, marginTop = 3 },
            };
            row.Add(new Label(label)
            {
                style = { width = 92, flexShrink = 0, color = C_MUTED, fontSize = 10 },
            });
            row.Add(new Label(value ?? "—")
            {
                style = { flexGrow = 1, flexShrink = 1, color = C_TEXT, fontSize = 10, whiteSpace = WhiteSpace.Normal },
            });
            return row;
        }

        private VisualElement UrlRow(string url)
        {
            var row = new VisualElement
            {
                style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, marginTop = 5 },
            };
            row.Add(new Label("Nguồn")
            {
                style = { width = 92, flexShrink = 0, color = C_MUTED, fontSize = 10 },
            });
            row.Add(new Label(Short(url, 44))
            {
                style = { flexGrow = 1, flexShrink = 1, color = C_ACCENT, fontSize = 10, whiteSpace = WhiteSpace.Normal },
            });
            row.Add(StyledButton("Mở ↗", C_PILL_OFF, C_TEXT, () => Application.OpenURL(url), 0));
            return row;
        }

        private static VisualElement LabeledField(string label, VisualElement field)
        {
            var wrap = new VisualElement
            {
                style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, marginLeft = 10 },
            };
            wrap.Add(new Label(label) { style = { marginRight = 3, color = C_MUTED, fontSize = 11 } });
            wrap.Add(field);
            return wrap;
        }

        private Button StyledButton(string text, Color bg, Color fg, Action onClick, int minWidth)
        {
            var btn = new Button(() => onClick?.Invoke()) { text = text };
            btn.style.backgroundColor = bg;
            btn.style.color = fg;
            btn.style.unityFontStyleAndWeight = FontStyle.Bold;
            btn.style.fontSize = 11;
            btn.style.paddingLeft = 12;
            btn.style.paddingRight = 12;
            btn.style.paddingTop = 5;
            btn.style.paddingBottom = 5;
            btn.style.marginLeft = 4;
            btn.style.marginRight = 0;
            FlatBorders(btn);
            if (minWidth > 0)
                btn.style.minWidth = minWidth;
            Round(btn, 6);
            return btn;
        }

        private VisualElement CategoryHeader(string text, int count)
        {
            var row = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row, alignItems = Align.Center,
                    paddingLeft = 12, paddingTop = 12, paddingBottom = 3, marginTop = 2,
                },
            };
            var icon = IconIdle(CategoryIconKey(text), 18);
            if (icon != null)
            {
                icon.style.marginRight = 6;
                row.Add(icon);
                row.RegisterCallback<MouseEnterEvent>(_ => icon.Play());
            }
            row.Add(new Label($"{text}   ({count})")
            {
                style =
                {
                    unityFontStyleAndWeight = FontStyle.Bold,
                    color = (Color)new Color32(122, 172, 255, 255), fontSize = 11,
                },
            });
            return row;
        }

        private static Label SubLabel(string text)
        {
            return new Label(text)
            {
                style = { fontSize = 10, color = C_MUTED, marginTop = 1, whiteSpace = WhiteSpace.NoWrap },
            };
        }

        private static VisualElement StatusDot(Color color)
        {
            var dot = new VisualElement
            {
                style = { width = 8, height = 8, marginRight = 8, flexShrink = 0, backgroundColor = color },
            };
            Round(dot, 4);
            return dot;
        }

        private static Label StatusPill(string text, Color color, bool withCheck)
        {
            var pill = new Label(withCheck ? "✓ " + text : text)
            {
                style =
                {
                    marginRight = 8, paddingLeft = 9, paddingRight = 9, paddingTop = 2, paddingBottom = 2,
                    minWidth = 76, unityTextAlign = TextAnchor.MiddleCenter, fontSize = 10,
                    color = color, backgroundColor = new Color(color.r, color.g, color.b, 0.18f),
                    unityFontStyleAndWeight = FontStyle.Bold, flexShrink = 0,
                },
            };
            Round(pill, 9);
            return pill;
        }

        private static void Round(VisualElement element, float radius)
        {
            element.style.borderTopLeftRadius = radius;
            element.style.borderTopRightRadius = radius;
            element.style.borderBottomLeftRadius = radius;
            element.style.borderBottomRightRadius = radius;
        }

        private static void FlatBorders(VisualElement element)
        {
            element.style.borderTopWidth = 0;
            element.style.borderBottomWidth = 0;
            element.style.borderLeftWidth = 0;
            element.style.borderRightWidth = 0;
        }

        private static string Short(string s, int max)
        {
            if (string.IsNullOrEmpty(s))
                return "—";
            return s.Length <= max ? s : s.Substring(0, max) + "…";
        }

        private static string FormatTime(string iso)
        {
            if (DateTime.TryParse(iso, null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt))
                return dt.ToLocalTime().ToString("dd/MM/yyyy HH:mm");
            return iso;
        }

        private static string UpmSourceType(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "—";
            if (value.StartsWith("file:"))
                return "Local (.tgz)";
            if (value.Contains("://") || value.StartsWith("git"))
                return "Git / URL";
            return "Registry (semver)";
        }

        #endregion

        #region Helpers — icon mapping

        private static string CategoryIconKey(string category) => category switch
        {
            "Editor" => LottieLibrary.SETTINGS,
            "Ads" => LottieLibrary.BELL,
            "Firebase" => LottieLibrary.ACTIVITY,
            "Google Play" => LottieLibrary.DOWNLOAD,
            "VFX & Shader" => LottieLibrary.STAR,
            "GUI" => LottieLibrary.STAR,
            "Feedback" => LottieLibrary.HEART,
            "SDK" => LottieLibrary.FOLDER,
            "Tween & Animation" => LottieLibrary.ACTIVITY,
            "EZG Base" => LottieLibrary.GITHUB,
            _ => LottieLibrary.FOLDER,
        };

        private static string UnityStatusIcon(UnityPackageStatus s) => s switch
        {
            UnityPackageStatus.Installed => LottieLibrary.CHECK2,
            UnityPackageStatus.UpdateAvailable => LottieLibrary.UPDATE,
            _ => LottieLibrary.DOWNLOAD,
        };

        private static string UpmStatusIcon(UpmStatus s) => s switch
        {
            UpmStatus.Installed => LottieLibrary.CHECK2,
            UpmStatus.UpdateAvailable => LottieLibrary.UPDATE,
            _ => LottieLibrary.DOWNLOAD,
        };

        #endregion

        #region Helpers — Status text/color

        private static string UnityStatusText(UnityPackageStatus s) => s switch
        {
            UnityPackageStatus.Installed => "Đã cài",
            UnityPackageStatus.UpdateAvailable => "Có bản mới",
            _ => "Chưa cài",
        };

        private static Color UnityStatusColor(UnityPackageStatus s) => s switch
        {
            UnityPackageStatus.Installed => C_SUCCESS,
            UnityPackageStatus.UpdateAvailable => C_WARN,
            _ => C_MUTED,
        };

        private static string UnityActionText(UnityPackageStatus s) => s switch
        {
            UnityPackageStatus.Installed => "Cài lại",
            UnityPackageStatus.UpdateAvailable => "Cập nhật",
            _ => "Cài",
        };

        private static string UpmStatusText(UpmStatus s) => s switch
        {
            UpmStatus.Installed => "Đã cài",
            UpmStatus.UpdateAvailable => "Có bản mới",
            _ => "Chưa cài",
        };

        private static Color UpmStatusColor(UpmStatus s) => s switch
        {
            UpmStatus.Installed => C_SUCCESS,
            UpmStatus.UpdateAvailable => C_WARN,
            _ => C_MUTED,
        };

        private static string UpmActionText(UpmStatus s) => s switch
        {
            UpmStatus.Installed => "Cài lại",
            UpmStatus.UpdateAvailable => "Cập nhật",
            _ => "Cài",
        };

        #endregion
    }
}
