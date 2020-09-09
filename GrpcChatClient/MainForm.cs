using Grpc.Core;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace GrpcChatClient
{
    public partial class MainForm : Form
    {
        private Channel channel;
        private ChatService.ChatServiceClient client;
        private AsyncDuplexStreamingCall<ChatMessage, ServerChatMessage> call;
        private bool connected;
        private delegate void SafeCallDelegate(object obj);

        public MainForm()
        {
            InitializeComponent();
            toolStripStatusLabel1.Text = "";
            btnDisconnect.Enabled = false;
        }

        private void HandleServerChatMessageSafe(object obj)
        {
            var msg = (ServerChatMessage)obj;

            if (txtChat.InvokeRequired)
            {
                var d = new SafeCallDelegate(HandleServerChatMessageSafe);
                txtChat.Invoke(d, new object[] { msg });
            }
            else
            {
                Process(msg);
            }
        }

        private void Process(ServerChatMessage sm)
        {
            if (sm.Status != null)
            {
                Process(sm.Status);
            }

            if (sm.Message != null)
            {
                Process(sm.Message);
            }
        }

        private void Process(Status st)
        {
            if (!string.IsNullOrEmpty(st.AddClient) || !string.IsNullOrEmpty(st.DeleteClient))
            {
                string text = "";
                if (!string.IsNullOrEmpty(st.AddClient))
                {
                    text = $"{st.AddClient} joined the chat{Environment.NewLine}";
                }
                else if (!string.IsNullOrEmpty(st.DeleteClient))
                {
                    text = $"{st.DeleteClient} left the chat{Environment.NewLine}";
                }

                var ff = new FontFamily(System.Drawing.Text.GenericFontFamilies.Monospace);
                var font = new System.Drawing.Font(ff, 8, FontStyle.Regular);
                var color = System.Drawing.Color.Black;

                AppendChatText(text, color, font);
            }

            lstClients.Items.Clear();
            lstClients.Items.AddRange(st.CurrentClients.OrderBy(x => x).ToArray());
        }

        private void Process(ChatMessage cm)
        {
            string text = $"{cm.Name}: {cm.Message}{Environment.NewLine}";
            var color = System.Drawing.Color.FromArgb(cm.Font.Color.Red, cm.Font.Color.Green, cm.Font.Color.Blue);

            var ff = new FontFamily(cm.Font.Name);
            var fs = (FontStyle)cm.Font.Style;
            var font = new System.Drawing.Font(ff, cm.Font.Size, fs);

            AppendChatText(text, color, font);
        }

        private void AppendChatText(string text, System.Drawing.Color color, System.Drawing.Font font)
        {
            txtChat.SelectionStart = txtChat.TextLength;
            txtChat.SelectionLength = 0;
            txtChat.SelectionColor = color;
            txtChat.SelectionFont = font;
            txtChat.AppendText(text);
            txtChat.SelectionColor = txtChat.ForeColor;
            txtChat.ScrollToCaret();
        }

        private void Connect(string username, string host = "localhost", int port = 1337)
        {
            if (!connected)
            {
                Debug.Print($"Client Connect: Username='{username}' Host='{host}' Port='{port}'");

                channel = new Channel($"{host}:{port}", ChannelCredentials.Insecure);
                client = new ChatService.ChatServiceClient(channel);

                var headers = new Metadata();
                headers.Add("username", username);

                call = client.ChatStream(headers);
                Task.Run(HandleResponseStream);
                
                toolStripStatusLabel1.Text = "Connected";
                btnConnect.Enabled = false;
                btnDisconnect.Enabled = true;
                connected = true;
            }
        }

        private void Disconnect()
        {
            if (connected)
            {
                Debug.Print("Client Disconnect");
                call.RequestStream.CompleteAsync();
                channel.ShutdownAsync();
                toolStripStatusLabel1.Text = "Disconnected";
                lstClients.Items.Clear();
                txtChat.Clear();
                btnConnect.Enabled = true;
                btnDisconnect.Enabled = false;
                connected = false;
            }
        }

        private async void HandleResponseStream()
        {
            while (await call.ResponseStream.MoveNext())
            {
                HandleServerChatMessageSafe(call.ResponseStream.Current);
            }
        }

        private void Form1_Load(object sender, EventArgs e)
        {

        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            Disconnect();
        }

        private void txtMessage_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                try
                {
                    var msg = BuildChatMessage();
                    call.RequestStream.WriteAsync(msg).Wait();
                    txtMessage.Clear();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message, ex.GetType().Name, MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private ChatMessage BuildChatMessage()
        {
            return new ChatMessage
            {
                Name = txtName.Text,
                Font = new Font
                {
                    Name = fontDialog1.Font.Name,
                    Style = (int)fontDialog1.Font.Style,
                    Size = fontDialog1.Font.Size,
                    Color = new Color
                    {
                        Red = colorDialog1.Color.R,
                        Blue = colorDialog1.Color.B,
                        Green = colorDialog1.Color.G
                    }
                },
                Message = txtMessage.Text
            };
        }

        private void btnColor_Click(object sender, EventArgs e)
        {
            colorDialog1.ShowDialog(this);
        }

        private void btnFont_Click(object sender, EventArgs e)
        {
            fontDialog1.ShowDialog(this);
        }

        private void btnConnect_Click(object sender, EventArgs e)
        {
            try
            {
                string username = txtName.Text;
                if (string.IsNullOrWhiteSpace(username))
                    throw new Exception("You must enter a username");

                Connect(username);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, ex.GetType().Name, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void btnDisconnect_Click(object sender, EventArgs e)
        {
            try
            {
                Disconnect();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, ex.GetType().Name, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
