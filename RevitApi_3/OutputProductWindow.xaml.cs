using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace RevitApi_3
{
    public partial class OutputProductWindow : Window
    {
        private readonly List<ErpTreeNode> _roots;
        private readonly List<RefNamedItem> _types;
        private readonly List<RefNamedItem> _units;

        private List<ErpItem> _codesFull = new List<ErpItem>();
        private List<ErpItem> _codesView = new List<ErpItem>();

        private string _selectedKindRefKey;
        private string _selectedKindName;

        public ErpItem SelectedProduct { get; private set; }

        public OutputProductWindow(
            List<ErpTreeNode> roots,
            List<RefNamedItem> types,
            List<RefNamedItem> units)
        {
            InitializeComponent();

            _roots = roots ?? new List<ErpTreeNode>();
            _types = types ?? new List<RefNamedItem>();
            _units = units ?? new List<RefNamedItem>();

            Tree.ItemsSource = _roots;

            TypeCombo.ItemsSource = _types;
            UnitCombo.ItemsSource = _units;
        }

        private void Tree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            var node = Tree.SelectedItem as ErpTreeNode;
            if (node == null)
            {
                _selectedKindRefKey = null;
                _selectedKindName = null;
                KindLabel.Text = "";
                _codesFull.Clear();
                RebuildCodesView();
                return;
            }

            _selectedKindRefKey = node.RefKey;
            _selectedKindName = node.Description;
            KindLabel.Text = _selectedKindName ?? "";

            // Для вкладки подбор — загружаем коды
            try
            {
                _codesFull = ErpClient.LoadErpItems(node.RefKey);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Ошибка при загрузке номенклатуры: " + ex.Message,
                    "ERP", MessageBoxButton.OK, MessageBoxImage.Error);
                _codesFull = new List<ErpItem>();
            }
            RebuildCodesView();
        }

        private void RebuildCodesView()
        {
            string term = SearchBox != null ? SearchBox.Text : null;
            term = string.IsNullOrWhiteSpace(term) ? "" : term.Trim().ToLowerInvariant();

            _codesView = new List<ErpItem>();
            foreach (var e in _codesFull)
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
                _codesView.Add(e);
            }

            CodesGrid.ItemsSource = _codesView;
            CodesGrid.Items.Refresh();
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            RebuildCodesView();
        }

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            // режим "подбор": берём выделенную строку справа
            var erp = CodesGrid.SelectedItem as ErpItem;
            if (erp == null)
            {
                MessageBox.Show("Выберите существующую номенклатуру в таблице подбора или используйте кнопку 'Создать'.",
                    "ERP", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            SelectedProduct = erp;
            SelectedProductLabel.Text = erp.Name + " (" + erp.Code + ")";
            this.DialogResult = true;
            this.Close();
        }

        private void BtnCreate_Click(object sender, RoutedEventArgs e)
        {
            // режим создания на вкладке "Создание"
            Tabs.SelectedIndex = 1;

            ClearErrors();

            if (string.IsNullOrEmpty(_selectedKindRefKey))
            {
                MessageBox.Show("Выберите вид номенклатуры в дереве слева.", "ERP",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var selectedType = TypeCombo.SelectedItem as RefNamedItem;
            var selectedUnit = UnitCombo.SelectedItem as RefNamedItem;
            string name = NameBox.Text != null ? NameBox.Text.Trim() : "";
            string article = ArticleBox.Text != null ? ArticleBox.Text.Trim() : "";

            try
            {
                var created = ErpClient.CreateNomenclature(
                    _selectedKindRefKey,
                    selectedType != null ? selectedType.RefKey : null,
                    selectedUnit != null ? selectedUnit.RefKey : null,
                    name,
                    article);

                SelectedProduct = created;
                SelectedProductLabel.Text = created.Name + " (" + created.Code + ")";
                this.DialogResult = true;
                this.Close();
            }
            catch (ErpValidationException vex)
            {
                ApplyValidationErrors(vex.Errors);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Ошибка при создании номенклатуры: " + ex.Message,
                    "ERP", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }

        private void ClearErrors()
        {
            NameError.Text = "";
            ArticleError.Text = "";
            TypeError.Text = "";
            UnitError.Text = "";

            NameBox.ClearValue(TextBox.BackgroundProperty);
            ArticleBox.ClearValue(TextBox.BackgroundProperty);
            TypeCombo.ClearValue(ComboBox.BackgroundProperty);
            UnitCombo.ClearValue(ComboBox.BackgroundProperty);
        }

        private void ApplyValidationErrors(Dictionary<string, List<string>> errors)
        {
            ClearErrors();

            foreach (var kv in errors)
            {
                string key = kv.Key != null ? kv.Key.ToLowerInvariant() : "";
                string msg = string.Join("; ", kv.Value ?? new List<string>());

                if (key.Contains("name"))
                {
                    NameError.Text = msg;
                    NameBox.Background = System.Windows.Media.Brushes.MistyRose;
                }
                else if (key.Contains("art"))
                {
                    ArticleError.Text = msg;
                    ArticleBox.Background = System.Windows.Media.Brushes.MistyRose;
                }
                else if (key.Contains("type"))
                {
                    TypeError.Text = msg;
                    TypeCombo.Background = System.Windows.Media.Brushes.MistyRose;
                }
                else if (key.Contains("unit"))
                {
                    UnitError.Text = msg;
                    UnitCombo.Background = System.Windows.Media.Brushes.MistyRose;
                }
                else if (key.Contains("kind"))
                {
                    // общая ошибка по виду номенклатуры
                    NameError.Text += (string.IsNullOrEmpty(NameError.Text) ? "" : " ") + msg;
                }
                else
                {
                    // неизвестное поле — покажем в общем виде
                    NameError.Text += (string.IsNullOrEmpty(NameError.Text) ? "" : " ") + msg;
                }
            }
        }
    }
}
