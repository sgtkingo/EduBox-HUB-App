using System;                                                   // Základní typy a události
using System.Collections.Generic;                               // Kolekce jako List<>, Dictionary<> 
using System.Data;                                              // (aktuálně nepoužito)
using System.Drawing;                                           // Barvy a grafické typy (pro graf/obrázky)
using System.Linq;                                              // LINQ operace
using System.Text;                                              // StringBuilder a textové utility
using System.Threading.Tasks;                                   // async/await Task
using System.Windows.Forms;                                     // WinForms UI
using System.IO.Ports;                                          // Sériová komunikace (SerialPort)
using System.Windows.Forms.DataVisualization.Charting;          // Ovládací prvek Chart
using System.IO;                                                // Práce se soubory a cestami
using System.Text.Json;                                         // JSON serializace/deserializace


namespace NewGUI
{
    public partial class Senzory : UserControl
    {
        private bool isSendingRequest = false;
        public string request;
        private string lastUsedID = null;

        private Timer comPortWatcherTimer;
        private List<string> lastKnownPorts = new List<string>();
        private readonly Dictionary<string, string> sensorIdMap
            = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // serial controller (encapsulates SerialManager + SerialParser)
        private SerialController _serialController;

        private System.Threading.CancellationTokenSource _sendCts;

        private List<Komponenty> SenzoryData;
        private string _lastSentMode = null;

        // Popup okna
        private SerialPopupForm _linkForm;   // vše mimo INIT
        private SerialPopupForm _initForm;   // jen INIT odpověď
        private PinsSelect _pinsForm;         // výběr pinů

        // Host form pro popup (používáme pro odhlašování handlerů)
        private Form _popupHost;

        // INIT stav
        private bool _awaitingInitResponse = false; // čekám na odpověď INIT?
        private bool _initRequestSent = false;      // byl odeslán INIT request?
        private string _lastInitPayload = null;     // poslední přijatá INIT odpověď (řetězec)

        // do třídy Senzory:
        private readonly StringBuilder _linkBuffer = new StringBuilder();
        private readonly StringBuilder _initBuffer = new StringBuilder();   // jen INIT odpovědi

        // UI managers
        private ValueDisplayManager _valueDisplayManager;
        private ChartManager _chartManager;
        private ImageManager _imageManager; // NEW: replace old image-loading method

        private const string ApiVersion = VscpProtocol.ApiVersion;
        private Timer _resetHoldTimer;
        private bool _suppressNextResetClick = false;

        // NEW: track if RESET can be performed (enabled only after user inputs params or after first data receipt)
        private bool _canReset = false;

        public Senzory(Form1 rodic)
        {
            InitializeComponent();
            InitializeChart();

            comboBoxTIMER.SelectedIndex = 1;

            comboBoxTIMER.SelectedIndexChanged += (s, e) => ApplyTimerIntervalFromUi();
            ApplyTimerIntervalFromUi();

            comPortWatcherTimer = new Timer();
            comPortWatcherTimer.Interval = 500;
            comPortWatcherTimer.Tick += ComPortWatcherTimer_Tick;
            comPortWatcherTimer.Start();
            ComPortWatcherTimer_Tick(null, EventArgs.Empty);

            // serial controller must exist before UI queries IsOpen
            _serialController = new SerialController();
            _serialController.InitReceived += Parser_InitReceived;
            _serialController.PeerDisconnected += (_, e) =>
            {
                if (!IsDisposed && IsHandleCreated) BeginInvoke((Action)(() =>
                {
                    StopSendingRequest();
                    SetUiForConnection(false);
                    comboBoxCOM.Enabled = false;
                    ConnectBtn.Text = "Obnovit";
                    UiLog("Device closed the session (BYE).");
                }));
            };
            _serialController.DataFrameReceived += Parser_DataFrameReceived;
            _serialController.RawLineReceived += Parser_RawLineReceived;

            SetUiForConnection(false);

            LoadSensorsFromJson();

            if (comboBoxSensor.Items.Contains("DHT11"))
                comboBoxSensor.SelectedItem = "DHT11";
            else if (comboBoxSensor.Items.Count > 0)
                comboBoxSensor.SelectedIndex = 0;

            if (comboBoxMode.Items.Contains("UPDATE"))
                comboBoxMode.SelectedItem = "UPDATE";
            else if (comboBoxMode.Items.Count > 0)
                comboBoxMode.SelectedIndex = 0;

            pictureBox1.SizeMode = PictureBoxSizeMode.Zoom;

            // initialize ImageManager to handle image updates
            _imageManager = new ImageManager(pictureBox1);

            comboBoxSensor.SelectedIndexChanged += comboBoxSensor_SelectedIndexChanged;
            comboBoxSensor.SelectedIndexChanged += (s, e) => UpdateRequestFromUi();
            comboBoxMode.SelectedIndexChanged += (s, e) => UpdateRequestFromUi();

            // make comboBox non-editable and owner-drawn so we can detect hovered/selected item in dropdown and show image
            comboBoxSensor.DropDownStyle = ComboBoxStyle.DropDownList;
            comboBoxSensor.DrawMode = DrawMode.OwnerDrawFixed;
            comboBoxSensor.DrawItem += ComboBoxSensor_DrawItem;
            comboBoxSensor.DropDownClosed += ComboBoxSensor_DropDownClosed;

            // INIT stav reset při změně módu/senzoru
            comboBoxSensor.SelectedIndexChanged += (s, e) => { _initRequestSent = false; _lastInitPayload = null; UpdatePinInputsUi(); };
            comboBoxMode.SelectedIndexChanged += (s, e) => { _initRequestSent = false; _lastInitPayload = null; UpdatePinInputsUi(); };


            textPIN1.TextChanged += (s, e) => { UpdateRequestFromUi(); MarkResetAvailableFromParams(); };
            textPIN2.TextChanged += (s, e) => { UpdateRequestFromUi(); MarkResetAvailableFromParams(); };
            textPIN3.TextChanged += (s, e) => { UpdateRequestFromUi(); MarkResetAvailableFromParams(); };

            // value display manager (thread-safe updates)
            _valueDisplayManager = new ValueDisplayManager(valueText);

            // chart manager (will process frames and update chart on UI thread)
            _chartManager = new ChartManager(chart1, _valueDisplayManager, LogLink, intervalMs: 100, maxPoints: 50, maxSamples: 10000);
            _chartManager.MaxSamplesReached += ChartManager_MaxSamplesReached;
            ApplyTimerIntervalFromUi();

            // Note: SerialController already wires to SerialManager internally

            // reset hold timer for long-press on reset_btn
            _resetHoldTimer = new Timer { Interval = 3000 };
            _resetHoldTimer.Tick += ResetHoldTimer_Tick;

            // wire mouse events on reset button to detect long press
            try
            {
                reset_btn.MouseDown += ResetBtn_MouseDown;
                reset_btn.MouseUp += ResetBtn_MouseUp;
                reset_btn.MouseLeave += ResetBtn_MouseLeave;
            }
            catch { }
        }

