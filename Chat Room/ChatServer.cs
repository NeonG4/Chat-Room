using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Collections.Concurrent;

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
                            Console.WriteLine("Unknown command. Available commands: /broadcast, /kick, /list, /stop");
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
            else if (command == "/exit")
            {
                Console.WriteLine($"{clientId} requested disconnect");
            }
            else if (command == "/help")
            {
                var helpText = new StringBuilder();
                helpText.AppendLine("Available commands:");
                helpText.AppendLine("  /help  - Show this help message");
                helpText.AppendLine("  /list  - List all connected users");
                helpText.AppendLine("  /say   - Sends a message to the server");
                helpText.Append("  /exit  - Disconnect from the chat");
                
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
