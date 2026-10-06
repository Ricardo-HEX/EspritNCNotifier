using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;
using ESPRIT.NetApi.Ribbon;

namespace EspritNcNotifier
{
    internal sealed class ExtensionController : IDisposable
    {
        private const string PostKey = "EspritNcNotifier.Post", TabKey = "EspritNcNotifier.Tab", GroupKey = "EspritNcNotifier.Group";
        private readonly Esprit.Application _app; private readonly IRibbon _ribbon; private readonly Icon _commandIcon; private AppSettings _settings; private bool _fallbackCreated;
        public ExtensionController(Esprit.Application app) { _app = app; _settings = AppSettings.Load(); _ribbon = (IRibbon)app.Ribbon; _commandIcon = IconResources.LoadNcSendIcon(); _ribbon.OnButtonClick += OnButtonClick; AddRibbonCommands(); LoadTooltips(); }

        private void AddRibbonCommands()
        {
            IRibbonSplitButton ncSplit = FindNcSplitButton();
            if (ncSplit != null)
            {
                if (!ncSplit.Items.Contains(PostKey)) ncSplit.Items.AddButton(PostKey, "Código NC y notificación", _commandIcon);
                return;
            }
            IRibbonTab tab = _ribbon.Tabs.Contains(TabKey) ? _ribbon.Tabs.Item(TabKey) : _ribbon.Tabs.Add(TabKey, "NC y correo");
            IRibbonGroup group = tab.Groups.Contains(GroupKey) ? tab.Groups.Item(GroupKey) : tab.Groups.Add(GroupKey, "Postprocesado NC");
            if (!group.Items.Contains(PostKey)) group.Items.AddButton(PostKey, "Código NC y notificación", true, _commandIcon);
            _fallbackCreated = true;
        }

        private void LoadTooltips()
        {
            try
            {
                string assemblyFolder = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                string tooltipFile = Path.Combine(assemblyFolder ?? string.Empty, "EspritNcNotifier.Tooltips.xml");
                if (File.Exists(tooltipFile)) _ribbon.LoadToolTipConfiguration(tooltipFile);
            }
            catch (Exception ex) { Log("No se pudo cargar la descripción del comando: " + ex.Message); }
        }

        private IRibbonSplitButton FindNcSplitButton()
        {
            // Clave de interfaz declarada por ESPRITEDGE_layout.xml. El id de
            // presentación (42515) no coincide con espCmdNccode (3320).
            var nativeItems = _ribbon.FindRibbonItems("File.NcCode");
            if (nativeItems != null)
            {
                foreach (IRibbonItem nativeItem in nativeItems)
                    if (nativeItem is IRibbonSplitButton nativeSplit) return nativeSplit;
            }

            int ncCommand = (int)EspritConstants.espCommands.espCmdNccode;
            for (int ti = 0; ti < _ribbon.Tabs.Count; ti++)
            {
                IRibbonTab tab = _ribbon.Tabs.Item(ti);
                for (int gi = 0; gi < tab.Groups.Count; gi++)
                {
                    IRibbonGroup group = tab.Groups.Item(gi);
                    for (int ii = 0; ii < group.Items.Count; ii++)
                    {
                        IRibbonItem item = group.Items.Item(ii);
                        if (!(item is IRibbonSplitButton split)) continue;

                        // En los controles divididos integrados, CommandId suele pertenecer
                        // al primer comando hijo y no al contenedor del desplegable.
                        if (item.CommandId == ncCommand) return split;
                        for (int childIndex = 0; childIndex < split.Items.Count; childIndex++)
                        {
                            IRibbonButton child = split.Items.Item(childIndex);
                            if (child.CommandId == ncCommand) return split;
                        }
                    }
                }
            }
            return null;
        }

