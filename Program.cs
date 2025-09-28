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
        // Try multiple date fields in order of preference
        // 1. DeliveryTime - when the message was delivered
        if (message.DeliveryTime != default)
            return message.DeliveryTime;
            
        // 2. ClientSubmitTime - when the sender submitted the message
        if (message.ClientSubmitTime != default)
            return message.ClientSubmitTime;
            
        // 3. Try to get creation time from properties
        try
        {
            var creationTimeProp = message.Properties[MapiPropertyTag.PR_CREATION_TIME];
            if (creationTimeProp != null && creationTimeProp.GetDateTime() != default)
                return creationTimeProp.GetDateTime();
        }
        catch
        {
            // Ignore property access errors
        }
        
        // 4. Try to get last modification time from properties
        try
        {
            var modTimeProp = message.Properties[MapiPropertyTag.PR_LAST_MODIFICATION_TIME];
            if (modTimeProp != null && modTimeProp.GetDateTime() != default)
                return modTimeProp.GetDateTime();
        }
        catch
        {
            // Ignore property access errors
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

                bool addedToRange = false;
                foreach (var range in pstMap.Keys)
                {
                    if (messageDate >= range.Item1 && messageDate <= range.Item2)
                    {
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