using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.Mail;
using System.Windows.Forms;

namespace EspritNcNotifier
{
    internal sealed class JobDialog : Form
    {
        private readonly TextBox _name = new TextBox();
        private readonly TextBox _email = new TextBox();
        public string NcName => _name.Text.Trim();
        public string Recipient => _email.Text.Trim();

        public JobDialog(int operationCount, bool allOperations, string folder, string suggestedName, string email, string postSummary)
        {
            Text = "Postprocesar y preparar correo"; FormBorderStyle = FormBorderStyle.FixedDialog; StartPosition = FormStartPosition.CenterScreen;
            MaximizeBox = MinimizeBox = false; Width = 650; Height = 350;
            var table = NewTable(6);
            AddRow(table, 0, "Operaciones:", allOperations ? $"Programa completo ({operationCount})" : $"{operationCount} seleccionada(s)");
            AddRow(table, 1, "Postprocesador:", postSummary); AddRow(table, 2, "Carpeta NC:", folder);
            AddEditRow(table, 3, "Nombre NC:", _name, suggestedName); AddEditRow(table, 4, "Enviar a:", _email, email);
            table.Controls.Add(new Label { Text = "Se abrirá un mensaje preparado en la aplicación de correo predeterminada. El último destinatario se recordará.", AutoSize = true, MaximumSize = new Size(450, 45) }, 1, 5);
            Controls.Add(table);
            var buttons = NewButtons(OnAccept); Controls.Add(buttons); AcceptButton = buttons.Controls.OfType<Button>().First(b => b.DialogResult == DialogResult.OK);
        }

