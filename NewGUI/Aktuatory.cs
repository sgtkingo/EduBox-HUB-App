using System;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Windows.Forms;


namespace NewGUI
{
    public partial class Aktuatory : UserControl
    {
        private Timer comPortWatcherTimer;                         // Kontrola přítomnosti COM zařízení
        private List<string> lastKnownPorts = new List<string>();
        private string BasePath = Directory.GetParent(Application.StartupPath).Parent.Parent.FullName;
        private Timer delayedSendTimer;                            // Timer pro jednorázové zpožděné odeslání

        // --- NOVĚ: JSON datový model místo CSV DataTable ---
        private List<Komponenty> aktuatoryData; // Načtené položky z aktuatory.json
                                               
        // Serial controller
        private SerialController _serialController;

        // Image manager for actuator pictures
        private ImageManager _imageManager;

        private bool _suppressAfterReset = false;
        private Timer _resetQuietTimer;

        public Aktuatory(Form1 rodic)
        {
            InitializeComponent();
            LoadJsonData();                                        // ⟵ místo LoadCsvData()

            // Initialize image manager (pictureBox1 is created by InitializeComponent)
            _imageManager = new ImageManager(pictureBox1);

            // Inicializace SerialController
            _serialController = new SerialController();

            // Kontrola COM portů
            comPortWatcherTimer = new Timer();
            comPortWatcherTimer.Interval = 500;
            comPortWatcherTimer.Tick += ComPortWatcherTimer_Tick;
            comPortWatcherTimer.Start();
            ComPortWatcherTimer_Tick(null, EventArgs.Empty);

            // skrytí textboxů a labelů
            textBox1.Visible = false;
            textBox2.Visible = false;
            textBox3.Visible = false;

            label1.Visible = false;
            label2.Visible = false;
            label3.Visible = false;

            // Timer pro jednorázové odložené odeslání
            delayedSendTimer = new Timer();
            delayedSendTimer.Interval = 1000; // 1 s
            delayedSendTimer.Tick += DelayedSendTimer_Tick;

            // aby se UI pinů přepínalo při změně módu/aktuátoru/vstupu
            ModBox.SelectedIndexChanged += (s, e) => { UpdatePinInputsUi_Actuators(); UpdateStartEnabled_Actuators(); };
            AktBox.SelectedIndexChanged += (s, e) => { UpdatePinInputsUi_Actuators(); UpdateStartEnabled_Actuators(); };

            textBox1.TextChanged += (s, e) => UpdateStartEnabled_Actuators();
            textBox2.TextChanged += (s, e) => UpdateStartEnabled_Actuators();
            textBox3.TextChanged += (s, e) => UpdateStartEnabled_Actuators();
            textBox4.TextChanged += (s, e) => UpdateStartEnabled_Actuators();

            _serialController.RawLineReceived += (_, e) => AppendLineToMainTextBox(e.Line);
            _serialController.DataFrameReceived += (_, e) => AppendLineToMainTextBox(e.Line); // pokud chceš i datové rámce
            _serialController.InitReceived += (_, e) => AppendLineToMainTextBox(e.Payload);

            _resetQuietTimer = new Timer { Interval = 3000 };
            _resetQuietTimer.Tick += (s, e) => { _suppressAfterReset = false; _resetQuietTimer.Stop(); };

            UpdatePinInputsUi_Actuators();
            UpdateStartEnabled_Actuators();

            // pokud nemáš v Designeru přiřazený event:

            // Default zobrazení
            SetControlButtonsEnabled(false);
        }