        private async void OnButtonClick(object sender, ButtonClickEventArgs e)
        {
            if (e.Key != PostKey) return; e.Handled = true;
            try { await PostAndNotifyAsync(); }
            catch (OperationCanceledException) { Log("Operación cancelada."); }
            catch (Exception ex) { Log("ERROR: " + ex); MessageBox.Show(ex.Message, "Postprocesar y notificar", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        private async Task PostAndNotifyAsync()
        {
            dynamic doc = _app.Document;
            if (doc == null) { MessageBox.Show("No hay ningún documento abierto.", "Postprocesar y notificar"); return; }
            string docPath = Convert.ToString(doc.DocumentProperties.Path), docFolder = Path.GetDirectoryName(docPath);
            if (string.IsNullOrWhiteSpace(docFolder)) { MessageBox.Show("Guarda el documento ESPRIT antes de postprocesar.", "Postprocesar y notificar"); return; }
            bool all; List<dynamic> selected = GetSelectedPartOperations(doc, out all); if (selected == null) return;
            string operationSummary = all ? "Programa completo" : GetOperationNumberSummary(doc, selected);
            string ncFolder = Path.Combine(docFolder, "NC"); Directory.CreateDirectory(ncFolder);
            string baseName = Path.GetFileNameWithoutExtension(docPath), suggested = baseName + (all ? "_completo" : "_seleccion");
            dynamic ncOutput = EnsureNcOutput(doc, ncFolder, suggested); if (ncOutput == null) return;
            string postSummary = GetPostSummary(ncOutput);
            string machineName = GetMachineName(doc);
            using (var dialog = new JobDialog(selected.Count, all, ncFolder, suggested, _settings.LastRecipient, machineName, postSummary))
            {
                if (dialog.ShowDialog() != DialogResult.OK) return;
                _settings.LastRecipient = dialog.Recipient; _settings.Save();
                ncFolder = dialog.NcFolder;
                ncOutput.NCFileFolder = ncFolder;
                ncOutput.NCFileName = dialog.NcName;
                string[] files;
                try { files = Postprocess(doc, selected, ncOutput, ncFolder, dialog.NcName); }
                catch (PostprocessorExecutionException)
                {
                    ncOutput = ConfigureNcOutput(doc.InitialMachineSetup.NCOutputs, ncOutput, ncFolder, dialog.NcName);
                    if (ncOutput == null) return;
                    ncOutput.Enabled = true;
                    postSummary = GetPostSummary(ncOutput);
                    files = Postprocess(doc, selected, ncOutput, ncFolder, dialog.NcName);
                }
                string imageFile = CapturePartView(doc, ncFolder, dialog.NcName);
                PrepareEmail(dialog.Recipient, docPath, files, imageFile, operationSummary, machineName, postSummary);
                OpenFolderAndSelect(imageFile);
                MessageBox.Show("NC y captura generados correctamente.\r\n\r\nSe ha abierto el correo y la carpeta NC con la captura seleccionada para que puedas arrastrarla al mensaje.\r\n\r\n" + string.Join("\r\n", files) + "\r\n" + imageFile, "Postprocesar y preparar correo", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private static string CapturePartView(dynamic doc, string folder, string ncName)
        {
            string imageFile = Path.Combine(folder, ncName + "_vista.png");
            if (File.Exists(imageFile)) File.Delete(imageFile);

            Esprit.Document typedDocument = (Esprit.Document)doc;
            var originalSelection = new List<object>();
            for (int i = 1; i <= (int)doc.Group.Count; i++) originalSelection.Add((object)doc.Group.Item(i));
            var operationVisibility = new List<Tuple<object, bool>>();
            for (int i = 1; i <= (int)doc.PartOperations.Count; i++)
            {
                dynamic operation = doc.PartOperations.Item(i);
                operationVisibility.Add(Tuple.Create((object)operation, Convert.ToBoolean(operation.Visible)));
            }
            Esprit.Window originalWindow = typedDocument.Windows.ActiveWindow;
            Esprit.Window partWindow = FindPartWindow(typedDocument.Windows) ?? originalWindow;
            EspritGeometryBase.IComMatrix originalViewMatrix = (EspritGeometryBase.IComMatrix)partWindow.ViewMatrix.Clone();
            EspritGeometryBase.IComPoint originalViewCenter = (EspritGeometryBase.IComPoint)partWindow.ViewCenter.Clone();
            double originalViewScale = partWindow.ViewScale;
            var hiddenMasks = new[]
            {
                EspritConstants.espViewMaskType.espViewMaskGeometry,
                EspritConstants.espViewMaskType.espViewMaskSurfaces,
                EspritConstants.espViewMaskType.espViewMaskCurves,
                EspritConstants.espViewMaskType.espViewMaskFeatures,
                EspritConstants.espViewMaskType.espViewMaskChainFeature,
                EspritConstants.espViewMaskType.espViewMaskPTOPFeature,
                EspritConstants.espViewMaskType.espViewMaskTechnology,
                EspritConstants.espViewMaskType.espViewMaskDraftConicFeature,
                EspritConstants.espViewMaskType.espViewMaskRuledFeature,
                EspritConstants.espViewMaskType.espViewMaskHolesFeature,
                EspritConstants.espViewMaskType.espViewMaskTurningFeature,
                EspritConstants.espViewMaskType.espViewMask3AxisType,
                EspritConstants.espViewMaskType.espViewMaskToolPointAxis,
                EspritConstants.espViewMaskType.espViewMaskTouchPointNormal,
                EspritConstants.espViewMaskType.espViewMaskToolPath,
                EspritConstants.espViewMaskType.espViewMaskAnnotation
            };
            var originalMasks = hiddenMasks.ToDictionary(mask => mask, mask => partWindow.GetMask(mask));

            try
            {
                doc.Group.Clear();
                foreach (var state in operationVisibility) ((dynamic)state.Item1).Visible = false;
                partWindow.SetActiveView();
                foreach (var mask in hiddenMasks) partWindow.SetMask(mask, false);
                partWindow.SetView(EspritConstants.espView.espViewIsometric);
                partWindow.Fit();
                partWindow.Refresh();
                System.Windows.Forms.Application.DoEvents();

                bool created = typedDocument.OutputPartViewImage(1280, 720, imageFile);
                if (!created || !File.Exists(imageFile) || new FileInfo(imageFile).Length == 0)
                    throw new InvalidOperationException("No se pudo generar la captura de la vista de la pieza.");
            }
            finally
            {
                foreach (var state in originalMasks) partWindow.SetMask(state.Key, state.Value);
                partWindow.ViewMatrix = originalViewMatrix;
                partWindow.ViewCenter = originalViewCenter;
                partWindow.ViewScale = originalViewScale;
                partWindow.Refresh();
                if (originalWindow != null) originalWindow.SetActiveView();
                doc.Group.Clear();
                foreach (object item in originalSelection) doc.Group.Add(item);
                foreach (var state in operationVisibility) ((dynamic)state.Item1).Visible = state.Item2;
                typedDocument.Refresh();
                System.Windows.Forms.Application.DoEvents();
            }

            return imageFile;
        }

        private static Esprit.Window FindPartWindow(Esprit.Windows windows)
        {
            for (int index = 1; index <= windows.Count; index++)
            {
                Esprit.Window window = windows[index];
                if (window.ViewType == EspritConstants.espViewType.espViewTypePart) return window;
            }
            return null;
        }

        private static void OpenFolderAndSelect(string file)
        {
            Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + file + "\"") { UseShellExecute = true });
        }

        private static void PrepareEmail(string recipients, string document, string[] files, string imageFile, string operationSummary, string machineName, string postSummary)
        {
            string subject = "NC listo - " + Path.GetFileNameWithoutExtension(document);
            string body = "El programa NC ya está listo para enviar a máquina.\r\n\r\nDocumento: " + document + "\r\nOperaciones: " +
                          operationSummary + "\r\nMáquina: " + machineName + "\r\nPostprocesador: " + postSummary +
                          "\r\n\r\nFicheros NC:\r\n" + string.Join("\r\n", files) + "\r\n\r\nCaptura de la pieza:\r\n" + imageFile +
                          "\r\n\r\nGenerado por: " + Environment.UserName +
                          "\r\nFecha: " + DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss");
            string to = string.Join(",", recipients.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()));
            string uri = "mailto:" + Uri.EscapeDataString(to) + "?subject=" + Uri.EscapeDataString(subject) + "&body=" + Uri.EscapeDataString(body);
            Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
        }

        private static string GetOperationNumberSummary(dynamic doc, List<dynamic> selected)
        {
            var selectedKeys = new HashSet<string>(
                selected.Select(operation => NormalizeOperationKey((object)operation.Key)),
                StringComparer.OrdinalIgnoreCase);
            var numbers = new List<int>();
            for (int i = 1; i <= (int)doc.PartOperations.Count; i++)
            {
                dynamic operation = doc.PartOperations.Item(i);
                if (selectedKeys.Contains(NormalizeOperationKey((object)operation.Key))) numbers.Add(i);
            }
            return numbers.Count > 0 ? string.Join(", ", numbers) : selected.Count + " seleccionada(s)";
        }

        private List<dynamic> GetSelectedPartOperations(dynamic doc, out bool all)
        {
            all = false; var result = new List<dynamic>();
            for (int i = 1; i <= (int)doc.Group.Count; i++) { dynamic item = doc.Group.Item(i); if ((int)item.GraphicObjectType == (int)EspritConstants.espGraphicObjectType.espOperation) result.Add(item); }
            if (result.Count > 0) return result;
            if ((int)doc.PartOperations.Count == 0) { MessageBox.Show("El documento no contiene operaciones.", "Postprocesar y notificar"); return null; }
            if (MessageBox.Show("No hay ninguna operación seleccionada.\r\n\r\n¿Quieres postprocesar el programa completo?", "Postprocesar y notificar", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return null;
            all = true; for (int i = 1; i <= (int)doc.PartOperations.Count; i++) result.Add(doc.PartOperations.Item(i)); return result;
        }

        private dynamic EnsureNcOutput(dynamic doc, string ncFolder, string suggestedName)
        {
            dynamic outputs = doc.InitialMachineSetup.NCOutputs;
            dynamic output = null, firstOutput = null, firstValidOutput = null;
            for (int index = 1; index <= (int)outputs.Count; index++)
            {
                dynamic candidate = outputs.Item(index);
                if (firstOutput == null) firstOutput = candidate;
                if (!IsNcOutputValid(candidate)) continue;
                if (firstValidOutput == null) firstValidOutput = candidate;
                try { if (Convert.ToBoolean(candidate.Enabled)) { output = candidate; break; } } catch { }
            }
            if (output == null) output = firstValidOutput;
            if (output == null)
            {
                output = firstOutput;
                output = ConfigureNcOutput(outputs, output, ncFolder, suggestedName);
                if (output == null) return null;
            }
            output.Enabled = true; return output;
        }

        private dynamic ConfigureNcOutput(dynamic outputs, dynamic output, string ncFolder, string suggestedName)
        {
            string initial = ""; try { initial = _app.Configuration.GetFileDirectory(EspritConstants.espFileType.espFileTypePostProcessor); } catch { }
            string currentExtension = output == null ? "nc" : Convert.ToString(output.NCFileExtension);
            using (var dialog = new PostProcessorDialog(initial, ncFolder, suggestedName, currentExtension))
            {
                if (dialog.ShowDialog() != DialogResult.OK) return null;
                if (output == null)
                {
                    Array posts = dialog.Files.ToArray();
                    return outputs.Add(ref posts, ncFolder, suggestedName, dialog.NcExtension);
                }
                output.RemoveAllPostProcessors();
                foreach (string file in dialog.Files) output.AddPostProcessorFile(file);
                output.NCFileFolder = ncFolder; output.NCFileName = suggestedName; output.NCFileExtension = dialog.NcExtension;
                return output;
            }
        }

        private bool IsNcOutputValid(dynamic output)
        {
            try
            {
                int postCount = (int)output.PostFilesCount;
                if (postCount == 0) return false;
                for (int index = 1; index <= postCount; index++)
                {
                    string postFile = Convert.ToString(output.PostProcessorItem(index));
                    if (!PostProcessorFileExists(postFile)) return false;
                }
                return true;
            }
            catch { return false; }
        }

        private bool PostProcessorFileExists(string configuredPath)
        {
            if (string.IsNullOrWhiteSpace(configuredPath)) return false;
            string postFile = Environment.ExpandEnvironmentVariables(configuredPath.Trim().Trim('"'));
            if (File.Exists(postFile)) return true;

            string postFolder = "";
            try { postFolder = Convert.ToString(_app.Configuration.GetFileDirectory(EspritConstants.espFileType.espFileTypePostProcessor)); }
            catch { }
            if (string.IsNullOrWhiteSpace(postFolder) || !Directory.Exists(postFolder)) return false;

            const string postDirectoryToken = "<PostDirectory>";
            if (postFile.StartsWith(postDirectoryToken, StringComparison.OrdinalIgnoreCase))
                postFile = postFile.Substring(postDirectoryToken.Length).TrimStart('\\', '/');
            if (!Path.IsPathRooted(postFile) && File.Exists(Path.Combine(postFolder, postFile))) return true;

            string fileName = Path.GetFileName(postFile);
            if (string.IsNullOrWhiteSpace(fileName)) return false;
            try { return Directory.EnumerateFiles(postFolder, fileName, SearchOption.AllDirectories).Any(); }
            catch { return false; }
        }

        private string GetPostSummary(dynamic output)
        { var names = new List<string>(); for (int i = 1; i <= (int)output.PostFilesCount; i++) names.Add(Path.GetFileName(Convert.ToString(output.PostProcessorItem(i)))); return string.Join(" + ", names); }

        private static string GetMachineName(dynamic doc)
        {
            try
            {
                string name = Convert.ToString(doc.InitialMachineSetup.MachineName).Trim();
                if (!string.IsNullOrWhiteSpace(name)) return name;
            }
            catch { }
            try
            {
                string name = Convert.ToString(doc.InitialMachineSetup.MachineDefinition.Name).Trim();
                if (!string.IsNullOrWhiteSpace(name)) return name;
            }
            catch { }
            try
            {
                string file = Convert.ToString(doc.InitialMachineSetup.MachineFileName);
                if (!string.IsNullOrWhiteSpace(file)) return Path.GetFileNameWithoutExtension(file);
            }
            catch { }
            return "No especificada";
        }

        private string[] Postprocess(dynamic doc, List<dynamic> selected, dynamic output, string folder, string ncName)
        {
            var keys = new HashSet<string>(selected.Select(x => NormalizeOperationKey((object)x.Key)), StringComparer.OrdinalIgnoreCase);
            var machineOps = new List<object>(); dynamic allMachineOps = doc.MachineOperations;
            for (int i = 1; i <= (int)allMachineOps.Count; i++)
            {
                dynamic mo = allMachineOps.Item(i); object rawKey;
                try { rawKey = mo.PartOperation.Key; } catch { rawKey = mo.Key; }
                if (keys.Contains(NormalizeOperationKey(rawKey))) machineOps.Add((object)mo);
            }
            if (machineOps.Count == 0) throw new InvalidOperationException("No se encontraron operaciones de máquina asociadas a la selección.");
            Array operationArray = machineOps.ToArray();
            Esprit.NCCode ncCode = (Esprit.NCCode)doc.NCCode;
            Esprit.NCOutput typedOutput = (Esprit.NCOutput)output;
            Array generated;
            try
            {
                generated = ncCode.CreateOperationsNCOutput(
                    ref operationArray,
                    typedOutput,
                    EspritConstants.espNCCodeType.espNCCodeStandard,
                    false);
            }
            catch (Exception ex) { throw new PostprocessorExecutionException("El postprocesador no pudo generar el fichero NC.", ex); }
            if (generated == null || generated.Length == 0) throw new PostprocessorExecutionException("El postprocesador no generó ningún fichero NC.");
            var final = new List<string>(); int n = generated.Length, index = 0;
            foreach (object item in generated)
            {
                index++; string source = Convert.ToString(item); if (!File.Exists(source)) throw new FileNotFoundException("No se encontró el fichero generado.", source);
                string configuredExtension = Convert.ToString(output.NCFileExtension)?.Trim().TrimStart('.');
                string extension = string.IsNullOrWhiteSpace(configuredExtension) ? Path.GetExtension(source) : "." + configuredExtension;
                string target = Path.Combine(folder, ncName + (n > 1 ? "_" + index : "") + extension);
                if (File.Exists(target) && MessageBox.Show("El fichero ya existe:\r\n" + target + "\r\n\r\n¿Quieres reemplazarlo?", "Postprocesar y notificar", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) throw new OperationCanceledException();
                if (!string.Equals(Path.GetFullPath(source), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase)) { if (File.Exists(target)) File.Delete(target); File.Copy(source, target); File.Delete(source); }
                if (new FileInfo(target).Length == 0) throw new InvalidDataException("El fichero NC generado está vacío: " + target); final.Add(target);
            }
            foreach (dynamic op in selected) { string original = Convert.ToString(op.Name); int marker = original.IndexOf(" ** ", StringComparison.Ordinal); if (marker >= 0) original = original.Substring(0, marker).Trim(); op.Name = original + " ** " + ncName + " **"; }
            doc.Refresh(); return final.ToArray();
        }

        private static string NormalizeOperationKey(object value)
        {
            return Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)?.Trim() ?? string.Empty;
        }

        private sealed class PostprocessorExecutionException : Exception
        {
            public PostprocessorExecutionException(string message) : base(message) { }
            public PostprocessorExecutionException(string message, Exception innerException) : base(message, innerException) { }
        }

        private void Log(string text) { try { _app.EventWindow.Visible = true; _app.EventWindow.AddMessage(EspritConstants.espMessageType.espMessageTypeInformation, "EspritNcNotifier", text); } catch { } }
        public void Dispose()
        {
            _ribbon.OnButtonClick -= OnButtonClick;
            try { IRibbonSplitButton split = FindNcSplitButton(); if (split != null && split.Items.Contains(PostKey)) split.Items.Remove(PostKey); if (_fallbackCreated && _ribbon.Tabs.Contains(TabKey)) _ribbon.Tabs.Remove(TabKey); } catch { }
            _commandIcon.Dispose();
        }
    }
}
