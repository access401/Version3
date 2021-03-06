﻿// ---------------------------------------------------------------------------
//  Copyright (c) 2018, The .NET Foundation.
//  This software is released under the Apache License, Version 2.0.
//  The license and further copyright text can be found in the file LICENSE.md
//  at the root directory of the distribution.
// ---------------------------------------------------------------------------

using Chem4Word.Core;
using Chem4Word.Core.Helpers;
using Chem4Word.Core.UI.Forms;
using Chem4Word.Helpers;
using Chem4Word.Library;
using Chem4Word.Model.Converters.CML;
using Chem4Word.Telemetry;
using IChem4Word.Contracts;
using Microsoft.Office.Core;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Threading;
using System.Xml.Linq;
using Extensions = Microsoft.Office.Tools.Word.Extensions;
using OfficeTools = Microsoft.Office.Tools;
using Word = Microsoft.Office.Interop.Word;
using WordTools = Microsoft.Office.Tools.Word;

namespace Chem4Word
{
    public partial class Chem4WordV3
    {
        // Internal variables for class
        private static string _product = Assembly.GetExecutingAssembly().FullName.Split(',')[0];

        private static string _class = MethodBase.GetCurrentMethod().DeclaringType?.Name;

        public static CustomRibbon Ribbon;

        public int VersionsBehind = 0;
        public XDocument AllVersions;
        public XDocument ThisVersion;

        public bool EventsEnabled = true;

        public bool ChemistryAllowed = false;
        public string ChemistryProhibitedReason = "";
        private string _lastContentControlAdded = "";

        private bool _chemistrySelected = false;
        private bool _markAsChemistryHandled = false;
        private int _rightClickEvents = 0;

        public bool LibraryState = false;

        public C4wAddInInfo AddInInfo = new C4wAddInInfo();
        public Options SystemOptions = null;
        public TelemetryWriter Telemetry = new TelemetryWriter(true);

        public List<IChem4WordEditor> Editors;
        public List<IChem4WordRenderer> Renderers;
        public List<IChem4WordSearcher> Searchers;

        public Dictionary<string, int> LibraryNames = null;

        private static readonly string[] ContextMenusTargets = { "Text", "Table Text", "Spelling", "Grammar", "Grammar (2)", "Lists", "Table Lists" };
        private const string ContextMenuTag = "2829AECC-061C-4DC5-8CC0-CAEC821B9127";
        private const string ContextMenuText = "Convert to Chemistry";

        public int WordWidth
        {
            get
            {
                int width = 0;

                try
                {
                    CommandBar commandBar1 = Application.CommandBars["Ribbon"];
                    if (commandBar1 != null)
                    {
                        width = Math.Max(width, commandBar1.Width);
                    }
                    CommandBar commandBar2 = Application.CommandBars["Status Bar"];
                    if (commandBar2 != null)
                    {
                        width = Math.Max(width, commandBar2.Width);
                    }
                }
                catch
                {
                    //
                }

                try
                {
                    if (width == 0)
                    {
                        width = Screen.PrimaryScreen.Bounds.Width;
                    }
                }
                catch
                {
                    //
                }

                return width;
            }
        }

        public Point WordTopLeft
        {
            get
            {
                Point pp = new Point();

                try
                {
                    // Get position of Standard CommandBar (<ALT>+F)
                    CommandBar commandBar = Application.CommandBars["Standard"];
                    pp.X = commandBar.Left + commandBar.Height;
                    pp.Y = commandBar.Top + commandBar.Height;
                }
                catch
                {
                    //
                }
                return pp;
            }
        }

        public int WordVersion
        {
            get
            {
                int version = -1;

                try
                {
                    switch (Application.Version)
                    {
                        case "12.0":
                            version = 2007;
                            break;

                        case "14.0":
                            version = 2010;
                            break;

                        case "15.0":
                            version = 2013;
                            break;

                        case "16.0":
                            version = 2016;
                            break;

                        case "17.0":
                            version = 2019;
                            break;
                    }
                }
                catch
                {
                    //
                }

                return version;
            }
        }

        /// <summary>
        ///   Override the BeginInit method to load Chemistry Fonts.
        /// </summary>
        public override void BeginInit()
        {
            base.BeginInit();

            // Install 2 Chemisty fonts into current process.
            // Font name: Ms ChemSans, MS ChemSerif
            //Chem4Word.Font.ChemistryFontManager.AddChemistryFontToProcess();
        }

        private void C4WLiteAddIn_Startup(object sender, EventArgs e)
        {
            Debug.WriteLine("C4WLiteAddIn_Startup ...");
            string module = $"{_product}.{_class}.{MethodBase.GetCurrentMethod().Name}()";

            PerformStartUpActions();
        }

        private void C4WLiteAddIn_Shutdown(object sender, EventArgs e)
        {
            string module = $"{_product}.{_class}.{MethodBase.GetCurrentMethod().Name}()";
            Debug.WriteLine(module);

            PerformShutDownActions();
        }

        private void PerformStartUpActions()
        {
            string module = $"{_product}.{_class}.{MethodBase.GetCurrentMethod().Name}()";
            Debug.WriteLine(module);

            try
            {
                ServicePointManager.DefaultConnectionLimit = 100;
                ServicePointManager.UseNagleAlgorithm = false;
                ServicePointManager.Expect100Continue = false;

                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

                UpdateHelper.ReadThisVersion(Assembly.GetExecutingAssembly());

                Word.Application app = Application;

                // Hook in Global Application level events
                app.WindowBeforeDoubleClick += OnWindowBeforeDoubleClick;
                app.WindowSelectionChange += OnWindowSelectionChange;
                app.WindowActivate += OnWindowActivate;
                app.WindowBeforeRightClick += OnWindowBeforeRightClick;

                // Hook in Global Document Level Events
                app.DocumentOpen += OnDocumentOpen;
                app.DocumentChange += OnDocumentChange;
                app.DocumentBeforeClose += OnDocumentBeforeClose;
                app.DocumentBeforeSave += OnDocumentBeforeSave;
                ((Word.ApplicationEvents4_Event)Application).NewDocument += OnNewDocument;

                if (app.Documents.Count > 0)
                {
                    EnableDocumentEvents(app.Documents[1]);
                    if (app.Documents[1].CompatibilityMode >= (int)Word.WdCompatibilityMode.wdWord2010)
                    {
                        SetButtonStates(ButtonState.CanInsert);
                    }
                    else
                    {
                        SetButtonStates(ButtonState.NoDocument);
                    }
                }

                if (AddInInfo.DeploymentPath.ToLower().Contains("vso-ci"))
                {
                    StringBuilder sb = new StringBuilder();
                    sb.AppendLine($"Hey {Environment.UserName}");
                    sb.AppendLine("");
                    sb.AppendLine("You should not be running this build configuration");
                    sb.AppendLine("Please select Debug or Release build!");
                    UserInteractions.StopUser(sb.ToString());
                }

                // Changed to Lazy Loading
                //LoadOptions();

                // Set parameter mustBeSigned true if assemblies must be signed by us
                LoadPlugIns(false);
                ListPlugInsFound();

                ConfigWatcher cw = new ConfigWatcher(AddInInfo.ProductAppDataPath);

                // Changed to Lazy Loading
                //LoadNamesFromLibrary();

                // Deliberate crash to test Error Reporting
                //int ii = 2;
                //int dd = 0;
                //int bang = ii / dd;
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
                Debug.WriteLine(ex.StackTrace);
                new ReportError(Telemetry, WordTopLeft, module, ex).ShowDialog();
            }
        }

        public void LoadNamesFromLibrary()
        {
            string module = $"{_product}.{_class}.{MethodBase.GetCurrentMethod().Name}()";
            try
            {
                var lib = new Database.Library();
                LibraryNames = lib.GetLibraryNames();
            }
            catch (Exception ex)
            {
                Telemetry.Write(module, "Exception", ex.Message);
                Telemetry.Write(module, "Exception", ex.StackTrace);
                LibraryNames = null;
            }
        }

