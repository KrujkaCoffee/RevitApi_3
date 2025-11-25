using System;
using System.IO;
using Autodesk.Revit.DB;
using Autodesk.Revit.ApplicationServices;

namespace RevitErpIntegration
{
    public static class ErpParameters
    {
        public const string ErpCodeParamName = "Код 1C-ERP";
        private const string GroupName = "ERP";

        public const string EndpointParamName = "ERP_Endpoint";
        public const string DocTitleParamName = "ERP_DocumentTitle";

        public static void EnsureErpCodeParameter(Document doc)
        {
            Application app = doc.Application;

            // 1. Получаем (или создаём) shared-определение параметра
            Definition def = GetOrCreateErpDefinition(app);

            // 2. Привязываем параметр к нужным категориям в текущем документе
            CategorySet catSet = app.Create.NewCategorySet();
            catSet.Insert(doc.Settings.Categories.get_Item(BuiltInCategory.OST_MechanicalEquipment));
            catSet.Insert(doc.Settings.Categories.get_Item(BuiltInCategory.OST_PipeFitting));
            // сюда можно добавить ещё категории по необходимости

            BindingMap map = doc.ParameterBindings;
            ElementBinding existingBinding = map.get_Item(def) as ElementBinding;

            using (Transaction t = new Transaction(doc, "Bind ERP code parameter"))
            {
                t.Start();

                if (existingBinding == null)
                {
                    // привязываем как параметр типа
                    TypeBinding binding = app.Create.NewTypeBinding(catSet);
                    map.Insert(def, binding, BuiltInParameterGroup.PG_DATA);
                }
                else
                {
                    // расширяем существующую привязку новыми категориями
                    foreach (Category c in catSet)
                        if (!existingBinding.Categories.Contains(c))
                            existingBinding.Categories.Insert(c);

                    map.ReInsert(def, existingBinding, BuiltInParameterGroup.PG_DATA);
                }

                t.Commit();
            }
        }

        /// <summary>
        /// Находит или создаёт shared-определение параметра "Код 1C-ERP".
        /// Работает бесшовно: при отсутствии файла общих параметров
        /// создаёт и временно подключает свой.
        /// </summary>
        private static Definition GetOrCreateErpDefinition(Application app)
        {
            string originalPath = app.SharedParametersFilename;

            // Если файл не задан или его нет - используем свой путь
            bool useTempFile = string.IsNullOrEmpty(originalPath) || !File.Exists(originalPath);
            string tempFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "RevitErpIntegration");
            string tempFile = Path.Combine(tempFolder, "ERPSharedParameters.txt");

            if (useTempFile)
            {
                Directory.CreateDirectory(tempFolder);
                if (!File.Exists(tempFile))
                {
                    // создаём пустой файл
                    File.WriteAllText(tempFile, string.Empty);
                }

                // временно подменяем SharedParametersFilename
                app.SharedParametersFilename = tempFile;
            }

            DefinitionFile defFile = app.OpenSharedParameterFile();
            if (defFile == null)
                throw new InvalidOperationException("Не удалось открыть файл общих параметров для ERP.");

            DefinitionGroup group = defFile.Groups.get_Item(GroupName) ?? defFile.Groups.Create(GroupName);

            Definition def = group.Definitions.get_Item(ErpCodeParamName);
            if (def == null)
            {
                ExternalDefinitionCreationOptions opt =
                    new ExternalDefinitionCreationOptions(ErpCodeParamName, ParameterType.Text);
                def = group.Definitions.Create(opt);
            }

            // Если мы подменяли файл — возвращаем пользователю его старый путь
            if (useTempFile)
            {
                app.SharedParametersFilename = originalPath;
            }

            return def;
        }

        public static void EnsureProjectParameters(Document doc)
        {
            // Можно по аналогии автоматически сделать shared-параметры
            // ERP_Endpoint и ERP_DocumentTitle, привязанные к категории
            // OST_ProjectInformation, если захочешь – допишем.
        }
    }
}
