using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Chat_Room
{
    internal class UserManager
    {
        private readonly string _userDataFile = "users.json";
        private Dictionary<string, UserAccount> _users = new();
        private readonly object _fileLock = new object();

        public UserManager()
        {
            LoadUsers();
        }

        private void LoadUsers()
        {
            lock (_fileLock)
            {
                if (File.Exists(_userDataFile))
                {
                    try
                    {
                        var json = File.ReadAllText(_userDataFile);
                        _users = JsonSerializer.Deserialize<Dictionary<string, UserAccount>>(json) ?? new();
                    }
                    catch
                    {
                        _users = new Dictionary<string, UserAccount>();
                    }
                }
            }
        }

        private void SaveUsers()
        {
            lock (_fileLock)
            {
                try
                {
                    var json = JsonSerializer.Serialize(_users, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(_userDataFile, json);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error saving users: {ex.Message}");
                }
            }
        }

        private string HashPassword(string password)
        {
            using var sha256 = SHA256.Create();
            var bytes = Encoding.UTF8.GetBytes(password);
            var hash = sha256.ComputeHash(bytes);
            return Convert.ToBase64String(hash);
        }

        public bool RegisterUser(string username, string password)
        {
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
                return false;

            if (_users.ContainsKey(username))
                return false;

            var account = new UserAccount
            {
                Username = username,
                PasswordHash = HashPassword(password),
                MessageCount = 0,
                CreatedDate = DateTime.Now,
                LastLogin = DateTime.Now
            };

            _users[username] = account;
            SaveUsers();
            return true;
        }

        public bool AuthenticateUser(string username, string password)
        {
            if (!_users.ContainsKey(username))
                return false;

            var user = _users[username];
            var inputHash = HashPassword(password);

            if (user.PasswordHash == inputHash)
            {
                user.LastLogin = DateTime.Now;
                SaveUsers();
                return true;
            }

            return false;
        }

        public void IncrementMessageCount(string username)
        {
            if (_users.ContainsKey(username))
            {
                _users[username].MessageCount++;
                SaveUsers();
            }
        }

        public int GetMessageCount(string username)
        {
            return _users.ContainsKey(username) ? _users[username].MessageCount : 0;
        }

        public UserAccount? GetUserAccount(string username)
        {
            return _users.ContainsKey(username) ? _users[username] : null;
        }

        public bool UserExists(string username)
        {
            return _users.ContainsKey(username);
        }

        public int GetTotalUsers()
        {
            return _users.Count;
        }

        public List<UserAccount> GetAllUsers()
        {
            return _users.Values.ToList();
        }

        public void MuteUser(string username, int minutes)
        {
            if (_users.ContainsKey(username))
            {
                _users[username].IsMuted = true;
                _users[username].MutedUntil = DateTime.Now.AddMinutes(minutes);
                SaveUsers();
            }
        }

        public void UnmuteUser(string username)
        {
            if (_users.ContainsKey(username))
            {
                _users[username].IsMuted = false;
                _users[username].MutedUntil = null;
                SaveUsers();
            }
        }

        public bool IsUserMuted(string username)
        {
            if (!_users.ContainsKey(username))
                return false;

            var user = _users[username];
            
            if (!user.IsMuted)
                return false;

            if (user.MutedUntil.HasValue && DateTime.Now >= user.MutedUntil.Value)
            {
                user.IsMuted = false;
                user.MutedUntil = null;
                SaveUsers();
                return false;
            }

            return user.IsMuted;
        }

        public void AddMessageToHistory(string username, string message)
        {
            if (_users.ContainsKey(username))
            {
                var user = _users[username];
                user.MessageHistory.Add($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}");
                
                // Keep only last 100 messages
                if (user.MessageHistory.Count > 100)
                {
                    user.MessageHistory.RemoveAt(0);
                }
                SaveUsers();
            }
        }

        public List<string> GetMessageHistory(string username, int count = 10)
        {
            if (_users.ContainsKey(username))
            {
                var history = _users[username].MessageHistory;
                return history.TakeLast(count).ToList();
            }
            return new List<string>();
        }

        public void PromoteToAdmin(string username, string promotedBy)
        {
            if (_users.ContainsKey(username))
            {
                _users[username].IsAdmin = true;
                _users[username].PromotedBy = promotedBy;
                _users[username].PromotedDate = DateTime.Now;
                SaveUsers();
            }
        }

        public void DemoteFromAdmin(string username)
        {
            if (_users.ContainsKey(username))
            {
                _users[username].IsAdmin = false;
                _users[username].PromotedBy = null;
                _users[username].PromotedDate = null;
                SaveUsers();
            }
        }

        public bool IsAdmin(string username)
        {
            return _users.ContainsKey(username) && _users[username].IsAdmin;
        }

        public List<UserAccount> GetAdmins()
        {
            return _users.Values.Where(u => u.IsAdmin).ToList();
        }
    }
}
