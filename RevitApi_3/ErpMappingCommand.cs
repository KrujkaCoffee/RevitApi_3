using System;
using System.Collections.Generic;
using Autodesk.Revit.UI;
using Autodesk.Revit.DB;

namespace RevitErpIntegration
{
    [Autodesk.Revit.Attributes.Transaction(Autodesk.Revit.Attributes.TransactionMode.Manual)]
    public class ErpMappingCommand : IExternalCommand
    {
        public Result Execute(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            if (uidoc == null)
            {
                TaskDialog.Show("ERP", "Откройте документ перед запуском.");
                return Result.Failed;
            }

            Document doc = uidoc.Document;

            try
            {
                // убедимся, что параметр существует
                ErpParameters.EnsureErpCodeParameter(doc);

                List<RevitItem> items = RevitCollectors.CollectItemsFromSpecs(doc);

                if (items.Count == 0)
                {
                    TaskDialog.Show("ERP", "Не найдено элементов в спецификациях с префиксом 'Спецификация_'.");
                    return Result.Succeeded;
                }

                // WPF Application, если нет
                if (System.Windows.Application.Current == null)
                    new System.Windows.Application();

                MappingWindow win = new MappingWindow(doc, items);
                var helper = new System.Windows.Interop.WindowInteropHelper(win);
                helper.Owner = commandData.Application.MainWindowHandle;
                win.ShowDialog();

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                TaskDialog.Show("ERP", "Ошибка: " + ex.Message);
                return Result.Failed;
            }
        }
    }
}