        // DrawItem handler for comboBoxSensor: draw text and update image when item is highlighted
        private void ComboBoxSensor_DrawItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;

            // draw background/focus
            e.DrawBackground();

            string text = comboBoxSensor.Items[e.Index] as string ?? string.Empty;

            // choose text color based on state
            Color textColor = ((e.State & DrawItemState.Selected) == DrawItemState.Selected) ? SystemColors.HighlightText : comboBoxSensor.ForeColor;
            using (var brush = new SolidBrush(textColor))
            {
                var textRect = new RectangleF(e.Bounds.Left + 2, e.Bounds.Top + 2, e.Bounds.Width - 4, e.Bounds.Height - 4);
                e.Graphics.DrawString(text, e.Font, brush, textRect);
            }

            // when the item is highlighted (hover/selection in dropdown), update picture
            bool isSelected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
            if (isSelected)
            {
                try
                {
                    // reuse same baseDir logic as comboBoxSensor_SelectedIndexChanged
                    string baseDir = Application.StartupPath;
                    _imageManager?.UpdateImageForLabel(text, "Senzory_ikony", baseDir);
                }
                catch
                {
                    // swallow exceptions to avoid interfering with drawing
                }
            }

            e.DrawFocusRectangle();
        }

        private void link_btn_Click(object sender, EventArgs e)
        {
            var host = this.FindForm();

            if (_linkForm == null || _linkForm.IsDisposed)
            {
                _linkForm = new SerialPopupForm("Sériový výpis")
                {
                    Owner = host // nastavíme owner, aby popup byl "spojen" s hostem
                };
                _linkForm.FormClosed += LinkForm_FormClosed;

                // vždy nasyp aktuální buffer:
                if (_linkBuffer.Length > 0)
                    _linkForm.SetText(_linkBuffer.ToString());

                // umístění vedle hosta
                PositionNextToHost(_linkForm);

                // přihlásit se na pohyb/resize hosta, aby popup sledoval okno
                AttachPopupHostHandlers(host);

                _linkForm.Show();
                _linkForm.BringToFront();
                return;
            }

            if (_linkForm.Visible)
            {
                _linkForm.Hide();
            }
            else
            {
                // dorovnat stav (kdyby se v mezidobí buffer zvětšil):
                _linkForm.SetText(_linkBuffer.ToString());
                PositionNextToHost(_linkForm);
                _linkForm.Show();
                _linkForm.BringToFront();
            }
        }

        private void LinkForm_FormClosed(object sender, FormClosedEventArgs e)
        {
            // odhlásíme handlery a uvolníme reference
            DetachPopupHostHandlers();
            try { _linkForm.FormClosed -= LinkForm_FormClosed; } catch { }
            _linkForm = null;
        }

        private void AttachPopupHostHandlers(Form host)
        {
            if (host == null) return;

            // odhlásit předchozí hosta, pokud nějaký je
            DetachPopupHostHandlers();

            _popupHost = host;
            _popupHost.LocationChanged += PopupHost_PositionOrSizeChanged;
            _popupHost.SizeChanged += PopupHost_PositionOrSizeChanged;
            _popupHost.Move += PopupHost_PositionOrSizeChanged;
        }

        private void DetachPopupHostHandlers()
        {
            if (_popupHost == null) return;
            try { _popupHost.LocationChanged -= PopupHost_PositionOrSizeChanged; } catch { }
            try { _popupHost.SizeChanged -= PopupHost_PositionOrSizeChanged; } catch { }
            try { _popupHost.Move -= PopupHost_PositionOrSizeChanged; } catch { }
            _popupHost = null;
        }

        private void PopupHost_PositionOrSizeChanged(object sender, EventArgs e)
        {
            // pokud popup existuje a je zobrazené, posuňme ho vedle hosta
            if (_linkForm != null && !_linkForm.IsDisposed)
            {
                // Při minimalizaci/nezobrazitelnosti hosta můžeme popup skrýt, ale necháme to na tobě.
                PositionNextToHost(_linkForm);
            }
        }

        private void PositionNextToHost(Form form, int offsetX = 10)
        {
            var host = this.FindForm();
            if (host != null && form != null)
            {
                form.Left = host.Right + offsetX;
                form.Top = host.Top;
            }
        }
        private void ComboBoxSensor_DropDownClosed(object sender, EventArgs e)
        {
            // If no item is selected, clear picture
            if (comboBoxSensor.SelectedIndex < 0)
            {
                ClearPictureBoxImage();
            }
        }

