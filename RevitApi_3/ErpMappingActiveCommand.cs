using System;
using System.Collections.Generic;
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
                ViewSchedule vs = activeView as ViewSchedule;
                if (vs == null)
                {
                    TaskDialog.Show("ERP", "Активный вид не является спецификацией. Откройте нужную спецификацию и повторите.");
                    return Result.Failed;
                }

                ErpParameters.EnsureErpCodeParameter(doc);

                List<RevitItem> items = RevitCollectors.CollectFromSchedule(doc, vs);
                if (items.Count == 0)
                {
                    TaskDialog.Show("ERP", "В активной спецификации нет элементов для обработки.");
                    return Result.Succeeded;
                }

                if (System.Windows.Application.Current == null)
                    new System.Windows.Application();

                string ctx = "Спецификация: " + vs.Name;
                MappingWindow win = new MappingWindow(doc, items, ctx);
                WindowInteropHelper helper = new WindowInteropHelper(win);
                helper.Owner = commandData.Application.MainWindowHandle;

                win.ShowDialog();
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                TaskDialog.Show("ERP", "Ошибка: " + ex.ToString());
                return Result.Failed;
            }
        }
    }
}
