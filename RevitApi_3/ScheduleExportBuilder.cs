using System;
using System.Collections.Generic;
using System.Reflection;
using Autodesk.Revit.DB;

namespace RevitApi_3
{
    internal static class ScheduleExportBuilder
    {
        // Скрытые колонки, которые НЕ показываем в UI, но включаем в payload.
        // Подстрой под свой реальный заголовок "системной" 1С колонки в спецификации.
        private static readonly string[] SystemColumnTokens = new[]
        {
            "ref_key", "refkey", "guid", "1c", "1с", "ссылка", "uuid", "ключ"
        };

        // Как ищем колонку ERP-кода для подсветки (по заголовку колонки)
        private static readonly string[] ErpCodeTokens = new[]
        {
            "код 1c", "код 1с", "код erp", "1c-erp", "1с-erp", "1c erp", "1с erp", "erp"
        };

        // Единица измерения (если она есть видимой колонкой)
        private static readonly string[] UnitTokens = new[]
        {
            "ед", "ед.", "ед. изм", "единиц", "unit"
        };

        public static ScheduleExportTable Build(ViewSchedule vs)
        {
            if (vs == null) throw new ArgumentNullException(nameof(vs));

            var table = new ScheduleExportTable { ScheduleName = vs.Name };

            TableData td = vs.GetTableData();
            TableSectionData header = td.GetSectionData(SectionType.Header);
            TableSectionData body = td.GetSectionData(SectionType.Body);

            int headerRow = Math.Max(0, header.NumberOfRows - 1);

            // 1) Заголовки по всем колонкам body (даже скрытым)
            var allHeaders = new List<string>();
            for (int c = 0; c < body.NumberOfColumns; c++)
            {
                string h = SafeGetCellText(vs, SectionType.Header, headerRow, c);
                if (string.IsNullOrWhiteSpace(h)) h = "Column_" + (c + 1);
                allHeaders.Add(h.Trim());
            }

            // 2) Колонки UI + payload
            var usedKeys = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            for (int c = 0; c < body.NumberOfColumns; c++)
            {
                string headerText = allHeaders[c];

                bool hidden = IsHiddenColumn(body, c);
                bool isSystem = ContainsAnyToken(headerText, SystemColumnTokens);

                bool showInUi = !hidden;
                bool includeInPayload = showInUi || isSystem;

                if (!showInUi && !includeInPayload)
                    continue;

                // ВАЖНО: Key в payload делаем максимально близким к Header,
                // но если есть дубликаты — добавим суффикс.
                string key = DedupKey(headerText, usedKeys);

                var col = new ScheduleExportColumn
                {
                    Header = headerText,
                    Key = key,
                    ScheduleColumnIndex = c,
                    ShowInUi = showInUi,
                    IncludeInPayload = includeInPayload
                };

                if (showInUi) table.UiColumns.Add(col);
                if (includeInPayload) table.PayloadColumns.Add(col);
            }

            table.UiErpCodeIndex = FindUiIndex(table.UiColumns, ErpCodeTokens);
            table.UiUnitIndex = FindUiIndex(table.UiColumns, UnitTokens);

            // 3) Строки: берем ровно то, что в body (и что не скрыто строкой)
            for (int r = 0; r < body.NumberOfRows; r++)
            {
                if (IsHiddenRow(body, r)) continue;

                var row = new ScheduleExportRow();

                foreach (var uiCol in table.UiColumns)
                {
                    string v = SafeGetCellText(vs, SectionType.Body, r, uiCol.ScheduleColumnIndex);
                    row.Values.Add(v);
                }

                foreach (var pCol in table.PayloadColumns)
                {
                    string v = SafeGetCellText(vs, SectionType.Body, r, pCol.ScheduleColumnIndex);
                    row.Payload[pCol.Key] = v;
                }

                if (table.UiErpCodeIndex >= 0 && table.UiErpCodeIndex < row.Values.Count)
                    row.ErpCode = row.Values[table.UiErpCodeIndex];

                if (table.UiUnitIndex >= 0 && table.UiUnitIndex < row.Values.Count)
                    row.Unit = row.Values[table.UiUnitIndex];

                table.Rows.Add(row);
            }

            return table;
        }

        private static int FindUiIndex(List<ScheduleExportColumn> uiCols, string[] tokens)
        {
            for (int i = 0; i < uiCols.Count; i++)
            {
                string h = uiCols[i].Header ?? "";
                if (ContainsAnyToken(h, tokens)) return i;
            }
            return -1;
        }

        private static bool ContainsAnyToken(string text, IEnumerable<string> tokens)
        {
            if (string.IsNullOrEmpty(text)) return false;
            foreach (var t in tokens)
            {
                if (string.IsNullOrEmpty(t)) continue;
                if (text.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            return false;
        }

        private static string DedupKey(string key, Dictionary<string, int> used)
        {
            key = (key ?? "").Trim();
            if (key.Length == 0) key = "Column";

            if (!used.ContainsKey(key))
            {
                used[key] = 1;
                return key;
            }

            int n = used[key] + 1;
            used[key] = n;
            return key + "_" + n;
        }

        private static string SafeGetCellText(ViewSchedule vs, SectionType sec, int r, int c)
        {
            try { return vs.GetCellText(sec, r, c) ?? ""; }
            catch { return ""; }
        }

        // Reflection — чтобы не зависеть от наличия IsColumnHidden/IsRowHidden в конкретной версии API
        private static bool IsHiddenColumn(TableSectionData sec, int col)
        {
            try
            {
                MethodInfo mi = sec.GetType().GetMethod("IsColumnHidden", new[] { typeof(int) });
                if (mi == null) return false;
                return (bool)mi.Invoke(sec, new object[] { col });
            }
            catch { return false; }
        }

        private static bool IsHiddenRow(TableSectionData sec, int row)
        {
            try
            {
                MethodInfo mi = sec.GetType().GetMethod("IsRowHidden", new[] { typeof(int) });
                if (mi == null) return false;
                return (bool)mi.Invoke(sec, new object[] { row });
            }
            catch { return false; }
        }
    }
}
