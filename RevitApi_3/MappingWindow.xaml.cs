using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Autodesk.Revit.DB;
using Newtonsoft.Json;

namespace RevitErpIntegration
{
    public partial class MappingWindow : Window
    {
        private readonly Document _doc;
        private readonly List<RevitItem> _revitItems;
        private List<ErpItem> _erpItems;

        public MappingWindow(Document doc, List<RevitItem> items)
        {
            InitializeComponent();

            _doc = doc;
            _revitItems = items;
            _erpItems = new List<ErpItem>();

            RevitGrid.ItemsSource = _revitItems;

            // попытаемся прочитать endpoint и title из ProjectInfo
            ProjectInfo pi = _doc.ProjectInformation;
            Parameter pUrl = pi.LookupParameter(ErpParameters.EndpointParamName);
            Parameter pTitle = pi.LookupParameter(ErpParameters.DocTitleParamName);
            if (pUrl != null) EndpointBox.Text = pUrl.AsString();
            if (pTitle != null) TitleBox.Text = pTitle.AsString();
        }

        private void BtnLoadErp_Click(object sender, RoutedEventArgs e)
        {
            string url = EndpointBox.Text.Trim();
            if (string.IsNullOrEmpty(url))
            {
                MessageBox.Show("Введите URL сервиса 1C-ERP в поле ERP Endpoint.", "ERP", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                // Простой POST без специфического payload — заменишь на свой формат.
                var requestObj = new { action = "get_nomenclature_list" };
                string json = JsonConvert.SerializeObject(requestObj);

                using (WebClient wc = new WebClient())
                {
                    wc.Encoding = Encoding.UTF8;
                    wc.Headers[HttpRequestHeader.ContentType] = "application/json; charset=utf-8";
                    string response = wc.UploadString(url, "POST", json);
                    // ожидаем массив объектов { "Code": "...", "Name": "...", "Extra": "..." }
                    _erpItems = JsonConvert.DeserializeObject<List<ErpItem>>(response);
                    if (_erpItems == null) _erpItems = new List<ErpItem>();
                }

                ErpGrid.ItemsSource = _erpItems;
                ErpGrid.Items.Refresh();
                MessageBox.Show("Список из 1C-ERP загружен: " + _erpItems.Count + " записей.", "ERP");
            }
            catch (Exception ex)
            {
                MessageBox.Show("Ошибка при запросе к 1C-ERP: " + ex.Message, "ERP", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

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
            MessageBox.Show("Автоматически сопоставлено " + count + " позиций.", "ERP");
        }

        private string NormalizeName(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.ToLowerInvariant().Trim();
            // по желанию: убрать лишние пробелы/символы
            return s;
        }

        private void ErpGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            ErpItem erp = ErpGrid.SelectedItem as ErpItem;
            if (erp == null) return;

            var selected = RevitGrid.SelectedItems.Cast<RevitItem>().ToList();
            if (selected.Count == 0)
            {
                MessageBox.Show("Выберите одну или несколько строк Revit слева, затем двойной клик по строке 1C справа.", "ERP");
                return;
            }

            foreach (RevitItem ri in selected)
                ri.ErpCode = erp.Code;

            RevitGrid.Items.Refresh();
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            int count = 0;
            using (Transaction t = new Transaction(_doc, "Set ERP codes"))
            {
                t.Start();

                // по типам
                var byType = _revitItems
                    .Where(r => !string.IsNullOrEmpty(r.ErpCode))
                    .GroupBy(r => r.TypeId.IntegerValue);

                foreach (var g in byType)
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
                        // fallback — в экземпляры
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

                // заодно сохраним Endpoint и Title обратно в проект
                ProjectInfo piProject = _doc.ProjectInformation;
                Parameter pUrl = piProject.LookupParameter(ErpParameters.EndpointParamName);
                if (pUrl != null && !pUrl.IsReadOnly && pUrl.StorageType == StorageType.String)
                    pUrl.Set(EndpointBox.Text);

                Parameter pTitle = piProject.LookupParameter(ErpParameters.DocTitleParamName);
                if (pTitle != null && !pTitle.IsReadOnly && pTitle.StorageType == StorageType.String)
                    pTitle.Set(TitleBox.Text);

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
