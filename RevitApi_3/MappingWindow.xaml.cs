using RevitApi_3;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
// алиас для красоты
using WpfGrid = System.Windows.Controls.Grid;

namespace RevitApi_3
{
    public partial class MappingWindow : Window
    {
        private readonly List<RevitItem> _revitItemsFull;
        private List<RevitItem> _revitView;

        private readonly List<ErpTreeNode> _erpTreeRoots;
        private List<ErpItem> _erpItemsFull = new List<ErpItem>();
        private List<ErpItem> _erpView = new List<ErpItem>();

        // (опционально) кешируем справочники, чтобы не дергать REST каждый раз
        private List<RefNamedItem> _cachedTypes;
        private List<RefNamedItem> _cachedUnits;

        private readonly string _contextInfo;

        public IList<RevitItem> ResultItems => _revitItemsFull;

        public MappingWindow(List<RevitItem> items,
                             List<ErpTreeNode> treeRoots,
                             string contextInfo)
        {
            InitializeComponent();

            _revitItemsFull = items ?? new List<RevitItem>();
            _erpTreeRoots = treeRoots ?? new List<ErpTreeNode>();
            _contextInfo = contextInfo ?? "";

            WpfGrid grid = RootGrid;

            // левая таблица
            RebuildRevitView();

            // дерево ERP
            ErpTree.ItemsSource = _erpTreeRoots;

            // правая таблица кодов — пока пустая
            RebuildErpView();

            this.Title = "Сопоставление кодов 1C-ERP — " + _contextInfo;
        }

        private static string NormalizeName(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.ToLowerInvariant().Trim();
        }

        // ===== Revit: фильтр "только без кода" =====


        private void BtnCreateNomenclature_Click(object sender, RoutedEventArgs e)
        {
            // Нужно, чтобы пользователь выбрал, кому назначать код
            if (RevitGrid.SelectedItems == null || RevitGrid.SelectedItems.Count == 0)
            {
                MessageBox.Show("Выберите одну или несколько строк слева (Revit), которым нужно назначить созданную номенклатуру.",
                    "ERP", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                if (_cachedTypes == null) _cachedTypes = ErpClient.LoadNomenclatureTypes();
                if (_cachedUnits == null) _cachedUnits = ErpClient.LoadUnits();
            }
            catch
            {
                MessageBox.Show("Сервис недоступен (не удалось загрузить справочники для создания).", "ERP",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // (улучшение UX) если в дереве справа что-то выбрано — передадим как предустановку
            //ErpTreeNode selectedNode = ErpTree.SelectedItem as ErpTreeNode;
            //var first = RevitGrid.SelectedItems.Count > 0 ? RevitGrid.SelectedItems[0] as RevitItem : null;
            //var win = new OutputProductWindow(
            //    _erpTreeRoots,
            //    _cachedTypes,
            //    _cachedUnits,
            //    OutputProductWindowMode.CreateOnly,
            //    selectedNode != null ? selectedNode.RefKey : null,
            //    selectedNode != null ? selectedNode.Description : null,
            //    suggestedName: first?.DisplayName);

            //
            var selectedNode = ErpTree.SelectedItem as ErpTreeNode;
            var first = RevitGrid.SelectedItems.Count > 0 ? RevitGrid.SelectedItems[0] as RevitItem : null;

            var win = new OutputProductWindow(
                _erpTreeRoots,
                _cachedTypes,
                _cachedUnits,
                OutputProductWindowMode.CreateOnly,
                initialName: (first?.DisplayName ?? "").Trim(),
                initialKindRefKey: selectedNode != null ? selectedNode.RefKey : OutputProductState.LastKindRefKey,
                initialKindName: selectedNode != null ? selectedNode.Description : OutputProductState.LastKindName);


            var helper = new System.Windows.Interop.WindowInteropHelper(win);
            helper.Owner = new System.Windows.Interop.WindowInteropHelper(this).Handle;

            bool? dlg = win.ShowDialog();
            if (dlg == true && win.SelectedProduct != null)
            {
                string createdCode = win.SelectedProduct.Code;

                foreach (var obj in RevitGrid.SelectedItems)
                {
                    var ri = obj as RevitItem;
                    if (ri != null)
                        ri.ErpCode = createdCode;
                }

                RebuildRevitView();
            }
        }

        private void RebuildRevitView()
        {
            bool onlyWithout = (ChkOnlyWithoutCode != null && ChkOnlyWithoutCode.IsChecked == true);

            _revitView = new List<RevitItem>();
            foreach (var ri in _revitItemsFull)
            {
                if (onlyWithout)
                {
                    if (!string.IsNullOrEmpty(ri.ErpCode) && ri.ErpCode != "-")
                        continue;
                }
                _revitView.Add(ri);
            }

            RevitGrid.ItemsSource = _revitView;
            RevitGrid.Items.Refresh();
        }

        private void ChkOnlyWithoutCode_Changed(object sender, RoutedEventArgs e)
        {
            RebuildRevitView();
        }

        // ===== ERP: обновление таблицы кодов с учётом поиска =====

        private void RebuildErpView()
        {
            string term = ErpSearchBox != null ? ErpSearchBox.Text : null;
            term = string.IsNullOrWhiteSpace(term) ? "" : term.Trim().ToLowerInvariant();

            _erpView = new List<ErpItem>();
            foreach (var e in _erpItemsFull)
            {
                if (!string.IsNullOrEmpty(term))
                {
                    string code = e.Code != null ? e.Code.ToLowerInvariant() : "";
                    string name = e.Name != null ? e.Name.ToLowerInvariant() : "";
                    string extra = e.Extra != null ? e.Extra.ToLowerInvariant() : "";
                    string unit = e.Unit != null ? e.Unit.ToLowerInvariant() : "";

                    if (!code.Contains(term) && !name.Contains(term) && !extra.Contains(term) && !unit.Contains(term))
                        continue;
                }

                _erpView.Add(e);
            }

            ErpGrid.ItemsSource = _erpView;
            ErpGrid.Items.Refresh();
        }

        private void ErpSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            RebuildErpView();
        }

        // ===== клик по дереву: грузим коды по RefKey =====

        private void ErpTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            var node = ErpTree.SelectedItem as ErpTreeNode;
            if (node == null) return;

            try
            {
                _erpItemsFull = ErpClient.LoadErpItems(node.RefKey);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Ошибка при загрузке кодов для выбранного узла: " + ex.Message,
                    "ERP", MessageBoxButton.OK, MessageBoxImage.Error);
                _erpItemsFull = new List<ErpItem>();
            }

            RebuildErpView();
        }

