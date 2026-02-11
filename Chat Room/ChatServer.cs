using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace Chat_Room
{
    internal class ChatServer
    {
        private TcpListener? _listener;
        private readonly ConcurrentDictionary<string, TcpClient> _clients = new();
        private bool _isRunning;
        private int _port;

        public ChatServer(int port = 5000)
        {
            _port = port;
        }

        public async Task Start()
        {
            _listener = new TcpListener(IPAddress.Any, _port);
            _listener.Start();
            _isRunning = true;

            Console.WriteLine($"Server started on port {_port}");
            Console.WriteLine("Waiting for clients to connect...");
            Console.WriteLine("\nServer Commands:");
            Console.WriteLine("  /broadcast <message> - Send a message to all clients");
            Console.WriteLine("  /say <message>       - Send a message to all clients (appears as chat message)");
            Console.WriteLine("  /msg <username> <message> - Send a private message to a specific user");
            Console.WriteLine("  /kick <username>     - Disconnect a specific user");
            Console.WriteLine("  /list                - List all connected users");
            Console.WriteLine("  /stop                - Stop the server\n");

            // Start server command handler
            _ = HandleServerCommandsAsync();

            while (_isRunning)
            {
                try
                {
                    var client = await _listener.AcceptTcpClientAsync();
                    _ = HandleClientAsync(client);
                }
                catch (Exception ex)
                {
                    if (_isRunning)
                    {
                        Console.WriteLine($"Error accepting client: {ex.Message}");
                    }
                }
            }
        }

        private async Task HandleServerCommandsAsync()
        {
            await Task.Run(async () =>
            {
                while (_isRunning)
                {
                    var input = Console.ReadLine();
                    if (string.IsNullOrWhiteSpace(input)) continue;

                    var parts = input.Split(' ', 2);
                    var command = parts[0].ToLower();

                    switch (command)
                    {
                        case "/broadcast":
                            if (parts.Length > 1)
                            {
                                await BroadcastJsonAsync("server", parts[1], null);
                                Console.WriteLine($"Broadcast: {parts[1]}");
                            }
                            else
                            {
                                Console.WriteLine("Usage: /broadcast <message>");
                            }
                            break;

                        case "/say":
                            if (parts.Length > 1)
                            {
                                await BroadcastJsonAsync("message", parts[1], null, "SERVER");
                                Console.WriteLine($"SERVER: {parts[1]}");
                            }
                            else
                            {
                                Console.WriteLine("Usage: /say <message>");
                            }
                            break;

                        case "/msg":
                            if (parts.Length > 1)
                            {
                                var msgParts = parts[1].Split(' ', 2);
                                if (msgParts.Length >= 2)
                                {
                                    var targetUsername = msgParts[0].Trim();
                                    var privateMessage = msgParts[1];
                                    
                                    if (_clients.TryGetValue(targetUsername, out var targetClient))
                                    {
                                        await SendJsonAsync(targetClient.GetStream(), "private", privateMessage, "SERVER");
                                        Console.WriteLine($"SERVER -> {targetUsername}: {privateMessage}");
                                    }
                                    else
                                    {
                                        Console.WriteLine($"User '{targetUsername}' not found");
                                    }
                                }
                                else
                                {
                                    Console.WriteLine("Usage: /msg <username> <message>");
                                }
                            }
                            else
                            {
                                Console.WriteLine("Usage: /msg <username> <message>");
                            }
                            break;

                        case "/kick":
                            if (parts.Length > 1)
                            {
                                var username = parts[1].Trim();
                                if (_clients.TryGetValue(username, out var client))
                                {
                                    await SendJsonAsync(client.GetStream(), "server", "You have been kicked from the server.");
                                    client.Close();
                                    _clients.TryRemove(username, out _);
                                    Console.WriteLine($"Kicked user: {username}");
                                    await BroadcastJsonAsync("server", $"{username} was kicked from the server", null);
                                }
                                else
                                {
                                    Console.WriteLine($"User '{username}' not found");
                                }
                            }
                            else
                            {
                                Console.WriteLine("Usage: /kick <username>");
                            }
                            break;

                        case "/list":
                            if (_clients.Count > 0)
                            {
                                Console.WriteLine($"Connected users ({_clients.Count}):");
                                foreach (var username in _clients.Keys)
                                {
                                    Console.WriteLine($"  - {username}");
                                }
                            }
                            else
                            {
                                Console.WriteLine("No users connected");
                            }
                            break;

                        case "/stop":
                            Console.WriteLine("Stopping server...");
                            Stop();
                            break;

                        default:
                            Console.WriteLine("Unknown command. Available commands: /broadcast, /say, /msg, /kick, /list, /stop");
                            break;
                    }
                }
            });
        }

        private async Task HandleClientAsync(TcpClient client)
        {
            string? clientId = null;
            NetworkStream? stream = null;

            try
            {
                stream = client.GetStream();
                var buffer = new byte[4096];
                
                // Read the initial username
                var bytesRead = await stream.ReadAsync(buffer);
                if (bytesRead == 0) return;

                clientId = Encoding.UTF8.GetString(buffer, 0, bytesRead).Trim();
                
                // Validate username
                if (string.IsNullOrWhiteSpace(clientId))
                {
                    await SendJsonAsync(stream, "server", "Invalid username. Connection rejected.");
                    client.Close();
                    return;
                }
                
                // Check for duplicate username
                if (_clients.ContainsKey(clientId))
                {
                    await SendJsonAsync(stream, "server", $"Username '{clientId}' is already taken. Connection rejected.");
                    client.Close();
                    Console.WriteLine($"Connection rejected: Username '{clientId}' already in use");
                    return;
                }
                
                if (_clients.TryAdd(clientId, client))
                {
                    Console.WriteLine($"{clientId} connected");
                    await BroadcastJsonAsync("server", $"{clientId} joined the chat", null);

                    // Handle incoming messages
                    while (_isRunning && client.Connected)
                    {
                        bytesRead = await stream.ReadAsync(buffer);
                        if (bytesRead == 0) break;

                        var jsonString = Encoding.UTF8.GetString(buffer, 0, bytesRead).Trim();
                        
                        try
                        {
                            var message = JsonSerializer.Deserialize<ClientMessage>(jsonString);
                            
                            if (message != null)
                            {
                                await ProcessMessageAsync(message, clientId, stream);
                            }
                        }
                        catch (JsonException ex)
                        {
                            Console.WriteLine($"Invalid JSON from {clientId}: {ex.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error handling client {clientId}: {ex.Message}");
            }
            finally
            {
                if (clientId != null && _clients.TryRemove(clientId, out _))
                {
                    Console.WriteLine($"{clientId} disconnected");
                    await BroadcastJsonAsync("server", $"{clientId} left the chat", null);
                }
                stream?.Close();
                client.Close();
            }
        }

        private async Task ProcessMessageAsync(ClientMessage message, string clientId, NetworkStream stream)
        {
            var command = message.Command?.ToLower();

            if (command == "/say")
            {
                Console.WriteLine($"{clientId}: {message.Body}");
                await BroadcastJsonAsync("message", message.Body ?? "", clientId, clientId);
            }
            else if (command == "/list")
            {
                var userList = string.Join(", ", _clients.Keys);
                var response = $"Connected users ({_clients.Count}): {userList}";
                await SendJsonAsync(stream, "command", response);
                Console.WriteLine($"{clientId} requested user list");
            }
            else if (command == "/msg")
            {
                if (!string.IsNullOrWhiteSpace(message.Body))
                {
                    var parts = message.Body.Split(' ', 2);
                    if (parts.Length >= 2)
                    {
                        var targetUsername = parts[0].Trim();
                        var privateMessage = parts[1];
                        
                        if (_clients.TryGetValue(targetUsername, out var targetClient))
                        {
                            await SendJsonAsync(targetClient.GetStream(), "private", privateMessage, clientId);
                            // Play notification sound for private message
                            Console.Beep(800, 100);
                            await SendJsonAsync(stream, "privatesent", $"To {targetUsername}: {privateMessage}", clientId);
                            Console.WriteLine($"{clientId} -> {targetUsername}: {privateMessage}");
                        }
                        else
                        {
                            await SendJsonAsync(stream, "command", $"User '{targetUsername}' not found");
                        }
                    }
                    else
                    {
                        await SendJsonAsync(stream, "command", "Usage: /msg <username> <message>");
                    }
                }
                else
                {
                    await SendJsonAsync(stream, "command", "Usage: /msg <username> <message>");
                }
            }
            else if (command == "/ping")
            {
                await SendJsonAsync(stream, "command", "Pong! Server is responsive.");
                Console.WriteLine($"{clientId} pinged the server");
            }
            else if (command == "/time")
            {
                var serverTime = DateTime.Now.ToString("HH:mm:ss");
                await SendJsonAsync(stream, "command", $"Server time: {serverTime}");
                Console.WriteLine($"{clientId} requested server time");
            }
            else if (command == "/uptime")
            {
                var uptime = DateTime.Now - Process.GetCurrentProcess().StartTime;
                await SendJsonAsync(stream, "command", $"Server uptime: {uptime.Days}d {uptime.Hours}h {uptime.Minutes}m {uptime.Seconds}s");
                Console.WriteLine($"{clientId} requested uptime");
            }
            else if (command == "/whoami")
            {
                await SendJsonAsync(stream, "command", $"You are logged in as: {clientId}");
                Console.WriteLine($"{clientId} used whoami");
            }
            else if (command == "/me")
            {
                if (!string.IsNullOrWhiteSpace(message.Body))
                {
                    var actionMessage = $"* {clientId} {message.Body}";
                    await BroadcastJsonAsync("action", actionMessage, null);
                    Console.WriteLine($"ACTION: {actionMessage}");
                }
                else
                {
                    await SendJsonAsync(stream, "command", "Usage: /me <action>");
                }
            }
            else if (command == "/exit")
            {
                Console.WriteLine($"{clientId} requested disconnect");
            }
            else if (command == "/help")
            {
                var helpText = new StringBuilder();
                helpText.AppendLine("Available commands:");
                helpText.AppendLine("  /help     - Show this help message");
                helpText.AppendLine("  /list     - List all connected users");
                helpText.AppendLine("  /msg <username> <message> - Send a private message to a user");
                helpText.AppendLine("  /me <action> - Send an action message (e.g., /me waves)");
                helpText.AppendLine("  /whoami   - Show your current username");
                helpText.AppendLine("  /ping     - Check server responsiveness");
                helpText.AppendLine("  /time     - Show server time");
                helpText.AppendLine("  /uptime   - Show server uptime");
                helpText.AppendLine("  /say      - Sends a message to the server");
                helpText.Append("  /exit     - Disconnect from the chat");
                
                await SendJsonAsync(stream, "command", helpText.ToString());
                Console.WriteLine($"{clientId} requested help");
            }
            else
            {
                await SendJsonAsync(stream, "command", "Unknown command. Type /help for available commands.");
            }
        }

        private async Task SendJsonAsync(NetworkStream stream, string type, string body, string? username = null)
        {
            var response = new ServerMessage
            {
                Type = type,
                Body = body,
                Username = username
            };
            
            var jsonString = JsonSerializer.Serialize(response) + "\n";
            var jsonBytes = Encoding.UTF8.GetBytes(jsonString);
            
            try
            {
                await stream.WriteAsync(jsonBytes);
            }
            catch
            {
                // Client disconnected
            }
        }

        private async Task BroadcastJsonAsync(string type, string body, string? excludeClientId, string? username = null)
        {
            var response = new ServerMessage
            {
                Type = type,
                Body = body,
                Username = username
            };
            
            var jsonString = JsonSerializer.Serialize(response) + "\n";
            var jsonBytes = Encoding.UTF8.GetBytes(jsonString);

            var tasks = _clients
                .Where(kvp => kvp.Key != excludeClientId)
                .Select(async kvp =>
                {
                    try
                    {
                        var stream = kvp.Value.GetStream();
                        await stream.WriteAsync(jsonBytes);
                    }
                    catch
                    {
                        // Client disconnected, will be cleaned up by HandleClientAsync
                    }
                });

            await Task.WhenAll(tasks);
        }

        public void Stop()
        {
            _isRunning = false;
            _listener?.Stop();
            
            foreach (var client in _clients.Values)
            {
                client.Close();
            }
            _clients.Clear();
        }
    }
}
