using System;
using System.Collections.Generic;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System.Windows.Interop;

namespace RevitApi_3
{
    [Transaction(TransactionMode.Manual)]
    public class ErpMappingAllCommand : IExternalCommand
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

            try
            {
                ErpParameters.EnsureErpCodeParameter(doc);

                List<RevitItem> items = RevitCollectors.CollectFromAllSpecs(doc);
                if (items.Count == 0)
                {
                    TaskDialog.Show("ERP", "Не найдено элементов в спецификациях с префиксом 'Спецификация_'.");
                    return Result.Succeeded;
                }

                //if (System.Windows.Application.Current == null)
                    //new System.Windows.Application();

                MappingWindow win = new MappingWindow(doc, items, "Все спецификации проекта");
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
