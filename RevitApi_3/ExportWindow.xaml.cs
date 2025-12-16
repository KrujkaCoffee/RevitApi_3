using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Interop;

// алиасы
using WpfGrid = System.Windows.Controls.Grid;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace RevitApi_3
{
    public partial class ExportWindow : Window
    {
        private readonly ScheduleExportTable _table;
        private readonly string _contextInfo;

        private readonly List<ErpTreeNode> _treeRoots;
        private readonly List<RefNamedItem> _types;
        private readonly List<RefNamedItem> _units;

        private ErpItem _outputProduct;

        public string DocTitle => (TitleBox.Text ?? "").Trim();

        public ExportWindow(
            ScheduleExportTable table,
            string contextInfo,
            string initialTitle,
            List<ErpTreeNode> treeRoots,
            List<RefNamedItem> types,
            List<RefNamedItem> units)
        {
            InitializeComponent();

            _table = table ?? new ScheduleExportTable();
            _contextInfo = contextInfo ?? "";

            _treeRoots = treeRoots ?? new List<ErpTreeNode>();
            _types = types ?? new List<RefNamedItem>();
            _units = units ?? new List<RefNamedItem>();

            TitleBox.Text = initialTitle ?? string.Empty;
            ContextLabel.Text = _contextInfo;

            this.Title = "Выгрузка ресурсной в ERP — " + _contextInfo;

            ExportGrid.ItemsSource = _table.Rows;
            BuildColumnsFromSchedule();

            // просто чтобы алиасы реально были использованы (как ты просил)
            WpfGrid grid = RootGrid;
            WpfTextBox titleBox = TitleBox;
        }

        private void BuildColumnsFromSchedule()
        {
            ExportGrid.Columns.Clear();

            for (int i = 0; i < _table.UiColumns.Count; i++)
            {
                var c = _table.UiColumns[i];

                ExportGrid.Columns.Add(new DataGridTextColumn
                {
                    Header = c.Header,
                    Binding = new Binding($"Values[{i}]"),
                    IsReadOnly = true
                });
            }
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

            var badRows = _table.Rows
                .Where(r => string.IsNullOrWhiteSpace(r.ErpCode) || r.ErpCode == "-")
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
                string response = ErpClient.ExportResources(title, _contextInfo, _table, _outputProduct);
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