        private void InitializeChart()
        {
            chart1.Series.Clear();

            if (chart1.ChartAreas.Count == 0)
            {
                var ca = new ChartArea("ChartArea1");
                ca.BackColor = Color.White;
                ca.BorderWidth = 0;
                chart1.ChartAreas.Add(ca);
            }

            var area = chart1.ChartAreas[0];
            area.AxisX.Title = "Počet vzorků";
            area.AxisX.TitleFont = new Font("Segoe UI Variable Text", 9F, FontStyle.Regular);
            area.AxisX.TitleForeColor = Color.FromArgb(90, 90, 95);
            area.AxisX.Minimum = 0;
            area.AxisX.Maximum = 10;
            area.AxisX.Interval = 1;
            area.AxisX.MajorGrid.LineColor = Color.FromArgb(230, 232, 236);
            area.AxisX.LineColor = Color.Gainsboro;

            area.AxisY.Title = "Hodnota";
            area.AxisY.TitleFont = new Font("Segoe UI Variable Text", 9F, FontStyle.Regular);
            area.AxisY.TitleForeColor = Color.FromArgb(90, 90, 95);
            area.AxisY.Minimum = 0;
            area.AxisY.Maximum = 100;
            area.AxisY.Interval = 20;
            area.AxisY.MajorGrid.LineColor = Color.FromArgb(230, 232, 236);
            area.AxisY.LineColor = Color.Gainsboro;

            if (chart1.Legends.Count == 0)
            {
                var leg = new Legend("Legend1")
                {
                    BackColor = Color.Transparent,
                    BorderWidth = 0,
                    Docking = Docking.Top,
                    Font = new Font("Segoe UI Variable Text", 8.25F, FontStyle.Regular),
                    IsTextAutoFit = false
                };
                chart1.Legends.Add(leg);
            }

            chart1.BringToFront();
        }

        private Komponenty FindSelectedComponent()
        {
            var label = comboBoxSensor.Text?.Trim();
            if (string.IsNullOrWhiteSpace(label) || SenzoryData == null) return null;
            return SenzoryData.FirstOrDefault(k =>
                string.Equals(k.Znaceni?.Trim(), label, StringComparison.OrdinalIgnoreCase));
        }

        private void UpdatePinInputsUi()
        {
            // Zapamatuj si, kde je focus a pozici kurzoru (pokud jsme v jednom z PIN textboxů)
            Control focused = this.FindForm()?.ActiveControl;
            TextBox focusedTb = null;
            int caret = 0;
            if (focused == textPIN1 || focused == textPIN2 || focused == textPIN3)
            {
                focusedTb = (TextBox)focused;
                caret = focusedTb.SelectionStart;
            }

            // Pomocná lokální funkce: nastav viditelnost jen když se mění
            void SetVis(Control c, bool vis)
            {
                if (c.Visible != vis) c.Visible = vis;
            }

            // NIC neschovávejte hromadně. Nejdřív spočítejte, co má být vidět:
            bool show1 = false, show2 = false, show3 = false;
            string mode = comboBoxMode.Text?.Trim();

            if (!string.IsNullOrWhiteSpace(mode) &&
                mode.Equals("CONFIG", StringComparison.OrdinalIgnoreCase))
            {
                var item = FindSelectedComponent();
                var configs = RequestBuilder.GetConfigNames(item);
                // note: UI label formatting kept local
                show1 = configs.Count >= 1;
                show2 = configs.Count >= 2;
                show3 = configs.Count >= 3;

                if (show1) PIN1.Text = (configs.Count >= 1) ? configs[0].Split(':')[0] + ":" : PIN1.Text;
                if (show2) PIN2.Text = (configs.Count >= 2) ? configs[1].Split(':')[0] + ":" : PIN2.Text;
                if (show3) PIN3.Text = (configs.Count >= 3) ? configs[2].Split(':')[0] + ":" : PIN3.Text;
            }
            else if (!string.IsNullOrWhiteSpace(mode) &&
                    (mode.Equals("CONNECT", StringComparison.OrdinalIgnoreCase) ||
                     mode.Equals("DISCONNECT", StringComparison.OrdinalIgnoreCase)))
            {
                var it2 = FindSelectedComponent();
                if (it2 != null)
                {
                    if (!string.IsNullOrWhiteSpace(it2.PIN1))
                    {
                        PIN1.Text = it2.PIN1;
                        show1 = true;
                    }
                    if (!string.IsNullOrWhiteSpace(it2.PIN2))
                    {
                        PIN2.Text = it2.PIN2;
                        show2 = true;

                        if (!show1)
                        {
                            PIN1.Text = "PIN1";
                            show1 = true;
                        }
                    }
                }
            }
            else
            {
                // jiné módy – PINy neukazuj
                show1 = show2 = show3 = false;
            }

            // A teď teprve „šetrně“ aplikuj viditelnost (jen když je změna)
            SetVis(PIN1, show1);
            SetVis(textPIN1, show1);
            SetVis(PIN2, show2);
            SetVis(textPIN2, show2);
            SetVis(PIN3, show3);
            SetVis(textPIN3, show3);

            // Vrať focus a caret, pokud to dává smysl
            if (focusedTb != null && focusedTb.Visible)
            {
                focusedTb.Focus();
                focusedTb.SelectionStart = Math.Min(caret, focusedTb.TextLength);
            }
        }

