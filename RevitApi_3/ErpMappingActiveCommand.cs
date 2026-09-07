using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System.Windows.Interop;

namespace RevitApi_3
{
    [Transaction(TransactionMode.Manual)]
    public class ErpMappingActiveCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            if (uidoc == null)
            {
                TaskDialog.Show("ERP", "Откройте документ.");
                return Result.Failed;
            }

            Document doc = uidoc.Document;
            ViewSchedule schedule = uidoc.ActiveView as ViewSchedule;
            if (schedule == null)
            {
                TaskDialog.Show("ERP",
                    "Активный вид не является спецификацией. Откройте нужную спецификацию и повторите.");
                return Result.Failed;
            }

            try
            {
                List<RevitItem> items = RevitCollectors.CollectFromSchedule(doc, schedule);
                if (items.Count == 0)
                {
                    TaskDialog.Show("ERP", "В активной спецификации нет элементов для обработки.");
                    return Result.Succeeded;
                }

                Guid parameterGuid = ErpParameters.EnsureErpCodeParameterForItems(doc, items);
                ScheduleMirrorTable mirror = ScheduleMirrorBuilder.Build(doc, schedule, parameterGuid);
                if (!mirror.ResourceRows.Any())
                {
                    TaskDialog.Show("ERP",
                        "В теле спецификации не удалось определить строки номенклатуры. " +
                        "Проверьте состав видимых колонок.");
                    return Result.Succeeded;
                }

                var window = new MappingWindow(mirror, "Спецификация: " + schedule.Name);
                new WindowInteropHelper(window).Owner = commandData.Application.MainWindowHandle;

                if (window.ShowDialog() != true) return Result.Succeeded;

                int count = ApplyErpCodes(doc, window.ResultRows, parameterGuid);
                TaskDialog.Show("ERP", "Код 1C-ERP успешно записан.");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                TaskDialog.Show("ERP", "Ошибка: " + ex.Message);
                return Result.Failed;
            }
        }

        private static int ApplyErpCodes(
            Document doc,
            IEnumerable<ScheduleMirrorRow> rows,
            Guid parameterGuid)
        {
            var byElement = new Dictionary<int, string>();

            foreach (ScheduleMirrorRow row in rows ?? Enumerable.Empty<ScheduleMirrorRow>())
            {
                string code = (row.ErpCode ?? "").Trim();
                string original = (row.OriginalErpCode ?? "").Trim();
                if (!row.CanWriteErpCode || string.Equals(code, original, StringComparison.OrdinalIgnoreCase))
                    continue;

                foreach (int id in row.ElementIds)
                {
                    if (byElement.TryGetValue(id, out string previous) &&
                        !string.Equals(previous, code, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException(
                            $"Элемент Revit {id} попал в несколько строк с разными кодами. " +
                            "Запись отменена; уточните группировку спецификации.");
                    }
                    byElement[id] = code;
                }
            }

            if (byElement.Count == 0) return 0;

            using (Transaction transaction = new Transaction(doc, "Запись кодов 1C-ERP"))
            {
                transaction.Start();
                foreach (KeyValuePair<int, string> pair in byElement)
                {
                    Element element = doc.GetElement(new ElementId(pair.Key));
                    Parameter parameter = ErpParameters.GetCodeParameter(element, parameterGuid);
                    if (parameter == null || parameter.IsReadOnly || parameter.StorageType != StorageType.String)
                    {
                        transaction.RollBack();
                        throw new InvalidOperationException(
                            $"Параметр «{ErpParameters.ErpCodeParamName}» недоступен для элемента {pair.Key}. " +
                            "Ни одно изменение не сохранено.");
                    }
                    parameter.Set(pair.Value ?? "");
                }
                transaction.Commit();
            }

            return byElement.Count;
        }
    }
}
