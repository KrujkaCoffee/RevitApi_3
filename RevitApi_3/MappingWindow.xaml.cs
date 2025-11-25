using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Autodesk.Revit.DB;
using Newtonsoft.Json;

using WpfGrid = System.Windows.Controls.Grid;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace RevitApi_3
{
    public partial class MappingWindow : Window
    {
        private readonly Document _doc;
        private readonly List<RevitItem> _revitItems;
        private List<ErpItem> _erpItems;
        private readonly string _contextInfo;

        public MappingWindow(Document doc, List<RevitItem> items, string contextInfo)
        {
            InitializeComponent();

            _doc = doc;
            _revitItems = items ?? new List<RevitItem>();
            _erpItems = new List<ErpItem>();
            _contextInfo = contextInfo;

            RevitGrid.ItemsSource = _revitItems;

            // пример использования алиасов (для явности типов)
            WpfGrid grid = RootGrid;
            WpfTextBox endpointBox = EndpointBox;
            WpfTextBox titleBox = TitleBox;
            // (переменные не обязательны для логики, но показывают явные типы)

            LoadProjectParams();
            this.Title = "Сопоставление кодов 1C-ERP — " + _contextInfo;
        }

        private void LoadProjectParams()
        {
            try
            {
                ProjectInfo pi = _doc.ProjectInformation;
                Parameter pUrl = pi.LookupParameter(ErpParameters.EndpointParamName);
                Parameter pTitle = pi.LookupParameter(ErpParameters.DocTitleParamName);

                if (pUrl != null && pUrl.StorageType == StorageType.String)
                    EndpointBox.Text = pUrl.AsString();
                if (pTitle != null && pTitle.StorageType == StorageType.String)
                    TitleBox.Text = pTitle.AsString();
            }
            catch
            {
                // если параметров нет - просто игнорируем
            }
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
            MessageBox.Show("Автоматически сопоставлено: " + count, "ERP");
        }

        private static string NormalizeName(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.ToLowerInvariant().Trim();
            // здесь можно добавить доп. нормализацию: убрать скобки, ГОСТ, и т.д.
            return s;
        }

        // ===== Двойной клик по 1С-строке =====

        private void ErpGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            ErpItem erp = ErpGrid.SelectedItem as ErpItem;
            if (erp == null) return;

            List<RevitItem> selected = new List<RevitItem>();
            foreach (object obj in RevitGrid.SelectedItems)
            {
                RevitItem ri = obj as RevitItem;
                if (ri != null) selected.Add(ri);
            }

            if (selected.Count == 0)
            {
                MessageBox.Show("Выберите одну или несколько строк слева (Revit), затем двойной клик по строке 1C справа.", "ERP");
                return;
            }

            foreach (RevitItem ri in selected)
                ri.ErpCode = erp.Code;

            RevitGrid.Items.Refresh();
        }

        // ===== Запись кодов в модель =====

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            int count = 0;

            using (Transaction t = new Transaction(_doc, "Set ERP codes"))
            {
                t.Start();

                // по типам (наследование на все экземпляры)
                var groups = _revitItems
                    .Where(r => !string.IsNullOrEmpty(r.ErpCode))
                    .GroupBy(r => r.TypeId.IntegerValue);

                foreach (var g in groups)
                {
                    ElementId typeId = new ElementId(g.Key);
                    Element type = _doc.GetElement(typeId);
                    if (type == null) continue;

                    Parameter p = type.LookupParameter(ErpParameters.ErpCodeParamName);
                    if (p != null && !p.IsReadOnly && p.StorageType == StorageType.String)
                    {
                        p.Set(g.First().ErpCode);
                        count++;
                    }
                    else
                    {
                        // fallback: пишем в экземпляры
                        foreach (RevitItem ri in g)
                        {
                            Element inst = _doc.GetElement(ri.ElementId);
                            if (inst == null) continue;

                            Parameter pi = inst.LookupParameter(ErpParameters.ErpCodeParamName);
                            if (pi != null && !pi.IsReadOnly && pi.StorageType == StorageType.String)
                            {
                                pi.Set(ri.ErpCode);
                                count++;
                            }
                        }
                    }
                }

                // заодно сохраним endpoint/title в ProjectInfo, если параметры созданы
                try
                {
                    ProjectInfo piProj = _doc.ProjectInformation;
                    Parameter pUrl = piProj.LookupParameter(ErpParameters.EndpointParamName);
                    if (pUrl != null && !pUrl.IsReadOnly && pUrl.StorageType == StorageType.String)
                        pUrl.Set(EndpointBox.Text);

                    Parameter pTitle = piProj.LookupParameter(ErpParameters.DocTitleParamName);
                    if (pTitle != null && !pTitle.IsReadOnly && pTitle.StorageType == StorageType.String)
                        pTitle.Set(TitleBox.Text);
                }
                catch
                {
                    // если таких параметров нет — тихо игнорируем
                }

                t.Commit();
            }

            MessageBox.Show("Записано кодов: " + count, "ERP");
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