        private void UpdateRequestFromUi()
        {
            bool hasSensor = comboBoxSensor.SelectedIndex >= 0 && !string.IsNullOrWhiteSpace(comboBoxSensor.Text);
            bool hasMode = comboBoxMode.SelectedIndex >= 0 && !string.IsNullOrWhiteSpace(comboBoxMode.Text);
            bool connected = _serialController?.IsOpen == true;

            string m = comboBoxMode.Text?.Trim() ?? string.Empty;

            // delegate building request to RequestBuilder
            request = RequestBuilder.BuildRequest(m, comboBoxSensor.Text?.Trim(), sensorIdMap, FindSelectedComponent(), textPIN1.Text, textPIN2.Text, textPIN3.Text);

            label8.Text = request ?? string.Empty;

            // povolení Start
            bool ready = connected && hasMode;

            if (m.Equals("CONNECT", StringComparison.OrdinalIgnoreCase) || m.Equals("DISCONNECT", StringComparison.OrdinalIgnoreCase))
            {
                var item = FindSelectedComponent();
                bool needTwo = item != null && !string.IsNullOrWhiteSpace(item.PIN2);
                bool p1ok = !string.IsNullOrWhiteSpace(RequestBuilder.NormalizePinInput(textPIN1.Text));
                bool p2ok = !needTwo || !string.IsNullOrWhiteSpace(RequestBuilder.NormalizePinInput(textPIN2.Text));
                ready = ready && hasSensor && p1ok && p2ok;
            }
            else if (m.Equals("CONFIG", StringComparison.OrdinalIgnoreCase))
            {
                var item = FindSelectedComponent();
                var cfgs = RequestBuilder.GetConfigNames(item);
                bool c1 = cfgs.Count < 1 || !string.IsNullOrWhiteSpace(textPIN1.Text?.Trim());
                bool c2 = cfgs.Count < 2 || !string.IsNullOrWhiteSpace(textPIN2.Text?.Trim());
                bool c3 = cfgs.Count < 3 || !string.IsNullOrWhiteSpace(textPIN3.Text?.Trim());
                ready = ready && hasSensor && c1 && c2 && c3;
            }
            else if (m.Equals("RESET", StringComparison.OrdinalIgnoreCase))
            {
                ready = ready && hasSensor;
            }
            else if (!string.Equals(m, "INIT", StringComparison.OrdinalIgnoreCase))
            {
                ready = ready && hasSensor;
            }

            if (string.IsNullOrWhiteSpace(request))
                ready = false;

            button1.Enabled = ready;
            UpdateAcceptButton();
        }

        private int GetTimerIntervalMs()
        {
            string txt = comboBoxTIMER.Text?.Trim();
            if (string.IsNullOrWhiteSpace(txt)) return 100;
            // odstranit případné jednotky "ms"
            txt = txt.ToLowerInvariant().Replace("ms", string.Empty).Trim();
            int val;
            if (!int.TryParse(txt, out val) || val < 1) val = 100;
            return val;
        }

        private void ApplyTimerIntervalFromUi()
        {
            int delay = GetTimerIntervalMs();
            _chartManager?.SetInterval(delay);
        }

        private void SetUiForConnection(bool isConnected)
        {
            comboBoxCOM.Enabled = !isConnected;

            comboBoxSensor.Enabled = isConnected;
            comboBoxMode.Enabled = isConnected;
            comboBoxTIMER.Enabled = isConnected;

            button1.Enabled = false;

            if (ConnectBtn != null)
                ConnectBtn.Text = isConnected ? "Odpojit" : "Připojit";

            badgeConn.Text = isConnected ? "Připojeno" : "Nepřipojeno";
            badgeConn.BackColor = isConnected
                ? Color.FromArgb(46, 125, 50)
                : Color.FromArgb(107, 114, 128);

            UpdateRequestFromUi();
            if (!isConnected)
            {
                _initRequestSent = false;
                _lastInitPayload = null;
                _canReset = false;
            }

            UpdateResetEnabled();
            UpdateAcceptButton();
        }

        private void ComPortWatcherTimer_Tick(object sender, EventArgs e)
        {
            var currentPorts = SerialPort.GetPortNames().ToList();
            if (!currentPorts.Contains("COM22", StringComparer.OrdinalIgnoreCase))
            {
                currentPorts.Insert(0, "COM22");
            }

            if (!currentPorts.SequenceEqual(lastKnownPorts))
            {
                string selected = comboBoxCOM.SelectedItem as string;

                comboBoxCOM.Items.Clear();
                comboBoxCOM.Items.AddRange(currentPorts.ToArray());

                if (selected != null && currentPorts.Contains(selected))
                {
                    comboBoxCOM.SelectedItem = selected;
                }
                else
                {
                    comboBoxCOM.SelectedItem = "COM22";
                    if (comboBoxCOM.SelectedIndex < 0 && comboBoxCOM.Items.Count > 0)
                        comboBoxCOM.SelectedIndex = 0;
                }

                lastKnownPorts = currentPorts;
            }
        }

