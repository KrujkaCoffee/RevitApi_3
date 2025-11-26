using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
// алиасы:
using WpfGrid = System.Windows.Controls.Grid;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace RevitApi_3
{
    public partial class MappingWindow : Window
    {
        private readonly List<RevitItem> _revitItems;
        private List<ErpItem> _erpItems;
        private readonly string _contextInfo;

        // Результаты для команды (после закрытия окна)
        public IList<RevitItem> ResultItems
        {
            get { return _revitItems; }
        }

        public string Endpoint
        {
            get { return EndpointBox.Text != null ? EndpointBox.Text.Trim() : string.Empty; }
        }

        public string DocTitle
        {
            get { return TitleBox.Text != null ? TitleBox.Text.Trim() : string.Empty; }
        }

        public MappingWindow(List<RevitItem> items,
                             string contextInfo,
                             string initialEndpoint,
                             string initialTitle)
        {
            InitializeComponent();

            _revitItems = items ?? new List<RevitItem>();
            _erpItems = new List<ErpItem>();
            _contextInfo = contextInfo ?? "";

            // Привязка таблиц
            RevitGrid.ItemsSource = _revitItems;
            ExportGrid.ItemsSource = _revitItems;

            // алиасы просто для явности типов
            WpfGrid grid = RootGrid;
            WpfTextBox endpointBox = EndpointBox;
            WpfTextBox titleBox = TitleBox;

            EndpointBox.Text = initialEndpoint ?? string.Empty;
            TitleBox.Text = initialTitle ?? string.Empty;

            this.Title = "Сопоставление кодов 1C-ERP — " + _contextInfo;
        }

        // ===== Загрузка списка из 1С =====

        private void BtnLoadErp_Click(object sender, RoutedEventArgs e)
        {
            string url = EndpointBox.Text.Trim();
            if (string.IsNullOrEmpty(url))
            {
                MessageBox.Show("Введите URL сервиса 1C-ERP.", "ERP", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                _erpItems = ErpClient.LoadErpItems(url);
                ErpGrid.ItemsSource = _erpItems;
                ErpGrid.Items.Refresh();

                MessageBox.Show("Загружено позиций: " + _erpItems.Count, "ERP");
            }
            catch (Exception ex)
            {
                MessageBox.Show("Ошибка запроса к 1C-ERP: " + ex.Message, "ERP", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ===== Авто-сопоставление по имени =====

        private void BtnAutoMap_Click(object sender, RoutedEventArgs e)
        {
            if (_erpItems == null || _erpItems.Count == 0)
            {
                MessageBox.Show("Сначала загрузите список из 1C-ERP.", "ERP");
                return;
            }

            Dictionary<string, ErpItem> dict = new Dictionary<string, ErpItem>();
            foreach (ErpItem erp in _erpItems)
            {
                string key = NormalizeName(erp.Name);
                if (!dict.ContainsKey(key))
                    dict[key] = erp;
            }

            int count = 0;
            foreach (RevitItem ri in _revitItems)
            {
                if (!string.IsNullOrEmpty(ri.ErpCode)) continue;
                string key = NormalizeName(ri.DisplayName);
                ErpItem found;
                if (dict.TryGetValue(key, out found))
                {
                    ri.ErpCode = found.Code;
                    count++;
                }
            }

            RevitGrid.Items.Refresh();
            ExportGrid.Items.Refresh();
            MessageBox.Show("Автоматически сопоставлено: " + count, "ERP");
        }

        private static string NormalizeName(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.ToLowerInvariant().Trim();
            // тут можно добавить доп. нормализацию: убрать скобки, ГОСТ, и т.п.
            return s;
        }

        // ===== Кнопка со стрелкой: применить выбранный код к выделенным Revit-строкам =====

        private void BtnAssignCode_Click(object sender, RoutedEventArgs e)
        {
            ErpItem erp = ErpGrid.SelectedItem as ErpItem;
            if (erp == null)
            {
                MessageBox.Show("Выберите строку в списке 1C-ERP справа.", "ERP");
                return;
            }

            List<RevitItem> selected = new List<RevitItem>();
            foreach (object obj in RevitGrid.SelectedItems)
            {
                RevitItem ri = obj as RevitItem;
                if (ri != null) selected.Add(ri);
            }

            if (selected.Count == 0)
            {
                MessageBox.Show("Выберите одну или несколько строк слева (Revit), которые нужно связать с выбранным кодом 1C.", "ERP");
                return;
            }

            foreach (RevitItem ri in selected)
                ri.ErpCode = erp.Code;

            RevitGrid.Items.Refresh();
            ExportGrid.Items.Refresh();
        }

        // ===== Кнопка "Записать в модель" — просто закрываем с DialogResult = true =====

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            // Можно добавить минимальную валидацию, если нужно
            this.DialogResult = true;
            this.Close();
        }

        // ===== Кнопка "Выгрузить ресурсную в ERP" =====

        private void BtnExport_Click(object sender, RoutedEventArgs e)
        {
            string url = Endpoint;
            string title = DocTitle;

            if (string.IsNullOrEmpty(url))
            {
                MessageBox.Show("Для выгрузки укажите ERP Endpoint.", "ERP", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrEmpty(title))
            {
                MessageBox.Show("Для выгрузки необходимо заполнить Title.", "ERP", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Проверяем, что у всех строк есть корректный код ERP
            var badRows = _revitItems
                .Where(ri => string.IsNullOrEmpty(ri.ErpCode) || ri.ErpCode == "-")
                .ToList();

            if (badRows.Count > 0)
            {
                ExportGrid.Items.Refresh(); // строки уже подсвечены стилем
                MessageBox.Show(
                    "Выгрузка невозможна: есть строки без кода 1C-ERP или с кодом '-'.\n" +
                    "Такие строки подсвечены красным в таблице предпросмотра.",
                    "ERP",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }

            try
            {
                string response = ErpClient.ExportResources(url, title, _contextInfo, _revitItems);
                MessageBox.Show("Выгрузка выполнена.\nОтвет сервера:\n" + response, "ERP");
            }
            catch (Exception ex)
            {
                MessageBox.Show("Ошибка при выгрузке ресурсной: " + ex.Message, "ERP", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }
    }
}
