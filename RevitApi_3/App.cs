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

            PushButtonData b1 = new PushButtonData(
                "ErpMappingAll",
                "Коды 1C (все)",
                path,
                "RevitApi_3.ErpMappingAllCommand");

            PushButtonData b2 = new PushButtonData(
                "ErpMappingActive",
                "Коды 1C (активная)",
                path,
                "RevitApi_3.ErpMappingActiveCommand");

            panel.AddItem(b1);
            panel.AddItem(b2);

            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            return Result.Succeeded;
        }
    }
}