        // ---------- COM PORT WATCHER ----------
        private void ComPortWatcherTimer_Tick(object sender, EventArgs e)
        {
            var currentPorts = SerialPort.GetPortNames().ToList();
            if (!currentPorts.Contains("COM22", StringComparer.OrdinalIgnoreCase))
            {
                currentPorts.Insert(0, "COM22");
            }
            if (!currentPorts.SequenceEqual(lastKnownPorts))
            {
                string selected = ComBox.SelectedItem as string;
                ComBox.Items.Clear();
                ComBox.Items.AddRange(currentPorts.ToArray());

                if (selected != null && currentPorts.Contains(selected))
                {
                    ComBox.SelectedItem = selected;
                }
                else
                {
                    ComBox.SelectedItem = "COM22";
                    if (ComBox.SelectedIndex < 0 && ComBox.Items.Count > 0)
                        ComBox.SelectedIndex = 0;
                }
                lastKnownPorts = currentPorts;
            }
        }
        private void AppendLineToMainTextBox(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return;
            if (!line.EndsWith("\r\n")) line += "\r\n";

            void Write()
            {
                MainTextBox.AppendText(line);
                MainTextBox.SelectionStart = MainTextBox.TextLength;
                MainTextBox.ScrollToCaret();
            }

            if (InvokeRequired) BeginInvoke((Action)Write);
            else Write();
        }
        // ---------- NOVĚ: NAČTENÍ JSON MÍSTO CSV ----------
        private void LoadJsonData()
        {
            try
            {
                string jsonPath = Path.Combine(Application.StartupPath, "Aktuatory.json");
                if (!File.Exists(jsonPath))
                {
                    string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                    string alt = Path.Combine(baseDir, "Aktuatory.json");
                    if (File.Exists(alt)) jsonPath = alt;
                    else
                    {
                        string proj = Path.Combine(@"D:\GitHub\Aplikace PC EduHub - old version\NewGUI", "Aktuatory.json");
                        if (File.Exists(proj)) jsonPath = proj;
                        else
                        {
                            string rel = Path.Combine(@"D:\GitHub\Aplikace PC EduHub - old version\NewGUI\bin\Release", "Aktuatory.json");
                            if (File.Exists(rel)) jsonPath = rel;
                        }
                    }
                }
                if (!File.Exists(jsonPath))
                {
                    return;
                }

                string jsonText = File.ReadAllText(jsonPath);

                // Deserializace do List<Komponenty>
                aktuatoryData = JsonSerializer.Deserialize<List<Komponenty>>(jsonText, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    NumberHandling = JsonNumberHandling.AllowReadingFromString
                }) ?? new List<Komponenty>();

                AktBox.Items.Clear();
                foreach (var a in aktuatoryData)
                {
                    if (!string.IsNullOrWhiteSpace(a.Alias))
                        AktBox.Items.Add(a.Alias);
                }

                pictureBox1.SizeMode = PictureBoxSizeMode.Zoom;
                AktBox.SelectedIndexChanged -= AktBox_UpdateImage;
                AktBox.TextChanged -= AktBox_UpdateImage;
                AktBox.SelectedIndexChanged += AktBox_UpdateImage;
                AktBox.TextChanged += AktBox_UpdateImage;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Chyba při načítání JSON: {ex.Message}");
            }
        }
        // Získá „zobrazovací alias“ – priorita: Alias (type) → Alias → Znackeni
        private static string GetDisplayAlias(Komponenty a)
        {
            return (a.Alias ?? string.Empty).Trim();
        }

        // Pomocná metoda: robustně získá alias z ComboBoxu
        private string GetSelectedAlias()
        {
            // preferuj Text (uživatel může psát / ComboBox není DropDownList)
            var text = AktBox.Text?.Trim();
            if (!string.IsNullOrWhiteSpace(text)) return text;

            // fallback na SelectedItem
            return AktBox.SelectedItem?.ToString();
        }

        // Najde položku v JSONu podle aliasu
        private Komponenty FindByDisplayAlias(string alias)
        {
            if (string.IsNullOrWhiteSpace(alias) || aktuatoryData == null)
                return null;

            return aktuatoryData.FirstOrDefault(a =>
                string.Equals(a.Alias?.Trim(), alias.Trim(), StringComparison.OrdinalIgnoreCase));
        }
        // ---------- OBRÁZEK PODLE VÝBĚRU ----------
        private void AktBox_UpdateImage(object sender, EventArgs e)
        {
            try
            {
                string label = AktBox.Text;
                if (string.IsNullOrWhiteSpace(label))
                {
                    pictureBox1.Image?.Dispose();
                    pictureBox1.Image = null;
                    return;
                }

                _imageManager.UpdateImageForLabel(label, "Aktuátory_ikony", BasePath);
            }
            catch
            {
                // ticho
            }
        }

