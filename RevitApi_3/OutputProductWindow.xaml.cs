using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

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
        private int _loadVersion;
        private bool _isBusy;
        private bool _isClosed;
        private string _restoreProductCode;
        private string _restoreKindRefKey;

        public ErpItem SelectedProduct { get; private set; }

        public OutputProductWindow(
            List<ErpTreeNode> roots,
            List<RefNamedItem> types,
            List<RefNamedItem> units,
            OutputProductWindowMode mode,
            string initialName = "",
            string initialKindRefKey = null,
            string initialKindName = null,
            ErpItem initialProduct = null)
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

            // Restore the accepted product, not the kind merely browsed before Cancel.
            ErpItem previousProduct = initialProduct ??
                (mode == OutputProductWindowMode.PickOrCreate ? OutputProductState.LastProduct : null);
            if (!string.IsNullOrWhiteSpace(previousProduct?.KindRefKey))
            {
                _selectedKindRefKey = previousProduct.KindRefKey;
                _selectedKindName = previousProduct.KindName ?? "";
                _restoreProductCode = previousProduct.Code;
                _restoreKindRefKey = previousProduct.KindRefKey;
            }
            else if (!string.IsNullOrWhiteSpace(initialKindRefKey))
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
                if (!string.IsNullOrWhiteSpace(_selectedKindRefKey))
                    Dispatcher.BeginInvoke(new Action(() => TrySelectNodeByRefKey(_selectedKindRefKey)),
                        DispatcherPriority.Loaded);
            };
            Closed += (s, e) => { _isClosed = true; ++_loadVersion; };
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

        private async void Tree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
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

            // В CreateOnly коды не нужны
            if (_mode == OutputProductWindowMode.CreateOnly)
                return;

            int loadVersion = ++_loadVersion;
            _codesFull.Clear();
            RebuildCodesView();
            SetBusy(true);
            try
            {
                List<ErpItem> loaded = await Task.Run(() => ErpClient.LoadErpItems(node.RefKey));
                if (_isClosed || loadVersion != _loadVersion) return;
                _codesFull = loaded ?? new List<ErpItem>();
            }
            catch (Exception ex)
            {
                if (_isClosed || loadVersion != _loadVersion) return;
                MessageBox.Show("Ошибка при загрузке номенклатуры: " + ex.Message,
                    "ERP", MessageBoxButton.OK, MessageBoxImage.Error);
                _codesFull = new List<ErpItem>();
            }

            finally
            {
                if (!_isClosed && loadVersion == _loadVersion)
                {
                    RebuildCodesView();
                    SetBusy(false);
                }
            }
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            RebuildCodesView();
        }

        private void RebuildCodesView()
        {
            if (CodesGrid == null) return;
            string selectedCode = (CodesGrid.SelectedItem as ErpItem)?.Code;
            if (string.IsNullOrWhiteSpace(selectedCode) &&
                string.Equals(_selectedKindRefKey, _restoreKindRefKey, StringComparison.OrdinalIgnoreCase))
                selectedCode = _restoreProductCode;
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
            ErpItem selected = _codesView.FirstOrDefault(x =>
                string.Equals(x.Code, selectedCode, StringComparison.OrdinalIgnoreCase));
            if (selected != null)
            {
                CodesGrid.SelectedItem = selected;
                CodesGrid.ScrollIntoView(selected);
            }
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

            AcceptProduct(erp);
            DialogResult = true;
        }

        private async void BtnCreate_Click(object sender, RoutedEventArgs e)
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

            string kindRef = _selectedKindRefKey;
            string typeRef = t?.RefKey;
            string unitRef = u2?.RefKey;
            SetBusy(true);
            try
            {
                Dictionary<string, string> errors = await Task.Run(() =>
                    ErpClient.ValidateNomenclature(kindRef, typeRef, unitRef, name, article));

                if (errors == null || errors.Count == 0)
                {
                    ErpItem created = await Task.Run(() =>
                        ErpClient.CreateNomenclature(kindRef, typeRef, unitRef, name, article));

                    AcceptProduct(created);
                    DialogResult = true;
                    return;
                }
                ApplySimpleValidationErrors(errors);
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
            finally
            {
                SetBusy(false);
            }
        }

        private async void BtnValidate_Click(object sender, RoutedEventArgs e)
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

            string kindRef = _selectedKindRefKey;
            string typeRef = t?.RefKey;
            string unitRef = u2?.RefKey;
            SetBusy(true);
            try
            {
                Dictionary<string, string> errors = await Task.Run(() =>
                    ErpClient.ValidateNomenclature(kindRef, typeRef, unitRef, name, article));

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
            finally
            {
                SetBusy(false);
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            if (_isBusy) return;
            DialogResult = false;
        }

        private void SetBusy(bool busy)
        {
            _isBusy = busy;
            Tree.IsEnabled = !busy;
            BtnOk.IsEnabled = !busy;
            BtnCreate.IsEnabled = !busy;
            BtnValidate.IsEnabled = !busy;
            CodesGrid.IsEnabled = !busy;
            Tabs.IsEnabled = !busy;
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
                else if (key.Contains("kind_ref") || key.Contains("видноменклатуры"))
                {
                    KindError.Text = (NameError.Text + " " + msg).Trim();
                    KindError.Background = Brushes.MistyRose;
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

        private void AcceptProduct(ErpItem product)
        {
            product.KindRefKey = _selectedKindRefKey;
            product.KindName = _selectedKindName;
            SelectedProduct = product;
            SelectedProductLabel.Text = product.Name + " (" + product.Code + ")";
            OutputProductState.LastKindRefKey = _selectedKindRefKey;
            OutputProductState.LastKindName = _selectedKindName;
            OutputProductState.LastProduct = product;
        }

        // Search the data first; expand only the ancestors of the selected kind.
        private void TrySelectNodeByRefKey(string refKey)
        {
            if (_isClosed || string.IsNullOrWhiteSpace(refKey)) return;
            var path = new List<ErpTreeNode>();
            if (!FindNodePath(_roots, refKey, path, new HashSet<string>(StringComparer.OrdinalIgnoreCase)))
            {
                _selectedKindRefKey = null;
                _selectedKindName = null;
                KindLabel.Text = "Ранее выбранный вид недоступен. Выберите другой вид.";
                return;
            }
            ItemsControl parent = Tree;
            for (int index = 0; index < path.Count; index++)
            {
                parent.UpdateLayout();
                var container = parent.ItemContainerGenerator.ContainerFromItem(path[index]) as TreeViewItem;
                if (container == null) return;
                if (index == path.Count - 1)
                {
                    container.IsSelected = true;
                    container.BringIntoView();
                }
                else
                    container.IsExpanded = true;
                parent = container;
            }
        }

        private static bool FindNodePath(IEnumerable<ErpTreeNode> nodes, string refKey,
            List<ErpTreeNode> path, HashSet<string> visited)
        {
            foreach (ErpTreeNode node in nodes)
            {
                if (node == null || !visited.Add(node.RefKey ?? "")) continue;
                path.Add(node);
                if (string.Equals(node.RefKey, refKey, StringComparison.OrdinalIgnoreCase) ||
                    FindNodePath(node.Children, refKey, path, visited))
                    return true;
                path.RemoveAt(path.Count - 1);
            }
            return false;
        }
    }
}
