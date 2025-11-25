using System;
using System.IO;
using Autodesk.Revit.DB;
using Autodesk.Revit.ApplicationServices;

namespace RevitApi_3
{
    public static class ErpParameters
    {
        public const string ErpCodeParamName = "Код 1C-ERP";
        public const string GroupName = "ERP";

        public const string EndpointParamName = "ERP_Endpoint";
        public const string DocTitleParamName = "ERP_DocumentTitle";

        /// <summary>
        /// Гарантирует существование shared-параметра "Код 1C-ERP"
        /// и его привязку как параметра типа к нужным категориям.
        /// </summary>
        public static void EnsureErpCodeParameter(Document doc)
        {
            Application app = doc.Application;

            Definition def = GetOrCreateErpDefinition(app);

            // Категории, на которых нужен код 1C
            CategorySet catSet = app.Create.NewCategorySet();
            catSet.Insert(doc.Settings.Categories.get_Item(BuiltInCategory.OST_MechanicalEquipment));
            catSet.Insert(doc.Settings.Categories.get_Item(BuiltInCategory.OST_PipeFitting));
            // при необходимости добавь ещё категорий

            BindingMap map = doc.ParameterBindings;
            ElementBinding existing = map.get_Item(def) as ElementBinding;

            using (Transaction t = new Transaction(doc, "Bind ERP code parameter"))
            {
                t.Start();

                if (existing == null)
                {
                    TypeBinding binding = app.Create.NewTypeBinding(catSet);
                    map.Insert(def, binding, BuiltInParameterGroup.PG_DATA);
                }
                else
                {
                    foreach (Category c in catSet)
                    {
                        if (!existing.Categories.Contains(c))
                            existing.Categories.Insert(c);
                    }

                    map.ReInsert(def, existing, BuiltInParameterGroup.PG_DATA);
                }

                t.Commit();
            }
        }

        /// <summary>
        /// Находит или создаёт shared-определение параметра "Код 1C-ERP".
        /// Если файл общих параметров не задан, создаёт временный ERP-файл в ProgramData.
        /// </summary>
        private static Definition GetOrCreateErpDefinition(Application app)
        {
            string originalPath = app.SharedParametersFilename;

            bool useTempFile = string.IsNullOrEmpty(originalPath) || !File.Exists(originalPath);
            string tempFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "RevitErpIntegration");
            string tempFile = Path.Combine(tempFolder, "ERPSharedParameters.txt");

            if (useTempFile)
            {
                Directory.CreateDirectory(tempFolder);
                if (!File.Exists(tempFile))
                    File.WriteAllText(tempFile, string.Empty);

                app.SharedParametersFilename = tempFile;
            }

            DefinitionFile defFile = app.OpenSharedParameterFile();
            if (defFile == null)
                throw new InvalidOperationException("Не удалось открыть файл общих параметров ERP.");

            DefinitionGroup group = defFile.Groups.get_Item(GroupName) ?? defFile.Groups.Create(GroupName);

            Definition def = group.Definitions.get_Item(ErpCodeParamName);
            if (def == null)
            {
                ExternalDefinitionCreationOptions opt =
                    new ExternalDefinitionCreationOptions(ErpCodeParamName, ParameterType.Text);
                def = group.Definitions.Create(opt);
            }

            if (useTempFile)
            {
                // возвращаем старый путь, чтобы не ломать настройки пользователя
                app.SharedParametersFilename = originalPath;
            }

            return def;
        }
    }
}