        // Při zavření (pokud tuhle událost někde připojuješ)
        private void Aktuator_Closing(object sender, FormClosedEventArgs e)
        {
            _serialController?.Close();
        }

        // ---------- PŘIPOJENÍ/ODPOJENÍ ----------
        private void btnConnect_Click(object sender, EventArgs e)
        {
            if (!_serialController.IsOpen)
            {
                if (ComBox.SelectedItem == null)
                {
                    MessageBox.Show("Vyberte COM port.");
                    return;
                }

                try
                {
                    _serialController.ConfigurePort(
                        portName: ComBox.SelectedItem.ToString(),
                        baudRate: 115200,
                        parity: Parity.None,
                        dataBits: 8,
                        stopBits: StopBits.One,
                        handshake: Handshake.None,
                        newLine: "\n"
                    );

                    // V Aktuátorech RX nepotřebujeme – případně:
                    // SerialManager.Instance.AttachExclusiveReceiver(Aktuatory_DataReceived);

                    _serialController.Open();
                    try { _serialController.WriteLine("?type=INIT&api=1.4"); } catch { }

                    btnConnect.Text = "Odpojit";
                    SetControlButtonsEnabled(true);

                    badgeConn.Text = "Připojeno";
                    badgeConn.BackColor = Color.FromArgb(46, 125, 50);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Chyba při připojení: {ex.Message}");
                    badgeConn.Text = "Chyba";
                    badgeConn.BackColor = Color.FromArgb(211, 47, 47);
                }
            }
            else
            {
                try
                {
                    if (delayedSendTimer.Enabled) delayedSendTimer.Stop(); // zruš odložené odeslání
                    _serialController.Close();
                }
                finally
                {
                    btnConnect.Text = "Připojit";
                    badgeConn.Text = "Nepřipojeno";
                    badgeConn.BackColor = Color.FromArgb(107, 114, 128);

                    SetControlButtonsEnabled(false);
                }
            }
        }

