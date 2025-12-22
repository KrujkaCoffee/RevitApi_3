using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace RevitApi_3
{
    public partial class ExportWindow : Window
    {
        private string _last1cLink;
        private readonly string _contextInfo;
        private readonly List<ExportRow> _exportRows;

        private readonly List<ErpTreeNode> _treeRoots;
        private readonly List<RefNamedItem> _types;
        private readonly List<RefNamedItem> _units;

        private ErpItem _outputProduct;

        // Стадии под ComboBox (грузим через REST уже в окне)
        private readonly ObservableCollection<RefNamedItem> _stages = new ObservableCollection<RefNamedItem>();

        // Ошибки таблицы
        private readonly ObservableCollection<TableErrorVm> _tableErrors = new ObservableCollection<TableErrorVm>();

        public string DocTitle => (TitleBox.Text ?? "").Trim();

        public ExportWindow(
            List<ExportRow> exportRows,
            string contextInfo,
            string initialTitle,
            List<ErpTreeNode> treeRoots,
            List<RefNamedItem> types,
            List<RefNamedItem> units)
        {
            InitializeComponent();

            _contextInfo = contextInfo ?? "";
            _treeRoots = treeRoots ?? new List<ErpTreeNode>();
            _types = types ?? new List<RefNamedItem>();
            _units = units ?? new List<RefNamedItem>();

            _exportRows = exportRows ?? new List<ExportRow>();

            TitleBox.Text = initialTitle ?? string.Empty;
            ContextLabel.Text = _contextInfo;

            // Автор/даты
            AuthorBox.Text = WindowsUserHelper.GetFullName();
            StartDatePicker.SelectedDate = DateTime.Today;
            EndDatePicker.SelectedDate = DateTime.Today.AddDays(7);

            BuildColumns();
            ExportGrid.ItemsSource = _exportRows;

            ExportGrid.LoadingRow += ExportGrid_LoadingRow;

            ErrorsGrid.ItemsSource = _tableErrors;

            this.Loaded += ExportWindow_Loaded;
            this.Title = "Выгрузка ресурсной в ERP — " + _contextInfo;
        }

        private void ExportWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // Стадии грузим уже здесь (чтобы окно не падало, если сервис недоступен)
            try
            {
                var stages = ErpClient.LoadStages() ?? new List<RefNamedItem>();
                _stages.Clear();
                foreach (var s in stages)
                    _stages.Add(s);
            }
            catch
            {
                // оставляем пустым: Stage по дефолту пустой, ComboBox просто будет без значений
            }
        }

        private void BuildColumns()
        {
            ExportGrid.Columns.Clear();
            ExportGrid.AutoGenerateColumns = false;

            // Этап — редактируемый ComboBox, значение в ExportRow.Stage = RefKey (строка)
            ExportGrid.Columns.Add(new DataGridComboBoxColumn
            {
                Header = "Этап",
                ItemsSource = _stages,
                DisplayMemberPath = nameof(RefNamedItem.Name),
                SelectedValuePath = nameof(RefNamedItem.RefKey),
                SelectedValueBinding = new Binding(nameof(ExportRow.Stage))
                {
                    Mode = BindingMode.TwoWay,
                    UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
                }
            });

            AddCol("Семейство", nameof(ExportRow.FamilyName));
            AddCol("Тип", nameof(ExportRow.TypeName));
            AddCol("Наименование", nameof(ExportRow.DisplayName));
            AddCol("Код 1C-ERP", nameof(ExportRow.ErpCode));
            AddCol("Ед. изм.", nameof(ExportRow.Unit));
            AddCol("Количество", nameof(ExportRow.QuantityText));
        }

        private void AddCol(string header, string prop)
        {
            ExportGrid.Columns.Add(new DataGridTextColumn
            {
                Header = header,
                Binding = new Binding(prop),
                IsReadOnly = true
            });
        }

        private void ExportGrid_LoadingRow(object sender, DataGridRowEventArgs e)
        {
            e.Row.Header = (e.Row.GetIndex() + 1).ToString();
        }

        private void BtnPickOutput_Click(object sender, RoutedEventArgs e)
        {
            var win = new OutputProductWindow(
                _treeRoots,
                _types,
                _units,
                OutputProductWindowMode.PickOrCreate,
                initialName: "",
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

        private void BtnCheck_Click(object sender, RoutedEventArgs e)
        {
            RunValidate(showOkMessage: true);
        }

        private void BtnExport_Click(object sender, RoutedEventArgs e)
        {
            if (!RunValidate(showOkMessage: false))
                return;

            string title = DocTitle;
            string author = (AuthorBox.Text ?? "").Trim();
            string startDate = FormatDate(StartDatePicker.SelectedDate);
            string endDate = FormatDate(EndDatePicker.SelectedDate);

            try
            {
                string response = ErpClient.ExportResources(
                    title,
                    _contextInfo,
                    startDate,
                    endDate,
                    author,
                    _exportRows,
                    _outputProduct);

                _last1cLink = TryExtract1cLink(response);
                SuccessText.Text = "Спецификация успешно создана ✅";

                Open1cLinkBlock.Visibility = string.IsNullOrWhiteSpace(_last1cLink)
                    ? Visibility.Collapsed
                    : Visibility.Visible;
                MessageBox.Show("Выгрузка выполнена.\nОтвет сервера:\n" + response, "ERP");
                //this.DialogResult = true;
                //this.Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Ошибка при выгрузке ресурсной: " + ex.Message,
                    "ERP", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private bool RunValidate(bool showOkMessage)
        {
            ClearValidationUi();

            string title = DocTitle;
            if (string.IsNullOrWhiteSpace(title))
            {
                MarkFieldError("title", "Необходимо заполнить Наименование ресурсной.");
                MessageBox.Show("Необходимо заполнить Наименование ресурсной.", "ERP",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            if (_outputProduct == null)
            {
                MarkFieldError("output_product", "Не выбрано выходное изделие.");
                MessageBox.Show("Не выбрано выходное изделие. Используйте кнопку 'Подобрать / создать'.",
                    "ERP", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            string author = (AuthorBox.Text ?? "").Trim();
            string startDate = FormatDate(StartDatePicker.SelectedDate);
            string endDate = FormatDate(EndDatePicker.SelectedDate);

            // локальная быстрая проверка ERP-кодов (оставляем)
            var badRows = _exportRows
                .Where(r => string.IsNullOrWhiteSpace(r.ErpCode) || r.ErpCode == "-")
                .ToList();

            if (badRows.Count > 0)
            {
                ExportGrid.Items.Refresh();
                MessageBox.Show(
                    "Проверка/выгрузка невозможна: есть строки без кода 1C-ERP или с кодом '-'.\n" +
                    "Такие строки подсвечены красным.",
                    "ERP", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }

            try
            {
                var result = ErpClient.ValidateResources(
                    title,
                    _contextInfo,
                    startDate,
                    endDate,
                    author,
                    _exportRows,
                    _outputProduct);

                ApplyValidationResult(result);

                if (result == null || !result.HasErrors)
                {
                    if (showOkMessage)
                    {
                        MessageBox.Show("Проверка успешна ✅", "ERP",
                            MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    return true;
                }

                // есть ошибки
                return false;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Ошибка проверки: " + ex.Message, "ERP",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        private void ApplyValidationResult(ErpClient.ResourceValidationResult result)
        {
            if (result == null)
                return;

            // field_errors -> красим поля + выводим текст
            if (result.FieldErrors != null && result.FieldErrors.Count > 0)
            {
                FieldErrorsText.Text = string.Join("\n", result.FieldErrors.Select(kv => $"{kv.Key}: {kv.Value}"));
                foreach (var kv in result.FieldErrors)
                    MarkFieldError(kv.Key, kv.Value);
            }

            // table_errors -> мини таблица
            _tableErrors.Clear();
            if (result.TableErrors != null && result.TableErrors.Count > 0)
            {
                foreach (var e in result.TableErrors)
                    _tableErrors.Add(new TableErrorVm { Row = e.Row, Msg = e.Msg });

                ErrorsExpander.Visibility = Visibility.Visible;
            }
            else
            {
                ErrorsExpander.Visibility = Visibility.Collapsed;
            }
        }

        private void ErrorsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var sel = ErrorsGrid.SelectedItem as TableErrorVm;
            if (sel == null) return;

            int idx = sel.Row - 1;
            if (idx < 0 || idx >= _exportRows.Count) return;

            var item = _exportRows[idx];
            ExportGrid.SelectedItem = item;
            ExportGrid.ScrollIntoView(item);
            ExportGrid.Focus();
        }

        private void ClearValidationUi()
        {
            FieldErrorsText.Text = "";
            ErrorsExpander.Visibility = Visibility.Collapsed;
            _tableErrors.Clear();

            // сброс подсветки полей
            ResetField(TitleBox);
            ResetField(AuthorBox);
            ResetField(StartDatePicker);
            ResetField(EndDatePicker);
        }

        private static string FormatDate(DateTime? dt)
        {
            return dt.HasValue ? dt.Value.ToString("yyyy-MM-dd") : "";
        }

        private void MarkFieldError(string key, string msg)
        {
            // Мягкая привязка по ключам (на случай разных имен на бэке)
            var k = (key ?? "").ToLowerInvariant();

            if (k.Contains("title") || k.Contains("name"))
                SetFieldError(TitleBox, msg);
            else if (k.Contains("author") || k.Contains("creator"))
                SetFieldError(AuthorBox, msg);
            else if (k.Contains("start"))
                SetFieldError(StartDatePicker, msg);
            else if (k.Contains("end") || k.Contains("finish"))
                SetFieldError(EndDatePicker, msg);
            else
            {
                // неизвестное поле — просто покажем текстом
                if (string.IsNullOrWhiteSpace(FieldErrorsText.Text))
                    FieldErrorsText.Text = $"{key}: {msg}";
                else
                    FieldErrorsText.Text += "\n" + $"{key}: {msg}";
            }
        }

        private static void SetFieldError(Control c, string msg)
        {
            c.BorderBrush = Brushes.Red;
            c.BorderThickness = new Thickness(2);
            c.ToolTip = msg;
        }

        private static void ResetField(Control c)
        {
            c.ClearValue(BorderBrushProperty);
            c.ClearValue(BorderThicknessProperty);
            c.ToolTip = null;
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }

        private void Open1cLinkBlock_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_last1cLink)) return;
            OpenIn1c(_last1cLink);
        }

        private static string TryExtract1cLink(string response)
        {
            if (string.IsNullOrWhiteSpace(response)) return null;

            try
            {
                var token = JToken.Parse(response);
                var found = Find1cLinkInJson(token);
                if (!string.IsNullOrWhiteSpace(found))
                    return Uri.UnescapeDataString(found);
            }
            catch { /* ignore */ }

            int idx = response.IndexOf("e1c://", StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                int end = idx;
                while (end < response.Length)
                {
                    char ch = response[end];
                    if (char.IsWhiteSpace(ch) || ch == '"' || ch == '\'' || ch == '\r' || ch == '\n')
                        break;
                    end++;
                }
                return Uri.UnescapeDataString(response.Substring(idx, end - idx));
            }

            return null;
        }

        private static string Find1cLinkInJson(JToken token)
        {
            if (token == null) return null;

            if (token.Type == JTokenType.String)
            {
                string s = token.Value<string>();
                if (!string.IsNullOrWhiteSpace(s) && s.IndexOf("e1c://", StringComparison.OrdinalIgnoreCase) >= 0)
                    return s;
            }

            if (token is JObject obj)
            {
                foreach (var prop in obj.Properties())
                {
                    string name = (prop.Name ?? "").ToLowerInvariant();

                    if (prop.Value?.Type == JTokenType.String)
                    {
                        string val = prop.Value.Value<string>();
                        if (!string.IsNullOrWhiteSpace(val) &&
                            (name.Contains("Ссылка") || name.Contains("url") || name.Contains("href") || name.Contains("e1c")) &&
                            val.IndexOf("e1c://", StringComparison.OrdinalIgnoreCase) >= 0)
                            return val;
                    }

                    var nested = Find1cLinkInJson(prop.Value);
                    if (!string.IsNullOrWhiteSpace(nested))
                        return nested;
                }
            }

            if (token is JArray arr)
                foreach (var it in arr)
                {
                    var nested = Find1cLinkInJson(it);
                    if (!string.IsNullOrWhiteSpace(nested))
                        return nested;
                }

            return null;
        }

        private static void OpenIn1c(string link)
        {
            if (string.IsNullOrWhiteSpace(link)) return;

            link = Uri.UnescapeDataString(link.Trim());

            string exe = Resolve1cStartExe();
            string args = "/url \"" + link + "\"";
            string cmdLine = (string.IsNullOrWhiteSpace(exe) ? "1cestart.exe" : exe) + " " + args;

            try
            {
                if (!string.IsNullOrWhiteSpace(exe) && File.Exists(exe))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = exe,
                        Arguments = args,
                        UseShellExecute = false
                    });
                    return;
                }

                Process.Start(new ProcessStartInfo
                {
                    FileName = link,
                    UseShellExecute = true
                });
            }
            catch
            {
                try
                {
                    Clipboard.SetText(cmdLine);
                    MessageBox.Show("Не удалось открыть ссылку автоматически.\nКоманда скопирована в буфер:\n" + cmdLine,
                        "ERP", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch
                {
                    MessageBox.Show("Не удалось открыть ссылку.\nКоманда:\n" + cmdLine,
                        "ERP", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
        }

        private static string Resolve1cStartExe()
        {
            try
            {
                string p1 = Environment.ExpandEnvironmentVariables(@"%ProgramFiles%\\1cv8\\common\\1cestart.exe");
                if (File.Exists(p1)) return p1;

                string p2 = Environment.ExpandEnvironmentVariables(@"%ProgramFiles(x86)%\\1cv8\\common\\1cestart.exe");
                if (File.Exists(p2)) return p2;
            }
            catch { }

            return null;
        }


        private class TableErrorVm
        {
            public int Row { get; set; }
            public string Msg { get; set; }
        }
    }
}
