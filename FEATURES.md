# Chat Room - Feature Summary

## ?? New Features Added

### 1. **User Muting System**
- **Server Commands:**
  - `/mute <username> <minutes>` - Temporarily mute a user for specified duration
  - `/unmute <username>` - Immediately unmute a user
- **Features:**
  - Time-based mutes that automatically expire
  - Muted users cannot send messages (regular, private, or action messages)
  - Server shows mute status in `/stats` command
  - Muted users receive notification when muted/unmuted
  - Mute status persists across server restarts

### 2. **Message History Tracking**
- **Automatic Features:**
  - Last 100 messages per user automatically saved
  - Timestamps on all messages
  - Includes regular messages, private messages, and actions
- **Server Commands:**
  - `/history <username> [count]` - View any user's message history
- **Client Commands:**
  - `/history [count]` - View your own message history (max 50)
- **Data Persistence:**
  - All history saved to `users.json`
  - Survives server restarts

### 3. **Enhanced User Statistics**
- **Updated `/stats` Command Shows:**
  - Total messages sent
  - Account creation date
  - Last login time
  - Online status
  - **NEW:** Mute status and expiration time
- **Updated `/whoami` Command Shows:**
  - Username
  - Total messages sent
  - Account creation date
  - Last login time

### 4. **Enhanced `/users` Command**
- Displays all registered users
- Shows message count for each user
- Shows last login time
- Indicates which users are currently online
- Sorted by message count (most active first)

## ?? Complete Feature List

### **Authentication & Security**
? User registration with password
? Secure login with SHA-256 password hashing
? Password confirmation on registration
? Duplicate username prevention
? Duplicate connection prevention
? Username validation
? Persistent user data storage

### **Messaging**
? Public chat messages
? Private messages (`/msg`)
? Action messages (`/me`)
? Message count tracking
? Message history (last 100 per user)
? Mute system with auto-expiration

### **User Management**
? User registration and login
? Password-based authentication
? User statistics tracking
? Last login tracking
? Account creation date
? Message count per user
? Temporary user muting

### **Server Administration**
? `/broadcast` - Server announcements
? `/say` - Chat as SERVER
? `/msg` - Send private messages as SERVER
? `/kick` - Remove users
? `/mute` - Temporarily mute users
? `/unmute` - Unmute users
? `/list` - List connected users
? `/users` - List all registered users
? `/stats` - View detailed user statistics
? `/history` - View user message history
? `/info` - Server IP and status
? `/help` - Command list

### **Client Commands**
? `/help` - Show available commands
? `/list` - List connected users
? `/msg` - Send private messages
? `/me` - Send action messages
? `/whoami` - View your statistics
? `/history` - View your message history
? `/ping` - Check server responsiveness
? `/time` - Show server time
? `/uptime` - Show server uptime
? `/exit` - Disconnect

### **User Experience**
? Color-coded messages (server, private, action, etc.)
? Sound notifications for private messages
? Input line preservation (no text splitting)
? Character-by-character input with backspace support
? Password masking during input
? Real-time message delivery
? Connection status messages

### **Data Persistence**
? User accounts saved to `users.json`
? Passwords securely hashed
? Message counts tracked
? Message history stored (last 100)
? Last login timestamps
? Mute status and expiration
? Auto-save on all changes

### **Server Monitoring**
? Color-coded server console logs
? Connection/disconnection tracking
? Command usage logging
? Error logging
? Registration/login attempt tracking
? Message activity logging
? Server IP detection and display
? Uptime tracking

## ?? Color Scheme

### Client Side:
- ?? **Blue** - Server announcements `[SERVER]`
- ?? **Cyan** - Private messages `[PM from/to]` 
- ?? **Yellow** - Action messages `* user action`
- ? **Dark Gray** - Command responses
- ?? **Magenta** - Your messages `[you]`
- ?? **Green** - Other users' messages

### Server Side:
- ?? **Green** - Connections, registrations, unmutes
- ?? **Yellow/Dark Yellow** - Kicks, disconnections, mutes
- ?? **Red** - Errors, rejections, failures
- ?? **Cyan** - User lists, statistics, info
- ? **Dark Gray** - Command usage logs
- ? **White** - Regular user messages

## ?? Data Files

### `users.json`
Stores all user data including:
- Usernames and password hashes
- Message counts
- Account creation dates
- Last login timestamps
- Mute status and expiration
- Message history (last 100 messages)

**Example Structure:**
```json
{
  "Alice": {
    "Username": "Alice",
    "PasswordHash": "...",
    "MessageCount": 42,
    "CreatedDate": "2024-01-15T10:30:00",
    "LastLogin": "2024-01-15T14:25:00",
    "IsMuted": false,
    "MutedUntil": null,
    "MessageHistory": [
      "[2024-01-15 10:31:00] Hello!",
      "[2024-01-15 10:32:15] PM to Bob: Hi there"
    ]
  }
}
```

## ?? Security Features

1. **Password Hashing** - SHA-256 encryption
2. **Password Masking** - Hidden during input
3. **Session Management** - One connection per user
4. **Input Validation** - Username and password validation
5. **Persistent Bans** - Via mute system
6. **Error Handling** - Graceful disconnection on errors

## ?? Best Practices Implemented

- Thread-safe console access
- Asynchronous I/O operations
- JSON-based protocol
- Structured message types
- Error handling and logging
- Data persistence
- Input validation
- Clean code separation
- Comprehensive help system
