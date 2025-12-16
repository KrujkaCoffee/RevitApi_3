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

        private string _lastKindRefKey;
        private string _lastKindName;

        private ErpItem _outputProduct;

        public string DocTitle => TitleBox.Text != null ? TitleBox.Text.Trim() : string.Empty;

        public ExportWindow(
            //ScheduleExportTable table,
            //ExportFormRefs refs,

            List<ExportRow> previewRows,
            string contextInfo,
            string initialTitle,
            List<ErpTreeNode> treeRoots,
            List<RefNamedItem> types,
            List<RefNamedItem> units)
        {
            InitializeComponent();

            _contextInfo = contextInfo ?? "";
            _treeRoots = treeRoots ?? new List<ErpTreeNode>();
            _types = types ?? new List<RefNamedItem>();
            _units = units ?? new List<RefNamedItem>();

            // 1:1 как в Revit — никаких группировок/подсчётов тут
            _exportRows = previewRows ?? new List<ExportRow>();
            ExportGrid.ItemsSource = _exportRows;

            TitleBox.Text = initialTitle ?? string.Empty;
            ContextLabel.Text = _contextInfo;
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
                    QuantityText = qty.ToString(),
                    MassPerItemText = massPerItem.ToString(),
                    TotalMassText = totalMass.ToString()
                });
            }

            return result;
        }

        private void BtnPickOutput_Click(object sender, RoutedEventArgs e)
        {

            var win = new OutputProductWindow(
                _treeRoots,
                _types,
                _units,
                OutputProductWindowMode.PickOrCreate,
                initialName: (TitleBox.Text ?? "").Trim(),
                initialKindRefKey: OutputProductState.LastKindRefKey,
                initialKindName: OutputProductState.LastKindName);


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
