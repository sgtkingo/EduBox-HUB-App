using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace NewGUI
{
    public partial class Form1 : Form
    {
        // --- Animace pro víc tlačítek ---
        private readonly Dictionary<Button, (int progress, int target)> _wipe =
         new Dictionary<Button, (int progress, int target)>();
        private Timer _animTimer;
        private int _speedPx = 14;

        private Color _baseColor = Color.FromArgb(5, 92, 169);
        private Color _hoverColor = Color.FromArgb(243, 95, 0);  // oranžová
        private Color _textColor = Color.White;             // barva pro symboly

        // Serial controller (replace direct SerialManager usage)
        private SerialController _serialController;

        public Form1()
        {
            InitializeComponent();
            _animTimer = new Timer { Interval = 15 };
            _animTimer.Tick += AnimTimer_Tick;

            // init serial controller
            _serialController = new SerialController();

            // příklady: nahraď svými skutečnými názvy tlačítek a panelů
            // oranžová varianta ikony z Resources (nebo Image.FromFile(...))
            AttachWipeAnimation(home_button,Properties.Resources.HOME_Click, hostPanel: home_panel);
            AttachWipeAnimation(setting_button, Properties.Resources.settings_click, hostPanel: setttings_panel);
            AttachWipeAnimation(help_button, Properties.Resources.help_click, hostPanel: help_panel);
            AttachWipeAnimation(info_button, Properties.Resources.info2_click, hostPanel: info_panel);

            NahraditObsah(new Home(this));

        }


        // Zavře OwnedForms typu SerialPopupForm (bez pádů)
        public void CloseOwnedSerialPopups()
        {
            try
            {
                var owned = this.OwnedForms.OfType<SerialPopupForm>().ToArray();
                foreach (var f in owned)
                {
                    try { f.Close(); } catch { }
                }
            }
            catch { }
        }
        private void menu_button_Click(object sender, EventArgs e)
        {
            sidebar_timer.Start();
        }
         /*
        private void sidebar_timer_Tick(object sender, EventArgs e)
        {
            int CurrentLOC = Main_panel.Location.X;
            if (SidebarExpand)
            {
 
                sidebar.Width -= 10;
                Main_panel.Location = new Point(CurrentLOC -= 10, Main_panel.Location.Y);
                Main_panel.Width += 10;
                //Main_panel.Left = sidebar.Left+sidebar.Width;
                

                if (sidebar.Width == sidebar.MinimumSize.Width)
                {
                    SidebarExpand = false;
                    sidebar_timer.Stop();
                }
            }
            else
            {
                sidebar.Width += 10;
                Main_panel.Location = new Point(CurrentLOC += 10, Main_panel.Location.Y);
                Main_panel.Width -= 10;
                if (sidebar.Width == sidebar.MaximumSize.Width)
                {
                    sidebar_timer.Stop();
                    SidebarExpand = true;
                }
            }

        }
         */
        public void NahraditObsah(UserControl novyObsah)
        {
            // Close any popups owned by this form (serial windows)
            try { CloseOwnedSerialPopups(); } catch { }

            // Pojistka: vždy odpojit staré serial věci (přes SerialController)
            try { _serialController?.Close(); } catch { }

            Main_panel.Controls.Clear();
            novyObsah.Dock = DockStyle.Fill;
            Main_panel.Controls.Add(novyObsah);
        }

        private void AttachWipeAnimation(Button btn, Image OrangeImage, Panel hostPanel = null)
        {
            if (!_wipe.ContainsKey(btn))
                _wipe.Add(btn, (0, 0));

            btn.Paint += (s, e) =>
            {
                var g = e.Graphics;
                var rect = btn.ClientRectangle;
                var state = _wipe[btn];
                int progress = state.progress;

                // Pruh barvy v pozadí
                if (progress > 0)
                {
                    using (var hoverBrush = new SolidBrush(_hoverColor))
                    {
                        g.FillRectangle(hoverBrush, 0, 0, Math.Min(progress, rect.Width), rect.Height);
                    }
                }

                // Text si necháme vykreslit automaticky WinForms -> tím nic nepřebíjíme
                // (proto zde TextRenderer.DrawText nevoláme!)

                // Obrázek – pokud je nastavený, přebarvíme ho podle stavu
                if (btn.Image != null)
                {
                    Image imgToDraw = btn.Image;

                    // pokud je tlačítko z 50% a víc "hoverované", použij přebarvenou verzi
                    if (progress > rect.Width / 5)
                    {
                        imgToDraw = OrangeImage; 
                    }

                    // spočítáme pozici obrázku přibližně na střed (neřešíme přesně Align kvůli jednoduchosti)
                    int x = 37;
                    int y = (rect.Height - imgToDraw.Height) / 2;

                    g.DrawImage(imgToDraw, x, y, imgToDraw.Width, imgToDraw.Height);
                }
                // 3) Text – buď standardní z Buttonu, nebo z předaného Labelu
                string textToDraw = btn.Text;
                Font fontToUse = btn.Font;
                Color colorToUse = btn.ForeColor;


                int offsetX = 35; // kolik pixelů odleva (místo "mezery" ve stringu)
                Rectangle textRect = new Rectangle(
                    rect.Left + offsetX,
                    rect.Top-1,
                    rect.Width - offsetX,
                    rect.Height
                );

                TextRenderer.DrawText(
                    g,
                    textToDraw,
                    fontToUse,
                    textRect,
                    colorToUse,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis
                );
            };

            btn.MouseEnter += (_, __) => SetTarget(btn, btn.Width);
            btn.MouseLeave += (_, __) => SetTarget(btn, 0);

            if (hostPanel != null)
            {
                hostPanel.MouseEnter += (_, __) => SetTarget(btn, btn.Width);
                hostPanel.MouseLeave += (_, __) => SetTarget(btn, 0);
            }
        }



        private void SetTarget(Button btn, int target)
        {
            if (!_wipe.ContainsKey(btn)) return;
            var (progress, _) = _wipe[btn];
            _wipe[btn] = (progress, target);
            if (!_animTimer.Enabled) _animTimer.Start();
        }

        private void AnimTimer_Tick(object sender, EventArgs e)
        {
            bool anyMoving = false;

            // kopie klíčů, abychom mohli měnit slovník bezpečně
            var keys = _wipe.Keys.ToList();

            foreach (var btn in keys)
            {
                var (progress, target) = _wipe[btn];

                if (progress < target)
                {
                    progress = Math.Min(target, progress + _speedPx);
                    anyMoving = true;
                }
                else if (progress > target)
                {
                    progress = Math.Max(target, progress - _speedPx);
                    anyMoving = true;
                }

                _wipe[btn] = (progress, target);
                btn.Invalidate();
            }

            if (!anyMoving)
                _animTimer.Stop();
        }




        private void setting_button_Click(object sender, EventArgs e)
        {
            try { CloseOwnedSerialPopups(); } catch { }
            NahraditObsah(new settings());
        }

        private void home_button_Click(object sender, EventArgs e)
        {
            try { CloseOwnedSerialPopups(); } catch { }
            NahraditObsah(new Home(this));

        }

        private void help_button_Click(object sender, EventArgs e)
        {
            try { CloseOwnedSerialPopups(); } catch { }
            NahraditObsah(new help(this));
        }

        private void info_button_Click(object sender, EventArgs e)
        {
            try { CloseOwnedSerialPopups(); } catch { }
            //NahraditObsah(new Info());
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "https://m-ta.cz/",
                UseShellExecute = true
            };
            System.Diagnostics.Process.Start(psi);
        }

        
    }
}