        // ===== Автоподбор по имени =====

        private void BtnAutoMap_Click(object sender, RoutedEventArgs e)
        {
            if (_erpItemsFull == null || _erpItemsFull.Count == 0)
            {
                MessageBox.Show("Сначала выберите узел в дереве справа и дождитесь загрузки кодов.", "ERP");
                return;
            }

            var dict = new Dictionary<string, ErpItem>();
            foreach (var erp in _erpItemsFull)
            {
                string key = NormalizeName(erp.Name);
                if (!dict.ContainsKey(key))
                    dict[key] = erp;
            }

            int count = 0;
            foreach (var ri in _revitItemsFull)
            {
                if (!string.IsNullOrEmpty(ri.ErpCode)) continue;
                string key = NormalizeName(ri.DisplayName);
                if (dict.TryGetValue(key, out var found))
                {
                    ri.ErpCode = found.Code;
                    count++;
                }
            }

            RebuildRevitView();
            MessageBox.Show("Автоматически сопоставлено: " + count, "ERP");
        }


        // ===== Стрелка: применить выбранный код к выделенным строкам =====

        private void BtnAssignCode_Click(object sender, RoutedEventArgs e)
        {
            var erp = ErpGrid.SelectedItem as ErpItem;
            if (erp == null)
            {
                MessageBox.Show("Выберите код в таблице 1C-ERP (справа внизу).", "ERP");
                return;
            }

            var selected = new List<RevitItem>();
            foreach (var obj in RevitGrid.SelectedItems)
            {
                if (obj is RevitItem ri)
                    selected.Add(ri);
            }

            if (selected.Count == 0)
            {
                MessageBox.Show("Выберите одну или несколько строк слева (Revit), которые нужно связать с кодом 1C.", "ERP");
                return;
            }

            foreach (var ri in selected)
                ri.ErpCode = erp.Code;

            RebuildRevitView();
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = true;
            this.Close();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }
    }
}
