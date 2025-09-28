using Aspose.Email.Storage.Pst;
using Aspose.Email.Mapi;
using System;
using System.IO;
using System.Collections.Generic;

class PstDeletedItemsSplitter
{
    // Configure the year span size (2 = pair of years, 1 = single year, etc.)
    const int SpanYears = 2;

    static List<(DateTime Start, DateTime End)> GetYearRanges(int startYear, int endYear, int spanYears)
    {
        var ranges = new List<(DateTime, DateTime)>();
        for (int year = startYear; year <= endYear; year += spanYears)
        {
            int lastYear = Math.Min(year + spanYears - 1, endYear);
            DateTime start = new(year, 1, 1);
            DateTime end = new(lastYear, 12, 31);
            ranges.Add((start, end));
        }
        return ranges;
    }

    static DateTime GetMessageDate(MapiMessage message)
    {
        DateTime deliveryTime = default;
        DateTime clientSubmitTime = default;
        DateTime creationTime = default;
        DateTime modificationTime = default;
        
        // Collect all available dates
        if (message.DeliveryTime != default)
            deliveryTime = message.DeliveryTime;
            
        if (message.ClientSubmitTime != default)
            clientSubmitTime = message.ClientSubmitTime;
            
        try
        {
            var creationTimeProp = message.Properties[MapiPropertyTag.PR_CREATION_TIME];
            if (creationTimeProp != null && creationTimeProp.GetDateTime() != default)
                creationTime = creationTimeProp.GetDateTime();
        }
        catch
        {
            // Ignore property access errors
        }
        
        try
        {
            var modTimeProp = message.Properties[MapiPropertyTag.PR_LAST_MODIFICATION_TIME];
            if (modTimeProp != null && modTimeProp.GetDateTime() != default)
                modificationTime = modTimeProp.GetDateTime();
        }
        catch
        {
            // Ignore property access errors
        }
        
        // DEBUG: Log all available dates to understand the pattern
        Console.WriteLine($"[DEBUG] Available dates - Delivery: {(deliveryTime == default ? "none" : deliveryTime.ToString("yyyy-MM-dd"))}, " +
                         $"Submit: {(clientSubmitTime == default ? "none" : clientSubmitTime.ToString("yyyy-MM-dd"))}, " +
                         $"Creation: {(creationTime == default ? "none" : creationTime.ToString("yyyy-MM-dd"))}, " +
                         $"Modification: {(modificationTime == default ? "none" : modificationTime.ToString("yyyy-MM-dd"))}");
        
        // Prefer delivery time and client submit time as they represent the original message dates
        if (deliveryTime != default)
            return deliveryTime;
            
        if (clientSubmitTime != default)
            return clientSubmitTime;
            
        if (creationTime != default)
            return creationTime;
        
        // Only use modification time as a last resort, and warn about it
        if (modificationTime != default)
        {
            Console.WriteLine($"[WARN] Using modification time {modificationTime:yyyy-MM-dd} - may not reflect original message date");
            return modificationTime;
        }
            
        // If no valid date found, return default
        return default;
    }

    static void ProcessFolder(
        PersonalStorage sourcePst,
        FolderInfo srcFolder,
        Dictionary<(DateTime, DateTime), PersonalStorage> pstMap,
        string folderPath)
    {
        int processedCount = 0;
        int skippedCount = 0;
        
        // Enumerate lightweight message metadata
        foreach (MessageInfo mi in srcFolder.EnumerateMessages())
        {
            MapiMessage? full = null;
            try
            {
                full = sourcePst.ExtractMessage(mi); // Promote to full MapiMessage

                // Try multiple date fields in order of preference
                DateTime messageDate = GetMessageDate(full);
                if (messageDate == default)
                {
                    Console.WriteLine($"[WARN] Message has no valid date fields, skipping (EntryId={mi.EntryIdString})");
                    skippedCount++;
                    continue;
                }

                // DEBUG: Log the extracted date to understand the distribution
                Console.WriteLine($"[DEBUG] Message date extracted: {messageDate:yyyy-MM-dd HH:mm:ss} (EntryId={mi.EntryIdString})");

                bool addedToRange = false;
                foreach (var range in pstMap.Keys)
                {
                    if (messageDate >= range.Item1 && messageDate <= range.Item2)
                    {
                        Console.WriteLine($"[DEBUG] Message {messageDate:yyyy-MM-dd} matched range {range.Item1:yyyy-MM-dd} to {range.Item2:yyyy-MM-dd}");
                        var destPst = pstMap[range];
                        var destFolder = GetOrCreateFolder(destPst, folderPath);
                        destFolder.AddMessage(full);
                        addedToRange = true;
                        processedCount++;
                        break; // Added to one, stop checking ranges
                    }
                }
                
                if (!addedToRange)
                {
                    Console.WriteLine($"[WARN] Message date {messageDate:yyyy-MM-dd} outside all ranges, skipping (EntryId={mi.EntryIdString})");
                    skippedCount++;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WARN] Failed to process a message (EntryId={mi.EntryIdString}): {ex.Message}");
                skippedCount++;
            }
            finally
            {
                full?.Dispose();
            }
        }
        
        if (processedCount > 0 || skippedCount > 0)
        {
            Console.WriteLine($"[INFO] Folder '{folderPath}': Processed {processedCount} messages, skipped {skippedCount}");
        }

        // Recurse into subfolders
        foreach (FolderInfo sub in srcFolder.GetSubFolders())
        {
            string subPath = folderPath + "\\" + sub.DisplayName;
            ProcessFolder(sourcePst, sub, pstMap, subPath);
        }
    }

