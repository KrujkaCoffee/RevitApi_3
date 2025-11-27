using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
// алиасы
using WpfGrid = System.Windows.Controls.Grid;

namespace RevitApi_3
{
    public partial class MappingWindow : Window
    {
        private readonly List<RevitItem> _revitItems;
        private readonly List<ErpItem> _erpItems;
        private readonly string _contextInfo;

        public IList<RevitItem> ResultItems => _revitItems;

        public MappingWindow(List<RevitItem> items,
                             List<ErpItem> erpItems,
                             string contextInfo)
        {
            InitializeComponent();

            _revitItems = items ?? new List<RevitItem>();
            _erpItems = erpItems ?? new List<ErpItem>();
            _contextInfo = contextInfo ?? "";

            RevitGrid.ItemsSource = _revitItems;
            ErpGrid.ItemsSource = _erpItems;

            WpfGrid grid = RootGrid; // просто для явности типов

            this.Title = "Сопоставление кодов 1C-ERP — " + _contextInfo;
        }

        private static string NormalizeName(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.ToLowerInvariant().Trim();
        }

        private void BtnAutoMap_Click(object sender, RoutedEventArgs e)
        {
            if (_erpItems.Count == 0)
            {
                MessageBox.Show("Список номенклатур 1C-ERP пуст. Проверьте работу сервиса.", "ERP");
                return;
            }

            var dict = new Dictionary<string, ErpItem>();
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
                if (dict.TryGetValue(key, out var found))
                {
                    ri.ErpCode = found.Code;
                    count++;
                }
            }

            RevitGrid.Items.Refresh();
            MessageBox.Show("Автоматически сопоставлено: " + count, "ERP");
        }

        private void BtnAssignCode_Click(object sender, RoutedEventArgs e)
        {
            var erp = ErpGrid.SelectedItem as ErpItem;
            if (erp == null)
            {
                MessageBox.Show("Выберите строку в списке 1C-ERP справа.", "ERP");
                return;
            }

            var selected = new List<RevitItem>();
            foreach (var obj in RevitGrid.SelectedItems)
                if (obj is RevitItem ri) selected.Add(ri);

            if (selected.Count == 0)
            {
                MessageBox.Show("Выберите одну или несколько строк слева (Revit), которые нужно связать с выбранным кодом 1C.", "ERP");
                return;
            }

            foreach (var ri in selected)
                ri.ErpCode = erp.Code;

            RevitGrid.Items.Refresh();
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
