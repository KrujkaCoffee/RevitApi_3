using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace RevitApi_3
{
    public partial class OutputProductWindow : Window
    {
        private readonly OutputProductWindowMode _mode;

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
            string initialName = "",
            string initialKindRefKey = null,
            string initialKindName = null)
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

            // имя в форму
            NameBox.Text = (initialName ?? "").Trim();

            // вид номенклатуры: приоритет — явно переданный, иначе lastKind
            if (!string.IsNullOrWhiteSpace(initialKindRefKey))
            {
                _selectedKindRefKey = initialKindRefKey;
                _selectedKindName = initialKindName ?? "";
            }
            else if (!string.IsNullOrWhiteSpace(OutputProductState.LastKindRefKey))
            {
                _selectedKindRefKey = OutputProductState.LastKindRefKey;
                _selectedKindName = OutputProductState.LastKindName ?? "";
            }

            // отрисуем label (даже если дерево ещё не выбрано)
            KindLabel.Text = _selectedKindName ?? "";

            Loaded += (s, e) =>
            {
                ApplyMode();
                // (best-effort) попробуем подсветить узел в дереве
                if (!string.IsNullOrWhiteSpace(_selectedKindRefKey))
                    TrySelectNodeByRefKey(_selectedKindRefKey);
            };
        }

        private void ApplyMode()
        {
            if (_mode == OutputProductWindowMode.CreateOnly)
            {
                Title = "Создание номенклатуры";

                TabPick.Visibility = Visibility.Collapsed;
                BtnOk.Visibility = Visibility.Collapsed;

                Tabs.SelectedItem = TabCreate;
            }
            else
            {
                Title = "Выходное изделие";
                TabPick.Visibility = Visibility.Visible;
                BtnOk.Visibility = Visibility.Visible;
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

            // lastKind
            OutputProductState.LastKindRefKey = _selectedKindRefKey;
            OutputProductState.LastKindName = _selectedKindName;

            // В CreateOnly коды не нужны
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

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            RebuildCodesView();
        }

        private void RebuildCodesView()
        {
            string term = (SearchBox?.Text ?? "").Trim().ToLowerInvariant();

            _codesView = new List<ErpItem>();
            foreach (var it in _codesFull)
            {
                if (!string.IsNullOrEmpty(term))
                {
                    string code = (it.Code ?? "").ToLowerInvariant();
                    string name = (it.Name ?? "").ToLowerInvariant();
                    string extra = (it.Extra ?? "").ToLowerInvariant();
                    string unit = (it.Unit ?? "").ToLowerInvariant();

                    if (!code.Contains(term) && !name.Contains(term) && !extra.Contains(term) && !unit.Contains(term))
                        continue;
                }

                _codesView.Add(it);
            }

            CodesGrid.ItemsSource = _codesView;
            CodesGrid.Items.Refresh();
        }

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            // только для режима PickOrCreate
            var erp = CodesGrid.SelectedItem as ErpItem;
            if (erp == null)
            {
                MessageBox.Show("Выберите существующую номенклатуру в таблице подбора или используйте кнопку 'Создать'.",
                    "ERP", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            SelectedProduct = erp;
            SelectedProductLabel.Text = erp.Name + " (" + erp.Code + ")";
            DialogResult = true;
            Close();
        }

        private void BtnCreate_Click(object sender, RoutedEventArgs e)
        {
            // если мы в режиме подбора — по клику "Создать" на вкладке Подбор:
            // забираем имя выбранной позиции и переходим на форму (без создания)
            if (_mode == OutputProductWindowMode.PickOrCreate && Tabs.SelectedItem == TabPick)
            {
                var picked = CodesGrid.SelectedItem as ErpItem;
                if (picked != null)
                {
                    if (string.IsNullOrWhiteSpace(NameBox.Text))
                        NameBox.Text = picked.Name ?? "";

                    if (!string.IsNullOrWhiteSpace(picked.Unit) && UnitCombo.SelectedItem == null)
                    {
                        var u = _units.FirstOrDefault(x => string.Equals(x.Name, picked.Unit, StringComparison.OrdinalIgnoreCase));
                        if (u != null) UnitCombo.SelectedItem = u;
                    }
                }

                Tabs.SelectedItem = TabCreate;
                return;
            }

            // иначе — реальное создание
            Tabs.SelectedItem = TabCreate;
            ClearErrors();

            if (string.IsNullOrWhiteSpace(_selectedKindRefKey))
            {
                MessageBox.Show("Выберите вид номенклатуры в дереве слева.", "ERP",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var t = TypeCombo.SelectedItem as RefNamedItem;
            var u2 = UnitCombo.SelectedItem as RefNamedItem;

            string name = (NameBox.Text ?? "").Trim();
            string article = (ArticleBox.Text ?? "").Trim();

            try
            {
                var created = ErpClient.CreateNomenclature(
                    _selectedKindRefKey,
                    t != null ? t.RefKey : null,
                    u2 != null ? u2.RefKey : null,
                    name,
                    article);

                SelectedProduct = created;
                SelectedProductLabel.Text = created.Name + " (" + created.Code + ")";
                DialogResult = true;
                Close();
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

        private void BtnValidate_Click(object sender, RoutedEventArgs e)
        {
            Tabs.SelectedItem = TabCreate;
            ClearErrors();

            if (string.IsNullOrWhiteSpace(_selectedKindRefKey))
            {
                MessageBox.Show("Выберите вид номенклатуры в дереве слева.", "ERP",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var t = TypeCombo.SelectedItem as RefNamedItem;
            var u2 = UnitCombo.SelectedItem as RefNamedItem;

            string name = (NameBox.Text ?? "").Trim();
            string article = (ArticleBox.Text ?? "").Trim();

            try
            {
                var errors = ErpClient.ValidateNomenclature(
                    _selectedKindRefKey,
                    t != null ? t.RefKey : null,
                    u2 != null ? u2.RefKey : null,
                    name,
                    article);

                if (errors == null || errors.Count == 0)
                {
                    MessageBox.Show("Проверка успешна ✅", "ERP",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                ApplySimpleValidationErrors(errors);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Ошибка проверки: " + ex.Message, "ERP",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
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

        private void ApplySimpleValidationErrors(Dictionary<string, string> errors)
        {
            foreach (var kv in errors)
            {
                var key = (kv.Key ?? "").ToLowerInvariant();
                var msg = kv.Value ?? "";

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
                    NameError.Text = (NameError.Text + " " + msg).Trim();
                    NameBox.Background = Brushes.MistyRose;
                }
                else
                {
                    NameError.Text = (NameError.Text + " " + msg).Trim();
                    NameBox.Background = Brushes.MistyRose;
                }
            }
        }

        private void ApplyValidationErrors(Dictionary<string, List<string>> errors)
        {
            ClearErrors();

            foreach (var kv in errors)
            {
                string key = (kv.Key ?? "").ToLowerInvariant();
                string msg = string.Join("; ", kv.Value ?? new List<string>());

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
                else
                {
                    NameError.Text = (string.IsNullOrEmpty(NameError.Text) ? "" : (NameError.Text + " ")) + msg;
                    NameBox.Background = Brushes.MistyRose;
                }
            }
        }

        // --- best effort: выделить узел дерева по RefKey ---
        private void TrySelectNodeByRefKey(string refKey)
        {
            if (string.IsNullOrWhiteSpace(refKey)) return;

            Tree.UpdateLayout();

            foreach (var root in _roots)
            {
                if (TrySelectNodeRecursive(Tree, root, refKey))
                    break;
            }
        }

        private bool TrySelectNodeRecursive(ItemsControl parent, ErpTreeNode node, string refKey)
        {
            if (node == null) return false;

            var container = parent.ItemContainerGenerator.ContainerFromItem(node) as TreeViewItem;
            if (container == null)
                return false;

            if (string.Equals(node.RefKey, refKey, StringComparison.OrdinalIgnoreCase))
            {
                container.IsSelected = true;
                container.BringIntoView();
                return true;
            }

            container.IsExpanded = true;
            container.UpdateLayout();

            foreach (var ch in node.Children)
            {
                if (TrySelectNodeRecursive(container, ch, refKey))
                    return true;
            }

            return false;
        }
    }
}
