using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace RevitApi_3
{
    public partial class MappingWindow : Window
    {
        private readonly ScheduleMirrorTable _table;
        private readonly Dictionary<string, string> _erpNameByCode =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private List<ScheduleMirrorRow> _revitView = new List<ScheduleMirrorRow>();
        private List<ErpItem> _erpItemsFull = new List<ErpItem>();
        private CancellationTokenSource _globalSearchCts;
        private int _erpRequestVersion;
        private bool _isClosed;

        public IList<ScheduleMirrorRow> ResultRows => _table.Rows;

        public MappingWindow(ScheduleMirrorTable table, string contextInfo)
        {
            InitializeComponent();
            _table = table ?? throw new ArgumentNullException(nameof(table));

            Title = "Сопоставление кодов 1C-ERP — " + (contextInfo ?? "");
            DiagnosticText.Text = _table.Diagnostic ?? "";

            BuildRevitColumns();
            RebuildRevitView();
            RebuildErpView();

            Loaded += MappingWindow_Loaded;
            Closed += MappingWindow_Closed;
        }

        private async void MappingWindow_Loaded(object sender, RoutedEventArgs e)
        {
            await Task.WhenAll(LoadTreeAsync(), PrefetchErpNamesAsync());
        }

        private void MappingWindow_Closed(object sender, EventArgs e)
        {
            _isClosed = true;
            _globalSearchCts?.Cancel();
        }

        private void BuildRevitColumns()
        {
            RevitGrid.Columns.Clear();

            foreach (ScheduleMirrorColumn column in _table.Columns.OrderBy(x => x.Index))
            {
                RevitGrid.Columns.Add(new DataGridTextColumn
                {
                    Header = column.Header,
                    Binding = new Binding($"Values[{column.Index}]") { Mode = BindingMode.OneWay },
                    IsReadOnly = true,
                    MinWidth = 85,
                    Width = DataGridLength.Auto
                });
            }

            RevitGrid.Columns.Add(new DataGridTextColumn
            {
                Header = "Код 1C-ERP (запись)",
                Binding = new Binding(nameof(ScheduleMirrorRow.ErpCode)),
                IsReadOnly = true,
                MinWidth = 125,
                Width = DataGridLength.Auto
            });
            RevitGrid.Columns.Add(new DataGridTextColumn
            {
                Header = "Наименование в 1C",
                Binding = new Binding(nameof(ScheduleMirrorRow.ErpName1c)),
                IsReadOnly = true,
                MinWidth = 180,
                Width = new DataGridLength(1, DataGridLengthUnitType.Star)
            });
        }

        private void RebuildRevitView()
        {
            string term = Normalize(RevitSearchBox?.Text);
            bool onlyWithout = ChkOnlyWithoutCode?.IsChecked == true;

            _revitView = _table.Rows
                .Where(row => !onlyWithout || row.HasMissingErpCode)
                .Where(row => string.IsNullOrEmpty(term) || MatchesRevitFilter(row, term))
                .ToList();

            RevitGrid.ItemsSource = _revitView;
            RevitGrid.Items.Refresh();
        }

        private static bool MatchesRevitFilter(ScheduleMirrorRow row, string normalizedTerm)
        {
            if (Normalize(row.ErpCode).Contains(normalizedTerm) ||
                Normalize(row.ErpName1c).Contains(normalizedTerm) ||
                Normalize(row.MatchInfo).Contains(normalizedTerm))
                return true;

            return row.Values.Any(value => Normalize(value).Contains(normalizedTerm));
        }

        private void RevitFilter_Changed(object sender, RoutedEventArgs e)
        {
            if (_table != null) RebuildRevitView();
        }

        private void RevitGrid_LoadingRow(object sender, DataGridRowEventArgs e)
        {
            var row = e.Row.Item as ScheduleMirrorRow;
            e.Row.Header = row?.SourceRowNumber.ToString() ?? "";
        }

        private async Task LoadTreeAsync()
        {
            int requestVersion = ++_erpRequestVersion;
            SetErpStatus("Загрузка дерева видов номенклатуры…", true);
            try
            {
                List<ErpTreeNode> roots = await Task.Run(() => ErpClient.LoadErpTree());
                if (_isClosed || requestVersion != _erpRequestVersion) return;
                ErpTree.ItemsSource = roots;
                if (ChkGlobalSearch.IsChecked == true) return;
                SetErpStatus("Выберите вид номенклатуры или включите глобальный поиск.", false);
            }
            catch (Exception ex)
            {
                if (_isClosed) return;
                SetErpStatus("Не удалось загрузить дерево ERP: " + ex.Message, false);
            }
        }

        private async Task PrefetchErpNamesAsync()
        {
            List<string> codes = _table.ResourceRows
                .Select(x => (x.ErpCode ?? "").Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x) && x != "-")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (codes.Count == 0) return;

            try
            {
                Dictionary<string, string> names = await Task.Run(
                    () => ErpClient.LoadNomenclatureNamesByCodes(codes));
                if (_isClosed || names == null) return;

                foreach (KeyValuePair<string, string> pair in names)
                    _erpNameByCode[pair.Key] = pair.Value ?? "";

                foreach (ScheduleMirrorRow row in _table.Rows)
                {
                    if (!string.IsNullOrWhiteSpace(row.ErpCode) &&
                        _erpNameByCode.TryGetValue(row.ErpCode.Trim(), out string name))
                        row.ErpName1c = name;
                }
                RebuildRevitView();
            }
            catch
            {
                // Название — справочное поле. Недоступность сервиса не блокирует
                // просмотр уже записанных кодов.
            }
        }

        private async void ErpTree_SelectedItemChanged(
            object sender,
            RoutedPropertyChangedEventArgs<object> e)
        {
            if (ChkGlobalSearch.IsChecked == true) return;
            ErpTreeNode node = ErpTree.SelectedItem as ErpTreeNode;
            if (node == null) return;

            int requestVersion = ++_erpRequestVersion;
            SetErpStatus("Загрузка номенклатуры выбранного вида…", true);
            try
            {
                List<ErpItem> items = await Task.Run(() => ErpClient.LoadErpItems(node.RefKey));
                if (_isClosed || requestVersion != _erpRequestVersion || ChkGlobalSearch.IsChecked == true)
                    return;

                _erpItemsFull = items ?? new List<ErpItem>();
                RebuildErpView();
                SetErpStatus("Найдено: " + _erpItemsFull.Count, false);
            }
            catch (Exception ex)
            {
                if (_isClosed || requestVersion != _erpRequestVersion) return;
                _erpItemsFull.Clear();
                RebuildErpView();
                SetErpStatus("Ошибка загрузки: " + ex.Message, false);
            }
        }

        private void ErpSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_table == null) return;
            if (ChkGlobalSearch.IsChecked == true)
                _ = RunGlobalSearchAsync();
            else
                RebuildErpView();
        }

        private void ChkGlobalSearch_Changed(object sender, RoutedEventArgs e)
        {
            if (_table == null) return;
            bool global = ChkGlobalSearch.IsChecked == true;
            ErpTree.IsEnabled = !global;
            _globalSearchCts?.Cancel();
            _erpItemsFull.Clear();
            RebuildErpView();

            if (global)
            {
                SetErpStatus("Введите не менее 2 символов. Поиск выполняется по всей номенклатуре ERP.", false);
                _ = RunGlobalSearchAsync();
            }
            else
            {
                ErpSearchBox.Text = "";
                SetErpStatus("Выберите вид номенклатуры.", false);
            }
        }

        private async Task RunGlobalSearchAsync()
        {
            _globalSearchCts?.Cancel();
            var cts = new CancellationTokenSource();
            _globalSearchCts = cts;
            CancellationToken token = cts.Token;

            string query = (ErpSearchBox.Text ?? "").Trim();
            if (query.Length < 2)
            {
                _erpItemsFull.Clear();
                RebuildErpView();
                SetErpStatus("Введите не менее 2 символов для глобального поиска.", false);
                return;
            }

            try
            {
                await Task.Delay(350, token);
                SetErpStatus("Глобальный поиск…", true);
                List<ErpItem> items = await Task.Run(
                    () => ErpClient.SearchErpItems(query, 200), token);
                if (_isClosed || token.IsCancellationRequested || ChkGlobalSearch.IsChecked != true)
                    return;

                _erpItemsFull = items ?? new List<ErpItem>();
                RebuildErpView(applyLocalFilter: false);
                SetErpStatus("Найдено по всей ERP: " + _erpItemsFull.Count, false);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                if (_isClosed || token.IsCancellationRequested) return;
                _erpItemsFull.Clear();
                RebuildErpView(applyLocalFilter: false);
                SetErpStatus("Ошибка глобального поиска: " + ex.Message, false);
            }
        }

        private void RebuildErpView(bool applyLocalFilter = true)
        {
            string term = applyLocalFilter ? Normalize(ErpSearchBox?.Text) : "";
            List<ErpItem> view = _erpItemsFull
                .Where(item => string.IsNullOrEmpty(term) ||
                               Normalize(item.Code).Contains(term) ||
                               Normalize(item.Name).Contains(term) ||
                               Normalize(item.Unit).Contains(term) ||
                               Normalize(item.Extra).Contains(term))
                .ToList();

            ErpGrid.ItemsSource = view;
            ErpGrid.Items.Refresh();
        }

        private void BtnAssignCode_Click(object sender, RoutedEventArgs e)
        {
            ErpItem erp = ErpGrid.SelectedItem as ErpItem;
            if (erp == null)
            {
                MessageBox.Show("Выберите номенклатуру ERP справа.", "ERP",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            List<ScheduleMirrorRow> selected = RevitGrid.SelectedItems
                .OfType<ScheduleMirrorRow>()
                .ToList();
            if (selected.Count == 0)
            {
                MessageBox.Show("Выберите одну или несколько строк спецификации слева.", "ERP",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            List<ScheduleMirrorRow> unsafeRows = selected
                .Where(x => !x.IsResourceRow || !x.CanWriteErpCode)
                .ToList();
            if (unsafeRows.Count > 0)
            {
                MessageBox.Show(
                    "Среди выбранных есть заголовки/итоги либо строки без однозначной связи с ElementId. " +
                    "Назначение отменено, чтобы не записать код не тем элементам.",
                    "ERP", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            foreach (ScheduleMirrorRow row in selected)
            {
                row.ErpCode = erp.Code;
                row.ErpName1c = erp.Name;
            }
            if (!string.IsNullOrWhiteSpace(erp.Code))
                _erpNameByCode[erp.Code.Trim()] = erp.Name ?? "";
            RebuildRevitView();
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        private void SetErpStatus(string text, bool busy)
        {
            if (ErpStatusText == null) return;
            ErpStatusText.Text = text ?? "";
            BtnAssignCode.IsEnabled = !busy;
        }

        private static string Normalize(string value)
        {
            return (value ?? "").Trim().ToLowerInvariant();
        }
    }
}