        // ---------- UI pro CONFIG parametry ----------
        private void ShowTextBoxesForRequest(string request)
        {
            // reset UI
            textBox1.Visible = textBox2.Visible = textBox3.Visible = false;
            label1.Visible = label2.Visible = label3.Visible = false;
            textBox1.Text = textBox2.Text = textBox3.Text = string.Empty;

            // vše za "id="
            var match = Regex.Match(request, @"\bid=[^&]*&(.+)");
            if (!match.Success) return;

            var paramString = match.Groups[1].Value;
            var parameters = paramString.Split('&');

            for (int i = 0; i < parameters.Length && i < 3; i++)
            {
                var kv = parameters[i].Split(new[] { '=' }, 2);
                if (kv.Length < 1) continue;

                string key = kv[0].Trim();
                string val = kv.Length > 1 ? kv[1].Trim() : "";

                if (i == 0)
                {
                    label1.Text = key; label1.Visible = true;
                    textBox1.Text = val; textBox1.Visible = true;
                }
                else if (i == 1)
                {
                    label2.Text = key; label2.Visible = true;
                    textBox2.Text = val; textBox2.Visible = true;
                }
                else if (i == 2)
                {
                    label3.Text = key; label3.Visible = true;
                    textBox3.Text = val; textBox3.Visible = true;
                }
            }
            UpdateStartEnabled_Actuators();
        }
        // ---------- Dosazení hodnot z textboxů do requestu ----------
        private string UpdateRequestWithTextBoxValues(string originalRequest)
        {
            var pattern = @"(?<key>[^&=?]+)=(?<value>[^&]*)";
            var matches = Regex.Matches(originalRequest, pattern);

            var dict = new Dictionary<string, string>();
            foreach (Match match in matches)
                dict[match.Groups["key"].Value] = match.Groups["value"].Value;

            if (textBox1.Visible && label1.Visible)
                dict[label1.Text] = textBox1.Text;
            if (textBox2.Visible && label2.Visible)
                dict[label2.Text] = textBox2.Text;
            if (textBox3.Visible && label3.Visible)
                dict[label3.Text] = textBox3.Text;

            string basePart = originalRequest.Split('?')[0];
            string typeAndId = Regex.Match(originalRequest, @"\?[^&]+&[^&]+").Value;

            var newParams = dict
                .Where(kvp => !typeAndId.Contains($"{kvp.Key}="))
                .Select(kvp => $"{kvp.Key}={kvp.Value}");

            return basePart + typeAndId + (newParams.Any() ? "&" + string.Join("&", newParams) : "");
        }
        // ---------- START / STOP ----------
        private void btnStart_Click(object sender, EventArgs e)
        {
            if (btnStart.Text == "Spustit")
            {
                if (!_serialController.IsOpen)
                {
                    MessageBox.Show("Nejste připojen k žádnému COM portu.");
                    return;
                }

                string selectedMod = ModBox.SelectedItem?.ToString();
                string selectedAlias = AktBox.SelectedItem?.ToString();


                // --- CONNECT / DISCONNECT (one-shot) ---
                if (string.Equals(selectedMod, "CONNECT", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(selectedMod, "DISCONNECT", StringComparison.OrdinalIgnoreCase))
                {
                    if (string.IsNullOrEmpty(selectedAlias))
                    {
                        MessageBox.Show("Vyberte aktuátor.");
                        return;
                    }

                    var item = FindByDisplayAlias(selectedAlias);
                    if (item == null)
                    {
                        MessageBox.Show("Alias nebyl nalezen v JSONu.");
                        return;
                    }

                    // ID pro request – ideálně přímo z objektu (pokud máš string Id), jinak fallback z Request_CONFIG
                    string idRaw = item.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);

                    if (string.IsNullOrWhiteSpace(idRaw))
                    {
                        var m = Regex.Match(item.Request_CONFIG ?? string.Empty, @"\bid=([^&]+)");
                        idRaw = m.Success ? m.Groups[1].Value : null;
                    }

                    string idTwoDigits = FormatTwoDigitId(idRaw);
                    if (string.IsNullOrWhiteSpace(idTwoDigits))
                    {
                        MessageBox.Show("Nelze zjistit ID aktuátoru.");
                        return;
                    }

                    var pinQuery = BuildPinQueryActuator4(item);
                    if (string.IsNullOrWhiteSpace(pinQuery))
                    {
                        MessageBox.Show("Doplňte požadované piny.");
                        return;
                    }

                    // POZOR: formát s čárkami mezi pinX=... částmi (přesně jak požaduješ)
                    string request = $"?type={selectedMod.ToUpperInvariant()}&id=A{idTwoDigits}&{pinQuery}";

                    try
                    {
                        _serialController.WriteLine(request);
                        MainTextBox.Clear();
                        MainTextBox.AppendText(request + Environment.NewLine);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Chyba při odesílání: {ex.Message}");
                    }
                    // one-shot: NEpřepínáme tlačítko na „Zastavit"
                    return;
                }
                // CONFIG/UPDATE apod.
                if (string.IsNullOrEmpty(selectedAlias))
                {
                    MessageBox.Show("Vyberte aktuátor.");
                    return;
                }

                var it = FindByDisplayAlias(selectedAlias);
                if (it == null)
                {
                    MessageBox.Show("Alias nebyl nalezen v JSONu.");
                    return;
                }

                string requestOriginal = it.Request_CONFIG;
                if (string.IsNullOrWhiteSpace(requestOriginal))
                {
                    MessageBox.Show("V JSONu chybí Request pro vybraný aktuátor.");
                    return;
                }

                // Přepiš pouze type=..., id zůstává z JSONu
                string requestFinal = Regex.Replace(requestOriginal, @"type=[^&]+", $"type={selectedMod}");
                requestFinal = UpdateRequestWithTextBoxValues(requestFinal);

                try
                {
                    _serialController.WriteLine(requestFinal);
                    MainTextBox.Clear();
                    MainTextBox.AppendText(requestFinal + Environment.NewLine);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Chyba při odesílání: {ex.Message}");
                }

            }
            else
            {
                btnStart.Text = "Spustit";
                this.btnStart.BackColor = Color.FromArgb(15, 108, 189);
                this.btnStart.FlatAppearance.BorderColor = Color.FromArgb(15, 108, 189);
                this.btnStart.FlatAppearance.MouseDownBackColor = Color.FromArgb(17, 94, 163);
                this.btnStart.FlatAppearance.MouseOverBackColor = Color.FromArgb(12, 83, 146);

                if (delayedSendTimer.Enabled)
                {
                    delayedSendTimer.Stop();
                    MainTextBox.AppendText("Odeslání bylo zrušeno tlačítkem STOP.\r\n");
                }
            }
            UpdateStartEnabled_Actuators();

        }

