using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DeadEditor.Services
{
    /// <summary>
    /// Provides autocomplete suggestions for album/release names from Data/releases.json.
    /// Lazily loaded singleton. Supports adding new standalone releases and persisting.
    /// </summary>
    public class ReleaseLookupService
    {
        private static readonly Lazy<ReleaseLookupService> _instance = new(() => new ReleaseLookupService());
        public static ReleaseLookupService Instance => _instance.Value;

        private readonly string _filePath;
        private readonly List<string> _allNames;
        private readonly HashSet<string> _knownNames;
        private JObject? _rawJson;

        private ReleaseLookupService()
        {
            _filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "releases.json");
            _allNames = new List<string>();
            _knownNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Load();
        }

        private void Load()
        {
            if (!File.Exists(_filePath))
            {
                Console.WriteLine($"[ReleaseLookupService] Warning: releases.json not found at {_filePath}");
                return;
            }

            try
            {
                var json = File.ReadAllText(_filePath);
                _rawJson = JObject.Parse(json);

                // Expand series templates
                var series = _rawJson["series"] as JArray;
                if (series != null)
                {
                    foreach (var s in series)
                    {
                        var template = s.Value<string>("template") ?? "";
                        var volumes = s["volumes"] as JArray;
                        if (volumes == null) continue;

                        foreach (var v in volumes)
                        {
                            string name;
                            if (v.Type == JTokenType.Object)
                            {
                                // Road Trips style: {"vol": 1, "num": 1}
                                var vol = v.Value<int>("vol");
                                var num = v.Value<int>("num");
                                name = template.Replace("{N}", vol.ToString()).Replace("{M}", num.ToString());
                            }
                            else
                            {
                                // Simple integer volume
                                name = template.Replace("{N}", v.ToString());
                            }
                            _allNames.Add(name);
                            _knownNames.Add(name);
                        }
                    }
                }

                // Add standalone names
                var standalone = _rawJson["standalone"] as JArray;
                if (standalone != null)
                {
                    foreach (var item in standalone)
                    {
                        var name = item.Value<string>() ?? "";
                        if (!string.IsNullOrEmpty(name))
                        {
                            _allNames.Add(name);
                            _knownNames.Add(name);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ReleaseLookupService] Error loading releases.json: {ex.Message}");
            }
        }

        /// <summary>
        /// Returns up to 20 release names matching the search text (case-insensitive).
        /// Prefix matches sort before contains matches.
        /// Returns empty list for empty/short search text.
        /// </summary>
        public List<string> GetSuggestions(string searchText)
        {
            if (string.IsNullOrWhiteSpace(searchText) || searchText.Length < 2)
                return new List<string>();

            var search = searchText.Trim();
            var prefixMatches = new List<string>();
            var containsMatches = new List<string>();

            foreach (var name in _allNames)
            {
                if (name.StartsWith(search, StringComparison.OrdinalIgnoreCase))
                    prefixMatches.Add(name);
                else if (name.Contains(search, StringComparison.OrdinalIgnoreCase))
                    containsMatches.Add(name);
            }

            return prefixMatches.Concat(containsMatches).Take(20).ToList();
        }

        /// <summary>
        /// Returns true if the name matches a known release exactly (case-insensitive).
        /// </summary>
        public bool IsKnownRelease(string name)
        {
            return !string.IsNullOrEmpty(name) && _knownNames.Contains(name);
        }

        /// <summary>
        /// Adds a new standalone release name and persists to releases.json.
        /// </summary>
        public void AddRelease(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || _knownNames.Contains(name))
                return;

            _allNames.Add(name);
            _knownNames.Add(name);

            // Add to JSON standalone array
            if (_rawJson != null)
            {
                var standalone = _rawJson["standalone"] as JArray;
                if (standalone == null)
                {
                    standalone = new JArray();
                    _rawJson["standalone"] = standalone;
                }
                standalone.Add(name);
            }

            SaveToFile();
        }

        /// <summary>
        /// Removes a standalone release name and persists to releases.json.
        /// Returns true if the release was found and removed.
        /// </summary>
        public bool RemoveStandaloneRelease(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;

            _allNames.Remove(name);
            _knownNames.Remove(name);

            if (_rawJson != null)
            {
                var standalone = _rawJson["standalone"] as JArray;
                if (standalone != null)
                {
                    var item = standalone.FirstOrDefault(t => string.Equals(t.Value<string>(), name, StringComparison.OrdinalIgnoreCase));
                    if (item != null)
                    {
                        standalone.Remove(item);
                        SaveToFile();
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// Renames a standalone release. Returns true if successful.
        /// </summary>
        public bool RenameStandaloneRelease(string oldName, string newName)
        {
            if (string.IsNullOrWhiteSpace(oldName) || string.IsNullOrWhiteSpace(newName)) return false;
            if (string.Equals(oldName, newName, StringComparison.OrdinalIgnoreCase)) return true;

            if (_rawJson != null)
            {
                var standalone = _rawJson["standalone"] as JArray;
                if (standalone != null)
                {
                    for (int i = 0; i < standalone.Count; i++)
                    {
                        if (string.Equals(standalone[i].Value<string>(), oldName, StringComparison.OrdinalIgnoreCase))
                        {
                            standalone[i] = newName;

                            // Update in-memory collections
                            var idx = _allNames.IndexOf(oldName);
                            if (idx >= 0) _allNames[idx] = newName;
                            _knownNames.Remove(oldName);
                            _knownNames.Add(newName);

                            SaveToFile();
                            return true;
                        }
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// Adds the next volume to a series. Returns the generated name.
        /// </summary>
        public string? AddVolumeToSeries(string seriesName)
        {
            if (_rawJson == null) return null;

            var series = _rawJson["series"] as JArray;
            if (series == null) return null;

            foreach (var s in series)
            {
                if (!string.Equals(s.Value<string>("name"), seriesName, StringComparison.OrdinalIgnoreCase))
                    continue;

                var template = s.Value<string>("template") ?? "";
                var volumes = s["volumes"] as JArray;
                if (volumes == null) continue;

                // Road Trips uses vol/num objects — skip for now (complex structure)
                if (template.Contains("{M}"))
                    return null;

                // Simple integer series: find max and add next
                int maxVol = 0;
                foreach (var v in volumes)
                {
                    if (v.Type == JTokenType.Integer)
                    {
                        var val = v.Value<int>();
                        if (val > maxVol) maxVol = val;
                    }
                }

                int nextVol = maxVol + 1;
                volumes.Add(nextVol);

                var name = template.Replace("{N}", nextVol.ToString());
                _allNames.Add(name);
                _knownNames.Add(name);

                SaveToFile();
                return name;
            }
            return null;
        }

        /// <summary>
        /// Removes the last volume from a series. Returns true if successful.
        /// </summary>
        public bool RemoveLastVolumeFromSeries(string seriesName)
        {
            if (_rawJson == null) return false;

            var series = _rawJson["series"] as JArray;
            if (series == null) return false;

            foreach (var s in series)
            {
                if (!string.Equals(s.Value<string>("name"), seriesName, StringComparison.OrdinalIgnoreCase))
                    continue;

                var template = s.Value<string>("template") ?? "";
                var volumes = s["volumes"] as JArray;
                if (volumes == null || volumes.Count == 0) return false;

                // Get the last volume entry
                var lastEntry = volumes.Last;
                string name;
                if (lastEntry?.Type == JTokenType.Object)
                {
                    var vol = lastEntry.Value<int>("vol");
                    var num = lastEntry.Value<int>("num");
                    name = template.Replace("{N}", vol.ToString()).Replace("{M}", num.ToString());
                }
                else
                {
                    name = template.Replace("{N}", lastEntry?.ToString() ?? "");
                }

                volumes.Last?.Remove();
                _allNames.Remove(name);
                _knownNames.Remove(name);

                SaveToFile();
                return true;
            }
            return false;
        }

        /// <summary>
        /// Returns the series data for display: list of (seriesName, template, expandedNames).
        /// </summary>
        public List<SeriesInfo> GetSeriesInfo()
        {
            var result = new List<SeriesInfo>();
            if (_rawJson == null) return result;

            var series = _rawJson["series"] as JArray;
            if (series == null) return result;

            foreach (var s in series)
            {
                var info = new SeriesInfo
                {
                    Name = s.Value<string>("name") ?? "",
                    Template = s.Value<string>("template") ?? "",
                    VolumeNames = new List<string>()
                };

                var volumes = s["volumes"] as JArray;
                if (volumes != null)
                {
                    foreach (var v in volumes)
                    {
                        if (v.Type == JTokenType.Object)
                        {
                            var vol = v.Value<int>("vol");
                            var num = v.Value<int>("num");
                            info.VolumeNames.Add(info.Template.Replace("{N}", vol.ToString()).Replace("{M}", num.ToString()));
                        }
                        else
                        {
                            info.VolumeNames.Add(info.Template.Replace("{N}", v.ToString()));
                        }
                    }
                }

                info.IsComplexSeries = info.Template.Contains("{M}");
                result.Add(info);
            }
            return result;
        }

        /// <summary>
        /// Returns the standalone release names.
        /// </summary>
        public List<string> GetStandaloneReleases()
        {
            var result = new List<string>();
            if (_rawJson == null) return result;

            var standalone = _rawJson["standalone"] as JArray;
            if (standalone != null)
            {
                foreach (var item in standalone)
                {
                    var name = item.Value<string>();
                    if (!string.IsNullOrEmpty(name))
                        result.Add(name);
                }
            }
            return result;
        }

        /// <summary>
        /// Returns the total count of all known releases (series + standalone).
        /// </summary>
        public int TotalCount => _allNames.Count;

        /// <summary>
        /// Persists the current state back to releases.json.
        /// </summary>
        public void SaveToFile()
        {
            if (_rawJson == null) return;

            try
            {
                var json = _rawJson.ToString(Formatting.Indented);
                var tempPath = _filePath + ".tmp";
                File.WriteAllText(tempPath, json);
                File.Move(tempPath, _filePath, overwrite: true);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ReleaseLookupService] Error saving releases.json: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Data class for series information used by the Releases editor view.
    /// </summary>
    public class SeriesInfo
    {
        public string Name { get; set; } = "";
        public string Template { get; set; } = "";
        public List<string> VolumeNames { get; set; } = new();
        public bool IsComplexSeries { get; set; }
    }
}
