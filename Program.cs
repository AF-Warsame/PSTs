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

    static void ProcessFolder(
        PersonalStorage sourcePst,
        FolderInfo srcFolder,
        Dictionary<(DateTime, DateTime), PersonalStorage> pstMap,
        string folderPath)
    {
        // Enumerate lightweight message metadata
        foreach (MessageInfo mi in srcFolder.EnumerateMessages())
        {
            MapiMessage? full = null;
            try
            {
                full = sourcePst.ExtractMessage(mi); // Promote to full MapiMessage

                // Some messages may not have DeliveryTime populated; skip if default
                DateTime delivery = full.DeliveryTime;
                if (delivery == default)
                    continue;

                foreach (var range in pstMap.Keys)
                {
                    if (delivery >= range.Item1 && delivery <= range.Item2)
                    {
                        var destPst = pstMap[range];
                        var destFolder = GetOrCreateFolder(destPst, folderPath);
                        destFolder.AddMessage(full);
                        break; // Added to one, stop checking ranges
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WARN] Failed to process a message (EntryId={mi.EntryIdString}): {ex.Message}");
            }
            finally
            {
                full?.Dispose();
            }
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