        // ---------- ODLOŽENÉ ODESLÁNÍ ----------
        private void DelayedSendTimer_Tick(object sender, EventArgs e)
        {
            delayedSendTimer.Stop(); // jednorázové odeslání

            if (!_serialController.IsOpen)
                return;

            string selectedAlias = AktBox.SelectedItem?.ToString();
            string selectedMod = ModBox.SelectedItem?.ToString();

            if (string.IsNullOrEmpty(selectedAlias) || string.IsNullOrEmpty(selectedMod))
                return;

            var item = FindByDisplayAlias(selectedAlias);
            if (item == null)
                return;

            string request = item.Request_CONFIG;
            if (string.IsNullOrWhiteSpace(request))
                return;

            request = Regex.Replace(request, @"type=[^&]+", $"type={selectedMod}");
            request = UpdateRequestWithTextBoxValues(request);

            try
            {
                _serialController.WriteLine(request);

                MainTextBox.Clear();
                MainTextBox.AppendText($"Odesláno po zpoždění: {request}{Environment.NewLine}");
            }
            catch (Exception ex)
            {
                MainTextBox.Clear();
                MainTextBox.AppendText($"Chyba při odesílání: {ex.Message}{Environment.NewLine}");
            }
        }
        private void ModBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            // reset viditelnosti – přepneme dle módu
            textBox1.Visible = textBox2.Visible = textBox3.Visible = textBox4.Visible = false;
            label1.Visible = label2.Visible = label3.Visible = label4.Visible = false;