        private void OnAccept(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(NcName) || NcName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) { MessageBox.Show("Introduce un nombre de fichero válido.", Text); DialogResult = DialogResult.None; return; }
            try { foreach (string address in Recipient.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)) _ = new MailAddress(address.Trim()); if (string.IsNullOrWhiteSpace(Recipient)) throw new FormatException(); }
            catch { MessageBox.Show("Introduce una dirección de correo válida.", Text); DialogResult = DialogResult.None; }
        }

        internal static TableLayoutPanel NewTable(int rows) => new TableLayoutPanel { Dock = DockStyle.Top, Height = 240, Padding = new Padding(12), ColumnCount = 2, RowCount = rows, ColumnStyles = { new ColumnStyle(SizeType.Absolute, 125), new ColumnStyle(SizeType.Percent, 100) } };
        internal static void AddRow(TableLayoutPanel t, int row, string label, string value) { t.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left }, 0, row); t.Controls.Add(new Label { Text = value, AutoSize = true, MaximumSize = new Size(450, 40), Anchor = AnchorStyles.Left }, 1, row); }
        internal static void AddEditRow(TableLayoutPanel t, int row, string label, TextBox box, string value) { box.Text = value; box.Dock = DockStyle.Fill; t.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left }, 0, row); t.Controls.Add(box, 1, row); }
        private FlowLayoutPanel NewButtons(EventHandler accept)
        {
            var p = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 52, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8) };
            var cancel = new Button { Text = "Cancelar", DialogResult = DialogResult.Cancel, AutoSize = true }; var ok = new Button { Text = "Postprocesar y preparar correo", DialogResult = DialogResult.OK, AutoSize = true };
            ok.Click += accept; p.Controls.Add(cancel); p.Controls.Add(ok); return p;
        }
    }

    internal sealed class PostProcessorDialog : Form
    {
        private readonly ListBox _files = new ListBox();
        private readonly string _initialPostFolder;
        private readonly TextBox _folder = new TextBox(), _name = new TextBox();
        private readonly ComboBox _extension = new ComboBox();
        private readonly Label _preview = new Label();
        public IReadOnlyList<string> Files => _files.Items.Cast<string>().ToList();
        public string NcExtension => _extension.Text.Trim().TrimStart('.');

        public PostProcessorDialog(string initialPostFolder, string ncFolder, string defaultName, string currentExtension)
        {
            _initialPostFolder = initialPostFolder; Text = "Archivo de salida NC"; Width = 680; Height = 625; StartPosition = FormStartPosition.CenterScreen; FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = MinimizeBox = false;
            var intro = new Label { Dock = DockStyle.Top, Height = 55, Padding = new Padding(14, 10, 10, 0), Text = "Añadir los archivos del postprocesador y configurar la salida NC.\r\nSeleccionar varios postprocesadores combinará el resultado en un único archivo NC." }; Controls.Add(intro);

            var posts = new GroupBox { Text = "Postprocesadores", Dock = DockStyle.Top, Height = 205, Padding = new Padding(12) };
            _files.Dock = DockStyle.Fill; _files.MouseDown += FilesMouseDown; posts.Controls.Add(_files);
            var side = new FlowLayoutPanel { Dock = DockStyle.Right, Width = 52, FlowDirection = FlowDirection.TopDown, Padding = new Padding(4, 10, 4, 4) };
            var add = NewSquareButton("…", "Añadir postprocesador"); add.Click += AddFiles;
            var up = NewSquareButton("↑", "Subir"); up.Click += (s, e) => MoveSelected(-1);
            var down = NewSquareButton("↓", "Bajar"); down.Click += (s, e) => MoveSelected(1);
            var remove = NewSquareButton("✕", "Quitar"); remove.Click += (s, e) => { int index = _files.SelectedIndex; if (index >= 0) _files.Items.RemoveAt(index); };
            side.Controls.Add(add); side.Controls.Add(up); side.Controls.Add(down); side.Controls.Add(remove); posts.Controls.Add(side); Controls.Add(posts);

            var folderGroup = new GroupBox { Text = "Carpeta de archivos NC", Dock = DockStyle.Top, Height = 75, Padding = new Padding(12) };
            _folder.Text = ncFolder; _folder.ReadOnly = true; _folder.Dock = DockStyle.Fill; folderGroup.Controls.Add(_folder); Controls.Add(folderGroup);

            var nameGroup = new GroupBox { Text = "Nombre de archivo NC", Dock = DockStyle.Top, Height = 105, Padding = new Padding(12) };
            var nameTable = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 2 };
            nameTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); nameTable.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 85)); nameTable.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 105));
            _name.Text = defaultName; _name.ReadOnly = true; _name.Dock = DockStyle.Fill; nameTable.Controls.Add(_name, 0, 0); nameTable.SetColumnSpan(_name, 3);
            nameTable.Controls.Add(new Label { Text = "Extensión:", AutoSize = true, Anchor = AnchorStyles.Right }, 1, 1);
            _extension.DropDownStyle = ComboBoxStyle.DropDown; _extension.Items.AddRange(new object[] { "nc", "cnc", "tap", "iso", "mpf", "eia", "txt", "min" }); _extension.Text = string.IsNullOrWhiteSpace(currentExtension) ? "nc" : currentExtension.TrimStart('.'); _extension.Dock = DockStyle.Fill; _extension.TextChanged += (s, e) => UpdatePreview(); nameTable.Controls.Add(_extension, 2, 1);
            nameGroup.Controls.Add(nameTable); Controls.Add(nameGroup);

            _preview.Dock = DockStyle.Top; _preview.Height = 45; _preview.Padding = new Padding(14, 6, 10, 0); Controls.Add(_preview); UpdatePreview();
            var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 52, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8) };
            var cancel = new Button { Text = "Cancelar", DialogResult = DialogResult.Cancel, AutoSize = true }; var ok = new Button { Text = "Aceptar", DialogResult = DialogResult.OK, AutoSize = true };
            ok.Click += (s, e) => { if (_files.Items.Count == 0) { MessageBox.Show("Selecciona al menos un postprocesador.", Text); DialogResult = DialogResult.None; return; } if (string.IsNullOrWhiteSpace(NcExtension) || NcExtension.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) { MessageBox.Show("Introduce una extensión válida, sin punto.", Text); DialogResult = DialogResult.None; } };
            bottom.Controls.Add(cancel); bottom.Controls.Add(ok); Controls.Add(bottom); AcceptButton = ok; CancelButton = cancel;
        }

        private static Button NewSquareButton(string text, string toolTip)
        {
            var button = new Button { Text = text, Width = 34, Height = 30, Margin = new Padding(2) }; new ToolTip().SetToolTip(button, toolTip); return button;
        }

        private void MoveSelected(int direction)
        {
            int from = _files.SelectedIndex; int to = from + direction;
            if (from < 0 || to < 0 || to >= _files.Items.Count) return;
            object item = _files.Items[from]; _files.Items.RemoveAt(from); _files.Items.Insert(to, item); _files.SelectedIndex = to;
        }

        private void UpdatePreview()
        {
            string extension = NcExtension; _preview.Text = "Vista previa: " + Path.Combine(_folder.Text, _name.Text + (string.IsNullOrWhiteSpace(extension) ? "" : "." + extension));
        }

        private void AddFiles(object sender, EventArgs e)
        {
            using (var d = new OpenFileDialog { Title = "Seleccionar postprocesadores", InitialDirectory = _initialPostFolder, Multiselect = true, Filter = "Postprocesadores|*.asc;*.pst;*.post;*.dll|Todos los archivos|*.*" })
                if (d.ShowDialog(this) == DialogResult.OK) foreach (string f in d.FileNames) if (!_files.Items.Contains(f)) _files.Items.Add(f);
        }

        private void FilesMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            if (_files.IndexFromPoint(e.Location) == ListBox.NoMatches) AddFiles(sender, EventArgs.Empty);
        }
    }
}
