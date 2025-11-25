using System;
using System.IO;
using System.Reflection;
using System.Windows.Media.Imaging;
using Autodesk.Revit.UI;

namespace RevitErpIntegration
{
    public class App : IExternalApplication
    {
        public Result OnStartup(UIControlledApplication application)
        {
            const string tabName = "ERP";
            try
            {
                application.CreateRibbonTab(tabName);
            }
            catch
            {
                // вкладка уже есть
            }

            RibbonPanel panel = application.CreateRibbonPanel(tabName, "1C Integration");

            string assemblyPath = Assembly.GetExecutingAssembly().Location;

            // Кнопка 1 — сопоставление кодов
            PushButtonData mapBtnData = new PushButtonData(
                "ErpMappingCommand",
                "Коды 1C-ERP",
                assemblyPath,
                "RevitErpIntegration.ErpMappingCommand"
            );

            PushButton mapBtn = panel.AddItem(mapBtnData) as PushButton;
            if (mapBtn != null)
                mapBtn.ToolTip = "Сопоставление номенклатур с кодами 1C-ERP";

            PushButtonData exportBtnData = new PushButtonData(
                "ExportSpecsCommand",
                "Выгрузка спецификаций",
                assemblyPath,
                "RevitErpIntegration.ExportSpecsCommand"
            );

            PushButton exportBtn = panel.AddItem(exportBtnData) as PushButton;
            if (exportBtn != null)
                exportBtn.ToolTip = "POST выгрузка спецификаций в 1C-ERP";

            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            return Result.Succeeded;
        }
    }
}
