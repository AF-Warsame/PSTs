# Debug Instructions for PST Splitter

## Enabling Debug Output

If you need to troubleshoot the date extraction process, you can enable detailed debug logging by changing `false` to `true` in the following locations in `Program.cs`:

### 1. Date Field Logging (line ~62)
```csharp
if (true) // Changed from false to true
{
    Console.WriteLine($"[DEBUG] Available dates - Delivery: ...");
}
```

### 2. Modification Time Warnings (line ~85)
```csharp
if (true) // Changed from false to true
    Console.WriteLine($"[WARN] Using modification time {modificationTime:yyyy-MM-dd}...");
```

### 3. Alternative Strategy Logging (line ~105)
```csharp
if (true) // Changed from false to true
    Console.WriteLine($"[INFO] Using oldest non-recent date: {oldestNonRecent:yyyy-MM-dd}...");
```

### 4. Message Processing Details (lines ~194, ~201, ~209, ~222)
```csharp
if (true) // Changed from false to true
    Console.WriteLine($"[DEBUG] Message date extracted: ...");
```

### 5. Year Range Display (line ~305)
```csharp
if (true) // Changed from false to true
{
    Console.WriteLine("[DEBUG] Year ranges configured:");
    // ...
}
```

## What Debug Output Shows

When enabled, you'll see:
- All available date fields for each message (DeliveryTime, ClientSubmitTime, CreationTime, ModificationTime)
- Which date field is being used for each message
- Year ranges being used for splitting
- Which range each message gets assigned to
- Warnings when using potentially misleading modification times
- Details about the alternative date extraction strategy when triggered

## Normal Operation

With debug output disabled (default), the program shows:
- Detection of suspicious date clustering (if occurs)
- Switching to alternative strategy (if needed)  
- Processing summary per folder
- Final message count per year range
- Essential warnings only

This keeps the output clean while still providing important information about the date extraction strategy being used.