        private void ConnectBtn_Click(object sender, EventArgs e)
        {
            if (_serialController.IsOpen && _serialController.SessionClosed)
            {
                try
                {
                    _serialController.WriteLine(VscpProtocol.InitRequest);
                    SetUiForConnection(true);
                }
                catch (Exception ex) { MessageBox.Show($"Chyba obnovení relace: {ex.Message}"); }
                return;
            }
            if (_serialController.IsOpen)
            {
                try
                {
                    StopSendingRequest();
                    _serialController.Close();
                    UiLog("Odpojeno od portu.");
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Chyba při odpojování: {ex.Message}");
                }
                finally
                {
                    SetUiForConnection(false);
                    UpdateRequestFromUi();
                }
                return;
            }

            string selectedPort = comboBoxCOM.Text?.Trim();
            if (string.IsNullOrWhiteSpace(selectedPort))
            {
                selectedPort = "COM22";
                comboBoxCOM.SelectedItem = "COM22";
            }

            try
            {
                _serialController.ConfigurePort(
                    portName: selectedPort,
                    baudRate: 115200,
                    parity: Parity.None,
                    dataBits: 8,
                    stopBits: StopBits.One,
                    handshake: Handshake.None,
                    newLine: "\n"
                );

                // open and let controller attach
                _serialController.Open();

                SetUiForConnection(true);
                UiLog($"Připojeno k {selectedPort}.");
                try
                {
                    _serialController.WriteLine($"?type=INIT&api={ApiVersion}");
                    UiLog($"Odesláno:{Environment.NewLine}?type=INIT&api={ApiVersion}");
                }
                catch { }
                UpdateRequestFromUi();
            }
            catch (Exception ex)
            {
                SetUiForConnection(false);
                MessageBox.Show($"Chyba při otevírání portu: {ex.Message}");
                badgeConn.Text = "Chyba";
                badgeConn.BackColor = Color.FromArgb(211, 47, 47);
                UpdateRequestFromUi();
            }
        }



        private void UiLog(string msg)
        {
            LogLink(msg);
        }


        private void buttonStart_Click(object sender, EventArgs e)
        {
            UpdateRequestFromUi();

            string selectedPort = comboBoxCOM.Text?.Trim();
            string currentID = comboBoxSensor.Text?.Trim();
            string currentType = comboBoxMode.Text?.Trim();

            if (button1.Text == "Spustit")
            {
                // CONFIG = one-shot (stejné chování jako v Aktuátory): odešli a zůstaň ve stavu "Spustit".
                if (!string.IsNullOrWhiteSpace(currentType) && currentType.Equals("CONFIG", StringComparison.OrdinalIgnoreCase))
                {
                    if (string.IsNullOrWhiteSpace(selectedPort))
                    {
                        MessageBox.Show("Prosím vyber COM port.");
                        return;
                    }
                    if (_serialController == null || !_serialController.IsOpen)
                    {
                        MessageBox.Show("Nejprve se připoj k sériovému portu.");
                        return;
                    }
                    if (string.IsNullOrWhiteSpace(currentID))
                    {
                        MessageBox.Show("Prosím zadej nebo vyber ID zařízení.");
                        return;
                    }

                    try
                    {
                        if (string.IsNullOrWhiteSpace(request))
                        {
                            UiLog("Požadavek není sestaven.");
                            return;
                        }

                        _serialController.WriteLine(request);
                        UiLog($"Odesláno:{Environment.NewLine}{request}");
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Chyba při odesílání: {ex.Message}");
                    }

                    // UI nechat beze změny
                    UpdateRequestFromUi();
                    return;
                }

                var intendedMode = comboBoxMode.Text?.Trim() ?? "";
                bool wantsConn = intendedMode.Equals("CONNECT", StringComparison.OrdinalIgnoreCase)
                              || intendedMode.Equals("DISCONNECT", StringComparison.OrdinalIgnoreCase);

                if (wantsConn && button1.Text == "Zastavit")
                {
                    comboBoxSensor.Enabled = true;
                    comboBoxMode.Enabled = true;
                    comboBoxCOM.Enabled = true;
                    comboBoxTIMER.Enabled = true;
                    ConnectBtn.Enabled = true;
                    button1.Text = "Spustit";
                    UpdateAcceptButton();

                    StopSendingRequest();
                    UiLog("Měření pozastaveno (přepnutí na CONNECT/DISCONNECT).");

                    button1.BackColor = Color.FromArgb(15, 108, 189);
                    button1.FlatAppearance.BorderColor = Color.FromArgb(15, 108, 189);
                    button1.FlatAppearance.MouseDownBackColor = Color.FromArgb(17, 94, 163);
                    button1.FlatAppearance.MouseOverBackColor = Color.FromArgb(12, 83, 146);
                }

                if (string.IsNullOrWhiteSpace(selectedPort))
                {
                    MessageBox.Show("Prosím vyber COM port.");
                    return;
                }
                if (!_serialController.IsOpen)
                {
                    MessageBox.Show("Nejprve se připoj k sériovému portu.");
                    return;
                }
                if (string.IsNullOrWhiteSpace(currentType))
                {
                    MessageBox.Show("Prosím vyber typ měření.");
                    return;
                }
                if (!currentType.Equals("INIT", StringComparison.OrdinalIgnoreCase))
                {
                    if (string.IsNullOrWhiteSpace(currentID))
                    {
                        MessageBox.Show("Prosím zadej nebo vyber ID zařízení.");
                        return;
                    }
                }

                if (currentID != lastUsedID)
                {
                    // Nový senzor = kompletně vynulovat graf/legendu i počítadlo vzorků a hodnoty
                    try { _chartManager?.Reset(); } catch { }
                    try { valueText.Text = string.Empty; } catch { }
                    lastUsedID = currentID;
                }

                bool isConnMode = currentType.Equals("CONNECT", StringComparison.OrdinalIgnoreCase)
                               || currentType.Equals("DISCONNECT", StringComparison.OrdinalIgnoreCase);

                if (isConnMode)
                {
                    try
                    {
                        if (request == null)
                        {
                            UiLog("Požadavek není sestaven.");
                            return;
                        }

                        _chartManager?.Start();
                        _serialController.WriteLine(request);
                        _lastSentMode = null;
                        //UiLog("Měření spuštěno.");
                        return;
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Chyba při odesílání: {ex.Message}");
                    }
                    return;
                }

                _lastSentMode = null;
                StartSendingRequest();

                if (currentType.Equals("INIT", StringComparison.OrdinalIgnoreCase))
                    UiLog("INIT odesláno.");

                button1.Text = "Zastavit";
                UpdateAcceptButton();
                comboBoxSensor.Enabled = false;
                comboBoxMode.Enabled = false;
                comboBoxCOM.Enabled = false;
                comboBoxTIMER.Enabled = false;
                ConnectBtn.Enabled = false;
                button1.FlatAppearance.MouseDownBackColor = Color.FromArgb(183, 28, 28);
                button1.FlatAppearance.MouseOverBackColor = Color.FromArgb(153, 0, 0);
                button1.BackColor = Color.FromArgb(211, 47, 47);
                button1.FlatAppearance.BorderColor = Color.FromArgb(211, 47, 47);

                // Pokud běží UPDATE => zakázat INIT a RESET
                bool isUpdateMode = currentType.Equals("UPDATE", StringComparison.OrdinalIgnoreCase);
                init_btn.Enabled = !isUpdateMode;
                reset_btn.Enabled = !isUpdateMode;
                if (isUpdateMode)
                {
                    try { _resetHoldTimer.Stop(); } catch { }
                }
            }
            else
            {
                comboBoxSensor.Enabled = true;
                comboBoxMode.Enabled = true;
                comboBoxCOM.Enabled = true;
                comboBoxTIMER.Enabled = true;
                ConnectBtn.Enabled = true;
                button1.Text = "Spustit";
                UpdateAcceptButton();
                StopSendingRequest();
                UiLog("Měření pozastavené.");

                button1.BackColor = Color.FromArgb(15, 108, 189);
                button1.FlatAppearance.BorderColor = Color.FromArgb(15, 108, 189);
                button1.FlatAppearance.MouseDownBackColor = Color.FromArgb(17, 94, 163);
                button1.FlatAppearance.MouseOverBackColor = Color.FromArgb(12, 83, 146);

                // opět povolit INIT/RESET po ukončení
                init_btn.Enabled = true;
                reset_btn.Enabled = true;

                UpdateRequestFromUi();
            }
        }