    static FolderInfo GetOrCreateFolder(PersonalStorage pst, string path)
    {
        var root = pst.RootFolder;
        string[] parts = path.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        FolderInfo current = root;

        foreach (string part in parts)
        {
            FolderInfo? existing = null;
            foreach (FolderInfo f in current.GetSubFolders())
            {
                if (f.DisplayName.Equals(part, StringComparison.OrdinalIgnoreCase))
                {
                    existing = f;
                    break;
                }
            }
            current = existing ?? current.AddSubFolder(part);
        }
        return current;
    }

    static int CountMessagesInPst(PersonalStorage pst)
    {
        return CountMessagesInFolder(pst.RootFolder);
    }

    static int CountMessagesInFolder(FolderInfo folder)
    {
        int count = folder.ContentCount;
        foreach (FolderInfo subfolder in folder.GetSubFolders())
        {
            count += CountMessagesInFolder(subfolder);
        }
        return count;
    }

    static void Main(string[] args)
    {
        // TODO: Update these paths before running
        string sourcePst = @"F:\Deleted Items - 2010.pst";   // Source PST with Deleted Items
        string outputFolder = @"F:\Segmented-PSTs";           // Destination output folder

        if (!File.Exists(sourcePst))
        {
            Console.WriteLine($"Source PST not found: {sourcePst}");
            return;
        }

        Directory.CreateDirectory(outputFolder);

        var yearRanges = GetYearRanges(2010, 2025, SpanYears);
        
        // DEBUG: Log the year ranges being used
        Console.WriteLine("[DEBUG] Year ranges configured:");
        foreach (var range in yearRanges)
        {
            Console.WriteLine($"[DEBUG]   {range.Start:yyyy-MM-dd} to {range.End:yyyy-MM-dd}");
        }

        // Prepare destination PST files
        var pstMap = new Dictionary<(DateTime, DateTime), PersonalStorage>();
        try
        {
            foreach (var range in yearRanges)
            {
                string destPath = Path.Combine(outputFolder, $"{range.Start:yyyy}-{range.End:yyyy}.pst");
                if (File.Exists(destPath))
                {
                    Console.WriteLine($"[INFO] Overwriting existing PST: {destPath}");
                    File.Delete(destPath);
                }
                pstMap[range] = PersonalStorage.Create(destPath, FileFormatVersion.Unicode);
            }

            using PersonalStorage source = PersonalStorage.FromFile(sourcePst);
            var deletedItems = source.RootFolder.GetSubFolder("Deleted Items");

            if (deletedItems == null)
            {
                Console.WriteLine("Deleted Items folder not found.");
                return;
            }

            Console.WriteLine("Processing...");
            ProcessFolder(source, deletedItems, pstMap, "Deleted Items");
            
            // Print summary statistics
            Console.WriteLine("\n=== Processing Summary ===");
            foreach (var kvp in pstMap)
            {
                var range = kvp.Key;
                var pst = kvp.Value;
                int messageCount = CountMessagesInPst(pst);
                Console.WriteLine($"Range {range.Item1:yyyy}-{range.Item2:yyyy}: {messageCount} messages");
            }
            
            Console.WriteLine("Done. Output written to: " + outputFolder);
        }
        finally
        {
            // Ensure all created PSTs are disposed
            foreach (var pst in pstMap.Values)
                pst.Dispose();
        }
    }
}