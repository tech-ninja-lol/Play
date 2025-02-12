﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

public static class SongMetaBuilder
{
    public static SongMeta ParseFile(string path, out List<SongIssue> songIssues, Encoding enc, bool useUniversalCharsetDetector)
    {
        using StreamReader reader = TxtReader.GetFileStreamReader(path, enc, useUniversalCharsetDetector);

        songIssues = new();

        string directory = new FileInfo(path).Directory.FullName;
        string filename = new FileInfo(path).Name;

        Dictionary<string, string> requiredFields = new()
        {
            {"artist", null},
            {"bpm", null},
            {"mp3", null},
            {"title", null}
        };
        Dictionary<string, string> voiceNames = new();
        Dictionary<string, string> otherFields = new();

        uint lineNumber = 0;
        while (!reader.EndOfStream)
        {
            ++lineNumber;
            string line = reader.ReadLine();
            if (!line.StartsWith("#", StringComparison.Ordinal))
            {
                if (lineNumber == 1)
                {
                    throw new SongMetaBuilderException("Does not look like a song file; ignoring");
                }
                // Finished headers
                break;
            }
            char[] separator = { ':' };
            string[] parts = line.Substring(1).Split(separator, 2);
            if (parts.Length < 2)
            {
                songIssues.Add(SongIssue.CreateWarning(null, "Invalid formatting on line " + line));
                // Ignore this line. Continue with the next line.
                continue;
            }

            string tagName = parts[0].TrimEnd();
            string tagNameLowerCase = tagName.ToLowerInvariant();
            if (tagNameLowerCase.Length < 1)
            {
                songIssues.Add(SongIssue.CreateWarning(null, "Missing tag name on line " + line));
                // Ignore this line. Continue with the next line.
                continue;
            }

            string tagValue = parts[1].TrimStart();
            if (tagNameLowerCase.Equals("encoding", StringComparison.Ordinal))
            {
                if (tagValue.Equals("UTF8", StringComparison.Ordinal))
                {
                    tagValue = "UTF-8";
                }
                Encoding newEncoding = Encoding.GetEncoding(tagValue);
                if (!newEncoding.Equals(reader.CurrentEncoding))
                {
                    reader.Dispose();
                    return ParseFile(path, out songIssues, newEncoding, useUniversalCharsetDetector);
                }
            }
            else if (requiredFields.ContainsKey(tagNameLowerCase))
            {
                requiredFields[tagNameLowerCase] = tagValue;
            }
            else if (tagNameLowerCase.Equals("previewstart"))
            {
                otherFields[tagNameLowerCase] = tagValue;
            }
            else if (tagNameLowerCase.StartsWith("previewend"))
            {
                otherFields[tagNameLowerCase] = tagValue;
            }
            else if (tagNameLowerCase.StartsWith("p", StringComparison.Ordinal)
                     && tagNameLowerCase.Length == 2
                     && Char.IsDigit(tagNameLowerCase, 1))
            {
                if (!voiceNames.ContainsKey(tagNameLowerCase.ToUpperInvariant()))
                {
                    voiceNames.Add(tagNameLowerCase.ToUpperInvariant(), tagValue);
                }
                // silently ignore already set voiceNames
            }
            else if (tagNameLowerCase.StartsWith("duetsingerp", StringComparison.Ordinal)
                     && tagNameLowerCase.Length == 12
                     && Char.IsDigit(tagNameLowerCase, 11))
            {
                string shorttag = tagNameLowerCase.Substring(10).ToUpperInvariant();
                if (!voiceNames.ContainsKey(shorttag))
                {
                    voiceNames.Add(shorttag, tagValue);
                }
                // silently ignore already set voiceNames
            }
            else
            {
                if (otherFields.ContainsKey(tagNameLowerCase))
                {
                    songIssues.Add(SongIssue.CreateWarning(null, $"Cannot set '{tagName}' multiple times"));
                }
                else
                {
                    otherFields[tagNameLowerCase] = tagValue;
                }
            }
        }

        // Check that required tags are set.
        foreach (var requiredFieldName in requiredFields)
        {
            if (requiredFieldName.Value == null)
            {
                throw new SongMetaBuilderException("Required tag '" + requiredFieldName.Key + "' was not set");
            }
        }

        //Read the song file body
        StringBuilder songBody = new();
        string bodyLine;
        while ((bodyLine = reader.ReadLine()) != null)
        {
            // Ignoring the newlines for the hash
            songBody.Append(bodyLine);
        }

        //Hash the song file body
        string songHash = Hashing.Md5(Encoding.UTF8.GetBytes(songBody.ToString()));

        try
        {
            SongMeta songMeta = new(
                directory,
                filename,
                songHash,
                requiredFields["artist"],
                ConvertToFloat(requiredFields["bpm"]),
                requiredFields["mp3"],
                requiredFields["title"],
                voiceNames,
                reader.CurrentEncoding
            );
            foreach (var item in otherFields)
            {
                switch (item.Key)
                {
                    case "background":
                        songMeta.Background = item.Value;
                        break;
                    case "cover":
                        songMeta.Cover = item.Value;
                        break;
                    case "edition":
                        songMeta.Edition = item.Value;
                        break;
                    case "end":
                        songMeta.End = ConvertToFloat(item.Value);
                        break;
                    case "gap":
                        songMeta.Gap = ConvertToFloat(item.Value);
                        break;
                    case "genre":
                        songMeta.Genre = item.Value;
                        break;
                    case "language":
                        songMeta.Language = item.Value;
                        break;
                    case "previewstart":
                        songMeta.PreviewStart = ConvertToFloat(item.Value);
                        break;
                    case "previewend":
                        songMeta.PreviewEnd = ConvertToFloat(item.Value);
                        break;
                    case "start":
                        songMeta.Start = ConvertToFloat(item.Value);
                        break;
                    case "video":
                        songMeta.Video = item.Value;
                        break;
                    case "videogap":
                        songMeta.VideoGap = ConvertToFloat(item.Value);
                        break;
                    case "year":
                        songMeta.Year = ConvertToUInt32(item.Value);
                        break;
                    default:
                        songMeta.SetUnknownHeaderEntry(item.Key, item.Value);
                        break;
                }
            }

            // Recreate issues with proper SongMeta
            songIssues = songIssues.Select(songIssue => new SongIssue(songIssue.Severity, songMeta, songIssue.Message, songIssue.StartBeat, songIssue.EndBeat))
                .ToList();
            songIssues.ForEach(songIssue =>
            {
                Debug.LogWarning($"{songIssue.Message} in file {path}");
            });

            return songMeta;
        }
        catch (ArgumentNullException e)
        {
            // if you get these with e.ParamName == "s", it's probably one of the non-nullable things (ie, float, uint, etc)
            throw new SongMetaBuilderException("Required tag '" + e.ParamName + "' was not set");
        }
    }

    private static float ConvertToFloat(string s)
    {
        // Some txt files use comma as decimal separator (e.g. "12,34" instead "12.34").
        // Convert this to English notation.
        string sWithDotAsDecimalSeparator = s.Replace(",", ".");
        if (float.TryParse(sWithDotAsDecimalSeparator, NumberStyles.Any, CultureInfo.InvariantCulture, out float res))
        {
            return res;
        }
        else
        {
            throw new SongMetaBuilderException("Could not convert " + s + " to a float.");
        }
    }

    private static uint ConvertToUInt32(string s)
    {
        if (s.IsNullOrEmpty())
        {
            return 0;
        }

        try
        {
            return Convert.ToUInt32(s, 10);
        }
        catch (FormatException e)
        {
            throw new SongMetaBuilderException("Could not convert " + s + " to an uint. Reason: " + e.Message);
        }
    }
}

[Serializable]
public class SongMetaBuilderException : Exception
{
    public SongMetaBuilderException(string message)
        : base(message)
    {
    }

    public SongMetaBuilderException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
