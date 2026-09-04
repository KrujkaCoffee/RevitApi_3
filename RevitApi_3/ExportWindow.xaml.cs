using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
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
        private readonly ScheduleMirrorTable _table;
        private readonly string _contextInfo;
        private readonly string _existingInfoText;
        private readonly Action<string, string> _saveScheduleLink;
        private readonly Action _clearScheduleLink;

        private readonly ObservableCollection<RefNamedItem> _stages =
            new ObservableCollection<RefNamedItem>();
        private readonly ObservableCollection<TableErrorVm> _tableErrors =
            new ObservableCollection<TableErrorVm>();

        private List<ErpTreeNode> _treeRoots = new List<ErpTreeNode>();
        private List<RefNamedItem> _types = new List<RefNamedItem>();
        private List<RefNamedItem> _units = new List<RefNamedItem>();
        private ErpItem _outputProduct;
        private string _last1cLink;
        private bool _isBusy;

        public string DocTitle => (TitleBox.Text ?? "").Trim();
        public bool ExportSucceeded { get; private set; }

        public ExportWindow(
            ScheduleMirrorTable table,
            string contextInfo,
            string initialTitle,
            string existing1cLink,
            string existingInfoText,
            Action<string, string> saveScheduleLink,
            Action clearScheduleLink)
        {
            InitializeComponent();

            _table = table ?? throw new ArgumentNullException(nameof(table));
            _contextInfo = contextInfo ?? "";
            _existingInfoText = existingInfoText ?? "";
            _saveScheduleLink = saveScheduleLink;
            _clearScheduleLink = clearScheduleLink;
            _last1cLink = existing1cLink ?? "";

            Title = "Выгрузка ресурсной в ERP — " + _contextInfo;
            TitleBox.Text = initialTitle ?? "";
            ContextLabel.Text = _contextInfo;
            DiagnosticText.Text = _table.Diagnostic ?? "";
            AuthorBox.Text = WindowsUserHelper.GetFullName();
            StartDatePicker.SelectedDate = DateTime.Today;
            EndDatePicker.SelectedDate = DateTime.Today.AddDays(7);

            if (!string.IsNullOrWhiteSpace(_last1cLink))
            {
                SuccessText.Text = string.IsNullOrWhiteSpace(_existingInfoText)
                    ? "Спецификация уже была создана ранее"
                    : _existingInfoText;
                Open1cLinkBlock.Visibility = Visibility.Visible;
            }

            BuildColumns();
            ExportGrid.ItemsSource = _table.Rows;
            ErrorsGrid.ItemsSource = _tableErrors;
            Loaded += ExportWindow_Loaded;
        }

        private async void ExportWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                await Task.WhenAll(LoadReferenceDataAsync(), ValidateStoredLinkAsync());
            }
            catch (Exception ex)
            {
                SetServiceStatus("Ошибка инициализации окна ERP: " + ex.Message);
            }
        }

        private void BuildColumns()
        {
            ExportGrid.Columns.Clear();
            ExportGrid.Columns.Add(new DataGridComboBoxColumn
            {
                Header = "Этап ERP",
                ItemsSource = _stages,
                DisplayMemberPath = nameof(RefNamedItem.Name),
                SelectedValuePath = nameof(RefNamedItem.RefKey),
                SelectedValueBinding = new Binding(nameof(ScheduleMirrorRow.Stage))
                {
                    Mode = BindingMode.TwoWay,
                    UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
                },
                MinWidth = 120
            });

            foreach (ScheduleMirrorColumn column in _table.Columns.OrderBy(x => x.Index))
            {
                bool editableQuantity = column.IsQuantity;
                ExportGrid.Columns.Add(new DataGridTextColumn
                {
                    Header = column.Header,
                    Binding = new Binding($"Values[{column.Index}]")
                    {
                        Mode = editableQuantity ? BindingMode.TwoWay : BindingMode.OneWay,
                        UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
                    },
                    IsReadOnly = !editableQuantity,
                    MinWidth = 85,
                    Width = DataGridLength.Auto
                });
            }

            if (_table.ErpCodeColumnIndex < 0)
            {
                ExportGrid.Columns.Add(new DataGridTextColumn
                {
                    Header = "Код 1C-ERP (служебный)",
                    Binding = new Binding(nameof(ScheduleMirrorRow.ErpCode)),
                    IsReadOnly = true,
                    MinWidth = 125,
                    Width = DataGridLength.Auto
                });
            }
        }

        private async Task LoadReferenceDataAsync()
        {
            SetServiceStatus("Загрузка справочников ERP…");
            try
            {
                Task<List<ErpTreeNode>> treeTask = Task.Run(() => ErpClient.LoadErpTree());
                Task<List<RefNamedItem>> typesTask = Task.Run(() => ErpClient.LoadNomenclatureTypes());
                Task<List<RefNamedItem>> unitsTask = Task.Run(() => ErpClient.LoadUnits());
                Task<List<RefNamedItem>> stagesTask = Task.Run(() => ErpClient.LoadStages());

                await Task.WhenAll(treeTask, typesTask, unitsTask, stagesTask);
                _treeRoots = treeTask.Result ?? new List<ErpTreeNode>();
                _types = typesTask.Result ?? new List<RefNamedItem>();
                _units = unitsTask.Result ?? new List<RefNamedItem>();
                _stages.Clear();
                foreach (RefNamedItem stage in stagesTask.Result ?? new List<RefNamedItem>())
                    _stages.Add(stage);

                BtnPickOutput.IsEnabled = true;
                SetServiceStatus("Справочники ERP загружены.");
            }
            catch (Exception ex)
            {
                BtnPickOutput.IsEnabled = false;
                SetServiceStatus("Справочники ERP недоступны: " + ex.Message);
            }
        }

        private async Task ValidateStoredLinkAsync()
        {
            if (string.IsNullOrWhiteSpace(_last1cLink)) return;
            string checkedLink = _last1cLink;
            bool? alive = await Task.Run(() => ErpClient.CheckResourceLinkAlive(checkedLink));
            if (alive != false || !string.Equals(_last1cLink, checkedLink, StringComparison.Ordinal))
                return;

            _last1cLink = "";
            SuccessText.Text = "Сохранённая ссылка ERP больше не существует.";
            Open1cLinkBlock.Visibility = Visibility.Collapsed;
            try { _clearScheduleLink?.Invoke(); }
            catch (Exception ex)
            {
                SetServiceStatus("Не удалось очистить ссылку в Revit: " + ex.Message);
            }
        }

        private void BtnPickOutput_Click(object sender, RoutedEventArgs e)
        {
            var window = new OutputProductWindow(
                _treeRoots,
                _types,
                _units,
                OutputProductWindowMode.PickOrCreate,
                initialName: "",
                initialKindRefKey: OutputProductState.LastKindRefKey,
                initialKindName: OutputProductState.LastKindName);
            new WindowInteropHelper(window).Owner = new WindowInteropHelper(this).Handle;

            if (window.ShowDialog() == true && window.SelectedProduct != null)
            {
                _outputProduct = window.SelectedProduct;
                OutputNameText.Text = _outputProduct.Name;
                OutputCodeText.Text = _outputProduct.Code;
            }
        }

        private async void BtnCheck_Click(object sender, RoutedEventArgs e)
        {
            await RunValidateAsync(showOkMessage: true);
        }

        private async void BtnExport_Click(object sender, RoutedEventArgs e)
        {
            if (!await RunValidateAsync(showOkMessage: false)) return;

            SetBusy(true, "Выгрузка ресурсной в ERP…");
            try
            {
                string title = DocTitle;
                string startDate = FormatDate(StartDatePicker.SelectedDate);
                string endDate = FormatDate(EndDatePicker.SelectedDate);
                string author = (AuthorBox.Text ?? "").Trim();
                ErpItem outputProduct = _outputProduct;
                string response = await Task.Run(() => ErpClient.ExportResources(
                    title,
                    _contextInfo,
                    startDate,
                    endDate,
                    author,
                    _table,
                    outputProduct));

                ExportSucceeded = true;
                _last1cLink = TryExtract1cLink(response);
                if (!string.IsNullOrWhiteSpace(_last1cLink))
                {
                    try { _saveScheduleLink?.Invoke(_last1cLink, author); }
                    catch (Exception storageError)
                    {
                        MessageBox.Show(
                            "Ресурсная создана, но ссылку не удалось сохранить в Revit: " + storageError.Message,
                            "ERP", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }

                SuccessText.Text = "Спецификация успешно создана";
                Open1cLinkBlock.Visibility = string.IsNullOrWhiteSpace(_last1cLink)
                    ? Visibility.Collapsed
                    : Visibility.Visible;
                MessageBox.Show("Выгрузка успешно выполнена.", "ERP",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Ошибка при выгрузке ресурсной: " + ex.Message,
                    "ERP", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                SetBusy(false, "");
            }
        }

        private async Task<bool> RunValidateAsync(bool showOkMessage)
        {
            ExportGrid.CommitEdit(DataGridEditingUnit.Cell, true);
            ExportGrid.CommitEdit(DataGridEditingUnit.Row, true);
            ClearValidationUi();

            if (string.IsNullOrWhiteSpace(DocTitle))
            {
                MarkFieldError("title", "Необходимо заполнить наименование ресурсной.");
                MessageBox.Show("Необходимо заполнить наименование ресурсной.", "ERP",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            if (_outputProduct == null)
            {
                MarkFieldError("output_product", "Не выбрано выпускаемое изделие.");
                MessageBox.Show("Выберите выпускаемое изделие.", "ERP",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            List<ScheduleMirrorRow> missingCodes = _table.ResourceRows
                .Where(x => x.HasMissingErpCode)
                .ToList();
            if (missingCodes.Count > 0)
            {
                ExportGrid.Items.Refresh();
                ScheduleMirrorRow first = missingCodes[0];
                ExportGrid.SelectedItem = first;
                ExportGrid.ScrollIntoView(first);
                MessageBox.Show(
                    $"Есть строки ресурсов без кода 1C-ERP: {missingCodes.Count}. " +
                    "Выполните сопоставление активной спецификации.",
                    "ERP", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }

            SetBusy(true, "Проверка данных в ERP…");
            try
            {
                string title = DocTitle;
                string startDate = FormatDate(StartDatePicker.SelectedDate);
                string endDate = FormatDate(EndDatePicker.SelectedDate);
                string author = (AuthorBox.Text ?? "").Trim();
                ErpItem outputProduct = _outputProduct;
                ErpClient.ResourceValidationResult result = await Task.Run(() =>
                    ErpClient.ValidateResources(
                        title,
                        _contextInfo,
                        startDate,
                        endDate,
                        author,
                        _table,
                        outputProduct));

                ApplyValidationResult(result);
                if (result == null || !result.HasErrors)
                {
                    if (showOkMessage)
                        MessageBox.Show("Проверка успешна.", "ERP",
                            MessageBoxButton.OK, MessageBoxImage.Information);
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Ошибка проверки: " + ex.Message, "ERP",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
            finally
            {
                SetBusy(false, "");
            }
        }

        private void ApplyValidationResult(ErpClient.ResourceValidationResult result)
        {
            if (result == null) return;

            if (result.FieldErrors != null)
            {
                FieldErrorsText.Text = string.Join("\n", result.FieldErrors.Values);
                foreach (KeyValuePair<string, string> error in result.FieldErrors)
                    MarkFieldError(error.Key, error.Value);
            }

            _tableErrors.Clear();
            List<ScheduleMirrorRow> resourceRows = _table.ResourceRows.ToList();
            foreach (ErpClient.ResourceTableError error in
                     result.TableErrors ?? new List<ErpClient.ResourceTableError>())
            {
                int sourceRow = error.SourceRow;
                if (sourceRow <= 0 && error.Row > 0 && error.Row <= resourceRows.Count)
                    sourceRow = resourceRows[error.Row - 1].SourceRowNumber;

                _tableErrors.Add(new TableErrorVm
                {
                    ExportRow = error.Row,
                    SourceRow = sourceRow,
                    Msg = error.Msg
                });
            }
            ErrorsExpander.Visibility = _tableErrors.Count == 0
                ? Visibility.Collapsed
                : Visibility.Visible;
        }

        private void ErrorsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            TableErrorVm selected = ErrorsGrid.SelectedItem as TableErrorVm;
            if (selected == null) return;

            ScheduleMirrorRow row = selected.SourceRow > 0
                ? _table.Rows.FirstOrDefault(x => x.SourceRowNumber == selected.SourceRow)
                : _table.ResourceRows.Skip(Math.Max(0, selected.ExportRow - 1)).FirstOrDefault();
            if (row == null) return;

            ExportGrid.SelectedItem = row;
            ExportGrid.ScrollIntoView(row);
            ExportGrid.Focus();
        }

        private void ExportGrid_LoadingRow(object sender, DataGridRowEventArgs e)
        {
            ScheduleMirrorRow row = e.Row.Item as ScheduleMirrorRow;
            e.Row.Header = row?.SourceRowNumber.ToString() ?? "";
        }

        private void ExportGrid_BeginningEdit(object sender, DataGridBeginningEditEventArgs e)
        {
            if (!(e.Row.Item is ScheduleMirrorRow row) || !row.IsResourceRow)
                e.Cancel = true;
        }

        private void ClearValidationUi()
        {
            FieldErrorsText.Text = "";
            ErrorsExpander.Visibility = Visibility.Collapsed;
            _tableErrors.Clear();
            ResetField(TitleBox);
            ResetField(AuthorBox);
            ResetField(StartDatePicker);
            ResetField(EndDatePicker);
        }

        private void MarkFieldError(string key, string message)
        {
            string normalized = (key ?? "").ToLowerInvariant();
            if (normalized.Contains("title") || normalized.Contains("name"))
                SetFieldError(TitleBox, message);
            else if (normalized.Contains("author") || normalized.Contains("creator"))
                SetFieldError(AuthorBox, message);
            else if (normalized.Contains("start"))
                SetFieldError(StartDatePicker, message);
            else if (normalized.Contains("end") || normalized.Contains("finish"))
                SetFieldError(EndDatePicker, message);
            else if (string.IsNullOrWhiteSpace(FieldErrorsText.Text))
                FieldErrorsText.Text = message;
            else
                FieldErrorsText.Text += "\n" + message;
        }

        private static void SetFieldError(Control control, string message)
        {
            control.BorderBrush = Brushes.Red;
            control.BorderThickness = new Thickness(2);
            control.ToolTip = message;
        }

        private static void ResetField(Control control)
        {
            control.ClearValue(BorderBrushProperty);
            control.ClearValue(BorderThicknessProperty);
            control.ToolTip = null;
        }

        private void SetBusy(bool busy, string status)
        {
            _isBusy = busy;
            BtnCheck.IsEnabled = !busy;
            BtnExport.IsEnabled = !busy;
            BtnPickOutput.IsEnabled = !busy && _treeRoots.Count > 0;
            ExportGrid.IsEnabled = !busy;
            TitleBox.IsEnabled = !busy;
            StartDatePicker.IsEnabled = !busy;
            EndDatePicker.IsEnabled = !busy;
            if (!string.IsNullOrWhiteSpace(status)) SetServiceStatus(status);
        }

        private void SetServiceStatus(string text)
        {
            ServiceStatusText.Text = text ?? "";
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            if (_isBusy) return;
            DialogResult = false;
        }

        private void Open1cLinkBlock_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(_last1cLink)) OpenIn1c(_last1cLink);
        }

        private static string FormatDate(DateTime? date)
        {
            return date.HasValue ? date.Value.ToString("yyyy-MM-dd") : "";
        }

        private static string TryExtract1cLink(string response)
        {
            if (string.IsNullOrWhiteSpace(response)) return null;
            try
            {
                string found = Find1cLinkInJson(JToken.Parse(response));
                if (!string.IsNullOrWhiteSpace(found)) return Uri.UnescapeDataString(found);
            }
            catch { }

            int index = response.IndexOf("e1c://", StringComparison.OrdinalIgnoreCase);
            if (index < 0) return null;
            int end = index;
            while (end < response.Length && !char.IsWhiteSpace(response[end]) &&
                   response[end] != '"' && response[end] != '\'')
                end++;
            return Uri.UnescapeDataString(response.Substring(index, end - index));
        }

        private static string Find1cLinkInJson(JToken token)
        {
            if (token == null) return null;
            if (token.Type == JTokenType.String)
            {
                string value = token.Value<string>();
                if (!string.IsNullOrWhiteSpace(value) &&
                    value.IndexOf("e1c://", StringComparison.OrdinalIgnoreCase) >= 0)
                    return value;
            }
            if (token is JObject obj)
            {
                foreach (JProperty property in obj.Properties())
                {
                    string nested = Find1cLinkInJson(property.Value);
                    if (!string.IsNullOrWhiteSpace(nested)) return nested;
                }
            }
            if (token is JArray array)
            {
                foreach (JToken item in array)
                {
                    string nested = Find1cLinkInJson(item);
                    if (!string.IsNullOrWhiteSpace(nested)) return nested;
                }
            }
            return null;
        }

        private static void OpenIn1c(string link)
        {
            link = Uri.UnescapeDataString((link ?? "").Trim());
            string executable = Resolve1cStartExe();
            string arguments = "/url \"" + link + "\"";
            string command = (string.IsNullOrWhiteSpace(executable) ? "1cestart.exe" : executable) +
                             " " + arguments;
            try
            {
                if (!string.IsNullOrWhiteSpace(executable) && File.Exists(executable))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = executable,
                        Arguments = arguments,
                        UseShellExecute = false
                    });
                }
                else
                {
                    Process.Start(new ProcessStartInfo { FileName = link, UseShellExecute = true });
                }
            }
            catch
            {
                try { Clipboard.SetText(command); } catch { }
                MessageBox.Show("Не удалось открыть ссылку автоматически. Команда скопирована:\n" + command,
                    "ERP", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private static string Resolve1cStartExe()
        {
            string first = Environment.ExpandEnvironmentVariables(
                @"%ProgramFiles%\1cv8\common\1cestart.exe");
            if (File.Exists(first)) return first;
            string second = Environment.ExpandEnvironmentVariables(
                @"%ProgramFiles(x86)%\1cv8\common\1cestart.exe");
            return File.Exists(second) ? second : null;
        }

        private sealed class TableErrorVm
        {
            public int ExportRow { get; set; }
            public int SourceRow { get; set; }
            public string DisplayRow => SourceRow > 0 ? SourceRow.ToString() : ExportRow.ToString();
            public string Msg { get; set; }
        }
    }
}
