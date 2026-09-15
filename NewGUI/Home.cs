using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.ListView;

namespace NewGUI
{
    public partial class Home : UserControl
    {
        private Form1 _rodic;

        public Home(Form1 rodic)
        {
            InitializeComponent();
            _rodic = rodic;

            // Předpoklad: máš tři host panely (A … B … C) v Designeru:
            // Aktuatory_panel, Sensor_panel, Simulator_panel
            // Vyčisti je a vlož HoverTile s Dock=Fill
            AddTile(Aktuatory_panel,
                title: "Aktuátory",
                normal: Properties.Resources.half_brain_mini3,
                hover:  Properties.Resources.half_brain_mini4,   // můžeš dát přebarvenou verzi
                detail: "Aplikace pro konfiguraci a řízení aktuátorů s možností nastavení provozních parametrů a provedení resetu.",
                onActivate: (_, __) => _rodic.NahraditObsah(new Aktuatory(_rodic)));

            AddTile(Sensor_panel,
                title: "Senzory",
                normal: Properties.Resources.half_brain_mini3,
                hover:  Properties.Resources.half_brain_mini4,
                detail: "Aplikace pro čtení a vizualizaci dat z měřicí desky s možností konfigurace senzorů a volby měřicích vstupů.",
                onActivate: (_, __) => _rodic.NahraditObsah(new Senzory(_rodic)));

            AddTile(Simulator_panel,
                title: "Simulator",
                normal: Properties.Resources.half_brain_mini3,
                hover:  Properties.Resources.half_brain_mini4,
                detail: "Virtuální prostředí pro simulaci senzorů.",
                onActivate: (_, __) => _rodic.NahraditObsah(new Simulator(_rodic)));

            var introPanel = new Panel
            {
                Name = "HomeIntroPanel",
                Dock = DockStyle.Bottom,
                Size = new Size(800, 148),
                BackColor = Color.White
            };

            var accentLine = new Panel
            {
                Dock = DockStyle.Top,
                Height = 3,
                BackColor = Color.FromArgb(235, 98, 9)
            };
            introPanel.Controls.Add(accentLine);

            var welcomeLabel = new Label
            {
                Name = "HomeWelcomeLabel",
                Text = "Vítejte v EduBox HUB App",
                Font = new Font("Segoe UI", 20F, FontStyle.Bold),
                ForeColor = Color.FromArgb(5, 93, 169),
                Location = new Point(24, 20),
                Size = new Size(510, 40)
            };
            introPanel.Controls.Add(welcomeLabel);

            var descriptionLabel = new Label
            {
                Name = "HomeDescriptionLabel",
                Text = "Připojte senzory a aktuátory, sledujte jejich data a ovládejte výstupy. Pro experimenty bez hardwaru použijte Simulator.",
                Font = new Font("Segoe UI", 11F),
                ForeColor = Color.FromArgb(39, 59, 77),
                Location = new Point(24, 68),
                Size = new Size(500, 62)
            };
            introPanel.Controls.Add(descriptionLabel);

            var appLogo = new PictureBox
            {
                Name = "EduBoxHubAppLogo",
                AccessibleName = "Logo EduBox HUB App",
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
                BackColor = Color.White,
                Image = Properties.Resources.EduBoxHubAppLogo,
                SizeMode = PictureBoxSizeMode.Zoom,
                Size = new Size(235, 65),
                Location = new Point(549, 68),
                TabStop = false
            };
            introPanel.Controls.Add(appLogo);
            Controls.Add(introPanel);
            introPanel.BringToFront();
        }

        public void AddTile(Panel host, string title, Image normal, Image hover, string detail, EventHandler onActivate)
        {
            host.Controls.Clear();

            var tile = new Tlacitka
            {
                Dock = DockStyle.Fill,
                Title = title,
                NormalImage = normal,
                HoverImage = hover,
                DetailText = detail,
                ExpandedHeight = 150 // výšku můžeš doladit
            };
            tile.Activated += onActivate;

            host.Controls.Add(tile);
        }

        private void Simulator_button_Click(object sender, EventArgs e)
        {
            _rodic.NahraditObsah(new Simulator(_rodic));
        }

        private void Sensor_button_Click(object sender, EventArgs e)
        {
            _rodic.NahraditObsah(new Senzory(_rodic));
        }

        private void Aktuatory_button_Click(object sender, EventArgs e)
        {
            _rodic.NahraditObsah(new Aktuatory(_rodic));
        }

        private void Aktuatory_panel_Paint(object sender, PaintEventArgs e)
        {

        }

        private void Home_Load(object sender, EventArgs e)
        {

        }
    }
}
