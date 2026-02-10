using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace Chat_Room
{
    internal class ChatClient
    {
        private TcpClient? _client;
        private NetworkStream? _stream;
        private string _username;
        private bool _isConnected;

        public ChatClient(string username)
        {
            _username = username;
        }

        public async Task ConnectAsync(string serverIp, int port = 5000)
        {
            try
            {
                _client = new TcpClient();
                await _client.ConnectAsync(serverIp, port);
                _stream = _client.GetStream();
                _isConnected = true;

                // Send username to server
                var usernameBytes = Encoding.UTF8.GetBytes(_username);
                await _stream.WriteAsync(usernameBytes);

                Console.WriteLine($"Connected to server at {serverIp}:{port}");
                Console.WriteLine("Type your messages and press Enter to send.");
                Console.WriteLine("Type /help for a list of available commands.\n");

                // Start receiving messages
                _ = ReceiveMessagesAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to connect: {ex.Message}");
                _isConnected = false;
            }
        }

        private async Task ReceiveMessagesAsync()
        {
            var buffer = new byte[4096];

            try
            {
                while (_isConnected && _stream != null)
                {
                    var bytesRead = await _stream.ReadAsync(buffer);
                    if (bytesRead == 0) break;

                    var jsonString = Encoding.UTF8.GetString(buffer, 0, bytesRead).Trim();
                    
                    try
                    {
                        var message = JsonSerializer.Deserialize<ServerMessage>(jsonString);
                        
                        if (message != null)
                        {
                            HandleServerMessage(message);
                        }
                    }
                    catch (JsonException ex)
                    {
                        Console.WriteLine($"Error parsing server message: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                if (_isConnected)
                {
                    Console.WriteLine($"\nConnection error: {ex.Message}");
                }
            }
            finally
            {
                Disconnect();
            }
        }

        private void HandleServerMessage(ServerMessage message)
        {
            switch (message.Type?.ToLower())
            {
                case "server":
                    Console.ForegroundColor = ConsoleColor.Blue;
                    Console.WriteLine($"[SERVER] {message.Body}");
                    Console.ResetColor();
                    break;

                case "command":
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.WriteLine(message.Body);
                    Console.ResetColor();
                    break;

                case "message":
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.Write(message.Username);
                    Console.ResetColor();
                    Console.WriteLine($": {message.Body}");
                    break;

                case "clear":
                    Console.Clear();
                    Console.WriteLine("Chat cleared by server.\n");
                    break;

                default:
                    Console.WriteLine(message.Body);
                    break;
            }
        }

        public async Task SendMessageAsync(string message)
        {
            if (!_isConnected || _stream == null)
            {
                Console.WriteLine("Not connected to server");
                return;
            }

            try
            {
                string command;
                string body;

                // Parse command and body
                if (message.StartsWith("/"))
                {
                    var parts = message.Split(' ', 2);
                    command = parts[0];
                    body = parts.Length > 1 ? parts[1] : "";
                }
                else
                {
                    command = "/say";
                    body = message;
                }

                var clientMessage = new ClientMessage
                {
                    Command = command,
                    Body = body
                };

                var jsonString = JsonSerializer.Serialize(clientMessage) + "\n";
                var jsonBytes = Encoding.UTF8.GetBytes(jsonString);
                await _stream.WriteAsync(jsonBytes);
                
                // Only display [you] tag for regular messages, not commands
                if (command == "/say")
                {
                    Console.ForegroundColor = ConsoleColor.Magenta;
                    Console.Write("[you]");
                    Console.ResetColor();
                    Console.WriteLine($": {body}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to send message: {ex.Message}");
                _isConnected = false;
            }
        }

        public async Task StartChatAsync()
        {
            while (_isConnected)
            {
                var message = Console.ReadLine();
                
                if (string.IsNullOrWhiteSpace(message))
                    continue;

                if (message.ToLower() == "/exit" || message.ToLower() == "exit")
                {
                    await SendMessageAsync("/exit");
                    Disconnect();
                    break;
                }

                await SendMessageAsync(message);
            }
        }

        public void Disconnect()
        {
            _isConnected = false;
            _stream?.Close();
            _client?.Close();
            Console.WriteLine("Disconnected from server");
        }
    }
}
