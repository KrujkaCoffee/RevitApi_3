using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
namespace RevitApi_3
{
    public partial class OutputProductWindow : Window
    {
        private readonly OutputProductWindowMode _mode;
        private readonly string _initialName;

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
            List<RefNamedItem> units,
            OutputProductWindowMode mode,
            string initialKindRefKey = null,
            string initialKindName = null,
            string suggestedName = null)
        {
            InitializeComponent();

            _roots = roots ?? new List<ErpTreeNode>();
            _types = types ?? new List<RefNamedItem>();
            _units = units ?? new List<RefNamedItem>();
            _mode = mode;

            Tree.ItemsSource = _roots;

            TypeCombo.ItemsSource = _types;
            TypeCombo.DisplayMemberPath = "Name";
            TypeCombo.SelectedValuePath = "RefKey";

            UnitCombo.ItemsSource = _units;
            UnitCombo.DisplayMemberPath = "Name";
            UnitCombo.SelectedValuePath = "RefKey";

            Loaded += OutputProductWindow_Loaded;
            // предустановка вида номенклатуры (если хотим)

            //_selectedKindRefKey = initialKindRefKey;
            //_selectedKindName = initialKindName;
            //KindLabel.Text = _selectedKindName ?? "";

            //if (!string.IsNullOrWhiteSpace(suggestedName)) { 
            //    NameBox.Text = suggestedName;
            //}

            //ApplyMode();
        }


        private void OutputProductWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // заполним Наименование сразу
            NameBox.Text = _initialName;

            if (_mode == OutputProductWindowMode.CreateOnly)
            {
                Title = "Создание номенклатуры";

                // скрываем “Подбор” и “OK”
                TabPick.Visibility = Visibility.Collapsed;
                BtnOk.Visibility = Visibility.Collapsed;

                // переходим сразу на вкладку создания
                Tabs.SelectedItem = TabCreate;

                // “создание из-под подбора”: таб “подбор” отсутствует — всё корректно
            }
        }

        private void ApplyMode()
        {
            if (_mode == OutputProductWindowMode.CreateOnly)
            {
                this.Title = "Создание номенклатуры";

                // скрыть вкладку «Подбор» и кнопку OK
                TabPick.Visibility = Visibility.Collapsed;
                BtnOk.Visibility = Visibility.Collapsed;

                // сразу на вкладку Создание
                Tabs.SelectedItem = TabCreate;
            }
            else
            {
                this.Title = "Выходное изделие";
            }
        }

        private void UnitCombo_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            ComboBox comboBox = sender as ComboBox;

            // Проверяем, существует ли введенный текст в ItemsSource
            var itemExists = comboBox.ItemsSource.Cast<RefNamedItem>()
                .Any(item => item.Name.StartsWith(comboBox.Text + e.Text, StringComparison.OrdinalIgnoreCase));

            // Если элемент не существует, предотвращаем ввод
            if (!itemExists)
            {
                e.Handled = true; // Блокируем ввод
            }
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

            // В режиме CreateOnly подгружать коды не нужно
            if (_mode == OutputProductWindowMode.CreateOnly)
                return;

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
        private void SaveLastKind()
        {
            OutputProductState.LastKindRefKey = _selectedKindRefKey;
            OutputProductState.LastKindName = _selectedKindName;
        }

        private void ApplySimpleValidationErrors(Dictionary<string, string> errors)
        {
            foreach (var kv in errors)
            {
                var key = (kv.Key ?? "").ToLowerInvariant();
                var msg = kv.Value ?? "";

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
                else
                {
                    // неизвестное поле — выведем в NameError как общий блок
                    NameError.Text = (NameError.Text + " " + msg).Trim();
                    NameBox.Background = System.Windows.Media.Brushes.MistyRose;
                }
            }
        }


        private void BtnValidate_Click(object sender, RoutedEventArgs e)
        {
            ClearErrors();

            if (string.IsNullOrEmpty(_selectedKindRefKey))
            {
                MessageBox.Show("Выберите вид номенклатуры в дереве слева.", "ERP");
                return;
            }

            var t = TypeCombo.SelectedItem as RefNamedItem;
            var u = UnitCombo.SelectedItem as RefNamedItem;

            string name = (NameBox.Text ?? "").Trim();
            string article = (ArticleBox.Text ?? "").Trim();

            try
            {
                var errors = ErpClient.ValidateNomenclature(
                    _selectedKindRefKey,
                    t != null ? t.RefKey : null,
                    u != null ? u.RefKey : null,
                    name,
                    article);

                if (errors.Count == 0)
                {
                    MessageBox.Show("Проверка успешна ✅", "ERP");
                    return;
                }

                ApplySimpleValidationErrors(errors); // подсветка полей + текст
            }
            catch (Exception ex)
            {
                MessageBox.Show("Ошибка проверки: " + ex.Message, "ERP");
            }
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
            // todo
            if (_mode == OutputProductWindowMode.PickOrCreate && Tabs.SelectedItem == TabPick) {
                var picked = CodesGrid.SelectedItem as ErpItem;
                if (picked != null) {
                    if (string.IsNullOrWhiteSpace(NameBox.Text)) {
                        NameBox.Text = picked.Name ?? "";
                    }
                    if (!string.IsNullOrWhiteSpace(picked.Unit) && UnitCombo.SelectedItem == null) {
                        foreach (var u in _units)
                        {
                            if (string.Equals(u.Name, picked.Unit, StringComparison.OrdinalIgnoreCase)) {
                                UnitCombo.SelectedItem = u;
                                break;
                            }
                        }
                    }
                    Tabs.SelectedItem= TabCreate;
                    return;
                }
                Tabs.SelectedItem = TabCreate;
                return;
            }
            // 
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
                string key = (kv.Key ?? "").ToLowerInvariant();
                string msg = string.Join("; ", kv.Value ?? new List<string>());

                // поддержка русских/англ ключей
                if (key.Contains("name") || key.Contains("наименование"))
                {
                    NameError.Text = msg;
                    NameBox.Background = Brushes.MistyRose;
                }
                else if (key.Contains("art") || key.Contains("артикул"))
                {
                    ArticleError.Text = msg;
                    ArticleBox.Background = Brushes.MistyRose;
                }
                else if (key.Contains("type") || key.Contains("типноменклатуры"))
                {
                    TypeError.Text = msg;
                    TypeCombo.Background = Brushes.MistyRose;
                }
                else if (key.Contains("unit") || key.Contains("единица"))
                {
                    UnitError.Text = msg;
                    UnitCombo.Background = Brushes.MistyRose;
                }
                else if (key.Contains("kind") || key.Contains("видноменклатуры"))
                {
                    // вид — показываем как общую ошибку (в NameError)
                    NameError.Text = (string.IsNullOrEmpty(NameError.Text) ? "" : (NameError.Text + " ")) + msg;
                }
                else
                {
                    NameError.Text = (string.IsNullOrEmpty(NameError.Text) ? "" : (NameError.Text + " ")) + msg;
                }
            }
        }
    }
}
