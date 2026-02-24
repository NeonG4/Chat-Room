using System.Text.Json;

namespace Chat_Room
{
    internal class ChannelManager
    {
        private readonly string _channelDataFile = "channels.json";
        private Dictionary<string, PrivateChannel> _channels = new();
        private readonly object _fileLock = new object();

        public ChannelManager()
        {
            LoadChannels();
        }

        private void LoadChannels()
        {
            lock (_fileLock)
            {
                if (File.Exists(_channelDataFile))
                {
                    try
                    {
                        var json = File.ReadAllText(_channelDataFile);
                        _channels = JsonSerializer.Deserialize<Dictionary<string, PrivateChannel>>(json) ?? new();
                    }
                    catch
                    {
                        _channels = new Dictionary<string, PrivateChannel>();
                    }
                }
            }
        }

        private void SaveChannels()
        {
            lock (_fileLock)
            {
                try
                {
                    var json = JsonSerializer.Serialize(_channels, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(_channelDataFile, json);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error saving channels: {ex.Message}");
                }
            }
        }

        public bool CreateChannel(string channelName, string owner)
        {
            if (string.IsNullOrWhiteSpace(channelName) || _channels.ContainsKey(channelName))
                return false;

            var channel = new PrivateChannel(channelName, owner);
            _channels[channelName] = channel;
            SaveChannels();
            return true;
        }

        public bool ChannelExists(string channelName)
        {
            return _channels.ContainsKey(channelName);
        }

        public PrivateChannel? GetChannel(string channelName)
        {
            return _channels.ContainsKey(channelName) ? _channels[channelName] : null;
        }

        public bool InviteToChannel(string channelName, string username, string invitedBy)
        {
            if (!_channels.ContainsKey(channelName))
                return false;

            var channel = _channels[channelName];
            
            if (!channel.IsMember(invitedBy))
                return false;

            var result = channel.InviteUser(username);
            if (result)
                SaveChannels();
            
            return result;
        }

        public bool JoinChannel(string channelName, string username)
        {
            if (!_channels.ContainsKey(channelName))
                return false;

            var channel = _channels[channelName];
            
            if (!channel.IsInvited(username) && channel.Owner != username)
                return false;

            var result = channel.AddMember(username);
            if (result)
                SaveChannels();
            
            return result;
        }

        public bool LeaveChannel(string channelName, string username)
        {
            if (!_channels.ContainsKey(channelName))
                return false;

            var channel = _channels[channelName];
            var result = channel.RemoveMember(username);
            
            if (result)
                SaveChannels();
            
            return result;
        }

        public bool DeleteChannel(string channelName, string username)
        {
            if (!_channels.ContainsKey(channelName))
                return false;

            var channel = _channels[channelName];
            
            if (channel.Owner != username)
                return false;

            _channels.Remove(channelName);
            SaveChannels();
            return true;
        }

        public void AddMessageToChannel(string channelName, string username, string message)
        {
            if (_channels.ContainsKey(channelName))
            {
                _channels[channelName].AddMessage(username, message);
                SaveChannels();
            }
        }

        public List<PrivateChannel> GetUserChannels(string username)
        {
            return _channels.Values.Where(c => c.IsMember(username)).ToList();
        }

        public List<PrivateChannel> GetUserInvitations(string username)
        {
            return _channels.Values.Where(c => c.IsInvited(username)).ToList();
        }

        public List<string> GetChannelMembers(string channelName)
        {
            if (_channels.ContainsKey(channelName))
            {
                return _channels[channelName].Members.ToList();
            }
            return new List<string>();
        }
    }
}
