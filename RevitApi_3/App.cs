using System.Reflection;
using Autodesk.Revit.UI;

namespace RevitApi_3
{
    public class App : IExternalApplication
    {
        public Result OnStartup(UIControlledApplication application)
        {
            const string tabName = "ERP";
            try { application.CreateRibbonTab(tabName); } catch { }

            RibbonPanel panel = application.CreateRibbonPanel(tabName, "1C Integration");

            string path = Assembly.GetExecutingAssembly().Location;

            var b1 = new PushButtonData(
                "ErpMappingAll",
                "Коды 1C\n(все)",
                path,
                "RevitApi_3.ErpMappingAllCommand");

            var b2 = new PushButtonData(
                "ErpMappingActive",
                "Коды 1C\n(активная)",
                path,
                "RevitApi_3.ErpMappingActiveCommand");

            var b3 = new PushButtonData(
                "ErpExportResources",
                "Выгрузить\nв ERP",
                path,
                "RevitApi_3.ErpExportResourcesCommand");

            panel.AddItem(b1);
            panel.AddItem(b2);
            panel.AddItem(b3);

            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            return Result.Succeeded;
        }
    }
}
