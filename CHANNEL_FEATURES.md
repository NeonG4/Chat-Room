# Private Channel Features

## Overview
Private channels allow users to create invitation-only group chats where they can communicate separately from the main chat room. Only invited members can join and participate in these channels.

## Channel Commands

### User Commands (Available to all users)

#### `/createchannel <name>`
Creates a new private channel with you as the owner.
- **Usage:** `/createchannel gaming`
- **Result:** Creates a channel called "gaming" with you as owner and first member
- **Note:** Channel names must be unique

#### `/invite <channel> <username>`
Invites a user to join one of your channels.
- **Usage:** `/invite gaming Bob`
- **Requirements:** 
  - You must be a member of the channel
  - The target user must exist
  - The user cannot already be a member
- **Result:** User receives an invitation notification

#### `/joinchannel <name>`
Joins a channel you were invited to.
- **Usage:** `/joinchannel gaming`
- **Requirements:** You must have been invited to the channel
- **Result:** You become a member and other members are notified

#### `/leavechannel <name>`
Leaves a channel you're a member of.
- **Usage:** `/leavechannel gaming`
- **Requirements:** 
  - You must be a member
  - You cannot leave if you're the owner (delete the channel instead)
- **Result:** You're removed from the channel and others are notified

#### `/deletechannel <name>`
Permanently deletes a channel.
- **Usage:** `/deletechannel gaming`
- **Requirements:** You must be the channel owner
- **Result:** Channel is permanently removed

#### `/channelmsg <channel> <message>`
Sends a message to a channel.
- **Usage:** `/channelmsg gaming Hey everyone!`
- **Requirements:** You must be a member of the channel
- **Result:** All channel members receive the message
- **Note:** Muted users cannot send channel messages

#### `/mychannels`
Lists all channels you're a member of.
- **Usage:** `/mychannels`
- **Shows:**
  - Channel name
  - Owner status (if you own it)
  - Number of members

#### `/channelinvites`
Lists all pending channel invitations.
- **Usage:** `/channelinvites`
- **Shows:**
  - Channel name
  - Owner name
  - Instructions to join

#### `/channelmembers <name>`
Lists all members of a channel.
- **Usage:** `/channelmembers gaming`
- **Requirements:** You must be a member of the channel
- **Shows:**
  - Member usernames
  - Owner indicator
  - Online/offline status

### Server Commands (Admin only)

#### `/channels`
Lists all private channels on the server.
- **Usage:** `/channels`
- **Shows:**
  - Channel name
  - Owner
  - Number of members

#### `/channelinfo <channel>`
Shows detailed information about a specific channel.
- **Usage:** `/channelinfo gaming`
- **Shows:**
  - Channel name
  - Owner
  - Creation date
  - List of members with online status
  - List of invited users (if any)
  - Total message count

## Channel Features

### Ownership and Permissions
- **Owner**: The user who created the channel
  - Can invite users
  - Can delete the channel
  - Cannot leave without deleting

- **Members**: Users who have joined the channel
  - Can invite other users
  - Can send messages
  - Can leave the channel
  - Can view member list

### Message Delivery
- Channel messages are delivered only to online members
- Messages are displayed with the format: `[channelname] username: message`
- Channel messages are color-coded in **magenta** on both client and server
- A notification sound plays when you receive a channel message

### Data Persistence
- All channels are saved to `channels.json`
- Channel data includes:
  - Channel name
  - Owner
  - Members list
  - Invited users list
  - Creation date
  - Message history (last 100 messages)

### Message History
- Each channel stores up to 100 messages
- Messages include timestamps and sender information
- History is viewable by server admins via `/channelinfo`

## Example Workflow

1. **Alice creates a channel:**
   ```
   Alice> /createchannel gamers
   [System] Channel 'gamers' created successfully! You are the owner.
   ```

2. **Alice invites Bob:**
   ```
   Alice> /invite gamers Bob
   [System] Invited Bob to channel 'gamers'
   ```

3. **Bob receives notification and joins:**
   ```
   [SERVER] Alice invited you to join channel 'gamers'. Use /joinchannel gamers to join.
   Bob> /joinchannel gamers
   [System] Successfully joined channel 'gamers'
   ```

4. **Alice and Bob can now chat in the channel:**
   ```
   Alice> /channelmsg gamers Hey Bob!
   [gamers] Alice: Hey Bob!
   
   Bob> /channelmsg gamers Hi Alice!
   [gamers] Bob: Hi Alice!
   ```

5. **They can invite more people:**
   ```
   Bob> /invite gamers Charlie
   [System] Invited Charlie to channel 'gamers'
   ```

6. **Check channel members:**
   ```
   Alice> /channelmembers gamers
   Members of 'gamers' (2):
     Alice [OWNER] (online)
     Bob (online)
   ```

## Color Coding

### Client Side:
- **Magenta** - Channel messages: `[channelname] username: message`
- **Blue** - Channel notifications: `[channelname] user joined/left`

### Server Side:
- **Green** - Channel creation, user joins
- **Yellow** - User leaves channel
- **Red** - Channel deletion
- **Cyan** - Channel invitations
- **Magenta** - Channel messages in server log

## Data Files

### `channels.json`
Stores all channel data:

```json
{
  "gamers": {
    "ChannelName": "gamers",
    "Owner": "Alice",
    "Members": ["Alice", "Bob"],
    "InvitedUsers": ["Charlie"],
    "CreatedDate": "2024-01-15T10:30:00",
    "MessageHistory": [
      "[2024-01-15 10:31:00] Alice: Hey Bob!",
      "[2024-01-15 10:32:00] Bob: Hi Alice!"
    ]
  }
}
```

## Security and Validation

- ? Channel names are validated and must be unique
- ? Only members can send messages
- ? Only the owner can delete the channel
- ? Muted users cannot send channel messages
- ? Users must be invited before joining
- ? All channel data persists across server restarts

## Notes

- Channel messages count toward your total message count
- Muted users can receive but not send channel messages
- When a user disconnects, they remain a channel member
- Channels persist even if all members are offline
- The owner cannot leave their own channel (must delete it instead)
