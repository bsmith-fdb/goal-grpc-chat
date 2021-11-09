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
        private delegate void SafeCallDelegate(object obj);

        private readonly List<string> Usernames = new List<string> { "Rusty Knuckles", "Big Dave", "Crazy Bob", "Ghost Wolf", "Seamus", "Astrid", "Null Blob", "a dog named Baron" };

        public MainForm()
        {
            InitializeComponent();
            toolStripStatusLabel1.Text = "";
            btnDisconnect.Enabled = false;
        }

        private void HandleServerChatMessageSafe(object obj)
        {
            if (InvokeRequired)
            {
                var d = new SafeCallDelegate(HandleServerChatMessageSafe);
                txtChat.Invoke(d, new object[] { obj });
            }
            else
            {
                var msg = (ServerChatMessage)obj;
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
            Debug.Print($"Client Connect: Username='{username}' Host='{host}' Port='{port}'");

            var channelOptions = new List<ChannelOption> { 
                new ChannelOption("GRPC_ARG_KEEPALIVE_TIME_MS", 60 * 1000),
                new ChannelOption("GRPC_ARG_KEEPALIVE_TIMEOUT_MS", 5 * 1000),
                new ChannelOption("GRPC_ARG_HTTP2_MAX_PINGS_WITHOUT_DATA", 0),
                new ChannelOption("GRPC_ARG_KEEPALIVE_PERMIT_WITHOUT_CALLS", 1)
            };

            channel = new Channel($"{host}:{port}", ChannelCredentials.Insecure, channelOptions);
            
            client = new ChatService.ChatServiceClient(channel);

            var headers = new Metadata();
            headers.Add("username", username);

            try
            {
                call = client.ChatStream(headers);
                var responseHeaders = call.ResponseHeadersAsync.Result;

                if (responseHeaders.GetValue("status") != "OK")
                {
                    throw new Exception("Connection failed");
                }

                toolStripStatusLabel1.Text = "Connected";
                btnConnect.Enabled = false;
                btnDisconnect.Enabled = true;

                Task.Run(async () => await ProcessResponseStream()).ContinueWith(t =>
                {
                    if (t.Exception != null)
                    {
                        MessageBox.Show(t.Exception.Message, t.Exception.GetType().ToString(), MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }

                    Disconnect();
                }, TaskScheduler.FromCurrentSynchronizationContext());
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, ex.GetType().ToString(), MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void Disconnect()
        {
            if (btnDisconnect.Enabled)
            {
                Debug.Print("Client Disconnect");

                try
                {
                    call.RequestStream.CompleteAsync().Wait(5000);
                }
                catch (Exception ex)
                {
                    Debug.Print($"Client Disconnect: {ex.Message}");
                }

                try
                {
                    channel.ShutdownAsync().Wait(5000);
                }
                catch (Exception ex)
                {
                    Debug.Print($"Client Disconnect: {ex.Message}");
                }

                toolStripStatusLabel1.Text = "Disconnected";
                lstClients.Items.Clear();
                txtChat.Clear();
                btnConnect.Enabled = true;
                btnDisconnect.Enabled = false;
            }
        }

        private async Task ProcessResponseStream()
        {
            while (await call.ResponseStream.MoveNext())
            {
                HandleServerChatMessageSafe(call.ResponseStream.Current);
            }
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            txtHost.Text = "bhschatserver.eastus.azurecontainer.io";
            txtPort.Text = "1337";
            txtUsername.Text = $"{Usernames[new Random().Next(0, Usernames.Count - 1)]}";

            //Connect(txtUsername.Text);
            //SendSpam();
        }

        private void SendSpam()
        {
            var args = new KeyEventArgs(Keys.Enter);

            for (int i = 0; i < 50; i++)
            {
                txtMessage.Text = DateTime.Now.Ticks.ToString();

                args.Handled = false;

                txtMessage_KeyDown(txtMessage, args);

                if (!args.Handled)
                {
                    break;
                }
            }
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
                    e.Handled = true;
                    e.SuppressKeyPress = true;
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
                Name = txtUsername.Text,
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
                if (string.IsNullOrEmpty(txtHost.Text))
                {
                    throw new Exception("Host is required");
                }

                if (string.IsNullOrEmpty(txtPort.Text))
                {
                    throw new Exception("Port is required");
                }

                if (!int.TryParse(txtPort.Text, out int port))
                {
                    throw new Exception("Port must be an integer");
                }

                if (string.IsNullOrWhiteSpace(txtUsername.Text))
                {
                    throw new Exception("Username is required");
                }

                Connect(txtUsername.Text, txtHost.Text, port);
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
