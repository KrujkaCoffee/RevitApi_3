using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;

// алиасы
using WpfGrid = System.Windows.Controls.Grid;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace RevitApi_3
{
    public partial class ExportWindow : Window
    {
        private readonly List<RevitItem> _sourceItems;
        private readonly List<ExportRow> _exportRows;
        private readonly string _contextInfo;

        private readonly List<ErpTreeNode> _treeRoots;
        private readonly List<RefNamedItem> _types;
        private readonly List<RefNamedItem> _units;

        private ErpItem _outputProduct;

        public string DocTitle => TitleBox.Text != null ? TitleBox.Text.Trim() : string.Empty;

        public ExportWindow(
            List<RevitItem> items,
            string contextInfo,
            string initialTitle,
            List<ErpTreeNode> treeRoots,
            List<RefNamedItem> types,
            List<RefNamedItem> units)
        {
            InitializeComponent();

            _sourceItems = items ?? new List<RevitItem>();
            _contextInfo = contextInfo ?? "";

            _treeRoots = treeRoots ?? new List<ErpTreeNode>();
            _types = types ?? new List<RefNamedItem>();
            _units = units ?? new List<RefNamedItem>();

            _exportRows = BuildExportRows(_sourceItems);
            ExportGrid.ItemsSource = _exportRows;

            WpfGrid grid = RootGrid;
            WpfTextBox titleBox = TitleBox;

            TitleBox.Text = initialTitle ?? string.Empty;
            ContextLabel.Text = _contextInfo;

            this.Title = "Выгрузка ресурсной в ERP — " + _contextInfo;
        }

        private List<ExportRow> BuildExportRows(List<RevitItem> items)
        {
            var result = new List<ExportRow>();

            var groups = items.GroupBy(i => new
            {
                i.ScheduleName,
                i.FamilyName,
                i.TypeName,
                i.DisplayName,
                i.ErpCode,
                i.Unit,
                i.MassPerItem
            });

            foreach (var g in groups)
            {
                int qty = g.Count();
                double? massPerItem = g.Key.MassPerItem;
                double? totalMass = null;
                if (massPerItem.HasValue)
                    totalMass = massPerItem.Value * qty;

                result.Add(new ExportRow
                {
                    ScheduleName = g.Key.ScheduleName,
                    FamilyName = g.Key.FamilyName,
                    TypeName = g.Key.TypeName,
                    DisplayName = g.Key.DisplayName,
                    ErpCode = g.Key.ErpCode,
                    Unit = g.Key.Unit,
                    Quantity = qty,
                    MassPerItem = massPerItem,
                    TotalMass = totalMass
                });
            }

            return result;
        }

        private void BtnPickOutput_Click(object sender, RoutedEventArgs e)
        {
            // Режим для экспорта: полноценный подбор + создание
            var win = new OutputProductWindow(
                _treeRoots,
                _types,
                _units,
                OutputProductWindowMode.PickOrCreate);

            // Важно: owner должен быть текущим окном, а не new IntPtr()
            var helper = new WindowInteropHelper(win);
            helper.Owner = new WindowInteropHelper(this).Handle;

            bool? dlg = win.ShowDialog();
            if (dlg == true && win.SelectedProduct != null)
            {
                _outputProduct = win.SelectedProduct;
                OutputNameText.Text = _outputProduct.Name;
                OutputCodeText.Text = _outputProduct.Code;
            }
        }

        private void BtnExport_Click(object sender, RoutedEventArgs e)
        {
            string title = DocTitle;
            if (string.IsNullOrEmpty(title))
            {
                MessageBox.Show("Необходимо заполнить Title.", "ERP", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (_outputProduct == null)
            {
                MessageBox.Show("Не выбрано выходное изделие. Используйте кнопку 'Подобрать / создать'.",
                    "ERP", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var badRows = _exportRows
                .Where(r => string.IsNullOrEmpty(r.ErpCode) || r.ErpCode == "-")
                .ToList();

            if (badRows.Count > 0)
            {
                ExportGrid.Items.Refresh();
                MessageBox.Show(
                    "Выгрузка невозможна: есть строки без кода 1C-ERP или с кодом '-'.\n" +
                    "Такие строки подсвечены красным.",
                    "ERP", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            try
            {
                string response = ErpClient.ExportResources(title, _contextInfo, _exportRows, _outputProduct);
                MessageBox.Show("Выгрузка выполнена.\nОтвет сервера:\n" + response, "ERP");
                this.DialogResult = true;
                this.Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Ошибка при выгрузке ресурсной: " + ex.Message,
                    "ERP", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }
    }
}