        public void LoadOptions()
        {
            string module = $"{_product}.{_class}.{MethodBase.GetCurrentMethod().Name}()";

            try
            {
                // Initiallize Telemetry with send permission
                Telemetry = new TelemetryWriter(true);

                // Read in options file
                string padPath = AddInInfo.ProductAppDataPath;
                string fileName = $"{AddInInfo.ProductName}.json";
                string optionsFile = Path.Combine(padPath, fileName);
                if (File.Exists(optionsFile))
                {
                    try
                    {
                        string json = File.ReadAllText(optionsFile);
                        SystemOptions = JsonConvert.DeserializeObject<Options>(json);
                        string temp = JsonConvert.SerializeObject(SystemOptions, Formatting.Indented);
                        if (!json.Equals(temp))
                        {
                            Telemetry.Write(module, "Information", "Patching Options file");
                            File.WriteAllText(optionsFile, temp);
                        }
                    }
                    catch (Exception e)
                    {
                        Telemetry.Write(module, "Exception", e.Message);
                        Telemetry.Write(module, "Exception", e.StackTrace);
                        SystemOptions = new Options();
                        SystemOptions.RestoreDefaults();
                    }
                }
                else
                {
                    SystemOptions = new Options();
                    SystemOptions.RestoreDefaults();
                    string temp = JsonConvert.SerializeObject(SystemOptions, Formatting.Indented);
                    // Check again before writing just in case two versions of word started at the same time
                    if (!File.Exists(optionsFile))
                    {
                        Telemetry.Write(module, "Information", "Writing initial Options file");
                        File.WriteAllText(optionsFile, temp);
                    }
                }

                string betaValue = ThisVersion.Root?.Element("IsBeta")?.Value;
                bool isBeta = betaValue != null && bool.Parse(betaValue);

                // Re-Initialize Telemetry with granted permissions
                Telemetry = new TelemetryWriter(isBeta || SystemOptions.TelemetryEnabled);

                try
                {
                    if (SystemOptions.SelectedEditorPlugIn.Equals(Constants.DefaultEditorPlugIn800))
                    {
                        var browser = new WebBrowser().Version;
                        if (browser.Major < Constants.ChemDoodleWeb800MinimumBrowserVersion)
                        {
                            SystemOptions.SelectedEditorPlugIn = Constants.DefaultEditorPlugIn702;
                            string temp = JsonConvert.SerializeObject(SystemOptions, Formatting.Indented);
                            Telemetry.Write(module, "Information", "IE 10+ not detected; Switching to ChemDoodle Web 7.0.2");
                            File.WriteAllText(optionsFile, temp);
                        }
                    }
                }
                catch
                {
                    //
                }
            }
            catch (Exception ex)
            {
                Telemetry.Write(module, "Exception", ex.Message);
                Telemetry.Write(module, "Exception", ex.StackTrace);
                SystemOptions = null;
            }
        }

