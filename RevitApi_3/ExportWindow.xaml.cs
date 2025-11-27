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
    public partial class ExportWindow : Window
    {
        private readonly List<RevitItem> _revitItems;
        private readonly string _contextInfo;

        public string Endpoint
        {
            get { return EndpointBox.Text != null ? EndpointBox.Text.Trim() : string.Empty; }
        }

        public string DocTitle
        {
            get { return TitleBox.Text != null ? TitleBox.Text.Trim() : string.Empty; }
        }

        public ExportWindow(List<RevitItem> items,
                            string contextInfo,
                            string initialEndpoint,
                            string initialTitle)
        {
            InitializeComponent();

            _revitItems = items ?? new List<RevitItem>();
            _contextInfo = contextInfo ?? "";

            ExportGrid.ItemsSource = _revitItems;

            WpfGrid grid = RootGrid;
            WpfTextBox endpointBox = EndpointBox;
            WpfTextBox titleBox = TitleBox;

            EndpointBox.Text = initialEndpoint ?? string.Empty;
            TitleBox.Text = initialTitle ?? string.Empty;

            this.Title = "Выгрузка ресурсной в ERP — " + _contextInfo;
        }

        private void BtnExport_Click(object sender, RoutedEventArgs e)
        {
            string url = Endpoint;
            string title = DocTitle;

            if (string.IsNullOrEmpty(url))
            {
                MessageBox.Show("Укажите ERP Endpoint.", "ERP", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrEmpty(title))
            {
                MessageBox.Show("Необходимо заполнить Title.", "ERP", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Валидация: все строки должны иметь код
            var badRows = _revitItems
                .Where(ri => string.IsNullOrEmpty(ri.ErpCode) || ri.ErpCode == "-")
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
                string response = ErpClient.ExportResources(url, title, _contextInfo, _revitItems);
                MessageBox.Show("Выгрузка выполнена.\nОтвет сервера:\n" + response, "ERP");
            }
            catch (Exception ex)
            {
                MessageBox.Show("Ошибка при выгрузке ресурсной: " + ex.Message, "ERP",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = true;
            this.Close();
        }
    }
}