            UpdatePinInputsUi_Actuators();
            UpdateStartEnabled_Actuators();
        }

        private void AktBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (AktBox.SelectedItem == null || aktuatoryData == null)
                return;

            string selectedAlias = AktBox.SelectedItem.ToString();

            var item = FindByDisplayAlias(selectedAlias);
            if (item == null) return;

            string request = item.Request_CONFIG;
            if (!string.IsNullOrWhiteSpace(request) && ModBox.Text == "CONFIG")
            {
                ShowTextBoxesForRequest(request);
            }
        }

        private void SetControlButtonsEnabled(bool enabled)
        {
            btnStart.Enabled = enabled;
            ModBox.Enabled = enabled;
            AktBox.Enabled = enabled;
            textBox1.Enabled = enabled;
            textBox2.Enabled = enabled;
            textBox3.Enabled = enabled;
            textBox4.Enabled = enabled; 
        }

        // Najde vybraný aktuátor podle AktBox
        // Vyhledá vybraný aktuátor
        private Komponenty FindSelectedActuator()
        {
            var alias = AktBox.SelectedItem?.ToString();
            return FindByDisplayAlias(alias);
        }

        // Normalizace vstupu pinu (stejně jako u senzorů)
        private static string NormalizePinInput(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return null;
            input = input.Trim();
            var digits = new string(input.Where(char.IsDigit).ToArray());
            return string.IsNullOrEmpty(digits) ? input : digits;
        }

        private string BuildPinQueryActuator4(Komponenty item)
        {
            if (item == null) return null;

            // Které piny aktuátor podle JSONu vyžaduje
            bool has1 = !string.IsNullOrWhiteSpace(item.PIN1);
            bool has2 = !string.IsNullOrWhiteSpace(item.PIN2);
            bool has3 = !string.IsNullOrWhiteSpace(item.PIN3);
            bool has4 = !string.IsNullOrWhiteSpace(item.PIN4);

            // Hodnoty z UI (pouze pro piny, které daný aktuátor má)
            string p1 = has1 ? NormalizePinInput(textBox1.Text) : null;
            string p2 = has2 ? NormalizePinInput(textBox2.Text) : null;
            string p3 = has3 ? NormalizePinInput(textBox3.Text) : null;
            string p4 = has4 ? NormalizePinInput(textBox4.Text) : null;

            // Pokud je některý vyžadovaný pin prázdný, nevracej nic (volající to ošetří)
            if (has1 && string.IsNullOrWhiteSpace(p1)) return null;
            if (has2 && string.IsNullOrWhiteSpace(p2)) return null;
            if (has3 && string.IsNullOrWhiteSpace(p3)) return null;
            if (has4 && string.IsNullOrWhiteSpace(p4)) return null;

            // Poskládej jen existující/požadované piny ve správném pořadí
            var pins = new List<string>();
            if (has1) pins.Add(p1);
            if (has2) pins.Add(p2);
            if (has3) pins.Add(p3);
            if (has4) pins.Add(p4);

            // 1 pin -> "pin=V"
            if (pins.Count == 1)
                return $"pin={pins[0]}";

            // 2–4 piny -> "pins=V1,V2[,V3,V4]"
            return $"pins={string.Join(",", pins)}";
        }

        // Přepíná UI pro CONNECT/DISCONNECT (zobrazení až 4 pinů podle JSONu)
        private void UpdatePinInputsUi_Actuators()
        {
            // výchozí skrytí
            textBox1.Visible = textBox2.Visible = textBox3.Visible = textBox4.Visible = false;
            label1.Visible = label2.Visible = label3.Visible = label4.Visible = false;

            var mode = ModBox.Text?.Trim();

            // CONFIG – ponecháváš existující ShowTextBoxesForRequest
            if (string.Equals(mode, "CONFIG", StringComparison.OrdinalIgnoreCase))
            {
                var itCfg = FindSelectedActuator();
                if (itCfg != null && !string.IsNullOrWhiteSpace(itCfg.Request_CONFIG))
                    ShowTextBoxesForRequest(itCfg.Request_CONFIG);
                return;
            }

            // CONNECT/DISCONNECT – ukaž piny dle JSONu
            bool isConn = string.Equals(mode, "CONNECT", StringComparison.OrdinalIgnoreCase) ||
                          string.Equals(mode, "DISCONNECT", StringComparison.OrdinalIgnoreCase);

            if (!isConn) return;

            var item = FindSelectedActuator();
            if (item == null) return;

            if (!string.IsNullOrWhiteSpace(item.PIN1))
            {
                label1.Text = item.PIN1;
                label1.Visible = true; textBox1.Visible = true;
            }
            if (!string.IsNullOrWhiteSpace(item.PIN2))
            {
                label2.Text = item.PIN2;
                label2.Visible = true; textBox2.Visible = true;
            }
            if (!string.IsNullOrWhiteSpace(item.PIN3))
            {
                label3.Text = item.PIN3;
                label3.Visible = true; textBox3.Visible = true;
            }
            if (!string.IsNullOrWhiteSpace(item.PIN4))
            {
                label4.Text = item.PIN4;
                label4.Visible = true; textBox4.Visible = true;
            }
        }

        // Povolení Start – hlídá vyžadované piny 1–4
        private void UpdateStartEnabled_Actuators()
        {
            bool connected = _serialController.IsOpen;
            bool hasMode = !string.IsNullOrWhiteSpace(ModBox.Text);
            bool hasAct = AktBox.SelectedItem != null;

            bool ready = connected && hasMode;
            string m = ModBox.Text?.Trim() ?? string.Empty;

            if (m.Equals("CONNECT", StringComparison.OrdinalIgnoreCase) ||
                m.Equals("DISCONNECT", StringComparison.OrdinalIgnoreCase))
            {
                var item = FindSelectedActuator();
                if (item == null) { btnStart.Enabled = false; return; }

                bool need1 = !string.IsNullOrWhiteSpace(item.PIN1);
                bool need2 = !string.IsNullOrWhiteSpace(item.PIN2);
                bool need3 = !string.IsNullOrWhiteSpace(item.PIN3);
                bool need4 = !string.IsNullOrWhiteSpace(item.PIN4);

                bool p1ok = !need1 || !string.IsNullOrWhiteSpace(NormalizePinInput(textBox1.Text));
                bool p2ok = !need2 || !string.IsNullOrWhiteSpace(NormalizePinInput(textBox2.Text));
                bool p3ok = !need3 || !string.IsNullOrWhiteSpace(NormalizePinInput(textBox3.Text));
                bool p4ok = !need4 || !string.IsNullOrWhiteSpace(NormalizePinInput(textBox4.Text));

                ready = ready && hasAct && p1ok && p2ok && p3ok && p4ok;
            }
            else if (m.Equals("CONFIG", StringComparison.OrdinalIgnoreCase))
            {
                // všechny viditelné textboxy musí být vyplněné
                bool t1ok = !textBox1.Visible || !string.IsNullOrWhiteSpace(textBox1.Text?.Trim());
                bool t2ok = !textBox2.Visible || !string.IsNullOrWhiteSpace(textBox2.Text?.Trim());
                bool t3ok = !textBox3.Visible || !string.IsNullOrWhiteSpace(textBox3.Text?.Trim());
                bool t4ok = !textBox4.Visible || !string.IsNullOrWhiteSpace(textBox4.Text?.Trim());

                ready = ready && hasAct && t1ok && t2ok && t3ok && t4ok;
            }
            else
            {
                ready = ready && hasAct;
            }

            btnStart.Enabled = ready;
        }

        // Vrátí pouze číslice z ID, doplněné zleva na 2 znaky (01–09), 10+ zůstane beze změny.
        private static string FormatTwoDigitId(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            string digitsOnly = new string(raw.Where(char.IsDigit).ToArray());
            if (string.IsNullOrEmpty(digitsOnly)) return null;
            return digitsOnly.PadLeft(2, '0');
        }

        private void reset_btn_Click(object sender, EventArgs e)
        {
            PerformResetActuator();
        }
        private void PerformResetActuator()
        {
            if (!_serialController.IsOpen)
            {
                MessageBox.Show("Nelze RESET – není připojen COM port.");
                return;
            }

            // nutný vybraný aktuátor pro cílený reset (nebo můžeš poslat wildcard - uprav podle potřeby)


            string alias = AktBox.SelectedItem?.ToString();
            if (string.IsNullOrWhiteSpace(alias))
            {
                MessageBox.Show("Vyber aktuátor před RESET.");
                return;
            }

            var item = FindByDisplayAlias(alias);
            if (item == null)
            {
                MessageBox.Show("Aktuátor nenalezen v datech.");
                return;
            }

            // Sestavení RESET requestu (analogicky k senzorům) – pokud máš Request_RESET v JSONu použij ho
            string idRaw = item.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);
            string twoDigits = FormatTwoDigitId(idRaw);
            if (string.IsNullOrWhiteSpace(twoDigits))
            {
                MessageBox.Show("Nelze zjistit ID aktuátoru.");
                return;
            }

            string request = $"?type=RESET&id=A{twoDigits}";

            try
            {
                _serialController.WriteLine(request);
                MainTextBox.AppendText(request + Environment.NewLine);
            }
            catch (Exception ex)
            {
                MainTextBox.AppendText("Chyba při RESET: " + ex.Message + Environment.NewLine);
            }

            // UI cleanup
            pictureBox1.Image?.Dispose();
            pictureBox1.Image = null;

            AktBox.SelectedIndex = -1;
            ModBox.SelectedIndex = -1;

            textBox1.Visible = textBox2.Visible = textBox3.Visible = textBox4.Visible = false;
            label1.Visible = label2.Visible = label3.Visible = label4.Visible = false;
            textBox1.Text = textBox2.Text = textBox3.Text = textBox4.Text = string.Empty;

            btnStart.Enabled = false;

            // vyčistit hlavní výpis (nebo ponechat – zde volíme nový začátek)
            // pokud chceš zachovat historii, tento řádek vynech:
            // MainTextBox.Clear();

            _suppressAfterReset = true;
            _resetQuietTimer.Stop();
            _resetQuietTimer.Start();
        }
    }
}
