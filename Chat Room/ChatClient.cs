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
        private string _password;
        private bool _isConnected;
        private string _currentInput = "";
        private readonly object _consoleLock = new object();

        public ChatClient(string username, string password)
        {
            _username = username;
            _password = password;
        }

        public async Task ConnectAsync(string serverIp, int port, bool isRegister)
        {
            try
            {
                _client = new TcpClient();
                await _client.ConnectAsync(serverIp, port);
                _stream = _client.GetStream();

                // Send authentication message
                var authMessage = new ClientMessage
                {
                    Command = isRegister ? "register" : "login",
                    Body = _username,
                    Password = _password
                };

                var jsonString = JsonSerializer.Serialize(authMessage);
                var jsonBytes = Encoding.UTF8.GetBytes(jsonString);
                await _stream.WriteAsync(jsonBytes);

                // Wait for authentication response
                var buffer = new byte[4096];
                var bytesRead = await _stream.ReadAsync(buffer);
                if (bytesRead > 0)
                {
                    var response = Encoding.UTF8.GetString(buffer, 0, bytesRead).Trim();
                    var serverMessage = JsonSerializer.Deserialize<ServerMessage>(response);
                    
                    if (serverMessage?.Type == "server")
                    {
                        Console.ForegroundColor = ConsoleColor.Blue;
                        Console.WriteLine($"[SERVER] {serverMessage.Body}");
                        Console.ResetColor();
                        
                        if (serverMessage.Body?.Contains("rejected") == true || 
                            serverMessage.Body?.Contains("failed") == true ||
                            serverMessage.Body?.Contains("not found") == true ||
                            serverMessage.Body?.Contains("Incorrect password") == true ||
                            serverMessage.Body?.Contains("already exists") == true)
                        {
                            _isConnected = false;
                            _client.Close();
                            return;
                        }
                    }
                }

                _isConnected = true;

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
            lock (_consoleLock)
            {
                // Clear the current input line
                ClearCurrentLine();

                // Display the incoming message
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

                    case "private":
                        Console.ForegroundColor = ConsoleColor.Cyan;
                        Console.Write($"[PM from {message.Username}]");
                        Console.ResetColor();
                        Console.WriteLine($": {message.Body}");
                        // Play notification sound for incoming PM
                        try { Console.Beep(800, 100); } catch { }
                        break;

                    case "privatesent":
                        Console.ForegroundColor = ConsoleColor.Cyan;
                        Console.Write($"[PM to {message.Username}]");
                        Console.ResetColor();
                        Console.WriteLine($": {message.Body}");
                        break;

                    case "action":
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine(message.Body);
                        Console.ResetColor();
                        break;

                    default:
                        Console.WriteLine(message.Body);
                        break;
                }

                // Restore the current input line
                RestoreCurrentLine();
            }
        }

        private void ClearCurrentLine()
        {
            // Move cursor to beginning of line and clear it
            Console.Write("\r" + new string(' ', Console.BufferWidth - 1) + "\r");
        }

        private void RestoreCurrentLine()
        {
            // Redraw the current input
            if (!string.IsNullOrEmpty(_currentInput))
            {
                Console.Write(_currentInput);
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
                    lock (_consoleLock)
                    {
                        Console.ForegroundColor = ConsoleColor.Magenta;
                        Console.Write("[you]");
                        Console.ResetColor();
                        Console.WriteLine($": {body}");
                    }
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
                _currentInput = "";
                
                // Read input character by character
                ConsoleKeyInfo keyInfo;
                while (_isConnected)
                {
                    if (!Console.KeyAvailable)
                    {
                        await Task.Delay(10);
                        continue;
                    }

                    keyInfo = Console.ReadKey(intercept: true);

                    lock (_consoleLock)
                    {
                        if (keyInfo.Key == ConsoleKey.Enter)
                        {
                            Console.WriteLine();
                            break;
                        }
                        else if (keyInfo.Key == ConsoleKey.Backspace)
                        {
                            if (_currentInput.Length > 0)
                            {
                                _currentInput = _currentInput.Substring(0, _currentInput.Length - 1);
                                Console.Write("\b \b");
                            }
                        }
                        else if (!char.IsControl(keyInfo.KeyChar))
                        {
                            _currentInput += keyInfo.KeyChar;
                            Console.Write(keyInfo.KeyChar);
                        }
                    }
                }

                if (!_isConnected)
                    break;

                var message = _currentInput.Trim();
                
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