        private void StartSendingRequest()
        {
            if (request == null)
            {
                UiLog("Požadavek není sestaven.");
                return;
            }

            _chartManager?.Start();

            // reset/obnova CTS
            _sendCts?.Cancel();
            _sendCts?.Dispose();
            _sendCts = new System.Threading.CancellationTokenSource();

            // 1) UPDATE = cyklické posílání
            if (request.StartsWith("?type=update", StringComparison.OrdinalIgnoreCase))
            {
                isSendingRequest = true;
                _ = SendLoopAsync(_sendCts.Token);
                return;
            }

            // 2) Ostatní (INIT / CONFIG / RESET / CONNECT / atd.) = jednorázově
            try
            {
                if (!_serialController.IsOpen)
                {
                    UiLog("Port není otevřen – požadavek se neodešle.");
                    return;
                }

                _serialController.WriteLine(request);

                // INIT: začínáme nový INIT cyklus – smaž starý INIT log a čekej odpověď
                if (request.StartsWith("?type=INIT", StringComparison.OrdinalIgnoreCase))
                {
                    _initRequestSent = true;
                    _awaitingInitResponse = true;
                    _lastInitPayload = null;
                    _initBuffer.Clear();
                }
            }
            catch (Exception ex)
            {
                UiLog($"Chyba při zápisu: {ex.Message}");
            }
            finally
            {
                isSendingRequest = false;
            }
        }


        private async Task SendLoopAsync(System.Threading.CancellationToken ct)
        {
            int loopCounter = 0;
            while (!ct.IsCancellationRequested &&
                   _serialController.IsOpen && !_serialController.SessionClosed &&
                   isSendingRequest)
            {
                int delay = GetTimerIntervalMs();
                try
                {
                    if (ct.IsCancellationRequested) break;

                    _serialController.WriteLine(request);

                    loopCounter++;
                    if (loopCounter <= 3 || loopCounter % 10 == 0)
                    {
                        UiLog($"Odesláno: {request}");
                    }

                    await Task.Delay(delay, ct);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    UiLog($"Chyba při zápisu: {ex.Message}");
                    break;
                }
            }
        }

        private void StopSendingRequest()
        {
            isSendingRequest = false;
            _chartManager?.Stop();
            _sendCts?.Cancel();

            try
            {
                // případně SerialManager.Instance.DiscardInOut();
            }
            catch { }
        }

        // Parser event handlers -> UI actions
        private void Parser_InitReceived(object sender, InitEventArgs e)
        {
            _lastInitPayload = e.Payload;
            _awaitingInitResponse = false;
            string full = string.IsNullOrWhiteSpace(e.Payload) ? "?type=INIT&status=1" : $"?type=INIT&{e.Payload}";
            UiLog($"Přijato:{Environment.NewLine}{full}");
            try
            {
                _initBuffer.AppendLine($"Přijato: {full}");
                if (_initForm != null && !_initForm.IsDisposed)
                    _initForm.AppendLine($"Přijato: {full}");
            }
            catch { }
        }

        private void Parser_DataFrameReceived(object sender, DataFrameEventArgs e)
        {
            _chartManager.ParseAndEnqueue(e.Line);
            LogLink($"Přijato: {e.Line}");
            // first frame -> allow reset
            MarkResetAvailableFromData();
        }

        private void Parser_RawLineReceived(object sender, RawLineEventArgs e)
        {
            LogLink($"Přijato: {e.Line}");
        }

        //----------------------------------------------------------------------

        private void ResetChart()
        {
            chart1.Series.Clear();
            InitializeChart();
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            try { _serialController?.Close(); } catch { }
        }

        private void ClearPictureBoxImage()
        {
            try
            {
                if (pictureBox1.Image != null)
                {
                    var old = pictureBox1.Image;
                    pictureBox1.Image = null;
                    old.Dispose();
                }
            }
            catch
            {
                // ignore dispose errors
            }
        }

