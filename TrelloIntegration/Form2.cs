using System;
using System.Text;
using System.Windows.Forms;

namespace TrelloIntegration
{
    public partial class Form2 : Form
    {
        public Form2()
        {
            InitializeComponent();
        }

        private void button1_Click(object sender, System.EventArgs e)
        {
            Clipboard.SetText(Base64Encode(textBox1.Text));
            MessageBox.Show(@"Password copied to clipboard.");
        }

        public static string Base64Encode(string plainText)
        {
            var plainTextBytes = Encoding.UTF8.GetBytes(plainText);
            return Convert.ToBase64String(plainTextBytes);
        }
    }
}