        private void PerformShutDownActions()
        {
            string module = $"{_product}.{_class}.{MethodBase.GetCurrentMethod().Name}()";
            Debug.WriteLine(module);

            try
            {
                //RemoveRightClickButton(_targetedPopUps);

                if (Editors != null)
                {
                    for (int i = 0; i < Editors.Count; i++)
                    {
                        Editors[i].Telemetry = null;
                        Editors[i] = null;
                    }
                }

                if (Renderers != null)
                {
                    for (int i = 0; i < Renderers.Count; i++)
                    {
                        Renderers[i].Telemetry = null;
                        Renderers[i] = null;
                    }
                }

                if (Searchers != null)
                {
                    for (int i = 0; i < Searchers.Count; i++)
                    {
                        Searchers[i].Telemetry = null;
                        Searchers[i] = null;
                    }
                }

                GC.WaitForPendingFinalizers();
                GC.Collect();
            }
            finally
            {
                // Fix reported issue with Windows 8 and WPF See
                // https://social.msdn.microsoft.com/Forums/office/en-US/bb990ddb-ecde-4161-8915-e66e913e3a3b/invalidoperationexception-localdatastoreslot-storage-has-been-freed?forum=exceldev
                // I saw this on Server 2008 R2 which is very closely related to Windows 8
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        }

        private void ListPlugInsFound()
        {
            string module = $"{_product}.{_class}.{MethodBase.GetCurrentMethod().Name}()";
            int count = 0;

            //Telemetry.Write(module, "Debug", $"Found {Editors.Count} Editor PlugIns");
            Debug.WriteLine($"Found {Editors.Count} Editor PlugIns");
            foreach (IChem4WordEditor editor in Editors)
            {
                count++;
                //Telemetry.Write(module, "Debug", editor.Name);
                Debug.WriteLine($"{editor.Name}");
            }

            //Telemetry.Write(module, "Debug", $"Found {Renderers.Count} Renderer PlugIns");
            Debug.WriteLine($"Found {Renderers.Count} Renderer PlugIns");
            foreach (IChem4WordRenderer renderer in Renderers)
            {
                count++;
                //Telemetry.Write(module, "Debug", renderer.Name);
                Debug.WriteLine($"{renderer.Name}");
            }

            //Telemetry.Write(module, "Debug", $"Found {Searchers.Count} Searchers PlugIns");
            Debug.WriteLine($"Found {Searchers.Count} Searcher PlugIns");
            foreach (IChem4WordSearcher searcher in Searchers)
            {
                count++;
                //Telemetry.Write(module, "Debug", searcher.Name);
                Debug.WriteLine($"{searcher.Name}");
            }
        }

        private void LoadPlugIns(bool mustBeSigned)
        {
            string module = $"{_product}.{_class}.{MethodBase.GetCurrentMethod().Name}()";
            // http://www.codeproject.com/Articles/453778/Loading-Assemblies-from-Anywhere-into-a-New-AppDom

            Stopwatch sw = new Stopwatch();
            sw.Start();

            string plugInPath = Path.Combine(AddInInfo.DeploymentPath, "PlugIns");
            //Telemetry.Write(module, "Debug", $"Looking for Plug-Ins in folder {plugInPath}");

            string[] files = null;
            int filesFound = 0;

            if (Directory.Exists(plugInPath))
            {
                files = Directory.GetFiles(plugInPath, "Chem4Word*.dll");
                filesFound = files.Length;
            }

            List<string> plugInsFound = new List<string>();

            #region Find Our PlugIns

            foreach (string file in files)
            {
                var manager = new AssemblyReflectionManager();

                try
                {
                    var success = manager.LoadAssembly(file, "Chem4WordTempDomain");
                    if (success)
                    {
                        #region Find Our Interfaces

                        var results = manager.Reflect(file, a =>
                        {
                            var names = new List<string>();

                            #region Get Code Signing Certificate details

                            string signedBy = "";

                            Module mod = a.GetModules().First();
                            var certificate = mod.GetSignerCertificate();
                            if (certificate != null)
                            {
                                // E=developer@chem4word.co.uk, CN="Open Source Developer, Mike Williams", O=Open Source Developer, C=GB
                                Debug.WriteLine(certificate.Subject);
                                signedBy = certificate.Subject;
                                // CN=Certum Code Signing CA SHA2, OU=Certum Certification Authority, O=Unizeto Technologies S.A., C=PL
                                Debug.WriteLine(certificate.Issuer);
                            }

                            #endregion Get Code Signing Certificate details

                            var types = a.GetTypes();
                            foreach (var t in types)
                            {
                                if (t.IsClass && t.IsPublic && !t.IsAbstract)
                                {
                                    var ifaces = t.GetInterfaces();
                                    foreach (var iface in ifaces)
                                    {
                                        if (iface.FullName.StartsWith("IChem4Word.Contracts"))
                                        {
                                            string[] parts = a.FullName.Split(',');
                                            FileInfo fi = new FileInfo(t.Module.FullyQualifiedName);
                                            names.Add($"{parts[0]}|{iface.FullName}|{fi.Name}|{signedBy}");
                                        }
                                    }
                                }
                            }

                            return names;
                        });

                        #endregion Find Our Interfaces

                        manager.UnloadAssembly(file);

                        plugInsFound.AddRange(results);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex.Message);
                }
            }

            #endregion Find Our PlugIns

            if (plugInsFound.Count == 0)
            {
                UserInteractions.StopUser("No Plug-Ins Loaded");
            }

            Editors = new List<IChem4WordEditor>();
            Renderers = new List<IChem4WordRenderer>();
            Searchers = new List<IChem4WordSearcher>();

            Type editorType = typeof(IChem4WordEditor);
            Type rendererType = typeof(IChem4WordRenderer);
            Type searcherType = typeof(IChem4WordSearcher);

            foreach (string plugIn in plugInsFound)
            {
                string[] parts = plugIn.Split('|');
                Debug.WriteLine(
                    $"Loading PlugIn {parts[0]} with Interface {parts[1]} from file {parts[2]} signed by {parts[3]}");

                bool allowed = true;
                if (mustBeSigned)
                {
                    // Is it signed by us?
                    allowed = parts[3].Contains("admin@chem4word.co.uk");
                }

                if (allowed)
                {
                    #region Find Source File

                    string sourceFile = "";
                    foreach (string file in files)
                    {
                        if (file.EndsWith(parts[2]))
                        {
                            sourceFile = file;
                            break;
                        }
                    }

                    #endregion Find Source File

                    if (!string.IsNullOrEmpty(sourceFile))
                    {
                        #region Load Editor(s)

                        if (parts[1].Contains("IChem4WordEditor"))
                        {
                            Assembly asm = Assembly.LoadFile(sourceFile);
                            Type[] types = asm.GetTypes();
                            foreach (Type type in types)
                            {
                                if (type.GetInterface(editorType.FullName) != null)
                                {
                                    IChem4WordEditor plugin = (IChem4WordEditor)Activator.CreateInstance(type);
                                    Editors.Add(plugin);
                                    break;
                                }
                            }
                        }

                        #endregion Load Editor(s)

                        #region Load Renderer(s)

                        if (parts[1].Contains("IChem4WordRenderer"))
                        {
                            Assembly asm = Assembly.LoadFile(sourceFile);
                            Type[] types = asm.GetTypes();
                            foreach (Type type in types)
                            {
                                if (type.GetInterface(rendererType.FullName) != null)
                                {
                                    IChem4WordRenderer plugin = (IChem4WordRenderer)Activator.CreateInstance(type);
                                    Renderers.Add(plugin);
                                    break;
                                }
                            }
                        }

                        #endregion Load Renderer(s)

                        #region Load Searcher(s)

                        if (parts[1].Contains("IChem4WordSearcher"))
                        {
                            Assembly asm = Assembly.LoadFile(sourceFile);
                            Type[] types = asm.GetTypes();
                            foreach (Type type in types)
                            {
                                if (type.GetInterface(searcherType.FullName) != null)
                                {
                                    IChem4WordSearcher plugin = (IChem4WordSearcher)Activator.CreateInstance(type);
                                    Searchers.Add(plugin);
                                    break;
                                }
                            }
                        }

                        #endregion Load Searcher(s)
                    }
                }
            }

            sw.Stop();
            Debug.WriteLine($"Examining {filesFound} files took {sw.ElapsedMilliseconds.ToString("#,000")}ms");
            //Telemetry.Write(module, "Verbose", $"Examining {filesFound} files took {sw.ElapsedMilliseconds.ToString("#,000")}ms");
        }

        public IChem4WordEditor GetEditorPlugIn(string name)
        {
            string module = $"{_product}.{_class}.{MethodBase.GetCurrentMethod().Name}()";

            IChem4WordEditor plugin = null;

            if (!string.IsNullOrEmpty(name))
            {
                foreach (IChem4WordEditor ice in Editors)
                {
                    if (ice.Name.Equals(name))
                    {
                        plugin = ice;
                        plugin.Telemetry = Telemetry;
                        plugin.ProductAppDataPath = AddInInfo.ProductAppDataPath;
                        plugin.TopLeft = WordTopLeft;
                        break;
                    }
                }
            }

            return plugin;
        }

        public IChem4WordRenderer GetRendererPlugIn(string name)
        {
            string module = $"{_product}.{_class}.{MethodBase.GetCurrentMethod().Name}()";

            IChem4WordRenderer plugin = null;

            if (!string.IsNullOrEmpty(name))
            {
                foreach (IChem4WordRenderer ice in Renderers)
                {
                    if (ice.Name.Equals(name))
                    {
                        plugin = ice;
                        plugin.Telemetry = Telemetry;
                        plugin.ProductAppDataPath = AddInInfo.ProductAppDataPath;
                        plugin.TopLeft = WordTopLeft;
                        break;
                    }
                }
            }

            return plugin;
        }

        public IChem4WordSearcher GetSearcherPlugIn(string name)
        {
            string module = $"{_product}.{_class}.{MethodBase.GetCurrentMethod().Name}()";

            IChem4WordSearcher plugin = null;

            if (!string.IsNullOrEmpty(name))
            {
                foreach (IChem4WordSearcher ice in Searchers)
                {
                    if (ice.Name.Equals(name))
                    {
                        plugin = ice;
                        plugin.Telemetry = Telemetry;
                        plugin.ProductAppDataPath = AddInInfo.ProductAppDataPath;
                        plugin.TopLeft = WordTopLeft;
                        break;
                    }
                }
            }

            return plugin;
        }

        public void EnableDocumentEvents(Word.Document doc)
        {
            string module = $"{_product}.{_class}.{MethodBase.GetCurrentMethod().Name}()";

            Debug.WriteLine("EnableDocumentEvents()");

            // Get reference to active document
            WordTools.Document wdoc = Extensions.DocumentExtensions.GetVstoObject(doc, Globals.Factory);

            // Hook in Content Control Events
            // See: https://msdn.microsoft.com/en-us/library/Microsoft.Office.Interop.Word.DocumentEvents2_methods%28v=office.14%29.aspx
            // See: https://msdn.microsoft.com/en-us/library/microsoft.office.interop.word.documentevents2_event_methods%28v=office.14%29.aspx

            // Remember to add corresponding code in DisableDocumentEvents()

            // ContentControlOnEnter Event Handler
            try
            {
                wdoc.ContentControlOnEnter -= OnContentControlOnEnter;
                wdoc.ContentControlOnEnter += OnContentControlOnEnter;
            }
            catch (Exception e)
            {
                Debug.WriteLine(e.Message);
            }
            // ContentControlOnExit Event Handler
            try
            {
                wdoc.ContentControlOnExit -= OnContentControlOnExit;
                wdoc.ContentControlOnExit += OnContentControlOnExit;
            }
            catch (Exception e)
            {
                Debug.WriteLine(e.Message);
            }
            // ContentControlBeforeDelete Event Handler
            try
            {
                wdoc.ContentControlBeforeDelete -= OnContentControlBeforeDelete;
                wdoc.ContentControlBeforeDelete += OnContentControlBeforeDelete;
            }
            catch (Exception e)
            {
                Debug.WriteLine(e.Message);
            }
            // ContentControlAfterAdd Event Handler
            try
            {
                wdoc.ContentControlAfterAdd -= OnContentControlAfterAdd;
                wdoc.ContentControlAfterAdd += OnContentControlAfterAdd;
            }
            catch (Exception e)
            {
                Debug.WriteLine(e.Message);
            }

            EventsEnabled = true;
        }

        public void DisableDocumentEvents(Word.Document doc)
        {
            string module = $"{_product}.{_class}.{MethodBase.GetCurrentMethod().Name}()";

            Debug.WriteLine("DisableDocumentEvents()");

            EventsEnabled = false;

            // Get reference to active document
            WordTools.Document wdoc = Extensions.DocumentExtensions.GetVstoObject(doc, Globals.Factory);

            // Hook out Content Control Events
            // See: https://msdn.microsoft.com/en-us/library/Microsoft.Office.Interop.Word.DocumentEvents2_methods%28v=office.14%29.aspx
            // See: https://msdn.microsoft.com/en-us/library/microsoft.office.interop.word.documentevents2_event_methods%28v=office.14%29.aspx

            // Remember to add corresponding code in EnableDocumentEvents()

            // ContentControlOnEnter Event Handler
            try
            {
                wdoc.ContentControlOnEnter -= OnContentControlOnEnter;
            }
            catch (Exception e)
            {
                Debug.WriteLine(e.Message);
            }
            // ContentControlOnExit Event Handler
            try
            {
                wdoc.ContentControlOnExit -= OnContentControlOnExit;
            }
            catch (Exception e)
            {
                Debug.WriteLine(e.Message);
            }
            // ContentControlBeforeDelete Event Handler
            try
            {
                wdoc.ContentControlBeforeDelete -= OnContentControlBeforeDelete;
            }
            catch (Exception e)
            {
                Debug.WriteLine(e.Message);
            }
            // ContentControlAfterAdd Event Handler
            try
            {
                wdoc.ContentControlAfterAdd -= OnContentControlAfterAdd;
            }
            catch (Exception e)
            {
                Debug.WriteLine(e.Message);
            }
        }

        private void SetButtonStates(ButtonState state)
        {
            string module = $"{_product}.{_class}.{MethodBase.GetCurrentMethod().Name}()";

            if (Ribbon != null)
            {
                Debug.WriteLine(state.ToString());
                // Not needed, just here for completeness
                Ribbon.ChangeOptions.Enabled = true;
                Ribbon.HelpMenu.Enabled = true;

                switch (state)
                {
                    case ButtonState.NoDocument:
                        Ribbon.EditStructure.Enabled = false;
                        Ribbon.EditStructure.Label = "Draw";
                        Ribbon.EditLabels.Enabled = false;
                        Ribbon.ViewCml.Enabled = false;
                        Ribbon.ImportFromFile.Enabled = false;
                        Ribbon.ExportToFile.Enabled = false;
                        Ribbon.ShowAsMenu.Enabled = false;
                        Ribbon.ShowNavigator.Enabled = false;
                        Ribbon.ShowLibrary.Enabled = false;
                        Ribbon.WebSearchMenu.Enabled = false;
                        Ribbon.SaveToLibrary.Enabled = false;
                        Ribbon.ArrangeMolecules.Enabled = false;
                        Ribbon.ButtonsDisabled.Enabled = true;
                        break;

                    case ButtonState.CanEdit:
                        Ribbon.EditStructure.Enabled = true;
                        Ribbon.EditStructure.Label = "Edit";
                        Ribbon.EditLabels.Enabled = true;
                        Ribbon.ViewCml.Enabled = true;
                        Ribbon.ImportFromFile.Enabled = false;
                        Ribbon.ExportToFile.Enabled = true;
                        Ribbon.ShowAsMenu.Enabled = true;
                        Ribbon.ShowNavigator.Enabled = true;
                        Ribbon.ShowLibrary.Enabled = true;
                        Ribbon.WebSearchMenu.Enabled = false;
                        Ribbon.SaveToLibrary.Enabled = true;
                        Ribbon.ArrangeMolecules.Enabled = true;
                        Ribbon.ButtonsDisabled.Enabled = false;
                        break;

                    case ButtonState.CanInsert:
                        Ribbon.EditStructure.Enabled = true;
                        Ribbon.EditStructure.Label = "Draw";
                        Ribbon.EditLabels.Enabled = false;
                        Ribbon.ViewCml.Enabled = false;
                        Ribbon.ImportFromFile.Enabled = true;
                        Ribbon.ExportToFile.Enabled = false;
                        Ribbon.ShowAsMenu.Enabled = false;
                        Ribbon.ShowNavigator.Enabled = true;
                        Ribbon.ShowLibrary.Enabled = true;
                        Ribbon.WebSearchMenu.Enabled = true;
                        Ribbon.SaveToLibrary.Enabled = false;
                        Ribbon.ArrangeMolecules.Enabled = false;
                        Ribbon.ButtonsDisabled.Enabled = false;
                        break;
                }

                SetUpdateButtonState();
            }
        }

        public void SetUpdateButtonState()
        {
            if (VersionsBehind == 0)
            {
                Ribbon.Update.Visible = false;
                Ribbon.Update.Image = Properties.Resources.Shield_Good;
            }
            else
            {
                Ribbon.Update.Visible = true;
                if (VersionsBehind < 3)
                {
                    Ribbon.Update.Image = Properties.Resources.Shield_Warning;
                    Ribbon.Update.Label = "Update Advised";
                    Ribbon.Update.ScreenTip = "Please update";
                    Ribbon.Update.SuperTip = $"You are {VersionsBehind} versions behind";
                }
                else
                {
                    Ribbon.Update.Image = Properties.Resources.Shield_Danger;
                    Ribbon.Update.Label = "Update Essential";
                    Ribbon.Update.ScreenTip = "Please update";
                    Ribbon.Update.SuperTip = $"You are {VersionsBehind} versions behind";
                }
            }
        }

        public void SelectChemistry(Word.Selection sel)
        {
            string module = $"{_product}.{_class}.{MethodBase.GetCurrentMethod().Name}()";

            EventsEnabled = false;

            bool chemistrySelected = false;
            ChemistryProhibitedReason = "";

            try
            {
                Word.Document doc = sel.Application.ActiveDocument;
                int ccCount = sel.ContentControls.Count;
                //Debug.WriteLine($"SelectChemistry() Document: {doc.Name} Selection from {sel.Range.Start} to {sel.Range.End}");
                //Debug.WriteLine($"SelectChemistry() Document: {doc.Name} Selection has {sel.ContentControls.Count} CCs");

                foreach (Word.ContentControl cc in doc.ContentControls)
                {
                    //Debug.WriteLine($"CC '{cc.Tag}' Range from {cc.Range.Start} to {cc.Range.End}");

                    // Already Selected
                    if (sel.Range.Start == cc.Range.Start -1 && sel.Range.End == cc.Range.End + 1)
                    {
                        if (cc.Title != null && cc.Title.Equals(Constants.ContentControlTitle))
                        {
                            //Debug.WriteLine($"  Existing Selected Chemistry");
                            chemistrySelected = true;
                        }
                        break;
                    }

                    // Inside CC
                    if (sel.Range.Start >= cc.Range.Start && sel.Range.End <= cc.Range.End)
                    {
                        if (cc.Title != null && cc.Title.Equals(Constants.ContentControlTitle))
                        {
                            //Debug.WriteLine($"  SelectChemistry() Selecting CC at {cc.Range.Start - 1} to {cc.Range.End + 1}");
                            doc.Application.Selection.SetRange(cc.Range.Start - 1, cc.Range.End + 1);
#if DEBUG
                            //Word.Selection s = doc.Application.Selection;
                            //Debug.WriteLine($"  SelectChemistry() New Selected Range is {s.Range.Start - 1} to {s.Range.End + 1}");
#endif
                            chemistrySelected = true;
                        }
                        break;
                    }
                }

                if (chemistrySelected)
                {
                    Ribbon.ActivateChemistryTab();
                    SetButtonStates(ButtonState.CanEdit);
                }
                else
                {
                    if (ccCount == 0)
                    {
                        SetButtonStates(ButtonState.CanInsert);
                    }
                    else
                    {
                        SetButtonStates(ButtonState.NoDocument);
                        ChemistryProhibitedReason = "more than a single content control selected";
                    }
                }

                _chemistrySelected = chemistrySelected;
            }
            catch (Exception e)
            {
                Debug.WriteLine(e.Message);
            }
            finally
            {
                EventsEnabled = true;
            }
        }

        #region Right Click

        #region Events

        private void OnWindowBeforeRightClick(Word.Selection sel, ref bool cancel)
        {
            string module = $"{_product}.{_class}.{MethodBase.GetCurrentMethod().Name}()";
            try
            {
                EvaluateChemistryAllowed();
                if (ChemistryAllowed)
                {
                    if (sel.Start != sel.End)
                    {
                        HandleRightClick(sel);
                    }
                }
            }
            catch (Exception ex)
            {
                if (SystemOptions == null)
                {
                    LoadOptions();
                }

                new ReportError(Telemetry, WordTopLeft, module, ex).ShowDialog();
                UpdateHelper.ClearSettings();
                UpdateHelper.CheckForUpdates(SystemOptions.AutoUpdateFrequency);
            }
        }

        private void OnCommandBarButtonClick(CommandBarButton ctrl, ref bool cancelDefault)
        {
            string module = $"{_product}.{_class}.{MethodBase.GetCurrentMethod().Name}()";
            try
            {
                if (SystemOptions == null)
                {
                    LoadOptions();
                }

                _rightClickEvents++;
                Debug.WriteLine($"{module} Event#{_rightClickEvents} Handled: {_markAsChemistryHandled}");

                ClearChemistryContextMenus();
                if (!_markAsChemistryHandled)
                {
                    Debug.WriteLine($"Convert '{ctrl.Tag}' to Chemistry");
                    TargetWord tw = JsonConvert.DeserializeObject<TargetWord>(ctrl.Tag);

                    var lib = new Database.Library();
                    string cml = lib.GetChemistryByID(tw.ChemistryId);

                    if (cml == null)
                    {
                        UserInteractions.WarnUser($"No match for '{tw.ChemicalName}' was found in your library");
                    }
                    else
                    {
                        Word.Application app = Application;
                        Word.Document doc = app.ActiveDocument;

                        // Generate new CustomXmlPartGuid
                        CMLConverter converter = new CMLConverter();
                        var model = converter.Import(cml);
                        model.CustomXmlPartGuid = Guid.NewGuid().ToString("N");
                        cml = converter.Export(model);

                        #region Find Id of name

                        string tagPrefix = "";
                        foreach (var mol in model.Molecules)
                        {
                            foreach (var name in mol.ChemicalNames)
                            {
                                if (tw.ChemicalName.ToLower().Equals(name.Name.ToLower()))
                                {
                                    tagPrefix = name.Id;
                                    break;
                                }
                            }

                            if (!string.IsNullOrEmpty(tagPrefix))
                            {
                                break;
                            }
                        }

                        if (string.IsNullOrEmpty(tagPrefix))
                        {
                            tagPrefix = "c0";
                        }

                        #endregion Find Id of name

                        // Test phrases (ensure benzene is in your library)
                        // This is benzene, this is not.
                        // This is benzene. This is not.

                        Word.ContentControl cc = null;
                        bool previousState = app.Options.SmartCutPaste;

                        try
                        {
                            app.ScreenUpdating = false;
                            DisableDocumentEvents(doc);

                            //Debug.WriteLine($"'{tw.ChemicalName}' {tw.Start} > {tw.End}");

                            app.Options.SmartCutPaste = false;
                            int insertionPoint = tw.Start;
                            doc.Range(tw.Start, tw.Start + tw.ChemicalName.Length).Delete();

                            app.Selection.SetRange(insertionPoint, insertionPoint);

                            string tag = $"{tagPrefix}:{model.CustomXmlPartGuid}";
                            cc = ChemistryHelper.Insert1DChemistry(doc, tw.ChemicalName, true, tag);

                            Telemetry.Write(module, "Information", $"Inserted 1D version of {tw.ChemicalName} from library");
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine(e);
                            throw;
                        }
                        finally
                        {
                            EnableDocumentEvents(doc);
                            app.ScreenUpdating = true;
                            app.Options.SmartCutPaste = previousState;
                        }

                        if (cc != null)
                        {
                            doc.CustomXMLParts.Add(cml);
                            app.Selection.SetRange(cc.Range.Start, cc.Range.End);
                        }
                    }

                    _markAsChemistryHandled = true;
                }
            }
            catch (Exception ex)
            {
                if (SystemOptions == null)
                {
                    LoadOptions();
                }

                new ReportError(Telemetry, WordTopLeft, module, ex).ShowDialog();
                UpdateHelper.ClearSettings();
                UpdateHelper.CheckForUpdates(SystemOptions.AutoUpdateFrequency);
            }
        }

        #endregion Events

        #region Methods

        private void HandleRightClick(Word.Selection sel)
        {
            string module = $"{_product}.{_class}.{MethodBase.GetCurrentMethod().Name}()";

            _markAsChemistryHandled = false;
            _rightClickEvents = 0;

            List<TargetWord> selectedWords = new List<TargetWord>();

            try
            {
                if (LibraryNames == null)
                {
                    LoadNamesFromLibrary();
                }
                if (LibraryNames != null && LibraryNames.Any())
                {
                    // Limit to selections which have less than 5 sentences
                    if (sel.Sentences.Count <= 5)
                    {
                        Word.Document doc = Application.ActiveDocument;
                        if (doc != null)
                        {
                            int last = doc.Range().End;
                            // Handling the selected text sentence by sentence should make us immune to return character sizing.
                            for (int i = 1; i <= sel.Sentences.Count; i++)
                            {
                                var sentence = sel.Sentences[i];
                                int start = Math.Max(sentence.Start, sel.Start);
                                start = Math.Max(0, start);
                                int end = Math.Min(sel.End, sentence.End);
                                end = Math.Min(end, last);
                                if (start < end)
                                {
                                    var range = doc.Range(start, end);
                                    //Exclude any ranges which contain content controls
                                    if (range.ContentControls.Count == 0)
                                    {
                                        string sentenceText = range.Text;
                                        //Debug.WriteLine($"Sentences[{i}] --> {sentenceText}");
                                        if (!string.IsNullOrEmpty(sentenceText))
                                        {
                                            foreach (var kvp in LibraryNames)
                                            {
                                                int idx = sentenceText.IndexOf(kvp.Key, StringComparison.InvariantCultureIgnoreCase);
                                                if (idx > 0)
                                                {
                                                    selectedWords.Add(new TargetWord
                                                    {
                                                        ChemicalName = kvp.Key,
                                                        Start = start + idx,
                                                        ChemistryId = kvp.Value,
                                                        End = start + idx + kvp.Key.Length
                                                    });
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (COMException cex)
            {
                string comCode = HexErrorCode(cex.ErrorCode);
                switch (comCode)
                {
                    case "0x800A1200":
                        ChemistryAllowed = false;
                        ChemistryProhibitedReason = "can't create a selection object";
                        break;

                    default:
                        // Keep exception hidden from end user.
                        Telemetry.Write(module, "Exception", $"ErrorCode: {comCode}");
                        Telemetry.Write(module, "Exception", cex.Message);
                        Telemetry.Write(module, "Exception", cex.ToString());
                        break;
                }
            }
            catch (Exception ex)
            {
                // Keep exception hidden from end user.
                Telemetry.Write(module, "Exception", ex.Message);
                Telemetry.Write(module, "Exception", ex.ToString());
            }

            if (selectedWords.Count > 0)
            {
                AddChemistryMenuPopup(selectedWords);
            }
        }

        #endregion Methods

        private void AddChemistryMenuPopup(List<TargetWord> selectedWords)
        {
            ClearChemistryContextMenus();

            WordTools.Document doc = Extensions.DocumentExtensions.GetVstoObject(Application.ActiveDocument, Globals.Factory);
            Application.CustomizationContext = doc.AttachedTemplate;

            foreach (string contextMenuName in ContextMenusTargets)
            {
                CommandBar contextMenu = Application.CommandBars[contextMenuName];
                if (contextMenu != null)
                {
                    CommandBarPopup popupControl = (CommandBarPopup)contextMenu.Controls.Add(
                                                                             MsoControlType.msoControlPopup,
                                                                             Type.Missing, Type.Missing, Type.Missing, true);
                    if (popupControl != null)
                    {
                        popupControl.Caption = ContextMenuText;
                        popupControl.Tag = ContextMenuTag;
                        foreach (var word in selectedWords)
                        {
                            CommandBarButton button = (CommandBarButton)popupControl.Controls.Add(
                                                                            MsoControlType.msoControlButton,
                                                                            Type.Missing, Type.Missing, Type.Missing, true);
                            if (button != null)
                            {
                                button.Caption = word.ChemicalName;
                                button.Tag = JsonConvert.SerializeObject(word);
                                button.FaceId = 1241;
                                button.Click += OnCommandBarButtonClick;
                            }
                        }
                    }
                }
            }

            ((Word.Template)doc.AttachedTemplate).Saved = true;
        }

        private void ClearChemistryContextMenus()
        {
            WordTools.Document doc = Extensions.DocumentExtensions.GetVstoObject(Application.ActiveDocument, Globals.Factory);
            Application.CustomizationContext = doc.AttachedTemplate;

            foreach (string contextMenuName in ContextMenusTargets)
            {
                CommandBar contextMenu = Application.CommandBars[contextMenuName];
                if (contextMenu != null)
                {
                    CommandBarPopup popupControl = (CommandBarPopup)contextMenu.FindControl(
                                                                         MsoControlType.msoControlPopup, Type.Missing,
                                                                         ContextMenuTag, true, true);
                    if (popupControl != null)
                    {
                        popupControl.Delete(true);
                    }
                }
            }

            ((Word.Template)doc.AttachedTemplate).Saved = true;
        }

        #endregion Right Click

        #region Document Events

        private void OnNewDocument(Word.Document doc)
        {
            string module = $"{_product}.{_class}.{MethodBase.GetCurrentMethod().Name}()";

            try
            {
                Debug.WriteLine($"{module.Replace("()", $"({doc.Name})")}");
            }
            catch (Exception ex)
            {
                if (SystemOptions == null)
                {
                    LoadOptions();
                }

                new ReportError(Telemetry, WordTopLeft, module, ex).ShowDialog();
                UpdateHelper.ClearSettings();
                UpdateHelper.CheckForUpdates(SystemOptions.AutoUpdateFrequency);
            }
        }

        private void OnDocumentChange()
        {
            string module = $"{_product}.{_class}.{MethodBase.GetCurrentMethod().Name}()";

            try
            {
                if (Application.Documents.Count > 0)
                {
                    Word.Document doc = null;
                    try
                    {
                        doc = Application.ActiveDocument;
                    }
                    catch (Exception ex1)
                    {
                        // This only happens when document is in protected mode
                        Debug.WriteLine($"Module: {module}; Exception: {ex1.Message}");
                        //Telemetry.Write(module, "Information", $"Exception: {ex1.Message}");
                    }

                    if (doc != null)
                    {
                        bool docxMode = doc.CompatibilityMode >= (int)Word.WdCompatibilityMode.wdWord2010;

                        Debug.WriteLine($"{module.Replace("()", $"({doc.Name})")}");

                        // Call disable first to ensure events not registered multiple times
                        DisableDocumentEvents(doc);

                        if (Ribbon != null)
                        {
                            Ribbon.ShowNavigator.Checked = false;
                            Ribbon.ShowLibrary.Checked = LibraryState;
                            Ribbon.ShowLibrary.Label = Ribbon.ShowLibrary.Checked ? "Close" : "Open ";
                        }

                        DialogResult answer = Upgrader.UpgradeIsRequired(doc);
                        switch (answer)
                        {
                            case DialogResult.Yes:
                                if (SystemOptions == null)
                                {
                                    LoadOptions();
                                }

                                Upgrader.DoUpgrade(doc);
                                break;

                            case DialogResult.No:
                                Telemetry.Write(module, "Information", "User chose not to upgrade");
                                break;

                            case DialogResult.Cancel:
                                // Returns Cancel if nothing to do
                                break;
                        }

                        #region Handle Navigator Task Panes

                        try
                        {
                            foreach (var taskPane in CustomTaskPanes)
                            {
                                if (taskPane.Window != null)
                                {
                                    string taskdoc = ((Word.Window)taskPane.Window).Document.Name;
                                    if (doc.Name.Equals(taskdoc))
                                    {
                                        if (taskPane.Title.Equals(Constants.NavigatorTaskPaneTitle))
                                        {
                                            //Debug.WriteLine($"Found Navigator Task Pane. Visible: {taskPane.Visible}");
                                            if (Ribbon != null)
                                            {
                                                Ribbon.ShowNavigator.Checked = taskPane.Visible;
                                            }
                                            break;
                                        }
                                    }
                                }
                            }
                        }
                        catch
                        {
                            // Do Nothing
                        }

                        #endregion Handle Navigator Task Panes

                        #region Handle Library Task Panes

                        try
                        {
                            bool libraryFound = false;

                            foreach (var taskPane in CustomTaskPanes)
                            {
                                if (taskPane.Window != null)
                                {
                                    string taskdoc = ((Word.Window)taskPane.Window).Document.Name;
                                    if (doc.Name.Equals(taskdoc))
                                    {
                                        if (taskPane.Title.Equals(Constants.LibraryTaskPaneTitle))
                                        {
                                            //Debug.WriteLine($"Found Library Task Pane. Visible: {taskPane.Visible}");
                                            if (Ribbon != null)
                                            {
                                                if (!docxMode)
                                                {
                                                    Ribbon.ShowLibrary.Checked = false;
                                                }
                                                taskPane.Visible = Ribbon.ShowLibrary.Checked;
                                                Ribbon.ShowLibrary.Label = Ribbon.ShowLibrary.Checked ? "Close" : "Open";
                                            }
                                            libraryFound = true;
                                            break;
                                        }
                                    }
                                }
                            }

                            if (!libraryFound)
                            {
                                if (Ribbon != null && Ribbon.ShowLibrary.Checked)
                                {
                                    if (docxMode)
                                    {
                                        OfficeTools.CustomTaskPane custTaskPane =
                                            CustomTaskPanes.Add(new LibraryHost(),
                                                Constants.LibraryTaskPaneTitle, Application.ActiveWindow);
                                        // Opposite side to Navigator's default placement
                                        custTaskPane.DockPosition = MsoCTPDockPosition.msoCTPDockPositionLeft;
                                        custTaskPane.Width = WordWidth / 4;
                                        custTaskPane.VisibleChanged += Ribbon.OnLibraryPaneVisibleChanged;
                                        custTaskPane.Visible = true;
                                        (custTaskPane.Control as LibraryHost)?.Refresh();
                                    }
                                }
                            }
                        }
                        catch
                        {
                            // Do Nothing
                        }

                        #endregion Handle Library Task Panes

                        EnableDocumentEvents(doc);

                        if (docxMode)
                        {
                            SetButtonStates(ButtonState.NoDocument);
                            SelectChemistry(doc.Application.Selection);
                            EvaluateChemistryAllowed();
                        }
                        else
                        {
                            SetButtonStates(ButtonState.NoDocument);
                        }

                    }
                }
            }
            catch (Exception ex)
            {
                if (SystemOptions == null)
                {
                    LoadOptions();
                }

                new ReportError(Telemetry, WordTopLeft, module, ex).ShowDialog();
                UpdateHelper.ClearSettings();
                UpdateHelper.CheckForUpdates(SystemOptions.AutoUpdateFrequency);
            }
        }

        private void OnDocumentOpen(Word.Document doc)
        {
            string module = $"{_product}.{_class}.{MethodBase.GetCurrentMethod().Name}()";

            try
            {
                Debug.WriteLine($"{module.Replace("()", $"({doc.Name})")}");
                if (SystemOptions == null)
                {
                    LoadOptions();
                }
            }
            catch (Exception ex)
            {
                if (SystemOptions == null)
                {
                    LoadOptions();
                }

                new ReportError(Telemetry, WordTopLeft, module, ex).ShowDialog();
                UpdateHelper.ClearSettings();
                UpdateHelper.CheckForUpdates(SystemOptions.AutoUpdateFrequency);
            }
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="doc">The document that is being saved.</param>
        /// <param name="saveAsUi">True if the Save As dialog box is displayed, whether to save a new document, in response to the Save command; or in response to the Save As command; or in response to the SaveAs or SaveAs2 method.</param>
        /// <param name="cancel">False when the event occurs.
        /// If the event procedure sets this argument to True, the document is not saved when the procedure is finished.</param>
        private void OnDocumentBeforeSave(Word.Document doc, ref bool saveAsUi, ref bool cancel)
        {
            string module = $"{_product}.{_class}.{MethodBase.GetCurrentMethod().Name}()";

            try
            {
                Debug.WriteLine($"{module.Replace("()", $"({doc.Name})")}");

                if (SystemOptions == null)
                {
                    LoadOptions();
                }

                if (!doc.ReadOnly)
                {
                    if (Upgrader.LegacyChemistryCount(doc) == 0)
                    {
                        // Handle Word 2013+ AutoSave
                        if (WordVersion >= 2013)
                        {
                            if (!doc.IsInAutosave)
                            {
                                CustomXmlPartHelper.RemoveOrphanedXmlParts(doc);
                            }
                        }
                        else
                        {
                            CustomXmlPartHelper.RemoveOrphanedXmlParts(doc);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                if (SystemOptions == null)
                {
                    LoadOptions();
                }

                new ReportError(Telemetry, WordTopLeft, module, ex).ShowDialog();
                UpdateHelper.ClearSettings();
                UpdateHelper.CheckForUpdates(SystemOptions.AutoUpdateFrequency);
            }
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="doc">The document that's being closed.</param>
        /// <param name="cancel">False when the event occurs.
        /// If the event procedure sets this argument to True, the document doesn't close when the procedure is finished.</param>
        private void OnDocumentBeforeClose(Word.Document doc, ref bool cancel)
        {
            string module = $"{_product}.{_class}.{MethodBase.GetCurrentMethod().Name}()";

            try
            {
                Debug.WriteLine($"{module.Replace("()", $"({doc.Name})")}");

                if (SystemOptions == null)
                {
                    LoadOptions();
                }

                if (Ribbon != null)
                {
                    SetButtonStates(ButtonState.NoDocument);
                }

                Word.Application app = Application;
                OfficeTools.CustomTaskPane custTaskPane = null;

                foreach (OfficeTools.CustomTaskPane taskPane in CustomTaskPanes)
                {
                    try
                    {
                        if (app.ActiveWindow == taskPane.Window)
                        {
                            custTaskPane = taskPane;
                        }
                    }
                    catch
                    {
                        // Nothing much we can do here!
                    }
                }
                if (custTaskPane != null)
                {
                    try
                    {
                        CustomTaskPanes.Remove(custTaskPane);
                    }
                    catch
                    {
                        // Nothing much we can do here!
                    }
                }
            }
            catch (Exception ex)
            {
                if (SystemOptions == null)
                {
                    LoadOptions();
                }

                new ReportError(Telemetry, WordTopLeft, module, ex).ShowDialog();
                UpdateHelper.ClearSettings();
                UpdateHelper.CheckForUpdates(SystemOptions.AutoUpdateFrequency);
            }
        }

        #endregion Document Events

        #region Window Events

        /// <summary>
        ///
        /// </summary>
        /// <param name="sel">The text selected.
        /// If no text is selected, the Sel parameter returns either nothing or the first character to the right of the insertion point.</param>
        private void OnWindowSelectionChange(Word.Selection sel)
        {
            string module = $"{_product}.{_class}.{MethodBase.GetCurrentMethod().Name}()";

            try
            {
                try
                {
                    if (sel != null)
                    {
                        Debug.WriteLine($"{module.Replace("()", $"({sel.Document.Name})")}");
                        Debug.WriteLine($"  OnWindowSelectionChange() Selection from {sel.Range.Start} to {sel.Range.End} CC's {sel.ContentControls.Count}");
                    }
                }
                catch
                {
                    //
                }

                if (EventsEnabled)
                {
                    EventsEnabled = false;
                    SelectChemistry(sel);
                    EvaluateChemistryAllowed();
                    if (!ChemistryAllowed)
                    {
                        SetButtonStates(ButtonState.NoDocument);
                    }
                    EventsEnabled = true;
                }

                // Deliberate crash to test Error Reporting
                //int ii = 2;
                //int dd = 0;
                //int bang = ii / dd;
            }
            catch (Exception ex)
            {
                if (SystemOptions == null)
                {
                    LoadOptions();
                }

                new ReportError(Telemetry, WordTopLeft, module, ex).ShowDialog();
                UpdateHelper.ClearSettings();
                UpdateHelper.CheckForUpdates(SystemOptions.AutoUpdateFrequency);
            }
        }

        public void EvaluateChemistryAllowed()
        {
            string module = $"{_product}.{_class}.{MethodBase.GetCurrentMethod().Name}()";

            bool allowed = true;

            // Skip checks if ChemistryProhibitedReason already set
            if (string.IsNullOrEmpty(ChemistryProhibitedReason))
            {
                ChemistryProhibitedReason = "";

                try
                {
                    if (Application.Documents.Count > 0)
                    {
                        Word.Document doc = null;
                        try
                        {
                            doc = Application.ActiveDocument;
                        }
                        catch
                        {
                            // This only happens when document is in protected mode
                            allowed = false;
                            ChemistryProhibitedReason = "document is readonly.";
                        }

                        if (doc != null)
                        {
                            if (doc.CompatibilityMode < (int)Word.WdCompatibilityMode.wdWord2010)
                            {
                                allowed = false;
                                ChemistryProhibitedReason = "document is in compatibility mode.";
                            }

                            try
                            {
                                //if (doc.CoAuthoring.Conflicts.Count > 0) // <-- This clears current selection ???
                                if (doc.CoAuthoring.Locks.Count > 0)
                                {
                                    allowed = false;
                                    ChemistryProhibitedReason = "document is in co-authoring mode.";
                                }
                            }
                            catch
                            {
                                // CoAuthoring or Conflicts/Locks may not be initialised!
                            }

                            Word.Selection sel = Application.Selection;
                            if (sel.OMaths.Count > 0)
                            {
                                ChemistryProhibitedReason = "selection is in an Equation.";
                                allowed = false;
                            }

                            if (sel.Tables.Count > 0)
                            {
                                try
                                {
                                    if (sel.Cells.Count > 1)
                                    {
                                        ChemistryProhibitedReason = "selection contains more than one cell of a table.";
                                        allowed = false;
                                    }
                                }
                                catch
                                {
                                    // Cells may not be initialised!
                                }
                            }

                            if (allowed)
                            {
                                try
                                {
                                    Word.WdStoryType story = sel.StoryType;
                                    if (story != Word.WdStoryType.wdMainTextStory)
                                    {
                                        ChemistryProhibitedReason = $"selection is in a '{DecodeStoryType(story)}' story.";
                                        allowed = false;
                                    }
                                }
                                catch
                                {
                                    // ComException 0x80004005
                                    ChemistryProhibitedReason = $"can't determine which part of the story the selection point is.";
                                    allowed = false;
                                }
                            }

                            if (allowed)
                            {
                                int ccCount = sel.ContentControls.Count;
                                if (ccCount > 1)
                                {
                                    allowed = false;
                                    ChemistryProhibitedReason = "more than one ContentControl is selected";
                                }
                            }

                            if (allowed)
                            {
                                Word.WdContentControlType? contentControlType = null;
                                string title = "";
                                foreach (Word.ContentControl ccd in doc.ContentControls)
                                {
                                    if (ccd.Range.Start <= sel.Range.Start && ccd.Range.End >= sel.Range.End)
                                    {
                                        contentControlType = ccd.Type;
                                        title = ccd.Title;
                                        break;
                                    }
                                }

                                if (contentControlType != null)
                                {
                                    if (!string.IsNullOrEmpty(title) && title.Equals(Constants.ContentControlTitle))
                                    {
                                        // Handle old Word 2007 style
                                        if (contentControlType != Word.WdContentControlType.wdContentControlRichText
                                            && contentControlType != Word.WdContentControlType.wdContentControlPicture)
                                        {
                                            allowed = false;
                                            ChemistryProhibitedReason =
                                                $"selection is in a '{DecodeContentControlType(contentControlType)}' Content Control.";
                                        }
                                    }
                                    else
                                    {
                                        if (contentControlType != Word.WdContentControlType.wdContentControlRichText)
                                        {
                                            allowed = false;
                                            ChemistryProhibitedReason =
                                                $"selection is in a '{DecodeContentControlType(contentControlType)}' Content Control";
                                        }

                                        // Test for Shape inside CC which is not ours
                                        if (allowed)
                                        {
                                            try
                                            {
                                                if (sel.ShapeRange.Count > 0)
                                                {
                                                    ChemistryProhibitedReason =
                                                        "selection contains shape(s) inside Content Control.";
                                                    allowed = false;
                                                }
                                            }
                                            catch
                                            {
                                                // Shape may not evaluate
                                            }
                                        }
                                    }
                                }
                            }

                            // Test for Shape in document body
                            if (allowed)
                            {
                                try
                                {
                                    if (sel.ShapeRange.Count > 0)
                                    {
                                        ChemistryProhibitedReason = "selection contains shape(s).";
                                        allowed = false;
                                    }
                                }
                                catch
                                {
                                    // Shape may not evaluate
                                }
                            }
                        }
                    }
                    else
                    {
                        allowed = false;
                        ChemistryProhibitedReason = "no document is open.";
                    }
                }
                catch (COMException cex)
                {
                    string comCode = HexErrorCode(cex.ErrorCode);
                    switch (comCode)
                    {
                        case "0x80004005":
                            ChemistryAllowed = false;
                            ChemistryProhibitedReason = "can't determine where the current selection is.";
                            break;

                        case "0x800A11FD":
                            ChemistryAllowed = false;
                            ChemistryProhibitedReason = "changes are not permitted in the current selection.";
                            break;

                        case "0x800A1759":
                            ChemistryAllowed = false;
                            ChemistryProhibitedReason = "can't create a selection when a dialogue is active.";
                            break;

                        default:
                            // Keep exception hidden from end user.
                            Telemetry.Write(module, "Exception", $"ErrorCode: {comCode}");
                            Telemetry.Write(module, "Exception", cex.Message);
                            Telemetry.Write(module, "Exception", cex.ToString());
                            break;
                    }
                }
                catch (Exception ex)
                {
                    // Keep exception hidden from end user.
                    Telemetry.Write(module, "Exception", ex.Message);
                    Telemetry.Write(module, "Exception", ex.ToString());
                }
            }

            ChemistryAllowed = allowed;
            if (!allowed)
            {
                Debug.WriteLine(ChemistryProhibitedReason);
            }
        }

        private string HexErrorCode(int code)
        {
            try
            {
                long x = code & 0xFFFFFFFF;
                return string.Format("0x{0:X}", x);
            }
            catch
            {
                return string.Empty;
            }
        }

        private object DecodeStoryType(Word.WdStoryType storyType)
        {
            // Data from https://msdn.microsoft.com/en-us/vba/word-vba/articles/wdstorytype-enumeration-word
            string result = "";

            switch (storyType)
            {
                case Word.WdStoryType.wdCommentsStory:
                    result = "Comments";
                    break;

                case Word.WdStoryType.wdEndnoteContinuationNoticeStory:
                    result = "Endnote continuation notice";
                    break;

                case Word.WdStoryType.wdEndnoteContinuationSeparatorStory:
                    result = "Endnote continuation separator";
                    break;

                case Word.WdStoryType.wdEndnoteSeparatorStory:
                    result = "Endnote separator";
                    break;

                case Word.WdStoryType.wdEndnotesStory:
                    result = "Endnotes";
                    break;

                case Word.WdStoryType.wdEvenPagesFooterStory:
                    result = "Even pages footer";
                    break;

                case Word.WdStoryType.wdEvenPagesHeaderStory:
                    result = "Even pages header";
                    break;

                case Word.WdStoryType.wdFirstPageFooterStory:
                    result = "First page footer";
                    break;

                case Word.WdStoryType.wdFirstPageHeaderStory:
                    result = "First page header";
                    break;

                case Word.WdStoryType.wdFootnoteContinuationNoticeStory:
                    result = "Footnote continuation notice";
                    break;

                case Word.WdStoryType.wdFootnoteContinuationSeparatorStory:
                    result = "Footnote continuation separator";
                    break;

                case Word.WdStoryType.wdFootnoteSeparatorStory:
                    result = "Footnote separator";
                    break;

                case Word.WdStoryType.wdFootnotesStory:
                    result = "Footnotes";
                    break;

                case Word.WdStoryType.wdMainTextStory:
                    result = "Main text";
                    break;

                case Word.WdStoryType.wdPrimaryFooterStory:
                    result = "Primary footer";
                    break;

                case Word.WdStoryType.wdPrimaryHeaderStory:
                    result = "Primary header";
                    break;

                case Word.WdStoryType.wdTextFrameStory:
                    result = "Text frame";
                    break;

                default:
                    result = storyType.ToString();
                    break;
            }

            return result;
        }

        private static string DecodeContentControlType(Word.WdContentControlType? contentControlType)
        {
            // Date from https://msdn.microsoft.com/en-us/library/microsoft.office.interop.word.wdcontentcontroltype(v=office.14).aspx
            string result = "";

            switch (contentControlType)
            {
                case Word.WdContentControlType.wdContentControlRichText:
                    result = "Rich-Text";
                    break;

                case Word.WdContentControlType.wdContentControlText:
                    result = "Text";
                    break;

                case Word.WdContentControlType.wdContentControlBuildingBlockGallery:
                    result = "Picture";
                    break;

                case Word.WdContentControlType.wdContentControlComboBox:
                    result = "ComboBox";
                    break;

                case Word.WdContentControlType.wdContentControlDropdownList:
                    result = "Drop-Down List";
                    break;

                case Word.WdContentControlType.wdContentControlPicture:
                    result = "Building Block Gallery";
                    break;

                case Word.WdContentControlType.wdContentControlDate:
                    result = "Date";
                    break;

                case Word.WdContentControlType.wdContentControlGroup:
                    result = "Group";
                    break;

                case Word.WdContentControlType.wdContentControlCheckBox:
                    result = "CheckBox";
                    break;

                case Word.WdContentControlType.wdContentControlRepeatingSection:
                    result = "Repeating Section";
                    break;

                default:
                    result = contentControlType.ToString();
                    break;
            }

            return result;
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="sel">The current selection.</param>
        /// <param name="cancel">False when the event occurs.
        /// If the event procedure sets this argument to True, the default double-click action does not occur when the procedure is finished.</param>
        private void OnWindowBeforeDoubleClick(Word.Selection sel, ref bool cancel)
        {
            string module = $"{_product}.{_class}.{MethodBase.GetCurrentMethod().Name}()";

            try
            {
                Debug.WriteLine($"{module.Replace("()", $"({sel.Document.Name})")}");
                Debug.WriteLine("  Selection: from " + sel.Range.Start + " to " + sel.Range.End);

                if (EventsEnabled && _chemistrySelected)
                {
                    CustomRibbon.PerformEdit();
                }
            }
            catch (Exception ex)
            {
                if (SystemOptions == null)
                {
                    LoadOptions();
                }

                new ReportError(Telemetry, WordTopLeft, module, ex).ShowDialog();
                UpdateHelper.ClearSettings();
                UpdateHelper.CheckForUpdates(SystemOptions.AutoUpdateFrequency);
            }
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="doc"></param>
        /// <param name="win"></param>
        private void OnWindowActivate(Word.Document doc, Word.Window win)
        {
            string module = $"{_product}.{_class}.{MethodBase.GetCurrentMethod().Name}()";

            try
            {
                Debug.WriteLine($"{module.Replace("()", $"({doc.Name})")}");

                EvaluateChemistryAllowed();

                // Deliberate crash to test Error Reporting
                //int ii = 2;
                //int dd = 0;
                //int bang = ii / dd;
            }
            catch (Exception ex)
            {
                if (SystemOptions == null)
                {
                    LoadOptions();
                }

                new ReportError(Telemetry, WordTopLeft, module, ex).ShowDialog();
                UpdateHelper.ClearSettings();
                UpdateHelper.CheckForUpdates(SystemOptions.AutoUpdateFrequency);
            }
        }

        #endregion Window Events

        #region Content Control Events

        /// <summary>
        ///
        /// </summary>
        /// <param name="NewContentControl"></param>
        /// <param name="InUndoRedo"></param>
        private void OnContentControlAfterAdd(Word.ContentControl NewContentControl, bool InUndoRedo)
        {
            string module = $"{_product}.{_class}.{MethodBase.GetCurrentMethod().Name}()";

            try
            {
                Debug.WriteLine($"{module}");

                if (SystemOptions == null)
                {
                    LoadOptions();
                }

                string ccId = NewContentControl.ID;
                string ccTag = NewContentControl.Tag;
                if (!InUndoRedo && !string.IsNullOrEmpty(ccTag))
                {
                    if (!ccId.Equals(_lastContentControlAdded))
                    {
                        string message = $"ContentControl {ccId} added; Looking for structure {ccTag}";
                        Debug.WriteLine("  " + message);
                        Telemetry.Write(module, "Information", message);

                        Word.Document doc = NewContentControl.Application.ActiveDocument;
                        Word.Application app = Application;
                        CustomXMLPart cxml = CustomXmlPartHelper.GetCustomXmlPart(ccTag, app.ActiveDocument);
                        if (cxml != null)
                        {
                            Telemetry.Write(module, "Information", "Found copy of " + ccTag + " in this document.");
                        }
                        else
                        {
                            if (doc.Application.Documents.Count > 1)
                            {
                                Word.Application app1 = Application;
                                cxml = CustomXmlPartHelper.FindCustomXmlPart(ccTag, app1.ActiveDocument);
                                if (cxml != null)
                                {
                                    Telemetry.Write(module, "Information", "Found copy of " + ccTag + " in other document, adding it into this.");

                                    // Generate new molecule Guid and apply it
                                    string newGuid = Guid.NewGuid().ToString("N");
                                    NewContentControl.Tag = newGuid;

                                    CMLConverter cmlConverter = new CMLConverter();
                                    Model.Model model = cmlConverter.Import(cxml.XML);
                                    model.CustomXmlPartGuid = newGuid;
                                    doc.CustomXMLParts.Add(cmlConverter.Export(model));
                                }
                            }
                        }

                        _lastContentControlAdded = ccId;
                    }
                }
            }
            catch (Exception ex)
            {
                if (SystemOptions == null)
                {
                    LoadOptions();
                }

                new ReportError(Telemetry, WordTopLeft, module, ex).ShowDialog();
                UpdateHelper.ClearSettings();
                UpdateHelper.CheckForUpdates(SystemOptions.AutoUpdateFrequency);
            }
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="contentControl"></param>
        /// <param name="Cancel"></param>
        private void OnContentControlBeforeDelete(Word.ContentControl contentControl, bool Cancel)
        {
            string module = $"{_product}.{_class}.{MethodBase.GetCurrentMethod().Name}()";

            try
            {
                Debug.WriteLine($"{module.Replace("()", $"({contentControl.Application.ActiveDocument.Name})")}");

                if (SystemOptions == null)
                {
                    LoadOptions();
                }
            }
            catch (Exception ex)
            {
                if (SystemOptions == null)
                {
                    LoadOptions();
                }

                new ReportError(Telemetry, WordTopLeft, module, ex).ShowDialog();
                UpdateHelper.ClearSettings();
                UpdateHelper.CheckForUpdates(SystemOptions.AutoUpdateFrequency);
            }
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="contentControl"></param>
        /// <param name="Cancel"></param>
        private void OnContentControlOnExit(Word.ContentControl contentControl, ref bool Cancel)
        {
            string module = $"{_product}.{_class}.{MethodBase.GetCurrentMethod().Name}()";

            try
            {
                if (SystemOptions == null)
                {
                    LoadOptions();
                }

                Debug.WriteLine($"{module.Replace("()", $"({contentControl.Application.ActiveDocument.Name})")}");
                //if (EventsEnabled)
                //{
                //    //Debug.WriteLine("CC ID: " + contentControl.ID + " Tag: " + contentControl.Tag + " Title: " + contentControl.Title);
                //    Word.Document doc = contentControl.Application.ActiveApp;
                //    Word.Selection sel = doc.Application.Selection;
                //    Debug.WriteLine("  Selection: from " + sel.Range.Start + " to " + sel.Range.End);
                //    SelectChemistry(sel);
                //}
            }
            catch (Exception ex)
            {
                if (SystemOptions == null)
                {
                    LoadOptions();
                }

                new ReportError(Telemetry, WordTopLeft, module, ex).ShowDialog();
                UpdateHelper.ClearSettings();
                UpdateHelper.CheckForUpdates(SystemOptions.AutoUpdateFrequency);
            }
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="contentControl"></param>
        private void OnContentControlOnEnter(Word.ContentControl contentControl)
        {
            string module = $"{_product}.{_class}.{MethodBase.GetCurrentMethod().Name}()";

            try
            {
                if (SystemOptions == null)
                {
                    LoadOptions();
                }

                Debug.WriteLine($"{module.Replace("()", $"({contentControl.Application.ActiveDocument.Name})")}");

                if (EventsEnabled)
                {
                    EventsEnabled = false;
#if DEBUG
                    Word.Document doc = contentControl.Application.ActiveDocument;
                    Word.Selection sel = doc.Application.Selection;
                    Debug.WriteLine($"  OnContentControlOnEnter() Document: {doc.Name} Selection from {sel.Range.Start} to {sel.Range.End}");
                    Debug.WriteLine($"  OnContentControlOnEnter() Document: {doc.Name} Selection has {sel.ContentControls.Count} CCs");
#endif
                    EvaluateChemistryAllowed();
                    EventsEnabled = true;
                }
            }
            catch (Exception ex)
            {
                if (SystemOptions == null)
                {
                    LoadOptions();
                }

                new ReportError(Telemetry, WordTopLeft, module, ex).ShowDialog();
                UpdateHelper.ClearSettings();
                UpdateHelper.CheckForUpdates(SystemOptions.AutoUpdateFrequency);
            }
        }

        #endregion Content Control Events

        #region VSTO generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InternalStartup()
        {
            this.Startup += new EventHandler(C4WLiteAddIn_Startup);
            this.Shutdown += new EventHandler(C4WLiteAddIn_Shutdown);
        }

        #endregion VSTO generated code
    }

    public enum ButtonState
    {
        NoDocument,
        CanEdit,
        CanInsert
    }
}