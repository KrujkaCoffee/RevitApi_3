using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace RevitApi_3
{
    internal static class ScheduleToExportRows
    {
        public static List<ExportRow> Build(Document doc, ViewSchedule vs)
        {
            var table = ScheduleExportBuilder.Build(vs);
            var rows = new List<ExportRow>();

            // Пытаемся найти нужные колонки по заголовкам (подстрой токены под ваши шаблоны)
            int colFamily = Find(table.Headers, "Семейство", "Family");
            int colType = Find(table.Headers, "Тип", "Type");
            int colName = Find(table.Headers, "Наименование", "Имя", "Name");
            int colQty = Find(table.Headers, "Кол", "Количество", "Qty", "Count");
            int colMass1 = Find(table.Headers, "Масса / шт", "Масса/шт", "Масса шт", "Масса/ед");
            int colMassT = Find(table.Headers, "Масса всего", "Масса итого", "Итого масса");

            int colErp = table.ErpCodeCol;
            int colUnit = table.UnitCol;

            foreach (var sr in table.Rows)
            {
                string family = Get(sr, colFamily);
                string type = Get(sr, colType);
                string name = Get(sr, colName);

                // Фильтр от "шапок"/групповых заголовков/пустых строк:
                // если нет ни типа, ни имени — это не строка ресурса
                if (IsNonDataRow(family, type, name, sr))
                    continue;

                string erp = Get(sr, colErp);
                string unit = Get(sr, colUnit);

                rows.Add(new ExportRow
                {
                    Stage = "",                 // позже заполняется комбобоксами
                    ScheduleName = vs.Name,
                    FamilyName = family,
                    TypeName = type,
                    DisplayName = name,
                    ErpCode = erp,
                    Unit = unit,
                    QuantityText = Get(sr, colQty),
                    MassPerItemText = Get(sr, colMass1),
                    TotalMassText = Get(sr, colMassT)
                });
            }

            return rows;
        }

        private static bool IsNonDataRow(string family, string type, string name, ScheduleExportRow sr)
        {
            // минимально-надёжная эвристика
            bool allEmpty = true;
            foreach (var v in sr.Values)
            {
                if (!string.IsNullOrWhiteSpace(v)) { allEmpty = false; break; }
            }
            if (allEmpty) return true;

            if (string.IsNullOrWhiteSpace(type) && string.IsNullOrWhiteSpace(name))
                return true;

            return false;
        }

        private static string Get(ScheduleExportRow r, int idx)
        {
            if (idx < 0 || idx >= r.Values.Count) return "";
            return r.Values[idx] ?? "";
        }

        private static int Find(List<string> headers, params string[] tokens)
        {
            for (int i = 0; i < headers.Count; i++)
            {
                string h = headers[i] ?? "";
                foreach (var t in tokens)
                {
                    if (string.IsNullOrEmpty(t)) continue;
                    if (h.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0)
                        return i;
                }
            }
            return -1;
        }
    }
}