        private void LoadSensorsFromJson()
        {
            try
            {
                string jsonPath = Path.Combine(Application.StartupPath, "Senzory.json");
                if (!File.Exists(jsonPath))
                {
                    string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                    string alt = Path.Combine(baseDir, "Senzory.json");
                    if (File.Exists(alt)) jsonPath = alt;
                }
                if (!File.Exists(jsonPath))
                {
                    return;
                }

                string jsonText = File.ReadAllText(jsonPath);

                var data = JsonSerializer.Deserialize<List<Komponenty>>(
                    jsonText,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
                );

                if (data == null || data.Count == 0)
                {
                    Console.WriteLine("[DEBUG] Senzory.json je prázdný nebo ve špatném formátu.");
                    return;
                }

                SenzoryData = data;

                sensorIdMap.Clear();
                comboBoxSensor.BeginUpdate();
                comboBoxSensor.Items.Clear();

                foreach (var k in SenzoryData)
                {
                    string label = (k.Znaceni ?? string.Empty).Trim();
                    if (string.IsNullOrWhiteSpace(label)) continue;

                    if (!sensorIdMap.ContainsKey(label))
                        comboBoxSensor.Items.Add(label);

                    sensorIdMap[label] = k.Id.ToString();
                }

                comboBoxSensor.EndUpdate();
                if (comboBoxSensor.Items.Contains("DHT11"))
                    comboBoxSensor.SelectedItem = "DHT11";
                else if (comboBoxSensor.Items.Count > 0)
                    comboBoxSensor.SelectedIndex = 0;
                else
                    comboBoxSensor.SelectedIndex = -1;
                UpdateRequestFromUi();
            }
            catch (Exception ex)
            {
                Console.WriteLine("[DEBUG] Chyba při načítání Senzory.json: " + ex);
            }
        }

