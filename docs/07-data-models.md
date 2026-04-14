# Data Models

## Overview
ToxicDetectionBot uses Entity Framework Core with SQLite to store sentiment analysis data. The data model consists of several interconnected entities that track raw message classifications, aggregated statistics, user preferences, and alignment distributions.

## Entity Relationships

```
UserSentiment (many) ←→ UserSentimentScore (one) ←→ User (identified by UserId)
UserSentiment (many) ←→ UserAlignmentScore (one) ←→ User (identified by UserId)
UserOptOut (one) ←→ User (identified by UserId)
```

## Entity Details

### UserSentiment
Stores individual message analysis records.

**Fields:**
- `Id` (int): Auto-incrementing primary key
- `UserId` (string): Discord user ID
- `GuildId` (string): Discord server ID
- `MessageId` (string): Discord message ID
- `MessageContent` (string): The analyzed message text
- `Username` (string): Discord username at time of message
- `GuildName` (string): Server name
- `ChannelName` (string): Channel name
- `IsToxic` (bool): Classification result (false = toxic, true = non-toxic)
- `IsSummarized` (bool): Whether included in aggregate statistics
- `CreatedAt` (DateTime): Timestamp (UTC)

**Indexes:**
- UserId (for user-specific queries)
- GuildId (for server-specific queries)
- IsSummarized (for efficient processing)
- GuildName, ChannelName (for administrative queries)

### UserSentimentScore
Stores aggregated statistics per user per guild.

**Fields:**
- `Id` (int): Auto-incrementing primary key
- `UserId` (string): Discord user ID (part of composite key)
- `GuildId` (string): Discord server ID (part of composite key)
- `TotalMessages` (int): Running total of analyzed messages
- `ToxicMessages` (int): Running total of toxic messages
- `NonToxicMessages` (int): Running total of non-toxic messages
- `ToxicityPercentage` (int): Calculated percentage (0-100)
- `SummarizedAt` (DateTime): Last update timestamp (UTC)

**Constraints:**
- Composite primary key on (UserId, GuildId)
- Indexes on UserId and GuildId for efficient lookups

### UserOptOut
Tracks user privacy preferences.

**Fields:**
- `UserId` (string): Discord user ID (primary key)
- `IsOptedOut` (bool): Current opt-out status
- `LastChangedAt` (DateTime): Timestamp of last status change (UTC)

**Indexes:**
- IsOptedOut (for efficient opt-out user queries)

### UserAlignmentScore
Tracks distribution of alignment classifications (optional feature).

**Fields:**
- `Id` (int): Auto-incrementing primary key
- `UserId` (string): Discord user ID (part of composite key)
- `GuildId` (string): Discord server ID (part of composite key)
- `LawfulGood` (int): Count of Lawful Good classifications
- `NeutralGood` (int): Count of Neutral Good classifications
- `ChaoticGood` (int): Count of Chaotic Good classifications
- `LawfulNeutral` (int): Count of Lawful Neutral classifications
- `TrueNeutral` (int): Count of True Neutral classifications
- `ChaoticNeutral` (int): Count of Chaotic Neutral classifications
- `LawfulEvil` (int): Count of Lawful Evil classifications
- `NeutralEvil` (int): Count of Neutral Evil classifications
- `ChaoticEvil` (int): Count of Chaotic Evil classifications
- `SummarizedAt` (DateTime): Last update timestamp (UTC)

**Constraints:**
- Composite primary key on (UserId, GuildId)
- Indexes on UserId and GuildId

### AlignmentType
Enum defining possible alignment classifications (used with UserAlignmentScore).

**Values:**
- LawfulGood
- NeutralGood
- ChaoticGood
- LawfulNeutral
- TrueNeutral
- ChaoticNeutral
- LawfulEvil
- NeutralEvil
- ChaoticEvil

## Data Flow
1. Raw message classifications stored in UserSentiment
2. Periodic summarization moves data to UserSentimentScore
3. User preferences managed via UserOptOut
4. Optional alignment tracking in UserAlignmentScore

## Important Notes
- The IsToxic field uses reversed semantics: false = toxic, true = non-toxic
- All string IDs store Discord snowflake values as strings
- Timestamps are stored in UTC for consistency
- The summarization process marks UserSentiment.IsSummarized = true after processing
- Retention service removes old records from all tables based on UserSentiment timestamps