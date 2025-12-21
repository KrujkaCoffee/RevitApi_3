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
            View activeView = uidoc.ActiveView;

            try
            {
                var vs = activeView as ViewSchedule;
                if (vs == null)
                {
                    TaskDialog.Show("ERP", "Активный вид не является спецификацией. Откройте нужную спецификацию и повторите.");
                    return Result.Failed;
                }

                // 1. Элементы из этой спецификации
                var rawItems = RevitCollectors.CollectFromSchedule(doc, vs);
                if (rawItems.Count == 0)
                {
                    TaskDialog.Show("ERP", "В активной спецификации нет элементов для обработки.");
                    return Result.Succeeded;
                }

                // 2. Гарантируем параметр
                ErpParameters.EnsureErpCodeParameterForItems(doc, rawItems);

                // 3. По одному на тип
                var itemsByType = RevitItemUtils.GroupByType(rawItems);

                // 4. Дерево ERP
                List<ErpTreeNode> treeRoots;
                try
                {
                    treeRoots = ErpClient.LoadErpTree();
                }
                catch
                {
                    TaskDialog.Show("ERP", "Сервис недоступен. Дерево номенклатуры получить не удалsось.");
                    return Result.Succeeded;
                }
                string ctx = "Спецификация: " + vs.Name;
                var win = new MappingWindow(itemsByType, treeRoots, ctx);
                var helper = new System.Windows.Interop.WindowInteropHelper(win);
                helper.Owner = commandData.Application.MainWindowHandle;

                bool? dlgResult = win.ShowDialog();
                if (dlgResult != true)
                    return Result.Succeeded;

                ApplyErpCodes(doc, win.ResultItems);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                TaskDialog.Show("ERP", "Ошибка: " + ex);
                return Result.Failed;
            }
        }
        private static void ApplyErpCodes(Document doc, IList<RevitItem> items)
        {
            if (items == null) return;

            int count = 0;

            using (Transaction t = new Transaction(doc, "Set ERP codes (active spec)"))
            {
                t.Start();

                // По типам
                var groups = items
                    .Where(r => !string.IsNullOrEmpty(r.ErpCode))
                    .GroupBy(r => r.TypeId.IntegerValue);

                foreach (var g in groups)
                {
                    ElementId typeId = new ElementId(g.Key);
                    Element type = doc.GetElement(typeId);
                    if (type == null) continue;

                    Parameter p = type.LookupParameter(ErpParameters.ErpCodeParamName);
                    if (p != null && !p.IsReadOnly && p.StorageType == StorageType.String)
                    {
                        p.Set(g.First().ErpCode);
                        count++;
                    }
                    else
                    {
                        // fallback по экземплярам
                        foreach (var ri in g)
                        {
                            Element inst = doc.GetElement(ri.ElementId);
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

                t.Commit();
            }

            TaskDialog.Show("ERP", "Записано кодов (активная спецификация): " + count);
        }
    }
}