        private void comboBoxSensor_SelectedIndexChanged(object sender, EventArgs e)
        {
            string baseDir = Application.StartupPath;

            try
            {
                string label = comboBoxSensor.SelectedItem as string;
                if (string.IsNullOrWhiteSpace(label)) return;

                _imageManager.UpdateImageForLabel(label, "Senzory_ikony", baseDir);

                if (pictureBox1.Image == null)
                {
                    string sensorsDir = Path.Combine(baseDir, "Senzory_ikony");
                    UiLog($"Nenalezen obrázek pro „{label}“ ve složce {sensorsDir}.");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Chyba při načítání obrázku: {ex.Message}");
            }
        }

        private void init_btn_Click(object sender, EventArgs e)
        {
            // Literal request as requested by user
            string req = $"?type=INIT&api={ApiVersion}";


            try
            {
                // Send over serial if port is open
                if (_serialController?.IsOpen == true)
                {
                    _serialController.WriteLine(req);
                    UiLog($"Odesláno:{Environment.NewLine}{req}");
                }
                else
                {
                    UiLog($"Port není otevřen -{Environment.NewLine} INIT se neodeslal.");
                }
            }
            catch (Exception ex)
            {
                UiLog($"Chyba při odeslání INIT: {ex.Message}");
            }

            // Always append the literal request to INIT buffer/popup (do not clear previous contents)

            try
            {
                _initBuffer.AppendLine(req);
                if (_initForm != null && !_initForm.IsDisposed)
                    _initForm.AppendLine(req);
            }
            catch { }
        }

        // Mouse handlers for reset long-press
        private void ResetBtn_MouseDown(object sender, MouseEventArgs e)
        {
            _suppressNextResetClick = false;
            try { _resetHoldTimer.Start(); } catch { }
        }
        private void ResetBtn_MouseLeave(object sender, EventArgs e)
        {
            try { _resetHoldTimer.Stop(); } catch { }
        }
        private void ResetBtn_MouseUp(object sender, MouseEventArgs e)
        {
            try { _resetHoldTimer.Stop(); } catch { }
            // if long press already triggered, suppress Click action
            // otherwise Click will run reset_btn_Click (wired in Designer)
        }

        private void ResetHoldTimer_Tick(object sender, EventArgs e)
        {
            try { _resetHoldTimer.Stop(); } catch { }
            // send wildcard reset
            string req = "?type=RESET&id=*";
            try
            {
                if (_serialController?.IsOpen == true)
                {
                    _serialController.WriteLine(req);
                    UiLog($"Odesláno dlouhým stiskem:{Environment.NewLine}{req}");
                }
                else
                {
                    UiLog("Port není otevřen - RESET(*) se neodeslal.");
                }
            }
            catch (Exception ex)
            {
                UiLog($"Chyba při odeslání RESET(*): {ex.Message}");
            }
            _suppressNextResetClick = true;
        }

        private void reset_btn_Click(object sender, EventArgs e)
        {
            // if wildcard reset was sent by long-press, skip
            if (_suppressNextResetClick)
            {
                _suppressNextResetClick = false;
                return;
            }

            // potvrzení resetu
            var confirm = MessageBox.Show(
                "Opravdu chcete provést reset?",
                "Potvrzení resetu",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (confirm != DialogResult.Yes)
                return;

            // Normal reset for selected sensor
            string label = comboBoxSensor.Text?.Trim();
            if (string.IsNullOrWhiteSpace(label))
            {
                MessageBox.Show("Vyberte senzor před odesláním RESET.");
                return;
            }

            string req = RequestBuilder.BuildRequest("RESET", label, sensorIdMap, FindSelectedComponent(), null, null, null);
            if (string.IsNullOrWhiteSpace(req))
            {
                UiLog("Nelze sestavit RESET požadavek - chybné ID senzoru.");
                return;
            }

            try
            {
                if (_serialController?.IsOpen == true)
                {
                    _serialController.WriteLine(req);
                    UiLog($"Odesláno: {req}");
                }
                else
                {
                    UiLog("Port není otevřen - RESET se neodeslal.");
                }
            }
            catch (Exception ex)
            {
                UiLog($"Chyba při odeslánía RESET: {ex.Message}");
            }

            // existing cleanup behavior
            try
            {
                _initBuffer.Clear();
                _linkBuffer.Clear();
                _canReset = false;

                // vyčistit i obsah otevřených popup oken (aby nezůstal žádný výpis)
                try
                {
                    if (_linkForm != null && !_linkForm.IsDisposed)
                        _linkForm.SetText(string.Empty);
                }
                catch { }
                try
                {
                    if (_initForm != null && !_initForm.IsDisposed)
                        _initForm.SetText(string.Empty);
                }
                catch { }

                ClearPictureBoxImage();
                ResetChart();
                valueText.Text = string.Empty; // vynulovat text aktuálních dat

                // UI reset: vyprázdnit výběry a schovat PINy
                comboBoxSensor.SelectedIndex = -1;
                comboBoxSensor.Text = string.Empty;
                comboBoxMode.SelectedIndex = -1;

                textPIN1.Text = string.Empty;
                textPIN2.Text = string.Empty;
                textPIN3.Text = string.Empty;

                PIN1.Visible = false; textPIN1.Visible = false;
                PIN2.Visible = false; textPIN2.Visible = false;
                PIN3.Visible = false; textPIN3.Visible = false;

                // přepočti request a Accept button
                UpdateRequestFromUi();
                UpdatePinInputsUi();
                UpdateResetEnabled();
            }
            catch { }
        }

        // Bezpečně přidá řádek do LINK bufferu a do otevřeného LINK popupu
        private void LogLink(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return;
            if (!line.EndsWith("\r\n")) line += "\r\n";

            void Write()
            {
                _linkBuffer.Append(line);
                if (_linkForm != null && !_linkForm.IsDisposed)
                    _linkForm.AppendLine(line);
            }

            if (InvokeRequired) BeginInvoke((Action)Write);
            else Write();
        }



        private void UpdateAcceptButton()
        {
            var form = this.FindForm();
            if (form == null) return;
            form.AcceptButton = button1.Enabled ? button1 : null;
        }

        private void MarkResetAvailableFromParams()
        {
            // enable reset when user entered any parameter in any of the modes
            if (!string.IsNullOrWhiteSpace(textPIN1.Text) ||
                !string.IsNullOrWhiteSpace(textPIN2.Text) ||
                !string.IsNullOrWhiteSpace(textPIN3.Text))
            {
                _canReset = true;
            }
            UpdateResetEnabled();
        }

        private void MarkResetAvailableFromData()
        {
            _canReset = true;
            UpdateResetEnabled();
        }

        private void UpdateResetEnabled()
        {
            // If UPDATE is running, RESET is blocked (existing behavior)
            bool isUpdateRunning = string.Equals(comboBoxMode.Text?.Trim(), "UPDATE", StringComparison.OrdinalIgnoreCase)
                                && string.Equals(button1.Text, "Zastavit", StringComparison.OrdinalIgnoreCase);

            if (isUpdateRunning)
            {
                reset_btn.Enabled = false;
                return;
            }

            bool connected = _serialController?.IsOpen == true;
            reset_btn.Enabled = connected && _canReset;
        }

        private void ChartManager_MaxSamplesReached(object sender, EventArgs e)
        {
            void Do()
            {
                StopSendingRequest();

                // UI do klidového stavu
                comboBoxSensor.Enabled = true;
                comboBoxMode.Enabled = true;
                comboBoxCOM.Enabled = true;
                comboBoxTIMER.Enabled = true;
                ConnectBtn.Enabled = true;

                button1.Text = "Spustit";
                UpdateAcceptButton();

                try
                {
                    button1.BackColor = Color.FromArgb(15, 108, 189);
                    button1.FlatAppearance.BorderColor = Color.FromArgb(15, 108, 189);
                    button1.FlatAppearance.MouseDownBackColor = Color.FromArgb(17, 94, 163);
                    button1.FlatAppearance.MouseOverBackColor = Color.FromArgb(12, 83, 146);
                }
                catch { }

                init_btn.Enabled = true;
                UpdateResetEnabled();

                MessageBox.Show("Překročet maximální počet vzorků", "Limit vzorků", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            if (InvokeRequired) BeginInvoke((Action)Do);
            else Do();
        }

        private void savecsv_btn_Click(object sender, EventArgs e)
        {
            try
            {
                if (_chartManager == null)
                {
                    MessageBox.Show("Není k dispozici graf pro export.", "CSV export", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                string csv = _chartManager.ExportCsv(';',decimalComma: true);
                if (string.IsNullOrWhiteSpace(csv))
                {
                    MessageBox.Show("Nejsou k dispozici žádná data k uložení.", "CSV export", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                using (var sfd = new SaveFileDialog())
                {
                    sfd.Title = "Uložit naměřené hodnoty";
                    sfd.Filter = "CSV (*.csv)|*.csv|Všechny soubory (*.*)|*.*";
                    sfd.FileName = "mereni.csv";
                    sfd.AddExtension = true;
                    sfd.DefaultExt = "csv";
                    sfd.OverwritePrompt = true;

                    if (sfd.ShowDialog(this.FindForm()) != DialogResult.OK)
                        return;

                    // UTF-8 bez BOM
                    var utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
                    File.WriteAllText(sfd.FileName, csv, utf8NoBom);

                    MessageBox.Show("CSV bylo uloženo.", "CSV export", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Chyba při ukládání CSV: " + ex.Message, "CSV export", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
